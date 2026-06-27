# Performance & Benchmarks

EnrolmentRules is designed as an **embeddable, high-throughput library**, not just a CLI. The CLI is
a thin shim over `EnrolmentEngine`; the same façade is meant to be built once and reused across
requests in an ASP.NET host, a batch job, or any in-process API. This note records what that costs,
so the design claim ("stateless, reusable, low per-request overhead") is backed by numbers rather
than assertion.

## What the design buys

- **Construct once, evaluate many.** The expensive work — schema validation, workflow
  deserialization, and RulesEngine lambda compilation — happens at startup (`CreateAsync` /
  `WorkflowStore.LoadValidateBuildAndProbeAsync`). The built engine is immutable and the evaluation
  path threads every per-student fact through the call, so a single instance serves the whole
  process. Register it as a DI **singleton** (`AddEnrolmentEngine`); never rebuild per request.
- **Stateless and parallel-safe.** `EnrolmentEngine` and `RatingEvaluator` hold only the shared
  engine, catalogue, scale, and thresholds. There is no per-request mutable state, so concurrent
  `EvaluateAsync` calls neither contend nor interfere — batch is just `Task.WhenAll` over one
  instance.
- **GC-friendly per-request path.** A single evaluation allocates a handful of small, short-lived
  objects (per-subject dictionaries, `RuleParameter` arrays, the result projections). They die in
  Gen0; nothing survives to Gen1/Gen2 on the evaluation path.

## Methodology

Benchmarks live in `src/EnrolmentRules.Benchmarks` (`EnrolmentBenchmarks.cs`), built on
**BenchmarkDotNet** with `[MemoryDiagnoser]` so allocations are measured, not estimated. Four
benchmarks isolate the phases:

| Benchmark | What it measures |
|---|---|
| `ConstructEngine` | The one-time reusable RulesEngine build (the startup cost you pay once). |
| `EvaluateSingleAsync` | The warm per-student path — the hot loop an API hits per request. |
| `EvaluateBatchAsync` | Three students over **one shared engine**, to confirm linear scaling and zero reuse overhead. |
| `AdviseAsync` | Counterfactual advice for a worst-case middling student (the search-heavy path). |

## Results

Figures below are a **ShortRun** pass (3 iterations) — directional, not publication-grade — on the
following configuration. Treat them as orders of magnitude and relative ratios; re-run a full pass on
your target hardware for absolute numbers.

> Environment: BenchmarkDotNet v0.15.8, .NET 10.0.9, Apple Arm64 (armv8.0-a), Concurrent Workstation GC.

| Method | Mean | Allocated | Ratio (vs construct) |
|---|--:|--:|--:|
| `ConstructEngine` | 42 µs | 152 KB | 1.00× |
| `EvaluateSingleAsync` | **13 µs** | **59 KB** | 0.31× |
| `EvaluateBatchAsync` (3 students) | 39.5 µs | 177 KB | 0.94× |
| `AdviseAsync` | **631 ms** | **1.67 GB** | ~15,000× |

### Reading the numbers

- **Per-request evaluation is cheap.** ~13 µs and ~59 KB per student, entirely Gen0. A web endpoint
  doing one `EvaluateAsync` per request is dominated by RulesEngine lambda invocation, not I/O or
  allocation pressure. Throughput is bounded by CPU, and the work parallelises across cores cleanly.
- **The singleton lifetime pays for itself.** Construction costs ~3.2× a single evaluation. Not
  catastrophic if accidentally rebuilt, but pure waste — the DI singleton registration is the
  correct lifetime, confirmed quantitatively.
- **Batch reuse scales linearly with no overhead.** Three students ≈ 3 × the single-student cost
  (~13 µs / ~59 KB each), validating "build once, reuse across students" — the shared engine adds no
  contention penalty.
- **`AdviseAsync` is a different weight class.** Counterfactual advice re-runs the full
  predict → engine → constraint pipeline per node of its grade search; the worst-case middling
  student here costs ~631 ms and allocates ~1.67 GB (with real Gen2 traffic) for a single call —
  roughly 48,000× a single evaluation.

## Hosting guidance

- **`EvaluateAsync` / `ExplainAsync` are the endpoints you scale.** Build one engine at startup,
  hold it as a singleton, and call freely — including concurrently. No pooling, no per-request setup.
- **Treat `AdviseAsync` as a heavyweight, isolated operation.** Do **not** place it on a hot or
  uncontrolled HTTP path. Rate-limit it, run it on a background queue, cache results, and bound the
  search where you can. It is an occasional advisory operation, not part of the throughput budget.
- **Reference date at the edge.** For long-running hosts bind a live `Func<DateOnly>` (resolved per
  evaluation) or pass an explicit `asOf` per call, so a student's age cannot freeze at construction.

## Reproducing

```bash
# Full run (minutes; tightest figures):
dotnet run -c Release --project src/EnrolmentRules.Benchmarks

# Fast directional pass (the ShortRun used above):
dotnet run -c Release --project src/EnrolmentRules.Benchmarks -- --filter '*' --job short

# A single benchmark:
dotnet run -c Release --project src/EnrolmentRules.Benchmarks -- --filter '*EvaluateSingle*'
```

Confirm any performance change against these benchmarks rather than by eyeballing — that is what the
`[MemoryDiagnoser]` allocation column is for.
