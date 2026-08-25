# EnrolmentRules Technical Reference

This is a recreation of a proprietary project I developed a few years ago, to assist in enrolment decision-making and ensure policies were
consistently followed. The real system was also capable of writing the complete enrolment package into the management information system, and printing
forms for signature.

Here we show the core ideas in an open-source (AGPLv3 licence) project using modern idiomatic .NET 10. The code is designed as a library, so can be
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
binary.** Which subjects clash, what each subject needs as a prerequisite, what bars it, the priority
weights, the entry equivalents, the restudy bars — every individual rule of this kind lives in
`data/catalogue.yaml`, a deployable file loaded and schema-validated into an immutable startup snapshot
(`EnrolmentRules.Engine.Authoring.CatalogueStore`), with coverage and exclusion-symmetry enforced as load-time invariants. Adding,
removing, or retuning a clash pair, prerequisite, entry-equivalent, or restudy bar is a one-line
YAML edit, not a code change — as is editing the per-student rules in `workflows/` and the numeric
tuning knobs in `data/thresholds.yaml`.

What stays compiled is not a rule but the fixed, small catalogue of **relationship *types*** — the
shapes a rule can take (prerequisite, chosen-subject exclusion,
own-time, veto, restudy bar) and how their downgrades compose. That machinery is internal to the
engine and not part of the supported consumer surface.
These types are deliberately static: you touch them only to invent a *new kind* of relationship
that does not exist yet — a rare event, not something that changes within an academic year. Which
subjects relate, and how strictly, is everyday data.

The numeric **tuning knobs** the relationship code and the workflow lambdas read (pass grade,
entry thresholds, DfE-probability floors, the optional green cap, the amber score factor, ...) live in
`data/thresholds.yaml`, loaded and schema-validated at startup
(`PolicyThresholdsStore`); the qualification-grade scale for prior qualifications lives in
`data/qualifications.yaml` and is loaded by `QualificationScaleStore`; only the GCSE-scale
invariants (`Min`/`MaxGcseGrade`) remain compiled in `Thresholds` (`src/EnrolmentRules.Domain`),
since they define the grade scale rather than policy.

### Where Policy Lives

| If you are changing...                                                                           | Edit                       |
|--------------------------------------------------------------------------------------------------|----------------------------|
| Whole-student eligibility or a subject's green/amber/red base tier                               | Standard: `workflows/*.yaml`; auxiliary: `policies/<id>/workflows/*.yaml` |
| Numeric thresholds read by workflows or host code                                                | Standard: `data/thresholds.yaml`; auxiliary: `policies/<id>/data/thresholds.yaml` |
| Qualification grade ordering and A-level-points equivalence                                      | `data/qualifications.yaml` |
| Subject relationships, priority weights, regression coefficients, entry equivalents, or restudy bars | Standard: `data/catalogue.yaml`; auxiliary: `policies/<id>/data/catalogue.yaml` |
| A new relationship *type* or scale invariant                                                     | compiled C# in `src/`      |

Routine policy changes are YAML changes. C# changes are reserved for new evaluator shapes, such as
introducing a relationship type that does not already exist.

For the full rule-location matrix and authoring workflow, see
[the rule-authoring guide](rule-authoring.md). For a field-by-field map of the editable YAML
and JSON surfaces, see [the configuration reference](configuration-reference.md). New to the
project or to Microsoft RulesEngine?
Start with the [guided walk-through](walkthrough.md).

## Repository Layout

| Path                                                | Purpose                                                                                                                                                                                 |
|-----------------------------------------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `src/EnrolmentRules.Domain`                         | Immutable domain inputs, outputs, subject/rating vocabulary, thresholds, the `Catalogue` (loads/validates `data/catalogue.yaml`), and JSON source generation.                           |
| `src/EnrolmentRules.Prediction`                     | GCSE averaging and linear prediction from raw student facts to `StudentProfile`.                                                                                                        |
| `src/EnrolmentRules.Engine`                         | The `EnrolmentEngine` facade and the mainline evaluation contracts (`IEnrolmentEngine`, `IEnrolmentEvaluator`, `IEnrolmentAdvisor`). Supporting bootstrap and authoring APIs live in explicit `Hosting` / `Authoring` namespaces. |
| `src/EnrolmentRules.Extensions.DependencyInjection` | `AddEnrolmentEngineFactory` / `AddEnrolmentEngine` / `AddEnrolmentPolicies` registration helpers for `Microsoft.Extensions.DependencyInjection` hosts.                                |
| `src/EnrolmentRules.Cli`                            | `enrolment` executable and table/JSON/batch command line modes.                                                                                                                         |
| `src/EnrolmentRules.Web`                            | Vue reference web front-end: `localStorage`-backed anonymous facts editing, live re-explanation, no database, no server-side session. See [Web Interface](#web-interface) below. |
| `src/EnrolmentRules.Benchmarks`                     | BenchmarkDotNet harness for engine throughput.                                                                                                                                          |
| `tests/EnrolmentRules.Tests`                        | xUnit and Awesome Assertions coverage, including engine-driven rule tests, invariants, and golden files.                                                                                |
| `tests/EnrolmentRules.TestProcessHost`              | Out-of-process fixture exercised by the CLI/process tests.                                                                                                                              |
| `tests/EnrolmentRules.Web.Tests`                    | `WebApplicationFactory`-driven integration tests for the web front-end (cookie/localStorage round-trip, form posts, rendered explanations).                                            |
| `workflows`                                         | Standard policy's RulesEngine workflow YAML and its schema.                                                                                                                             |
| `policies/<id>/workflows`, `policies/<id>/data`     | An auxiliary policy's own workflows/catalogue/thresholds, layered over the shared `data/` schemas and qualification scale by `OverlayEnrolmentDataSource`. `policies/elite/` ships as the worked example — see [Policy registry](#policy-registry). |
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

**A committed choice may never be red.** A choice is only ever made against a subject rated green or
amber, so a red one means the document is self-inconsistent: the facts moved underneath the choice
after it was made — typically the GCSE grades were lowered, but a new blocking hobby, a prior
qualification, or a sibling choice that excludes it will do the same. There is no defensible verdict
to return for such a document, so `EvaluateValidated` and `ExplainValidated` **reject it outright** —
`Validation.Errors` names each offending entry with the reason it went red, and `Value` is null. Amber
choices are unaffected: only red is rejected, which matches the rule the front ends apply when
offering a Choose button. `AdviseValidated` is deliberately exempt: it answers "what would have to change",
which is exactly what a student holding a red choice needs, and advising returns counterfactuals
rather than accepting anything.
The check is not part of `StudentValidator` because a rating exists only *after* prediction,
the engine and the constraint pass have run, and the constraint pass reads `chosen_a_levels` as an
input; it is a post-run check reported as input validation.

A single-policy caller holding a mutable basket can avoid surfacing that rejection to the student by
calling `IEnrolmentEvaluator.StaleChoices(student)`, dropping exactly those subjects, and
re-evaluating. One pass always suffices: dropping choices only ever removes downgrades, so nothing
left in the basket can newly turn red. The shipped web front ends instead preserve one basket across
multiple policies and call `IEnrolmentPolicyRegistry.Compare`; a red or not-offered choice remains
visible with an `Unavailable` or `NotOffered` status.

The unchecked `Evaluate`/`Explain` paths are unaffected — they still rate a red chosen subject red
and return a result. Rejection is a validated-boundary concern, not a pipeline one.
`prior_qualifications` carries typed prior study separately from GCSEs. Each entry is validated
against `data/qualifications.yaml`, which defines the grade ordering and A-level-points
equivalence for each qualification type/grade pair. The list is used upstream to open
alternative entry paths and raise predicted points, and downstream to enforce restudy bars.
`date_of_birth` is a required ISO date (`YYYY-MM-DD`). Age is **not** stored on the document — it
is not a pure function of the input (it depends on *when* the student is assessed), so the engine
derives a whole-years age from the date of birth as of a per-run **reference ("as-of") date**. The
CLI uses the current local date; tests and golden files pin a fixed date so age-dependent outcomes
stay deterministic. The wall clock is read only at the CLI edge, never inside the engine. A 29
February birthday has no anniversary in a non-leap year; following UK legal convention the engine
treats 1 March (not 28 February) as that year's anniversary, so the student ages up on 1 March.
YAML input applies to the single-student modes only; `--batch` remains JSONL.

### Custom A-level subjects

A-level subject identity is **data-driven**: a subject is a validated snake_case value object loaded
from the catalogue. Adding a new A-level subject means:

1. add its row to `data/catalogue.yaml` (priority weight, regression coefficients, and any exclusions /
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
The base prediction runs the per-subject regression on the **average** GCSE, but a standout grade in
the subject's own cognate GCSE overrides it: the same regression line is fed the individual grade and
the higher of the two wins (`max`), so a strong individual result lifts that subject without dragging
prediction down when the grade is weaker than the average. DfE evidence stays average-based.

Two distinct mechanisms decide a subject's rating; keep them apart:

1. **Base tier** — a RulesEngine workflow picks green/amber/red by a first-hit-wins scan (green
   criteria, else amber, else red). This stage *is* an ordered cascade.
2. **Cross-subject constraints** — prerequisites, chosen-subject exclusions,
   own-time requirements, per-subject vetoes, and restudy bars. Each only ever moves a rating toward
   red, so *applying* them commutes and folds in by most-severe-wins, order-independently.
   *Evaluation*, though, runs in two phases: the single-subject and choice-activated constraints (veto,
   restudy bar, exclusions, own-time) read the base tier, then prerequisites read the ratings *after*
   those downgrades — so a dependency a sibling constraint drove to red no longer satisfies a
   `qualifying` prerequisite. That order is fixed but acyclic (prerequisites never feed the other
   constraints), not a fixpoint iteration. Within the prerequisite phase, a chain of dependent
   subjects (A requires B requires C) is itself resolved in dependency order — leaves first — so a
   downgrade at the bottom of a chain propagates through every dependant above it, not just the one
   directly above. This is a load-time invariant, not an assumption: `CatalogueData` rejects a
   self-prerequisite, a two-node cycle, or a longer one at construction, so the dependency-order walk
   always terminates.
3. **Programme-size caps** — the whole-student chosen-subject cap runs after the constraint fold:
   once the student has already committed to the maximum allowed programme size, every further unchosen
   green/amber subject is forced red. The optional green cap, when enabled, runs after that and counts
   only greens that survived the earlier downgrades. Both are downgrade-only, but they are distinct,
   ordered aggregation phases rather than commuting base-rating constraints.

The precedence table in the walk-through reads as one top-down ladder, but that is the *net* result
after the fold, not the execution order: the base tier is emitted first, then every downgrade is
folded in. Promotion never happens.

For the detailed signal table, precedence table, subject thresholds, prior-qualification mechanics,
and prerequisite semantics, see [the guided walk-through](walkthrough.md) and
[the rule-authoring guide](rule-authoring.md).

### Example Workflow

This is the shape of a real authored workflow file in `workflows/`:

```yaml
WorkflowName: 'subject-ratings'
Rules:
  - RuleName: 'english_literature:green'
    SuccessEvent: 'Entry met (an English GCSE at standard entry); predicted A-level grade at or above the green threshold'
    Expression: >-
      (facts.Gcse("english_literature") >= facts.StandardEntry ||
       facts.Gcse("english_language") >= facts.StandardEntry) &&
      facts.Predicted("english_literature") >= ALevelGrade.D &&
      facts.DfeProbabilityAtOrAbove("english_literature", ALevelGrade.D)
        >= facts.MinDfeGreenProbabilityAtOrAbove

  - RuleName: 'english_literature:amber'
    SuccessEvent: 'Entry met (an English GCSE at standard entry); predicted A-level grade at or above the amber threshold'
    Expression: >-
      (facts.Gcse("english_literature") >= facts.StandardEntry ||
       facts.Gcse("english_language") >= facts.StandardEntry) &&
      facts.Predicted("english_literature") >= ALevelGrade.E &&
      facts.DfeProbabilityAtOrAbove("english_literature", ALevelGrade.E)
        >= facts.MinDfeAmberProbabilityAtOrAbove

  - RuleName: 'english_literature:red'
    SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
    Expression: >-
      true
```

Entry bars are deliberately lenient — a subject's own cognate GCSE at `facts.StandardEntry`
(grade 5) is broadly enough to enrol — and the rating tiers are generous: a predicted **D**
(`ALevelGrade.D`) clears green and a predicted **E** clears amber. Two subjects are exceptions:
Maths and Physics gate hard on `facts.Gcse("maths") >= facts.ExceptionalEntry` (a top grade,
shipped as 8), ANDed with the same regression tiers.

The loader supplies `RuleExpressionType = LambdaExpression` automatically for actual
rules, so the YAML stays focused on the business logic.

Entry thresholds can branch on derived per-student signals. Art's GCSE bar is age-gated entirely
in the YAML — host code exposes `facts.Age` (derived from `date_of_birth`) and the loaded entry bands
(`facts.TopEntry`, `facts.StandardEntry`); the branching policy lives in the rule expression (adults —
age `>= 19`, a one-off literal — face the higher `facts.TopEntry` bar, under-adults the accessible
`facts.StandardEntry`):

```yaml
  - RuleName: 'art:green'
    SuccessEvent: 'Entry met (age-gated GCSE threshold); predicted A-level grade at or above the green threshold'
    Expression: >-
      facts.Gcse("art") >= (facts.Age >= 19 ? facts.TopEntry : facts.StandardEntry) &&
      facts.Predicted("art") >= ALevelGrade.D &&
      facts.DfeProbabilityAtOrAbove("art", ALevelGrade.D) >= facts.MinDfeGreenProbabilityAtOrAbove
```

### Workflow Authoring

- Edit the YAML files in `workflows/` rather than the normalized engine payload.
- Keep lambda expressions as the rule bodies; the loader injects the boilerplate `RuleExpressionType`.
- Startup still validates the workflow shape and probe-compiles every rule.
- Any workflow change should keep the same build, format, and test gate green.

For the evaluation stages and shipped rule patterns, see the [guided walk-through](walkthrough.md);
for the full YAML-editing reference and every datum a rule can read, the
[rule-authoring guide](rule-authoring.md).

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

Select a non-default policy with the global `--policy` option — accepted anywhere in the argument
list, alongside any of the modes above:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --policy elite --table examples/student.json
```

Omitting `--policy` runs against `standard`. An unrecognised id is a usage error (exit `2`); see
[Policy registry](#policy-registry) for the full set of registered ids and how a host adds its own.

### Subject criteria (`--criteria`)

Print what one subject requires of *anyone*, in plain English, with no student document involved:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --criteria music
```

The output is Markdown: what the three traffic lights mean, the college-wide eligibility gate, the
subject's green and amber criteria, any prior-qualification equivalents or restudy bars, and the
cross-subject relationships (prerequisites, timetable clashes, own-time requirements, vetoes) that can
downgrade it.

Every bullet is **narrated from the loaded rules** — the workflow expressions, `data/catalogue.yaml`
and `data/thresholds.yaml` — not written alongside them. Retune `standard_entry` and the wording
follows; there is no second prospectus to keep in step. `ExpressionNarrator` covers the expression
grammar the workflows use and *throws* on anything else, so a rule it cannot explain fails the test
suite rather than being silently dropped from what a student reads. Adding a genuinely new expression
shape to a workflow therefore means teaching the narrator that shape — see
`ExpressionNarratorTests.every_expression_in_the_shipped_workflows_narrates`, the guard that catches it.

An unknown subject exits `3` and lists the catalogued subjects on stderr.

The same output is available to hosts through `IEnrolmentCriteriaExplainer.Describe(Subject)`, which
`IEnrolmentEngine` inherits. Neither web front end surfaces it yet.

By default the advisor only proposes raising GCSEs the student already sat. Add `--all-gcses` to run
the heavier diagnostic search that may also propose sitting a brand-new GCSE — useful for working out
why a subject is reachable or unreachable. The same default can be flipped persistently via
`advice_considers_unsat_gcses` in `data/thresholds.yaml`; the flag overrides it for a single run.

```bash
dotnet run --project src/EnrolmentRules.Cli -- --advise --all-gcses examples/student.json
```

Statically lint the shipped workflows for structural faults (missing/duplicate tiers, tier
ordering, off-vocabulary field references, eligibility shape) — input-independent, so it catches a
typo before any student exercises it. The same lint runs inside `Create`/`Reload`, so a
misordered or off-vocabulary workflow fails startup; `--lint-workflows` is the cheaper authoring
pre-check that surfaces the identical findings without booting an engine:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --lint-workflows
```

Pass a directory to lint a candidate workflow set before shipping it:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --lint-workflows path/to/workflows
```

Report the build stamp — the version and the git commit the binaries were built from, so a decision can
be traced back to the exact build that produced it. The commit is stamped into `InformationalVersion`
at build time (the `StampGitCommit` target in `Directory.Build.props`), carries a `-dirty` suffix when
built from a working tree with uncommitted changes, and degrades to `unknown` when built from a source
drop with no git checkout. The web front-end shows the same stamp in its footer, linked to the commit
on GitHub.

```bash
dotnet run --project src/EnrolmentRules.Cli -- --version
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

## Web Interface

`src/EnrolmentRules.Web` is a small ASP.NET Core web host demonstrating the "staff-facing
website" integration path from [Designed For Integration](../README.md#designed-for-integration).
It serves a Vue 3 workflow at `/app` (also mapped to `/`, so there is no landing redirect). It is
a reference host, not a packable library project:

- **No accounts, no database, no server-side session.** Browser `localStorage` is the one
  editable-facts store (key `enrolmentRules.vue.snapshot.v1`, read/written by
  `ClientApp/src/state/localStorageSnapshot.ts`); its versioned snapshot preserves facts and
  choices when migrating the pre-policy v1 format, then resolves the saved policy against the
  server-provided registry rather than trusting stale client data. Vue reads/writes it directly.
- Vue evaluations re-run through `POST /api/enrolment/evaluate`, so the rendered result is always a
  fresh comparison, never a cached one.
- Client-side vocabulary (GCSE subject keys, prior-qualification subjects/types, hobby tags) comes
  from the same catalogue/scale the engine is bound to (`GcseSubjects.Known`,
  `IEnrolmentEngine.Catalogue`) — the web layer holds no parallel copy of policy data.
- `/app` accepts `?policy=<id>` (see [Policy registry](#policy-registry)) — `standard` and `elite`
  are registered in `Program.cs`. A chosen subject the selected policy now rates red, or does not
  offer at all, is never dropped from the basket; it is flagged `Unavailable`/`Not offered`
  instead, and switching policy re-evaluates the same facts and basket rather than resetting them.
- Static assets (Bootstrap 5) are restored via [libman](https://github.com/aspnet/LibraryManager)
  on every `dotnet build` (`Microsoft.Web.LibraryManager.Build`, driven by `libman.json`) into
  `wwwroot/lib/`, which is gitignored — nothing vendored is committed.
- The Vue bundle is generated from `src/EnrolmentRules.Web/ClientApp/` into `wwwroot/app/`, which
  is also gitignored. Do not check in `wwwroot/app`; commit the TypeScript/Vue source and
  `pnpm-lock.yaml`. `dotnet build` runs the ClientApp build before ASP.NET static-asset discovery,
  and `dotnet publish` includes the generated manifest and hashed assets.

### Web build and verification scripts

Build just the generated Vue assets:

```bash
./scripts/build-clientapp.sh
```

Run the focused web gate, including the Vue build, ASP.NET web integration tests, frontend verify,
and Playwright e2e tests:

```bash
./scripts/verify-web.sh
```

For ordinary solution-wide commits, still use the root commit gate. For changes under
`src/EnrolmentRules.Web/ClientApp/`, `pnpm verify` remains the frontend unit/static gate; `pnpm e2e`
or `./scripts/verify-web.sh` is the browser workflow gate.

### Running it locally

```bash
dotnet run --project src/EnrolmentRules.Web
```

works, and opens the Vue front-end at <http://localhost:5299/app>. ASP.NET Core would otherwise
resolve the content root to the **project source directory**, where the relative `workflows/` and
`data/` paths and the `policies/` overlays the app needs don't exist (they're only in the build
output, copied in by the same
`<Content Include>` pattern the CLI project uses). The `EnrolmentRules.Web` profile in
`Properties/launchSettings.json` fixes that with `ASPNETCORE_CONTENTROOT="."`: a *relative* content
root is resolved against the app's base directory — i.e. the build output — so it holds for
`dotnet run`, for the built executable, and for Debug and Release alike. Its `launchUrl` is `app`,
so Rider, Visual Studio and `dotnet run` all land on the Vue interface.

**Rider:** the checked-in `.NET Project` run configuration `EnrolmentRules.Web` launches the built
executable from `bin/Debug/net10.0` with that directory as the working directory (so the content
root is right without any environment variable) and opens `/app` in the browser. Run or Debug it
directly — breakpoints in C# work, and the `BuildClientApp` MSBuild target rebuilds the Vue bundle
as part of the normal build.

For a watching dev loop instead, build and run the executable from its output directory:

```bash
./scripts/run-web.sh
```

The script runs two watchers side by side: `dotnet watch run` for the C# side, and Vite's own
watch build (`pnpm build:watch`) for the Vue side. It polls `http://localhost:5299/app` and opens the
page once the server answers; `dotnet watch`'s own browser launcher is suppressed so a future logging
change cannot open a duplicate tab. `ASPNETCORE_WEBROOT` points at the source `wwwroot` so
Vite's output is served the moment it lands, and `ViteManifestReader` re-reads `manifest.json` per
request — so a Vue edit needs no dotnet restart, just a browser refresh (F5). There is no HMR; that
is the cost of serving the real ASP.NET host rather than the Vite dev server.

Without the watchers, it is equivalent to:

```bash
dotnet build src/EnrolmentRules.Web/EnrolmentRules.Web.csproj
cd src/EnrolmentRules.Web/bin/Debug/net10.0
ASPNETCORE_URLS="http://localhost:5299" ASPNETCORE_ENVIRONMENT=Development ./EnrolmentRules.Web
```

Then open `http://localhost:5299/`. Stop it with `Ctrl+C` (or `pkill -f EnrolmentRules.Web` if it
was started in the background).

For debugging in Rider, use the committed `EnrolmentRules.Web` run configuration (under
`.idea/.idea.EnrolmentRules/.idea/runConfigurations/`) — it already sets the working directory,
port, and `ASPNETCORE_ENVIRONMENT` correctly. `Properties/launchSettings.json` provides the same
port/environment for `dotnet run`-based tooling, but doesn't fix the content-root issue above on
its own.

## Using EnrolmentRules As A Library

The CLI is a thin shim over the same engine you can embed directly. Four packable library projects
ship from this solution:

| Package                                         | For                                                                                                                 |
|-------------------------------------------------|---------------------------------------------------------------------------------------------------------------------|
| `EnrolmentRules.Domain`                         | Immutable inputs/results, validation, catalogue and qualification-scale domain types.                               |
| `EnrolmentRules.Prediction`                     | GCSE averaging, DfE transition evidence, and A-level prediction.                                                    |
| `EnrolmentRules.Engine`                         | The pipeline façade, workflow bootstrap, constraints, aggregation, explanation, and advice.                         |
| `EnrolmentRules.Extensions.DependencyInjection` | `AddEnrolmentEngineFactory` / `AddEnrolmentEngine` / `AddEnrolmentPolicies` registration for `Microsoft.Extensions.DependencyInjection` hosts. |

Build the engine **once** — `Create` runs the full startup recipe (schema-validating thresholds,
the qualification scale, catalogue, and workflows; loading and validating the DfE transition matrix
— header shape, finite in-range probabilities, per-row totals, and no duplicate subject/band rows;
semantically linting the workflows; then building and probe-compiling them) — then reuse it across
students. Any error-severity lint finding or malformed matrix fails the build with an
`EnrolmentDataException`/`WorkflowException` rather than publishing a degraded engine. It is
stateless and safe to call in parallel:

```csharp
using EnrolmentRules.Engine;

// Read the wall clock at this edge; the engine stays a pure function of (document, as-of date).
var asOf      = DateOnly.FromDateTime(DateTime.Today);
var enrolment = EnrolmentEngine.Create("workflows/", "data/", asOf);

EnrolmentResult result    = enrolment.Evaluate(student);
ExplainedResult explained = enrolment.Explain(student);
AdviceResult    advice    = enrolment.Advise(student);
```

For HTTP or other request boundaries, prefer **`EvaluateValidated`** (and the matching `ExplainValidated`
/ `AdviseValidated` overloads): invalid student documents return a structured `ValidationOutcome` instead
of throwing, so the host can map problems to 400 responses without a catch block. This includes a null
`StudentInput`, reported as `student is required`; the unchecked `Evaluate`/`Explain`/`Advise`
overloads instead throw `ArgumentNullException` for null at their public boundary.

`EvaluateValidated` and `ExplainValidated` also reject a document naming a `chosen_a_levels` entry the pipeline
now rates red (see [Student Input](#student-input)); `AdviseValidated` does not. A host with an editable
basket should call `StaleChoices` first and drop what it returns, rather than presenting the
rejection to the user. A host juggling one basket across several policies (see
[Policy registry](#policy-registry)) should call `IEnrolmentPolicyRegistry.Compare` instead — it
never rejects on a red or unrecognised choice, it classifies it, so a basket that is valid under one
policy is never simply refused when viewed under a stricter one.

```csharp
ValidatedEvaluation<EnrolmentResult> validated = enrolment.EvaluateValidated(student);
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
var live = EnrolmentEngine.Create(
    "workflows/", "data/", () => DateOnly.FromDateTime(DateTime.Today));

EnrolmentResult atDate = enrolment.Evaluate(student, new DateOnly(2026, 1, 15));
```

Depend on **`IEnrolmentEngine`** (the evaluate/explain/advise surface plus the bound `Catalogue` and
`Scale`) rather than the concrete type to substitute a fake in your own tests. HTTP hosts can depend on
**`IEnrolmentEvaluator`** alone and reserve **`IEnrolmentAdvisor`** for diagnostic tooling — DI registers
the same singleton for all three. Every evaluation, explanation, advice, and bootstrap overload accepts
an optional `CancellationToken`.

#### Catalogue and scale snapshots

`EnrolmentEngine.Create` binds one immutable `CatalogueData` and `QualificationScale` for the
engine's lifetime. Every evaluation path threads those snapshots through prediction and the
internal constraint/aggregation machinery. If your host loads a custom `data/` tree (or boots via
`EnrolmentRules.Engine.Hosting.IEnrolmentDataSource`), **always** pass `enrolment.Catalogue` and
`enrolment.Scale` — or the snapshots you passed to `Create` — into boundary validation and any
direct calls to `GradePredictor`, `WorkflowLinter`, or `StudentValidator`. There are no implicit
defaults on those APIs: `Catalogue.Default` / `QualificationScale.Default` remain for the shipped
reference tables only (zero-wiring tests and CLI tooling).

```csharp
// Boundary validation must honour the same table the engine evaluates against.
var problems = StudentValidator.Validate(student, enrolment.Catalogue, enrolment.Scale);
```

For hosts that ship data as embedded resources or another non-filesystem source, implement
`EnrolmentRules.Engine.Hosting.IEnrolmentDataSource` and call `EnrolmentEngine.Create(source, ...)`.
The bootstrapper consumes
fresh streams for workflows, all schemas and YAML tables, and the transition matrix, then disposes
them after constructing the immutable engine snapshot.

In a DI host, bootstrap before building the container:

```csharp
using EnrolmentRules.Extensions.DependencyInjection;

services.AddEnrolmentEngine(options => {
    options.UseWorkflowsDirectory("workflows/");
    options.UseDataDirectory("data/");
    options.UseTimeProvider();        // live clock, resolved per evaluation; or UseFixedAsOf(date)
});
var provider = services.BuildServiceProvider();
```

If the host has already called `EnrolmentEngine.Create`, register the result with
`services.AddEnrolmentEngine(engine)` instead. Both paths serve the same stateless singleton through
`EnrolmentEngine` and `IEnrolmentEngine`.

Workflows and policy YAML are **editable without recompile**, but a running process keeps the snapshot
loaded at bootstrap until you reload. For hosts that edit `workflows/` or `data/` at runtime, register
`IEnrolmentEngineFactory` and call `Reload` after each coherent edit (the library does not watch
the filesystem — scheduling is the host's job). The DI package serves a stable interface proxy that
reads `Current` on each call, so existing `IEnrolmentEngine` / `IEnrolmentEvaluator` /
`IEnrolmentAdvisor` resolutions see the reloaded policy without rebuilding the container:

```csharp
services.AddEnrolmentEngineFactory(options => {
    options.UseWorkflowsDirectory("workflows/");
    options.UseDataDirectory("data/");
});
var factory = provider.GetRequiredService<IEnrolmentEngineFactory>();
factory.Reload(); // re-runs the full startup recipe; leaves Current unchanged on failure
```

## Policy registry

Everything above bootstraps **one** engine bound to one policy's `workflows/`/`data/` tree. A host
that must offer more than one policy at once — e.g. a stricter, smaller-cohort programme alongside
the standard one — registers an immutable **`IEnrolmentPolicyRegistry`** instead of a single engine.
There is no mutable "current policy": every call names the policy id it wants, and the registry
resolves it to the matching pre-built `IEnrolmentEngine`.

```csharp
using EnrolmentRules.Extensions.DependencyInjection;

services.AddEnrolmentPolicies(options => {
    var baseSource = new DirectoryDataSource("workflows/", "data/");
    options.UseDefault("standard", "Standard", baseSource);
    options.Add("elite", "Elite",
        new OverlayEnrolmentDataSource(new DirectoryDataSource("policies/elite/workflows", "policies/elite/data"), baseSource));
    options.UseTimeProvider();
});
```

Each `Add`/`UseDefault` call builds and startup-validates its own engine eagerly — a broken
auxiliary policy fails the host at startup, the same guarantee `EnrolmentEngine.Create` gives a
single policy. `EnrolmentPolicyId` is a validated lowercase-slug value type (`[a-z][a-z0-9-]*`);
`Descriptors` exposes an immutable snapshot of every registered `{Id, DisplayName}` pair for a
client to render a picker from, and `DefaultPolicyId` is the id `UseDefault` registered.
`GetPolicy`/`TryGetPolicy` resolve an
id to its `EnrolmentPolicy` (`{Descriptor, Engine}`); an unknown id throws
`UnknownEnrolmentPolicyException` (`GetPolicy`) or returns `false` (`TryGetPolicy`) rather than
falling back silently.

`OverlayEnrolmentDataSource` is the layering an auxiliary policy typically wants: it reads
`workflows/`, `catalogue.yaml`, and `thresholds.yaml` from the auxiliary policy's own directory, but
falls through to the base source for everything else (schemas, `data/qualifications.yaml`, the DfE
transition matrix) — an auxiliary policy overrides its rules and thresholds without duplicating the
machinery every policy shares. A policy that needs nothing shared can just point two independent
`DirectoryDataSource`s at two independent trees instead.

### Non-destructive comparison

Every strict API on `IEnrolmentEvaluator` (`EvaluateValidated`, `ExplainValidated`,
`ValidateFinalProgramme`) still rejects a `chosen_a_levels` entry the pipeline now rates red — that
invariant is unweakened for any individual policy. But a host juggling **one shared basket across
several policies** cannot apply that rule directly: switching from a lenient policy to a stricter
one would otherwise reject the student's own facts outright. `IEnrolmentPolicyRegistry.Compare`
exists for exactly that case — it evaluates the same document against a named policy and classifies
every chosen subject instead of rejecting the call:

```csharp
ValidatedEvaluation<PolicyComparisonResult> comparison =
    registry.Compare(new EnrolmentPolicyId("elite"), student, cancellationToken);
// comparison.Value?.ChoiceStatuses: one ChosenSubjectStatus per chosen_a_levels entry —
//   Available   — offered by this policy and currently green or amber
//   Unavailable — offered by this policy, but currently red
//   NotOffered  — absent from this policy's catalogue entirely
```

`Compare` never mutates the input and never drops a choice — a red or not-offered subject stays
represented, annotated with why, so a caller with an editable basket (the web front end) can show
the student exactly what changed rather than silently losing their selection. `PolicyComparisonResult`
also carries the resolved `Descriptor`, the full `Explanation` (`ExplainedResult`), and the policy's
effective `MinChosenALevels`/`MaxChosenALevels` for that student. The strict, single-policy APIs are
what a final "commit this enrolment" action should call; `Compare` is for the browsing/comparison
experience that leads up to it.

### CLI and Vue selection

Both shipped hosts resolve a policy id the same way — a query/argument the caller supplies,
falling back to the registry's own default, never a silent substitution on an *invalid* id:

- **CLI**: the global `--policy <id>` option (see [Command Line Examples](#command-line-examples));
  an unknown id is a usage error, exit code `2`.
- **Web API**: `GET /api/enrolment/options?policy=<id>` and
  `POST /api/enrolment/evaluate?policy=<id>`; omitted defaults to the registry default, an unknown id
  is `400 Bad Request` with an `application/problem+json` Problem Details body. The options
  response's `selectedPolicy`/`availablePolicies` fields are the client's own source of truth for
  which ids exist — never hard-code them. The client rejects malformed or contradictory registry
  metadata rather than guessing a policy.
- **Vue** (`/app?policy=<id>`): the precedence is URL, then the policy id last saved to
  `localStorage`, then the server default — resolved client-side by retrying
  `GET /api/enrolment/options` down that candidate list until one is accepted, so a `400` for a bad
  id just advances to the next candidate rather than needing a dedicated pre-flight request.
  `App.vue` shows the current policy's display name and a "Switch to *Other*" link next to the
  hero, and switching re-runs `Compare` against the same stored facts and basket — nothing is
  cleared, and a chosen subject the new policy rates red or doesn't offer is flagged rather than
  ejected (`ChoiceStatus` rendered on `ChosenBasket.vue`).

Building a second worked example of an auxiliary policy from scratch — the workflow/catalogue/data
shape an overlay needs, and the boundary decisions to pin before writing it — is covered in
[Authoring an auxiliary policy](rule-authoring.md#9-authoring-an-auxiliary-policy).

#### Hosting Advise

`Advise` runs many full pipeline evaluations per subject (grade-cost search × counterfactual
swaps). Treat it as a **diagnostic** operation: never map it to a synchronous hot HTTP path, always
pass a `CancellationToken` linked to request abort, and rate-limit or queue it. Limits live in
`data/thresholds.yaml` (`advice_max_grade_cost`, `advice_max_subjects_changed`, optional
`advice_max_pipeline_evaluations`); when the pipeline cap is hit, `AdviceResult.TruncationReason` is
`"advice truncated"`. Prefer `IEnrolmentAdvisor` only in tools that need this surface.

For the per-request and batch costs this design delivers — and why `Advise` is the one call to
keep off a hot path — see the [performance & benchmarks note](benchmarks.md). For why the engine's
API is synchronous despite that cost, and a worked example of offloading a slow `Advise` call from
an ASP.NET request thread with `Task.Run`, see the
[async vs. synchronous note](async-vs-sync.md#calling-a-slow-path-from-an-aspnet-action-without-tying-up-the-request-thread).

## Deeper References

- [Guided walk-through](walkthrough.md) explains the full pipeline from first principles.
- [Architecture layers](architecture-layers.md) breaks the app down layer by layer, listing the
  files behind each.
- [Rule authoring guide](rule-authoring.md) is the practical reference for changing workflows,
  catalogue data, thresholds, qualification scales, and tests.
- [Engine choice note](engine-choice.md) explains why this project uses Microsoft RulesEngine
  rather than NRules/RETE.
- [Performance & benchmarks note](benchmarks.md) records the per-request and batch costs that
  back the "high-performance, reusable library" design, with hosting guidance.
- [Async vs. synchronous note](async-vs-sync.md) explains why the evaluation path is synchronous
  end to end, and how to keep a slow `Advise` call off an ASP.NET request thread.

## Benchmarks

This project is built to be embedded as a **high-performance, stateless library**: construct the
engine once at startup, then reuse a single instance across requests — including concurrently. A
warm single-student evaluation costs roughly **13 µs / 59 KB** (all Gen0), and batch evaluation
scales linearly over the shared engine with no reuse overhead. The one exception is `Advise`
(counterfactual advice), which is orders of magnitude heavier and should be treated as an isolated,
rate-limited operation rather than a hot-path call. The focused Elite near-miss benchmark is much
narrower at roughly **2.3 ms / 5.77 MB**, but it does not replace the worst-case advice measurement.

The numbers, methodology, and hosting guidance are in the
[performance & benchmarks note](benchmarks.md). Run the suite directly:

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
[Framework Design Guidelines — Essentials](../../Framework_Design_Guidelines_Essentials.md) (a distilled
reference to Cwalina & Abrams, *Framework Design Guidelines*, 4th ed.): scenario-driven shapes,
consistent naming, the exception model, and the standard collection/async/dispose patterns. The
[API surface specification](design/2026-07-03-framework-design-guidelines-api-spec.md) is the
single authoritative inventory of every exported type, reflection-locked by
`PublicApiSurfaceTests`; this document does not repeat it.

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

AGPLv3. See [LICENSE](../LICENSE) and [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).
