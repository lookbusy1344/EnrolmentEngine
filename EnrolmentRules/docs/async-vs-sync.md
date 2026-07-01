# Async vs Synchronous: Why the Evaluation Path Should Not Be `async`

The engine's public API is now synchronous end to end — `Evaluate`, `Explain`, `Advise`,
`TryEvaluate`, and their overloads return concrete values. This document records why the earlier async surface was a
mismatch for the workload, and why the per-student evaluation path belongs in synchronous code.

The short version: **the evaluation path is CPU-bound from top to bottom and never performs I/O.**
`async` there expresses a concurrency that does not exist, pays a state-machine and allocation cost
on the one path the benchmarks care about, and advertises an I/O contract the code never honours.

## What the call graph actually does

Tracing `EnrolmentEngine.RunAsync` — the core of every evaluation — the whole chain is
computational:

| Stage | Work | Nature |
|---|---|---|
| `GradePredictor.Predict` | GCSE averaging + linear regression + prior-qual carry-through | Synchronous already |
| `RatingEvaluator.EvaluateWithGate` | Eligibility gate + per-subject tiers | `await`s RulesEngine |
| `RulesEngine.ExecuteAllRulesAsync` | Compiled-lambda evaluation over one student's facts | **CPU; completes synchronously** |
| `ConstraintPass.Evaluate` / `Apply` | Cross-subject prerequisites, exclusions, vetoes | Synchronous already |
| `Aggregator.CapGreens` / `Summarise` / `Rank` | Tariff fold, optional green cap, ordering | Synchronous already |

The only `await` that reaches a library boundary is `RulesEngine.ExecuteAllRulesAsync`. And that
call does not actually go async: RulesEngine compiles the YAML lambdas to delegates and **invokes
them synchronously**, returning an already-completed `Task`. Its sole async extension point is the
success/failure *action* hook — which this project does not use. There is no I/O, no yield point,
no thread ever released, no concurrency expressed. Every `async`/`await`/`ConfigureAwait(false)`
from `EnrolmentEngine` down to `RatingEvaluator` exists only to propagate one leaf `Task` that is
born completed.

## What `async` buys here — and doesn't

`async` earns its overhead when a method **yields a thread while waiting on something external** —
disk, network, a lock, a timer — so the caller can do other work. None of the three usual payoffs
applies to this path:

- **No thread liberation.** There is nothing to wait *on*. The work is arithmetic and delegate
  invocation; the thread runs flat-out until the answer exists. `async` cannot free a thread that
  never blocks.
- **No concurrency expressed.** A single student is evaluated by a strictly sequential chain of
  `await`s. Nothing runs in parallel; each stage feeds the next.
- **No scalability story.** This is a CLI and an in-process library, not a request-servicing
  server. There is no thread pool under pressure that `async` would relieve.

Against zero benefit stands a real cost. Each `async` method compiles to a state machine; each
invocation on a hot path allocates. CLAUDE.md explicitly weights allocation and GC on the
benchmarked evaluation path (`[MemoryDiagnoser]` in `EnrolmentRules.Benchmarks`) — and this *is*
that path. Worse than the cost is the dishonesty: a `Task`-returning signature tells every caller
"this may do I/O, await me" when it provably never will. Sequential `await`s in `CounterfactualAdvisor`'s
search loop read as if they might overlap; they cannot.

## The one place async is real: startup

`EnrolmentEngine.Create`, `WorkflowStore.LoadValidateBuildAndProbe`, the DI
`AddEnrolmentEngine` extensions, and `EnrolmentEngineFactory.Create`/`Reload` read
YAML and JSON-Schema files and deserialize them. That is genuine I/O.

But it is **one-time, small, and off the hot path**. The files are a handful of kilobytes, read
once at bootstrap (or on an explicit hot-swap reload), never per evaluation. Synchronous
file loading at startup is the overwhelmingly common, entirely acceptable pattern for a library of
this shape — the same category as reading `appsettings.json`. The async there buys responsiveness
for a cost nobody pays, because nothing is contending for the thread during bootstrap.

## The three shapes considered

**1. Full `Task` (the status quo).** Fits the RulesEngine API without friction and threads
`CancellationToken` freely. But it imposes state-machine + `Task` allocation on the benchmarked
path, forces `ConfigureAwait(false)` ceremony through ~40 signatures, and advertises I/O concurrency
that does not exist.

**2. `ValueTask`.** The worst fit. It removes only the `Task` allocation for synchronously-completing
calls — but keeps the async state machines, keeps the ceremony, keeps the misleading signatures,
and adds `ValueTask`'s "await once, never block" consumption hazards. Maximum complexity for the
smallest of the three payoffs.

**3. Fully synchronous.** Honest signatures — CPU work looks like CPU work. No state machine, no
`Task` allocation, simpler stack traces, simpler tests (no `async Task` test methods, no `await`).
Cancellation is still honoured via `token.ThrowIfCancellationRequested()` between phases, which the
code already does. The single cost is a bridge at the RulesEngine leaf.

## The objection, and why it does not bite

Synchronous code calling `ExecuteAllRulesAsync` must bridge the `Task` — `.GetAwaiter().GetResult()`.
"Sync-over-async" is a well-known deadlock and thread-starvation anti-pattern, so this deserves a
precise answer rather than a wave of the hand:

- **The leaf completes synchronously.** RulesEngine evaluates the compiled lambdas on the calling
  thread (no I/O, and no success/failure actions configured), so the returned `Task` is already
  completed. `GetAwaiter().GetResult()` observes a finished result and returns immediately — it
  never blocks a thread that is waiting for another thread.
- **No `SynchronizationContext` to deadlock against.** The library already uses
  `ConfigureAwait(false)` throughout; there is no captured UI/request context for a continuation to
  contend for. The classic sync-over-async deadlock requires exactly that captured context.
- **Not a server.** Blocking a pooled thread would matter under a request-servicing load that could
  exhaust the pool. A CLI and in-process library have no such pool pressure.

The bridge is isolated to a single call site (in `RatingEvaluator`, plus the startup probe in
`WorkflowStore`), so the anti-pattern's blast radius is one well-understood boundary, not a habit
smeared across the codebase.

The only residual risk is forward-looking: if a future RulesEngine version made
`ExecuteAllRulesAsync` genuinely async, the bridge would block a thread until it completed. For this
workload that is acceptable and localised — and it is the same bet the sync path makes everywhere it
loads a config file synchronously.

## Verdict

The per-student evaluation path gains nothing from `async` and pays for it on the exact path the
project optimises. It should be synchronous. The bridge at the RulesEngine boundary is safe here for
concrete, stated reasons — not by luck. The startup path reads files and is the one place `async`
represents real I/O; whether to keep it async or fold it into the sync surface for uniformity is a
separate, lower-stakes call (see the implementation plan).

`ValueTask` is a trap for this codebase: it keeps every downside of `async` to shave one allocation.
The choice is between honest synchronous code and the status-quo `Task`, and the workload argues
plainly for synchronous.

## Server-hosting note: does a sync engine hurt an ASP.NET site?

The natural worry is: if evaluation is CPU work and we host it behind ASP.NET, won't synchronous code
starve the thread pool, and wouldn't `async` protect us? Both halves rest on a misconception worth
stating plainly.

**`async` does not relieve CPU-bound load.** `async` frees a thread only across a *genuine wait* —
I/O, a lock, a timer — where the thread would otherwise sit idle. It returns that thread to the pool
for the duration of the wait, then resumes on the continuation. CPU work has no such idle window: the
thread is *actively computing* start to finish, and there is nothing to give back. You cannot `await`
your way out of needing a core to do arithmetic. In this codebase the point is sharper still —
`ExecuteAllRulesAsync` runs the lambdas **inline on the calling thread** and returns a completed
`Task`, so awaiting it and calling it synchronously produce identical thread behaviour. The current
async offloads nothing; it only wraps synchronous work in an I/O-shaped signature.

Two distinct failure modes get conflated under "starvation":

- **Classic thread-pool starvation** — threads sitting *blocked* (on I/O, a lock, `.Result`) while
  the pool injects replacements slowly and requests queue behind idle-but-unavailable threads. This
  is a *blocking* pathology. A synchronous CPU method does not cause it — it never blocks, it runs.
- **CPU saturation** — every core busy doing real work; more concurrent heavy requests than cores,
  so they contend and latency climbs. Nothing is blocked; there is simply more work than silicon.
  This is the only pressure the evaluation path can create, and `async` is irrelevant to it.

**A synchronous library is fine — even preferable — inside ASP.NET.** Sync-over-async danger is sync
code *blocking* on a task (`.Result`/`.Wait()`), which ties up a thread. A sync method that merely
*runs* has no task to block on: the controller action stays `async` for the framework's genuine I/O
(model binding, response writing, downstream calls) and calls the evaluation inline, where the CPU
work runs on the request thread exactly as it would if awaited.

**The scaling answer is cores, not keywords — and it is close to config-only here because the engine
is a stateless pure function.** One student in, one result out; no shared mutable state, no working
memory, no locks. Concurrency is embarrassingly parallel: you do not parallelise *within* an
evaluation, you run many independent evaluations at once and they never touch each other. The
codebase already relies on this — the CLI batch runner shares a single engine instance across cores
via `Parallel.ForAsync`, and the no-global-state tests pin it. On a server the shape is identical:
ASP.NET spreads concurrent requests across pool threads (hence cores) automatically; each request
calls the shared engine synchronously on its own thread.

Two caveats keep that honest:

- **Cores raise the throughput ceiling, not single-request latency.** One evaluation runs on one
  core; more cores serve more requests per second but make no individual request faster. A slow
  counterfactual search is fixed by cheaper computation or caching, not by adding cores.
- **Cores alone give saturation, not graceful degradation.** With no bound, concurrent heavy
  requests beyond core count do not fail cleanly — they all slow down (context-switching, queueing)
  and tail latency collapses. Pair the cores with a **concurrency limiter** (ASP.NET rate-limiting
  middleware or a bounded queue) so overload sheds or queues deliberately instead of thrashing.

The practical recipe, then: **run the synchronous engine, let the platform spread requests across
cores, add a concurrency limit for the overload case, and scale out horizontally when one box is not
enough** — the statelessness makes scale-out affinity-free. Because results are a pure function of
immutable inputs, they are also memoizable; caching is often the largest win before any of the above.
None of that is an argument for `async` on the evaluation path.

### Calling a slow path from an ASP.NET action without tying up the request thread

`Task.Run` is not a blanket wrapper for the engine — it is worth reaching for only when the call
is genuinely slow enough that freeing the request thread for its duration matters. `Advise`'s
counterfactual search is the case that qualifies: the benchmarks above show it re-running the
predict → engine pipeline tens of thousands of times per call (≈43,000 internal evaluations for
the worst-case student), giving wall-clock time in the millisecond-to-second range rather than
`Evaluate`/`Explain`'s microseconds. The controller action stays `async` for the framework's own
I/O; it just doesn't `await` the engine directly, because there is nothing to await — instead it
offloads the slow, synchronous, CPU-bound call to a thread-pool worker thread and awaits that:

```csharp
public sealed class AdvisorController(IEnrolmentAdvisor advisor) : ControllerBase
{
	[HttpPost("advise")]
	public async Task<ActionResult<AdviceResult>> Advise(
		StudentInput student,
		CancellationToken cancellationToken)
	{
		// Task.Run hands the synchronous, CPU-bound counterfactual search to a thread-pool
		// worker thread and frees the request thread while it runs; the state machine's
		// continuation resumes here once the search completes.
		var result = await Task.Run(
			() => advisor.Advise(student, cancellationToken),
			cancellationToken);

		return Ok(result);
	}
}
```

This is not "making the engine async" — `Advise` itself is still the honest synchronous call
described above. `Task.Run` is the caller's choice to spend a thread-pool thread now in exchange
for freeing the request thread during the computation. **Do not apply the same wrapper to
`Evaluate`/`Explain`/`TryEvaluate`/`TryExplain`:** those return in microseconds, so the `Task.Run`
scheduling overhead would exceed the work being offloaded, and the request thread was never tied
up long enough to matter — call them inline in the action body instead. Reach for lever 1
(caching) before reaching for `Task.Run` at all; a memoized `Advise` result needs no thread
offloaded.

### Mitigations, in order of reach

When the engine is under heavy load behind ASP.NET, reach for these — cheapest and highest-leverage
first:

| # | Lever | What it does | When it is the answer |
|---|---|---|---|
| 1 | **Cache results** | Memoize on the immutable input; a repeat request returns without recomputing | Any repeated or repeatable input; usually the largest win |
| 2 | **Concurrency limiter** | Rate-limiting middleware or a bounded queue; sheds/queues past capacity | You need graceful degradation instead of thrashing all cores |
| 3 | **Scale up (cores)** | Platform spreads concurrent requests across cores; raises throughput ceiling | Sustained request rate above one core's capacity |
| 4 | **Scale out (instances)** | Add stateless instances behind a load balancer; affinity-free | One box is not enough; also gives redundancy |

**Not on this list: `async`.** It frees threads across *waits*, and evaluation has none — it cannot
relieve CPU load. Reaching for it here is the wrong tool. Note too that levers 3 and 4 raise the
*throughput ceiling* but not single-request latency; a slow individual evaluation (e.g. a large
counterfactual search) is a job for lever 1 or cheaper computation, not more cores.

## Empirical check: benchmarked memory, GC, and CPU deltas

The conversion is complete, so the claims above can be checked against real numbers rather than
argued from first principles. `EnrolmentRules.Benchmarks` was run (`--job short`, 3 iterations, M4
Pro) at HEAD (fully synchronous) and at `2466c955` (the last commit before the conversion, async
end to end):

| Method | Async (`2466c955`) | Sync (HEAD) | Time Δ | Alloc Δ |
|---|---:|---:|---:|---:|
| ConstructEngine | 64.59 us / 223.83 KB | 61.22 us / 223.83 KB | −5% (noise) | 0 B |
| EvaluateSingle(Async) | 21.50 us / 85.04 KB | 21.72 us / 84.68 KB | flat | −368 B (−0.42%) |
| EvaluateBatch(Async) | 65.33 us / 255.79 KB | 64.75 us / 254.49 KB | flat | −1,331 B (−0.51%) |
| Advise(Async) | 1,288.4 ms / 3,505,684.66 KB | 1,322.1 ms / 3,490,114.22 KB | +2.6% | −15,570 KB (−0.44%) |

**The allocation drop is real but small, and the numbers explain why.** `ConstructEngine` never
touches an async call in either version, so its 0-byte delta is the noise floor — the same build
plumbing, same result. Every path that *does* cross the async boundary shows the same ~0.4–0.5%
allocation reduction, which is consistent with removing a handful of `Task`/`Task<T>` wrapper
objects (≈80–100 bytes each) per call chain rather than removing any state-machine boxing: because
`RulesEngine.ExecuteAllRulesAsync` always completes synchronously, the compiler's struct-based
async state machines never suspend and were never boxed to the heap in the async version either.
The theoretical worry — "sync should shrink long-lived heap allocations and GC load" — assumes the
async version was boxing state machines onto the heap; it wasn't, because nothing ever awaited a
pending task. What sync removes is strictly the `Task`/`Task<T>` result-wrapper allocations
themselves, which is a real but modest saving, not the larger state-machine-boxing cost that
`async` incurs when a method *actually* suspends.

**`Advise` scales the same small per-call saving by call count.** The counterfactual search
re-runs the whole predict → engine pipeline per node; ~368 B saved per `EvaluateSingle`-equivalent
call, multiplied out to the observed 15.6 MB saved over the search, implies roughly 15,570 KB ÷
0.36 KB ≈ **43,000 internal evaluation calls** inside one `Advise` run for this worst-case
(all-amber/red, 13-subject) student — a plausible node count for a per-subject grade search over
that many amber/red subjects.

**GC generation counts are unchanged (Gen0/Gen1/Gen2 collections per 1000 ops are within rounding
of each other in both versions).** Removing sub-0.5%-of-total wrapper allocations does not shift
enough bytes to trigger a different Gen0 budget or promote anything further, so there's no
measurable change in collection *frequency*, only in bytes allocated.

**Wall-clock time did not improve, and `Advise` is marginally slower (+2.6%, inside typical
3-iteration ShortRun noise but consistently non-negative across runs).** The likely offset:
`GetAwaiter().GetResult()` at the RulesEngine bridge (see above) adds a small fixed per-call cost
that roughly cancels the saved allocation time, and since none of the removed allocations were
Gen1/Gen2-triggering, there was no GC-pause time to recover in the first place. At this workload's
allocation scale (KB per call, GB per heavy search) the CPU-bound rules evaluation and prediction
pipeline dominate; the `Task` wrapper overhead being removed was already a rounding error against
that cost.

**Conclusion:** the theoretical GC/memory case for sync assumed heap-boxed async state machines
that this codebase never had — `RulesEngine`'s synchronous-completion behaviour meant the async
version was already close to optimal on that front. The measured saving (~0.4–0.5% allocation,
no time improvement) confirms the conversion's value is signature honesty and code simplicity, not
a throughput or GC win. That was always the primary argument in this document; the benchmarks
rule out GC pressure as an additional, secondary justification.

## When to revisit this

The verdict holds while evaluation stays CPU-bound and in-process. It would change if either of
these appears:

- **Real per-evaluation I/O** — e.g. the pipeline starts fetching a student record, a remote policy
  service, or a database during evaluation rather than receiving immutable facts. Then `async`
  earns its keep on the hot path and should return.
- **A server host under pool pressure** — if the engine is fronted by a high-throughput request
  server where blocking pooled threads on the RulesEngine bridge could starve the pool. Then the
  bridge, not `async` itself, becomes the problem to remove.

Absent those signals, synchronous is the faithful representation of what this code does.
</content>
</invoke>
