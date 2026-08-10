# Architecture — the layers, in order

A student's facts pass through a fixed sequence of layers, each owned by one project (or one file
group). This page walks that sequence and links the files that implement each step. For the *why*
behind the split, see the [technical reference](technical-reference.md#architecture-at-a-glance)
and the [guided walk-through](walkthrough.md#3-the-two-layer-architecture); this page is the
*what's where* companion to those.

## 0. Rules-as-data — the policy itself

Not compiled code — YAML, loaded and schema-validated at startup. Editing these files *is* how a
policy change ships.

| File | Holds |
|---|---|
| [`workflows/eligibility.yaml`](../workflows/eligibility.yaml) | The whole-student entry gate (a RulesEngine workflow). |
| [`workflows/subject-ratings.yaml`](../workflows/subject-ratings.yaml) | Per-subject green/amber/red tier rules (one RulesEngine workflow). |
| [`workflows/workflow.schema.json`](../workflows/workflow.schema.json) | JSON Schema the two workflow files are validated against at load. |
| [`data/catalogue.yaml`](../data/catalogue.yaml) | Cross-subject relationships: priority weights, prerequisites, exclusion pairs, entry equivalents, restudy bars, regression coefficients. |
| [`data/thresholds.yaml`](../data/thresholds.yaml) | Numeric tuning knobs the workflow lambdas and host code read (pass grade, entry bars, DfE-probability floors, amber score factor, the optional green cap...). |
| [`data/qualifications.yaml`](../data/qualifications.yaml) | The qualification-grade scale used to compare prior qualifications against GCSE/A-level grades. |
| [`data/catalogue.schema.json`](../data/catalogue.schema.json), [`data/thresholds.schema.json`](../data/thresholds.schema.json), [`data/qualifications.schema.json`](../data/qualifications.schema.json) | Schemas for the three data files above. |
| [`data/dfe-transition-matrices/`](../data/dfe-transition-matrices) | Historical GCSE→A-level transition data feeding the regression in step 2. |

## 1. Domain — [`src/EnrolmentRules.Domain`](../src/EnrolmentRules.Domain)

Shared vocabulary and the loaders that turn the YAML above into validated, immutable snapshots. No
RulesEngine dependency — this project defines the shapes everything else operates on.

- **Inputs/outputs:** [`Inputs.cs`](../src/EnrolmentRules.Domain/Inputs.cs),
  [`Results.cs`](../src/EnrolmentRules.Domain/Results.cs),
  [`Criteria.cs`](../src/EnrolmentRules.Domain/Criteria.cs),
  [`PolicyFacts.cs`](../src/EnrolmentRules.Domain/PolicyFacts.cs),
  [`PrerequisiteSatisfaction.cs`](../src/EnrolmentRules.Domain/PrerequisiteSatisfaction.cs),
  [`Validation.cs`](../src/EnrolmentRules.Domain/Validation.cs),
  [`Linting.cs`](../src/EnrolmentRules.Domain/Linting.cs)
- **Enums/scales:** [`Rating.cs`](../src/EnrolmentRules.Domain/Rating.cs),
  [`Subject.cs`](../src/EnrolmentRules.Domain/Subject.cs),
  [`ALevelGrade.cs`](../src/EnrolmentRules.Domain/ALevelGrade.cs),
  [`AdjustmentKind.cs`](../src/EnrolmentRules.Domain/AdjustmentKind.cs),
  [`QualificationType.cs`](../src/EnrolmentRules.Domain/QualificationType.cs),
  [`EnumNames.cs`](../src/EnrolmentRules.Domain/EnumNames.cs),
  [`Thresholds.cs`](../src/EnrolmentRules.Domain/Thresholds.cs) (the compiled GCSE 1–9 scale
  invariants — the one numeric constant set that isn't policy data)
- **Data loaders/validators:** [`Catalogue.cs`](../src/EnrolmentRules.Domain/Catalogue.cs) +
  [`CatalogueFile.cs`](../src/EnrolmentRules.Domain/CatalogueFile.cs) +
  [`CatalogueDataException.cs`](../src/EnrolmentRules.Domain/CatalogueDataException.cs)
  (`data/catalogue.yaml`), [`PolicyThresholds.cs`](../src/EnrolmentRules.Domain/PolicyThresholds.cs) +
  [`PolicyThresholdsStore.cs`](../src/EnrolmentRules.Domain/PolicyThresholdsStore.cs)
  (`data/thresholds.yaml`), [`QualificationScale.cs`](../src/EnrolmentRules.Domain/QualificationScale.cs) +
  [`QualificationScaleStore.cs`](../src/EnrolmentRules.Domain/QualificationScaleStore.cs)
  (`data/qualifications.yaml`)
- **Plumbing:** [`PredictionModel.cs`](../src/EnrolmentRules.Domain/PredictionModel.cs),
  [`AgeCalculator.cs`](../src/EnrolmentRules.Domain/AgeCalculator.cs),
  [`EnrolmentJson.cs`](../src/EnrolmentRules.Domain/EnrolmentJson.cs) +
  [`YamlConverter.cs`](../src/EnrolmentRules.Domain/YamlConverter.cs) +
  [`EquatableArray.cs`](../src/EnrolmentRules.Domain/EquatableArray.cs) /
  [`EquatableDictionary.cs`](../src/EnrolmentRules.Domain/EquatableDictionary.cs) /
  [`EquatableJsonConverters.cs`](../src/EnrolmentRules.Domain/EquatableJsonConverters.cs)
  (source-generated JSON and value-equality helpers),
  [`EnrolmentDataException.cs`](../src/EnrolmentRules.Domain/EnrolmentDataException.cs),
  [`BuildInfo.cs`](../src/EnrolmentRules.Domain/BuildInfo.cs),
  [`AssemblyInfo.cs`](../src/EnrolmentRules.Domain/AssemblyInfo.cs)

## 2. Prediction — [`src/EnrolmentRules.Prediction`](../src/EnrolmentRules.Prediction)

Statistical layer, upstream of the rules engine. Turns raw GCSE results (and any prior
qualifications) into a predicted A-level profile the rules can act on.

- [`GradePredictor.cs`](../src/EnrolmentRules.Prediction/GradePredictor.cs) — GCSE averaging and
  linear regression → `StudentProfile`
- [`DfeTransitionMatrix.cs`](../src/EnrolmentRules.Prediction/DfeTransitionMatrix.cs) — the
  historical transition data behind the regression (`data/dfe-transition-matrices/`)

## 3. Engine — [`src/EnrolmentRules.Engine`](../src/EnrolmentRules.Engine)

The symbolic-AI layer: runs the RulesEngine workflows, then the cross-subject constraint pass that
RulesEngine itself cannot express (a rule can't read sibling results).

- **Startup snapshots:** [`WorkflowStore.cs`](../src/EnrolmentRules.Engine/WorkflowStore.cs)
  (loads/validates `workflows/*.yaml`), [`CatalogueStore.cs`](../src/EnrolmentRules.Engine/CatalogueStore.cs)
  (loads/validates `data/catalogue.yaml` via `Domain.Catalogue`),
  [`DirectoryDataSource.cs`](../src/EnrolmentRules.Engine/DirectoryDataSource.cs),
  [`WorkflowContent.cs`](../src/EnrolmentRules.Engine/WorkflowContent.cs),
  [`RuleSettings.cs`](../src/EnrolmentRules.Engine/RuleSettings.cs)
- **Per-subject evaluation (RulesEngine):** [`RatingEvaluator.cs`](../src/EnrolmentRules.Engine/RatingEvaluator.cs)
  — evaluates `subject-ratings.yaml`/`eligibility.yaml` per subject, one pure function of one
  student's facts
- **Cross-subject constraint pass (host code, downstream):** [`ConstraintPass.cs`](../src/EnrolmentRules.Engine/ConstraintPass.cs)
  — prerequisites, mutual exclusions, chosen-A-level exclusions, own-time requirements,
  per-subject vetoes; downgrades only, most-severe-wins
- **Aggregation:** [`Aggregator.cs`](../src/EnrolmentRules.Engine/Aggregator.cs) — final score and
  the optional green cap (off by default)
- **Façade and contracts:** [`EnrolmentEngine.cs`](../src/EnrolmentRules.Engine/EnrolmentEngine.cs),
  [`EnrolmentEngineFactory.cs`](../src/EnrolmentRules.Engine/EnrolmentEngineFactory.cs),
  [`IEnrolmentEngine.cs`](../src/EnrolmentRules.Engine/IEnrolmentEngine.cs),
  [`IEnrolmentEngineFactory.cs`](../src/EnrolmentRules.Engine/IEnrolmentEngineFactory.cs),
  [`IEnrolmentEvaluator.cs`](../src/EnrolmentRules.Engine/IEnrolmentEvaluator.cs),
  [`IEnrolmentAdvisor.cs`](../src/EnrolmentRules.Engine/IEnrolmentAdvisor.cs),
  [`IEnrolmentDataSource.cs`](../src/EnrolmentRules.Engine/IEnrolmentDataSource.cs),
  [`IEnrolmentCriteriaExplainer.cs`](../src/EnrolmentRules.Engine/IEnrolmentCriteriaExplainer.cs)
- **Advice / explanation:** [`CounterfactualAdvisor.cs`](../src/EnrolmentRules.Engine/CounterfactualAdvisor.cs)
  (the heavier `Advise` path — see [`docs/benchmarks.md`](benchmarks.md)),
  [`Authoring/CriteriaExplainer.cs`](../src/EnrolmentRules.Engine/Authoring/CriteriaExplainer.cs),
  [`Authoring/ExpressionNarrator.cs`](../src/EnrolmentRules.Engine/Authoring/ExpressionNarrator.cs),
  [`Authoring/ExpressionSyntax.cs`](../src/EnrolmentRules.Engine/Authoring/ExpressionSyntax.cs)
- **Validation tooling:** [`WorkflowLinter.cs`](../src/EnrolmentRules.Engine/WorkflowLinter.cs),
  [`WorkflowExceptions.cs`](../src/EnrolmentRules.Engine/WorkflowExceptions.cs) (backs
  `--lint-workflows`)

## 4. DI integration — [`src/EnrolmentRules.Extensions.DependencyInjection`](../src/EnrolmentRules.Extensions.DependencyInjection)

Optional glue for `Microsoft.Extensions.DependencyInjection` hosts (ASP.NET, worker services). Not
on the path for a plain library consumer or the CLI.

- [`ServiceCollectionExtensions.cs`](../src/EnrolmentRules.Extensions.DependencyInjection/ServiceCollectionExtensions.cs)
  — `AddEnrolmentEngineFactory`/`AddEnrolmentEngine`
- [`ReloadingEnrolmentEngineProxy.cs`](../src/EnrolmentRules.Extensions.DependencyInjection/ReloadingEnrolmentEngineProxy.cs)
  — swaps in a freshly reloaded engine without restarting the host
- [`EnrolmentEngineOptions.cs`](../src/EnrolmentRules.Extensions.DependencyInjection/EnrolmentEngineOptions.cs)

## 5. Consumers — the two front ends

Both sit on top of the engine façade (`IEnrolmentEngine` et al.) and add no policy of their own.

### CLI — [`src/EnrolmentRules.Cli`](../src/EnrolmentRules.Cli)

Thin shim: parses arguments, calls the engine, renders.
[`Program.cs`](../src/EnrolmentRules.Cli/Program.cs),
[`CliRunner.cs`](../src/EnrolmentRules.Cli/CliRunner.cs),
[`TableRenderer.cs`](../src/EnrolmentRules.Cli/TableRenderer.cs),
[`CriteriaRenderer.cs`](../src/EnrolmentRules.Cli/CriteriaRenderer.cs),
[`ExplanationRenderer.cs`](../src/EnrolmentRules.Cli/ExplanationRenderer.cs).

### Web — [`src/EnrolmentRules.Web`](../src/EnrolmentRules.Web)

Session-backed, no database. Two UIs over the same API: a server-rendered Razor Pages flow and a
Vue single-page app.

- **Host/bootstrap:** [`Program.cs`](../src/EnrolmentRules.Web/Program.cs),
  [`Configuration/EnrolmentWebOptions.cs`](../src/EnrolmentRules.Web/Configuration/EnrolmentWebOptions.cs),
  [`Configuration/EnrolmentWebConfigurationException.cs`](../src/EnrolmentRules.Web/Configuration/EnrolmentWebConfigurationException.cs),
  [`Configuration/ExperienceKind.cs`](../src/EnrolmentRules.Web/Configuration/ExperienceKind.cs)
- **Session state:** [`Services/EnrolmentSessionStore.cs`](../src/EnrolmentRules.Web/Services/EnrolmentSessionStore.cs),
  [`Models/EnrolmentSession.cs`](../src/EnrolmentRules.Web/Models/EnrolmentSession.cs)
- **Razor Pages UI (`/razor`):** [`Pages/Index.cshtml`](../src/EnrolmentRules.Web/Pages/Index.cshtml)
  ([`.cs`](../src/EnrolmentRules.Web/Pages/Index.cshtml.cs)),
  [`Pages/Razor.cshtml`](../src/EnrolmentRules.Web/Pages/Razor.cshtml)
  ([`.cs`](../src/EnrolmentRules.Web/Pages/Razor.cshtml.cs)),
  [`Pages/Shared/_Layout.cshtml`](../src/EnrolmentRules.Web/Pages/Shared/_Layout.cshtml),
  [`Models/InputRows.cs`](../src/EnrolmentRules.Web/Models/InputRows.cs),
  [`Models/RowBindings.cs`](../src/EnrolmentRules.Web/Models/RowBindings.cs),
  [`Models/SaveFactsInput.cs`](../src/EnrolmentRules.Web/Models/SaveFactsInput.cs),
  [`Models/RatingDisplay.cs`](../src/EnrolmentRules.Web/Models/RatingDisplay.cs),
  [`Models/ResultViewModels.cs`](../src/EnrolmentRules.Web/Models/ResultViewModels.cs),
  [`Models/TextFormatting.cs`](../src/EnrolmentRules.Web/Models/TextFormatting.cs),
  [`Services/EnrolmentFormMapper.cs`](../src/EnrolmentRules.Web/Services/EnrolmentFormMapper.cs)
- **JSON API backing both UIs:** [`Api/EnrolmentApiEndpoints.cs`](../src/EnrolmentRules.Web/Api/EnrolmentApiEndpoints.cs),
  [`Api/EnrolmentApiContracts.cs`](../src/EnrolmentRules.Web/Api/EnrolmentApiContracts.cs),
  [`Api/EnrolmentApiMapper.cs`](../src/EnrolmentRules.Web/Api/EnrolmentApiMapper.cs),
  [`Api/EnrolmentEvaluateResponseFactory.cs`](../src/EnrolmentRules.Web/Api/EnrolmentEvaluateResponseFactory.cs),
  [`Api/EnrolmentOptionsResponseFactory.cs`](../src/EnrolmentRules.Web/Api/EnrolmentOptionsResponseFactory.cs),
  [`Api/EnrolmentApiJson.cs`](../src/EnrolmentRules.Web/Api/EnrolmentApiJson.cs),
  [`Services/EnrolmentOptionsService.cs`](../src/EnrolmentRules.Web/Services/EnrolmentOptionsService.cs),
  [`Models/WebJson.cs`](../src/EnrolmentRules.Web/Models/WebJson.cs)
- **Vue SPA (`/app`):** [`Pages/App.cshtml`](../src/EnrolmentRules.Web/Pages/App.cshtml)
  ([`.cs`](../src/EnrolmentRules.Web/Pages/App.cshtml.cs)) (host page),
  [`Services/ViteManifestReader.cs`](../src/EnrolmentRules.Web/Services/ViteManifestReader.cs)
  (reads the Vite build manifest so no `dotnet` restart is needed after a front-end edit); the app
  itself is [`ClientApp/src/`](../src/EnrolmentRules.Web/ClientApp/src) —
  [`App.vue`](../src/EnrolmentRules.Web/ClientApp/src/App.vue),
  [`main.ts`](../src/EnrolmentRules.Web/ClientApp/src/main.ts),
  [`components/`](../src/EnrolmentRules.Web/ClientApp/src/components) (`ChosenBasket.vue`,
  `FactsForm.vue`, `GcseRows.vue`, `HeroSection.vue`, `HobbyRows.vue`, `PriorQualificationRows.vue`,
  `ResultsPanel.vue`, `SubjectCard.vue`),
  [`api/contracts.ts`](../src/EnrolmentRules.Web/ClientApp/src/api/contracts.ts),
  [`api/enrolmentApi.ts`](../src/EnrolmentRules.Web/ClientApp/src/api/enrolmentApi.ts),
  [`api/validation.ts`](../src/EnrolmentRules.Web/ClientApp/src/api/validation.ts),
  [`state/enrolmentState.ts`](../src/EnrolmentRules.Web/ClientApp/src/state/enrolmentState.ts),
  [`state/debounce.ts`](../src/EnrolmentRules.Web/ClientApp/src/state/debounce.ts),
  [`state/gcseGrade.ts`](../src/EnrolmentRules.Web/ClientApp/src/state/gcseGrade.ts),
  [`state/localStorageSnapshot.ts`](../src/EnrolmentRules.Web/ClientApp/src/state/localStorageSnapshot.ts),
  [`display/formatting.ts`](../src/EnrolmentRules.Web/ClientApp/src/display/formatting.ts)
- **Equality/JSON plumbing shared with Domain but web-local:** [`Equatable/`](../src/EnrolmentRules.Web/Equatable)
  (`EquatableArray.cs`, `EquatableDictionary.cs`, `EquatableJsonConverters.cs`)

## 6. Cross-cutting

- [`src/EnrolmentRules.Benchmarks`](../src/EnrolmentRules.Benchmarks) — `[MemoryDiagnoser]`
  throughput harness over the engine façade
  ([`EnrolmentBenchmarks.cs`](../src/EnrolmentRules.Benchmarks/EnrolmentBenchmarks.cs)); see
  [`docs/benchmarks.md`](benchmarks.md).
- [`tests/EnrolmentRules.Tests`](../tests/EnrolmentRules.Tests) — engine-driven rule tests, golden
  files, invariants; the trust boundary for every YAML rule (see
  [`docs/rule-authoring.md`](rule-authoring.md)).
- [`tests/EnrolmentRules.TestProcessHost`](../tests/EnrolmentRules.TestProcessHost) —
  out-of-process fixture the CLI/process tests drive.
- [`tests/EnrolmentRules.Web.Tests`](../tests/EnrolmentRules.Web.Tests) — `WebApplicationFactory`
  integration tests for both `/razor` and the API `/app` calls;
  [`ClientApp/src/tests/`](../src/EnrolmentRules.Web/ClientApp/src/tests) covers the Vue layer with
  Vitest.

## Request flow, top to bottom

```
Student facts (JSON)
      │
      ▼
Prediction (§2)  ──►  StudentProfile
      │
      ▼
Engine: RatingEvaluator (§3)  ──►  per-subject green/amber/red, driven by workflows/*.yaml
      │
      ▼
Engine: ConstraintPass (§3)   ──►  cross-subject downgrades, driven by data/catalogue.yaml
      │
      ▼
Engine: Aggregator (§3)       ──►  final scored result
      │
      ▼
CLI or Web (§5) renders it
```
