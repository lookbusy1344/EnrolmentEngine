# EnrolmentRules - A monotonic, rules-as-data engine for A-Level enrolment decisions

This is a recreation of a proprietary project I developed about a decade ago, to assist in enrolment decision-making and ensure policies were
consistently followed. The real system was also capable of writing the complete enrolment package into the management information system, and printing
forms for signature.

Here we show the core ideas in an open-source (MIT licence) project using modern idiomatic .NET 10. The code is designed as a library, so can be
connected to ASP.NET, a language-agnostic REST API, MVC, or just used from the command line (as demonstrated here).

The system is a simple monotonic expert system using [Microsoft RulesEngine](https://microsoft.github.io/RulesEngine). It evaluates one student at a
time through a stateless pipeline:

1. GCSE results are normalised into a predicted A-level profile.
2. Microsoft RulesEngine evaluates the rules-as-data eligibility and subject rating tables.
3. Host code applies the constraints that relate one subject to another — prerequisites,
   exclusions, own-time requirements, per-subject vetoes, an optional green cap (off by default),
   and aggregate scoring.

## Quick Start

Requirements to build:

- .NET 10 SDK (Windows, Mac, Linux)

The solution file is `EnrolmentRules.slnx`.

Run from this directory:

```bash
dotnet build EnrolmentRules.slnx -warnaserror
dotnet format EnrolmentRules.slnx
dotnet test EnrolmentRules.slnx
```

Evaluate the checked-in sample student:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --table examples/student.json
```

Lint the workflow data before shipping a rule change:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --lint-workflows
```

## Architecture At A Glance

This split follows the structure of the problem: the per-subject ratings are independent unary
table lookups, naturally expressed as data, while the relations *between* subjects are relational
and monotone — a constraint can only ever push a rating toward red, so the constraints commute and
collapse to most-severe-wins (detailed under [Domain Policy](#domain-policy)). Applying those
relations belongs in typed, compiled code regardless of engine, and it aligns with a RulesEngine
constraint anyway: a rule cannot read sibling rule outcomes.

**The rules themselves are all configurable data — there is no rule logic compiled into the
binary.** Which subjects clash, what each subject needs as a prerequisite, what bars it, the UCAS
weights, the entry equivalents, the restudy bars — every individual rule of this kind lives in
`data/catalogue.yaml`, a deployable file loaded and schema-validated into an immutable startup snapshot
(`CatalogueStore`), with coverage and exclusion-symmetry enforced as load-time invariants. Adding,
removing, or retuning a clash pair, prerequisite, entry-equivalent, or restudy bar is a one-line
YAML edit, not a code change — as is editing the per-student rules in `workflows/` and the numeric
tuning knobs in `data/thresholds.yaml`.

What stays compiled is not a rule but the fixed, small catalogue of **relationship *types*** — the
half-dozen shapes a rule can take (prerequisite, mutual exclusion, prior-choice exclusion,
own-time, veto, restudy bar) and how their downgrades compose.
`src/EnrolmentRules.Engine/ConstraintPass.cs` reads the data and applies those shapes;
`src/EnrolmentRules.Engine/Aggregator.cs` does the over-the-set aggregate scoring and the optional
green cap (off by default).
These types are deliberately static: you touch them only to invent a *new kind* of relationship
that does not exist yet — a rare event, not something that changes within an academic year. Which
subjects relate, and how strictly, is everyday data.

The numeric **tuning knobs** the relationship code and the workflow lambdas read (pass grade,
entry thresholds, DfE-probability floors, the optional green cap, the amber tariff factor, ...) live in
`data/thresholds.yaml`, loaded and schema-validated at startup
(`PolicyThresholdsStore`); the qualification-grade scale for prior qualifications lives in
`data/qualifications.yaml` and is loaded by `QualificationScaleStore`; only the GCSE-scale
invariants (`Min`/`MaxGcseGrade`) remain compiled in `Thresholds` (`src/EnrolmentRules.Domain`),
since they define the grade scale rather than policy.

### Where Policy Lives

| If you are changing...                                                                           | Edit                       |
|--------------------------------------------------------------------------------------------------|----------------------------|
| Whole-student eligibility or a subject's green/amber/red base tier                               | `workflows/*.yaml`         |
| Numeric thresholds read by workflows or host code                                                | `data/thresholds.yaml`     |
| Qualification grade ordering and A-level-points equivalence                                      | `data/qualifications.yaml` |
| Subject relationships, UCAS weights, regression coefficients, entry equivalents, or restudy bars | `data/catalogue.yaml`      |
| A new relationship *type* or scale invariant                                                     | compiled C# in `src/`      |

Routine policy changes are YAML changes. C# changes are reserved for new evaluator shapes, such as
introducing a relationship type that does not already exist.

For the full rule-location matrix and authoring workflow, see
[the rule-authoring guide](docs/rule-authoring.md). For a field-by-field map of the editable YAML
and JSON surfaces, see [the configuration reference](docs/configuration-reference.md). New to the
project or to Microsoft RulesEngine?
Start with the [guided walk-through](docs/walkthrough.md).

## Repository Layout

| Path                                                | Purpose                                                                                                                                                                                 |
|-----------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `src/EnrolmentRules.Domain`                         | Immutable domain inputs, outputs, subject/rating vocabulary, thresholds, the `Catalogue` (loads/validates `data/catalogue.yaml`), and JSON source generation.                           |
| `src/EnrolmentRules.Prediction`                     | GCSE averaging and linear prediction from raw student facts to `StudentProfile`.                                                                                                        |
| `src/EnrolmentRules.Engine`                         | Workflow and catalogue loading, schema validation, probe compilation, rating evaluation, constraints, aggregation, the `EnrolmentEngine` facade and its `IEnrolmentEngine` abstraction. |
| `src/EnrolmentRules.Extensions.DependencyInjection` | `AddEnrolmentEngineAsync` / `AddEnrolmentEngine` registration helpers for `Microsoft.Extensions.DependencyInjection` hosts.                                                             |
| `src/EnrolmentRules.Cli`                            | `enrolment` executable and table/JSON/batch command line modes.                                                                                                                         |
| `src/EnrolmentRules.Benchmarks`                     | BenchmarkDotNet harness for engine throughput.                                                                                                                                          |
| `tests/EnrolmentRules.Tests`                        | xUnit and FluentAssertions coverage, including engine-driven rule tests, invariants, and golden files.                                                                                  |
| `workflows`                                         | Hot-swappable RulesEngine workflow YAML and its schema.                                                                                                                                 |
| `examples`                                          | Single-student JSON, JSONL batch input, and golden-file fixtures.                                                                                                                       |
| `docs/*.md`                                         | Reference docs for the pipeline, configuration surfaces, rule authoring, and engine-choice rationale.                                                                                   |
| `docs/plans`                                        | Design and implementation planning notes.                                                                                                                                               |

## Student Input

Input documents use this shape (shown as YAML; the same document may also be supplied as JSON).
The single-student modes select the parser by file extension (`.yaml`/`.yml` vs `.json`) and
normalize both to one shared validation path, so the two formats are interchangeable:

```yaml
student:
  id: S-1001
  gcses:
    maths: 9
    english_language: 7
    english_literature: 7
    physics: 7
    chemistry: 6
    biology: 6
    history: 5
    music: 6
    art: 8
  chosen_a_levels:
    - history
  hobbies:
    - plays_piano
    - chess_club
  prior_qualifications:
    - subject: applied_science
      type: btec_diploma
      grade: distinction
  date_of_birth: "2009-09-01"
```

GCSE grades are validated on the 1-9 scale. Missing subjects are treated as absent, not as
zeroes. Hobbies are opaque tags used by host constraints such as Music's own-time requirement.
`chosen_a_levels` records already-committed A-level choices. It feeds only the host constraint
pass; it does not change prediction, base rating, or ranking.
`prior_qualifications` carries typed prior study separately from GCSEs. Each entry is validated
against `data/qualifications.yaml`, which defines the grade ordering and A-level-points
equivalence for each qualification type/grade pair. The list is used upstream to open
alternative entry paths and raise predicted points, and downstream to enforce restudy bars.
`date_of_birth` is a required ISO date (`YYYY-MM-DD`). Age is **not** stored on the document — it
is not a pure function of the input (it depends on *when* the student is assessed), so the engine
derives a whole-years age from the date of birth as of a per-run **reference ("as-of") date**. The
CLI uses the current local date; tests and golden files pin a fixed date so age-dependent outcomes
stay deterministic. The wall clock is read only at the CLI edge, never inside the engine.
YAML input applies to the single-student modes only; `--batch` remains JSONL.

### Custom A-level subjects

A-level subject identity is **data-driven**: a subject is a validated snake_case value object loaded
from the catalogue. Adding a new A-level subject means:

1. add its row to `data/catalogue.yaml` (UCAS weight, regression coefficients, and any exclusions /
   prerequisites / own-time / entry-equivalent / restudy-bar policy),
2. add its `green` / `amber` / `red` rules to `workflows/subject-ratings.yaml`,
3. if the subject or its prior-qualification policy needs a new grade mapping for an existing
   qualification type, add it to `data/qualifications.yaml`,
4. if you want `--lint-workflows` to validate that custom workflow set, keep the matching
   `data/` and `workflows/` directories side by side.

The engine, prediction stage, constraint pass, aggregation, and workflow linter all operate on the
loaded catalogue snapshot, so a custom subject flows end-to-end without changing C#.

One boundary remains compiled: the **GCSE input vocabulary**. `GcseSubjects.Known` is still the
fixed list the input validator accepts, so adding an A-level subject that also needs a brand-new
GCSE key is outside this phase.

The **qualification-type vocabulary** is also compiled as `QualificationType` and repeated in the
qualification and catalogue schemas. `data/qualifications.yaml` controls the grades, ordering, and
equivalences for those known types; introducing a new type requires coordinated C# and schema changes.

A checked-in reference fixture for a custom `drama` subject lives in
`examples/custom-subject/`.

## Domain Policy

This is an **advisory** system: it produces a per-subject recommendation for a student choosing
their A-levels, never an actual enrolment. The tiers are:

- **green** — pre-approved; enrol with no staff authorisation.
- **amber** — may enrol, but the choice needs manual authorisation.
- **red** — not permitted.

The upstream prediction stage computes the mean GCSE score, an A-level-points prediction, and DfE
transition-matrix probability evidence for every catalogue subject. Prediction uses the A-level
scale `A*=6`, `A=5`, `B=4`, `C=3`, `D=2`, `E=1`, `U=0`; this is separate from the GCSE 1-9 scale.

Two distinct mechanisms decide a subject's rating; keep them apart:

1. **Base tier** — a RulesEngine workflow picks green/amber/red by a first-hit-wins scan (green
   criteria, else amber, else red). This stage *is* an ordered cascade.
2. **Cross-subject constraints** — prerequisites, mutual exclusions, prior-choice exclusions,
   own-time requirements, per-subject vetoes, and restudy bars. Each reads only the base tier,
   never another constraint's output, and when it fires can only move the rating toward red. They
   therefore commute and fold in by most-severe-wins, order-independently.
3. **Optional green cap** — when explicitly enabled, this runs after the constraint fold because it
   counts only greens that survived those downgrades. It is also downgrade-only, but it is a distinct,
   ordered aggregation phase rather than one of the commuting base-rating constraints.

The precedence table in the walk-through reads as one top-down ladder, but that is the *net* result
after the fold, not the execution order: the base tier is emitted first, then every downgrade is
folded in. Promotion never happens.

For the detailed signal table, precedence table, subject thresholds, prior-qualification mechanics,
and prerequisite semantics, see [the guided walk-through](docs/walkthrough.md) and
[the rule-authoring guide](docs/rule-authoring.md).

### Example Workflow

This is the shape of a real authored workflow file in `workflows/`:

```yaml
WorkflowName: 'subject-ratings'
Rules:
  - RuleName: 'english_literature:green'
    SuccessEvent: 'Entry met; predicted A-level grade at or above the green threshold'
    Expression: >-
      facts.Gcse("english_language") >= facts.TopEntry &&
      facts.Gcse("maths") >= facts.TopEntry &&
      facts.Predicted("english_literature") >= ALevelGrade.B &&
      facts.DfeProbabilityAtOrAbove("english_literature", ALevelGrade.B)
        >= facts.MinDfeGreenProbabilityAtOrAbove

  - RuleName: 'english_literature:amber'
    SuccessEvent: 'Entry met; predicted A-level grade at or above the amber threshold'
    Expression: >-
      facts.Gcse("english_language") >= facts.TopEntry &&
      facts.Gcse("maths") >= facts.TopEntry &&
      facts.Predicted("english_literature") >= ALevelGrade.C &&
      facts.DfeProbabilityAtOrAbove("english_literature", ALevelGrade.C)
        >= facts.MinDfeAmberProbabilityAtOrAbove

  - RuleName: 'english_literature:red'
    SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
    Expression: >-
      true
```

The loader supplies `RuleExpressionType = LambdaExpression` automatically for actual
rules, so the YAML stays focused on the business logic.

Entry thresholds can branch on derived per-student signals. Art's GCSE bar is age-gated entirely
in the YAML — host code exposes `facts.Age` (derived from `date_of_birth`) and the loaded tuning
values (`facts.AdultAge`, `facts.TopEntry`, `facts.StrongEntry`); the branching policy lives in the
rule expression:

```yaml
  - RuleName: 'art:green'
    SuccessEvent: 'Entry met (age-gated GCSE threshold); predicted A-level grade at or above the green threshold'
    Expression: >-
      facts.Gcse("art") >= (facts.Age >= facts.AdultAge ? facts.TopEntry : facts.StrongEntry) &&
      facts.Predicted("art") >= ALevelGrade.B &&
      facts.DfeProbabilityAtOrAbove("art", ALevelGrade.B) >= facts.MinDfeGreenProbabilityAtOrAbove
```

### Workflow Authoring

- Edit the YAML files in `workflows/` rather than the normalized engine payload.
- Keep lambda expressions as the rule bodies; the loader injects the boilerplate `RuleExpressionType`.
- Startup still validates the workflow shape and probe-compiles every rule.
- Any workflow change should keep the same build, format, and test gate green.

For the evaluation stages and shipped rule patterns, see the [guided walk-through](docs/walkthrough.md);
for the full YAML-editing reference and every datum a rule can read, the
[rule-authoring guide](docs/rule-authoring.md).

## Command Line Examples

Print the predicted profile only:

```bash
dotnet run --project src/EnrolmentRules.Cli -- examples/student.json
```

Print the traffic-light enrolment table:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --table examples/student.json
```

Print the raw enrolment result JSON:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --json examples/student.json
```

The single-student modes also accept a YAML document (extension-dispatched), producing
identical output to its JSON equivalent:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --json examples/student.yaml
```

Print an explained JSON result with rule provenance and host-code overrides:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --explain examples/student.json
```

Print the same explanation as Markdown prose:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --explain-text examples/student.json
```

Get counterfactual advice for the same student:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --advise examples/student.json
```

By default the advisor only proposes raising GCSEs the student already sat. Add `--all-gcses` to run
the heavier diagnostic search that may also propose sitting a brand-new GCSE — useful for working out
why a subject is reachable or unreachable. The same default can be flipped persistently via
`advice_considers_unsat_gcses` in `data/thresholds.yaml`; the flag overrides it for a single run.

```bash
dotnet run --project src/EnrolmentRules.Cli -- --advise --all-gcses examples/student.json
```

Statically lint the shipped workflows for structural faults (missing/duplicate tiers, tier
ordering, off-vocabulary field references, eligibility shape) — input-independent, so it catches a
typo before any student exercises it:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --lint-workflows
```

Pass a directory to lint a candidate workflow set before shipping it:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --lint-workflows path/to/workflows
```

Evaluate a JSONL batch with one shared stateless engine:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --batch examples/students.jsonl
```

Batch output is JSONL. Each output line includes the student id and either a successful
`result` or an isolated per-line `error`.

## Exit Codes

| Code | Meaning                                                             |
|-----:|---------------------------------------------------------------------|
|  `0` | Success.                                                            |
|  `2` | Usage error.                                                        |
|  `3` | Input or workflow loading error.                                    |
|  `4` | Single-student evaluation completed, but the student is ineligible. |
|  `5` | `--lint-workflows` found at least one error-severity finding.       |

## Using EnrolmentRules As A Library

The CLI is a thin shim over the same engine you can embed directly. Four packable library projects
ship from this solution:

| Package                                         | For                                                                                                                 |
|-------------------------------------------------|---------------------------------------------------------------------------------------------------------------------|
| `EnrolmentRules.Domain`                         | Immutable inputs/results, validation, catalogue and qualification-scale domain types.                               |
| `EnrolmentRules.Prediction`                     | GCSE averaging, DfE transition evidence, and A-level prediction.                                                    |
| `EnrolmentRules.Engine`                         | The pipeline façade, workflow bootstrap, constraints, aggregation, explanation, and advice.                         |
| `EnrolmentRules.Extensions.DependencyInjection` | `AddEnrolmentEngineAsync` / `AddEnrolmentEngine` registration for `Microsoft.Extensions.DependencyInjection` hosts. |

Build the engine **once** — `CreateAsync` runs the full startup recipe (schema-validating thresholds,
the qualification scale, catalogue, and workflows; loading the DfE transition matrix; then building
and probe-compiling the workflows) — then reuse it across students. It is stateless and safe to call
in parallel:

```csharp
using EnrolmentRules.Engine;

// Read the wall clock at this edge; the engine stays a pure function of (document, as-of date).
var asOf      = DateOnly.FromDateTime(DateTime.Today);
var enrolment = await EnrolmentEngine.CreateAsync("workflows/", "data/", asOf);

EnrolmentResult result    = await enrolment.EvaluateAsync(student);
ExplainedResult explained = await enrolment.ExplainAsync(student);
AdviceResult    advice    = await enrolment.AdviseAsync(student);
```

For HTTP or other request boundaries, prefer **`TryEvaluateAsync`** (and the matching `TryExplainAsync`
/ `TryAdviseAsync` overloads): invalid student documents return a structured `ValidationOutcome` instead
of throwing, so the host can map problems to 400 responses without a catch block.

```csharp
ValidatedEvaluation<EnrolmentResult> validated = await enrolment.TryEvaluateAsync(student);
if (!validated.Validation.IsValid)
{
    return Results.ValidationProblem(validated.Validation.Errors);
}

EnrolmentResult result = validated.Value!;
```

For a long-running host the reference date must not freeze at construction (a student's age would
go stale across midnight). Either bind a live source resolved per evaluation, or pin the date per
call:

```csharp
var live = await EnrolmentEngine.CreateAsync(
    "workflows/", "data/", () => DateOnly.FromDateTime(DateTime.Today));

EnrolmentResult atDate = await enrolment.EvaluateAsync(student, new DateOnly(2026, 1, 15));
```

Depend on **`IEnrolmentEngine`** (the evaluate/explain/advise surface plus the bound `Catalogue` and
`Scale`) rather than the concrete type to substitute a fake in your own tests. HTTP hosts can depend on
**`IEnrolmentEvaluator`** alone and reserve **`IEnrolmentAdvisor`** for diagnostic tooling — DI registers
the same singleton for all three. Every evaluation, explanation, advice, and bootstrap overload accepts
an optional `CancellationToken`.

#### Catalogue and scale snapshots

`EnrolmentEngine.CreateAsync` binds one immutable `CatalogueData` and `QualificationScale` for the
engine's lifetime. Every evaluation path threads those snapshots through prediction, the constraint
pass, and aggregation. If your host loads a custom `data/` tree (or boots via `IEnrolmentDataSource`),
**always** pass `enrolment.Catalogue` and `enrolment.Scale` — or the snapshots you passed to
`CreateAsync` — into boundary validation and any direct calls to `GradePredictor`, `ConstraintPass`,
`Aggregator`, `WorkflowLinter`, or `StudentValidator`. There are no implicit defaults on those APIs:
`Catalogue.Default` / `QualificationScale.Default` remain for the shipped reference tables only
(zero-wiring tests and CLI tooling).

```csharp
// Boundary validation must honour the same table the engine evaluates against.
var problems = StudentValidator.Validate(student, enrolment.Catalogue, enrolment.Scale);
```

For hosts that ship data as embedded resources or another non-filesystem source, implement
`IEnrolmentDataSource` and call `EnrolmentEngine.CreateAsync(source, ...)`. The bootstrapper consumes
fresh streams for workflows, all schemas and YAML tables, and the transition matrix, then disposes
them after constructing the immutable engine snapshot.

In a DI host, bootstrap asynchronously before building the container:

```csharp
using EnrolmentRules.Extensions.DependencyInjection;

await services.AddEnrolmentEngineAsync(options => {
    options.UseWorkflowsDirectory("workflows/");
    options.UseDataDirectory("data/");
    options.UseTimeProvider();        // live clock, resolved per evaluation; or UseFixedAsOf(date)
});
var provider = services.BuildServiceProvider();
```

If the host has already called `EnrolmentEngine.CreateAsync`, register the result with
`services.AddEnrolmentEngine(engine)` instead. Both paths serve the same stateless singleton through
`EnrolmentEngine` and `IEnrolmentEngine`.

Workflows and policy YAML are **editable without recompile**, but a running process keeps the snapshot
loaded at bootstrap until you reload. For hosts that edit `workflows/` or `data/` at runtime, register
`IEnrolmentEngineFactory` and call `ReloadAsync` after each coherent edit (the library does not watch
the filesystem — scheduling is the host's job). The DI package serves a stable interface proxy that
reads `Current` on each call, so existing `IEnrolmentEngine` / `IEnrolmentEvaluator` /
`IEnrolmentAdvisor` resolutions see the reloaded policy without rebuilding the container:

```csharp
await services.AddEnrolmentEngineFactoryAsync(options => {
    options.UseWorkflowsDirectory("workflows/");
    options.UseDataDirectory("data/");
});
var factory = provider.GetRequiredService<IEnrolmentEngineFactory>();
await factory.ReloadAsync(); // re-runs the full startup recipe; leaves Current unchanged on failure
```

#### Hosting Advise

`AdviseAsync` runs many full pipeline evaluations per subject (grade-cost search × counterfactual
swaps). Treat it as a **diagnostic** operation: never map it to a synchronous hot HTTP path, always
pass a `CancellationToken` linked to request abort, and rate-limit or queue it. Limits live in
`data/thresholds.yaml` (`advice_max_grade_cost`, `advice_max_subjects_changed`, optional
`advice_max_pipeline_evaluations`); when the pipeline cap is hit, `AdviceResult.TruncationReason` is
`"advice truncated"`. Prefer `IEnrolmentAdvisor` only in tools that need this surface.

For the per-request and batch costs this design delivers — and why `AdviseAsync` is the one call to
keep off a hot path — see the [performance & benchmarks note](docs/benchmarks.md).

## Deeper References

- [Guided walk-through](docs/walkthrough.md) explains the full pipeline from first principles.
- [Rule authoring guide](docs/rule-authoring.md) is the practical reference for changing workflows,
  catalogue data, thresholds, qualification scales, and tests.
- [Engine choice note](docs/engine-choice.md) explains why this project uses Microsoft RulesEngine
  rather than NRules/RETE.
- [Performance & benchmarks note](docs/benchmarks.md) records the per-request and batch costs that
  back the "high-performance, reusable library" design, with hosting guidance.

## Benchmarks

This project is built to be embedded as a **high-performance, stateless library**: construct the
engine once at startup, then reuse a single instance across requests — including concurrently. A
warm single-student evaluation costs roughly **13 µs / 59 KB** (all Gen0), and batch evaluation
scales linearly over the shared engine with no reuse overhead. The one exception is `AdviseAsync`
(counterfactual advice), which is orders of magnitude heavier and should be treated as an isolated,
rate-limited operation rather than a hot-path call.

The numbers, methodology, and hosting guidance are in the
[performance & benchmarks note](docs/benchmarks.md). Run the suite directly:

```bash
dotnet run -c Release --project src/EnrolmentRules.Benchmarks
```

## Development Notes

- Treat workflow YAML as executable policy, not static configuration. A rule is trusted only
  when a test drives it through the engine.
- No magic numbers: tunable policy lives in data (`data/thresholds.yaml` → `PolicyThresholds`,
  `data/catalogue.yaml` → `Catalogue`), and the values that stay in code are named in domain types
  such as `ALevelGrade` and the `Thresholds` scale invariants — never bare literals.
- Keep this project scoped to the `EnrolmentRules` directory. The parent repository contains
  unrelated sibling projects.

## Code Quality

The public API surface is designed to follow the
[Framework Design Guidelines — Essentials](../Framework_Design_Guidelines_Essentials.md) (a distilled
reference to Cwalina & Abrams, *Framework Design Guidelines*, 4th ed.): scenario-driven shapes,
consistent naming, the exception model, and the standard collection/async/dispose patterns.

These conventions are backed by three analyzer packages, run as part of the build (warnings are
errors), so drift is caught mechanically rather than by review alone:

- **Roslynator.Analyzers** — broad C# style and correctness analyzers.
  [NuGet](https://www.nuget.org/packages/Roslynator.Analyzers) ·
  [GitHub](https://github.com/dotnet/roslynator)
- **Microsoft.VisualStudio.Threading.Analyzers** — async/threading correctness (e.g. `ConfigureAwait`,
  sync-over-async).
  [NuGet](https://www.nuget.org/packages/Microsoft.VisualStudio.Threading.Analyzers) ·
  [GitHub](https://github.com/microsoft/vs-threading)
- **lookbusy1344.RecordValueAnalyser** — verifies that `record` types backed by reference members keep
  correct value-equality semantics.
  [NuGet](https://www.nuget.org/packages/lookbusy1344.RecordValueAnalyser) ·
  [GitHub](https://github.com/lookbusy1344/RecordValueAnalyser)

## License

MIT.
