# EnrolmentRules API surface specification — 2026-07-03

**Revised: 2026-08-17.**

This note records the supported public surface after the FDG remediation. It is the decision
record for which scenarios stay public, which move into explicit hosting/authoring namespaces, and
which compiled machinery becomes internal implementation detail.

The principle is simple: mainline consumers should discover the engine first; advanced consumers
should opt into hosting or authoring namespaces explicitly; implementation machinery should not be
part of the documented public API.

`tests/EnrolmentRules.Tests/PublicApiSurfaceTests.cs` parses the **Authoritative inventory**
section below and fails if the assembly's exported types and this document diverge. That test is
the only place the surface is enforced; this document is the only place it is described. Adding a
public type without adding it here fails the test.

## Scenario 1 — Evaluate, explain, and advise one student

Consumer shape:

```csharp
var engine = EnrolmentEngine.Create(workflowsDirectory, dataDirectory, asOf);
EnrolmentResult result = engine.Evaluate(student);
ExplainedResult explained = engine.Explain(student);
AdviceResult advice = engine.Advise(student);
```

Required types:

- `EnrolmentRules.Engine.EnrolmentEngine`
- `EnrolmentRules.Engine.IEnrolmentEngine`
- `EnrolmentRules.Engine.IEnrolmentEvaluator`
- `EnrolmentRules.Engine.IEnrolmentAdvisor`
- `EnrolmentRules.Domain.StudentInput`
- `EnrolmentRules.Domain.EnrolmentResult`
- `EnrolmentRules.Domain.ExplainedResult`
- `EnrolmentRules.Domain.AdviceResult`

Non-goals:

- direct composition of the pipeline from lower-level rating or constraint types;
- direct access to workflow internals, rating facts, or aggregation machinery.

## Scenario 2 — Validate input and final programme state without exceptions

Consumer shape:

```csharp
ValidatedEvaluation<EnrolmentResult> outcome = engine.EvaluateValidated(student);
if (!outcome.Validation.IsValid)
{
    return Results.BadRequest(outcome.Validation.Errors);
}

EnrolmentResult result = outcome.Value!;

// Or gate the caller's own committed-programme invariant directly:
ValidatedEvaluation<FinalProgramme> programme = engine.ValidateFinalProgramme(student);
```

Required types:

- `EnrolmentRules.Engine.IEnrolmentEvaluator`
- `EnrolmentRules.Engine.IEnrolmentAdvisor`
- `EnrolmentRules.Domain.ValidatedEvaluation`1`
- `EnrolmentRules.Domain.ValidationOutcome`
- `EnrolmentRules.Domain.FinalProgramme`
- `EnrolmentRules.Domain.StudentInput`
- `EnrolmentRules.Domain.StudentValidator`
- `EnrolmentRules.Domain.CatalogueData`
- `EnrolmentRules.Domain.QualificationScale`

Non-goals:

- throwing on malformed student documents when the caller explicitly wants structured validation;
- bypassing the catalogue/scale snapshot the engine is actually using.

## Scenario 3 — Describe subject criteria with no student input

Consumer shape:

```csharp
IEnrolmentCriteriaExplainer explainer = engine;
SubjectCriteria criteria = explainer.Describe(Subject.Physics);
```

Required types:

- `EnrolmentRules.Engine.IEnrolmentCriteriaExplainer`
- `EnrolmentRules.Domain.SubjectCriteria`
- `EnrolmentRules.Domain.Subject`

Non-goals:

- requiring a `StudentInput` to publish a course prospectus;
- exposing the narration/expression-syntax machinery that builds the bullets.

## Scenario 4 — Use prediction and transition evidence directly

Consumer shape:

```csharp
StudentProfile profile = GradePredictor.Predict(student, gcses, asOf, catalogue, scale);
EquatableArray<PredictedGrade> predicted = profile.PredictedGrades;
EquatableArray<TransitionEvidence> evidence = profile.TransitionEvidence;
```

Required types:

- `EnrolmentRules.Prediction.GradePredictor`
- `EnrolmentRules.Prediction.DfeTransitionMatrix`
- `EnrolmentRules.Prediction.TransitionMatrixException`
- `EnrolmentRules.Domain.StudentProfile`
- `EnrolmentRules.Domain.PredictedGrade`
- `EnrolmentRules.Domain.TransitionEvidence`

Non-goals:

- running prediction as a hidden step of `Evaluate`/`Explain`/`Advise` only — a consumer building
  its own front-end (e.g. a "what would you predict for me?" screen) can call it standalone;
- exposing the regression's internal coefficient-fitting code (`PredictionModel` stays a plain
  value type, not a fitting API).

## Scenario 5 — Register a fixed or reloadable engine with dependency injection

Consumer shape:

```csharp
services.AddEnrolmentEngine(options =>
{
    options.UseWorkflowsDirectory(workflowsDirectory);
    options.UseDataDirectory(dataDirectory);
});

// or, to reload policy without rebuilding the host:
services.AddEnrolmentEngineFactory(options =>
{
    options.UseWorkflowsDirectory(workflowsDirectory);
    options.UseDataDirectory(dataDirectory);
});
var factory = provider.GetRequiredService<IEnrolmentEngineFactory>();
factory.Reload();
```

Required types:

- `EnrolmentRules.Extensions.DependencyInjection.ServiceCollectionExtensions`
- `EnrolmentRules.Extensions.DependencyInjection.EnrolmentEngineOptions`
- `EnrolmentRules.Engine.EnrolmentEngine`
- `EnrolmentRules.Engine.IEnrolmentEngine`
- `EnrolmentRules.Engine.IEnrolmentEngineFactory`
- `EnrolmentRules.Engine.IEnrolmentEvaluator`
- `EnrolmentRules.Engine.IEnrolmentAdvisor`
- `EnrolmentRules.Engine.IEnrolmentCriteriaExplainer`
- `EnrolmentRules.Engine.Hosting.EnrolmentEngineFactory`

Non-goals:

- exposing the reload proxy as a required consumer dependency — a host resolves the four
  interfaces above, never `ReloadingEnrolmentEngineProxy` itself;
- forcing hosts to rebuild the container for policy refreshes.

## Scenario 6 — Supply filesystem, embedded, remote, or overlaid policy data

Consumer shape:

```csharp
public sealed class EmbeddedDataSource : IEnrolmentDataSource
{
    public IReadOnlyList<WorkflowContent> OpenWorkflows() => ...
    public Stream OpenWorkflowSchema() => ...
    public Stream OpenCatalogue() => ...
    // remaining streams...
}

// filesystem:
IEnrolmentDataSource baseSource = new DirectoryDataSource(workflowsDirectory, dataDirectory);

// an auxiliary policy overlaid on the base tree (its own workflows/catalogue/thresholds; shared
// schemas, qualifications, and DfE evidence fall through to the base source):
IEnrolmentDataSource elite = new OverlayEnrolmentDataSource(auxiliarySource, baseSource);
```

Required types:

- `EnrolmentRules.Engine.Hosting.IEnrolmentDataSource`
- `EnrolmentRules.Engine.Hosting.WorkflowContent`
- `EnrolmentRules.Engine.Hosting.DirectoryDataSource`
- `EnrolmentRules.Engine.Hosting.OverlayEnrolmentDataSource`
- `EnrolmentRules.Engine.EnrolmentEngine`
- `EnrolmentRules.Engine.Hosting.EnrolmentEngineFactory`

Non-goals:

- constraining policy loading to the filesystem;
- exposing evaluation internals as part of the data-source abstraction.

## Scenario 7 — Register, select, and compare multiple policies

Consumer shape:

```csharp
services.AddEnrolmentPolicies(options => options
    .UseDefault("standard", "Standard", standardSource)
    .Add("elite", "Elite", new OverlayEnrolmentDataSource(eliteSource, standardSource)));

var registry = provider.GetRequiredService<IEnrolmentPolicyRegistry>();
EnrolmentPolicy elite = registry.GetPolicy(new EnrolmentPolicyId("elite"));
ValidatedEvaluation<PolicyComparisonResult> comparison =
    registry.Compare(new EnrolmentPolicyId("elite"), student);
```

Required types:

- `EnrolmentRules.Extensions.DependencyInjection.EnrolmentPolicyOptions`
- `EnrolmentRules.Engine.IEnrolmentPolicyRegistry`
- `EnrolmentRules.Engine.EnrolmentPolicyRegistry`
- `EnrolmentRules.Engine.EnrolmentPolicyId`
- `EnrolmentRules.Engine.EnrolmentPolicyDescriptor`
- `EnrolmentRules.Engine.EnrolmentPolicyDefinition`
- `EnrolmentRules.Engine.EnrolmentPolicy`
- `EnrolmentRules.Engine.PolicyComparisonResult`
- `EnrolmentRules.Engine.EnrolmentPolicyRegistryException`
- `EnrolmentRules.Engine.EnrolmentPolicyBuildException`
- `EnrolmentRules.Engine.EnrolmentPolicyConfigurationException`
- `EnrolmentRules.Engine.UnknownEnrolmentPolicyException`

Non-goals:

- an ambiguous container-wide `IEnrolmentEngine` from `AddEnrolmentPolicies` — a consumer always
  names the policy it wants, from the registry;
- mutable "current policy" state on the registry — selection is an explicit per-call lookup.

## Scenario 8 — Load, validate, narrate, and lint policy files

Consumer shape:

```csharp
var scale = QualificationScaleStore.LoadAndValidate(dataDirectory);
var workflows = WorkflowStore.LoadAndValidate(workflowsDirectory);
var catalogue = CatalogueStore.LoadAndValidate(dataDirectory, scale);
var thresholds = PolicyThresholdsStore.LoadAndValidate(dataDirectory);
var findings = WorkflowLinter.Lint(workflows, catalogue);
```

Required types:

- `EnrolmentRules.Engine.Authoring.WorkflowStore`
- `EnrolmentRules.Engine.Authoring.CatalogueStore`
- `EnrolmentRules.Engine.Authoring.WorkflowLinter`
- `EnrolmentRules.Engine.Authoring.WorkflowException`
- `EnrolmentRules.Engine.Authoring.WorkflowSchemaException`
- `EnrolmentRules.Engine.Authoring.WorkflowProbeException`
- `EnrolmentRules.Engine.Authoring.WorkflowLintException`
- `EnrolmentRules.Engine.Authoring.CatalogueException`
- `EnrolmentRules.Engine.Authoring.CriteriaExplainer`
- `EnrolmentRules.Engine.Authoring.ExpressionNarrator`
- `EnrolmentRules.Engine.Authoring.CriteriaNarrationException`
- `EnrolmentRules.Domain.Authoring.QualificationScaleStore`
- `EnrolmentRules.Domain.Authoring.QualificationScaleException`
- `EnrolmentRules.Domain.Authoring.PolicyThresholdsStore`
- `EnrolmentRules.Domain.Authoring.PolicyThresholdsException`
- `EnrolmentRules.Domain.LintFinding`
- `EnrolmentRules.Domain.LintSeverity`

Non-goals:

- direct public consumption of rating, constraint, or aggregation machinery;
- expecting the authoring layer to be the runtime evaluation surface.

## Exported runtime support

These types are `public` because a runtime mechanism requires accessibility that scenario mainline
code never names directly. They are not entry points; a consumer following Scenarios 1–8 never
needs to import them.

- **Source-generated JSON contexts and converters**, isolated in `EnrolmentRules.Domain.Serialization`
  (`System.Text.Json` source generation requires the context type and any custom converter it
  references to be visible to the generated code): `EnrolmentJsonContext`, `BatchJsonContext`,
  `SubjectJsonConverter`, `YamlConverter` (YAML-to-JSON normalisation ahead of schema validation).
- **RulesEngine lambda-binding shape**, isolated in `EnrolmentRules.Domain.RuntimeBinding`.
  RulesEngine compiles workflow expressions against this type by reflection at runtime, so it must
  be public even though no consumer constructs it directly: `PolicyFacts`.
- **Schema-backed stores and their load/validation exceptions**, isolated in
  `EnrolmentRules.Domain.Authoring` (mirroring `EnrolmentRules.Engine.Authoring` one layer down):
  `PolicyThresholdsStore`, `PolicyThresholdsException`, `QualificationScaleStore`,
  `QualificationScaleException`. `CatalogueDataException` stays in mainline
  `EnrolmentRules.Domain` — it is a runtime invariant of the already-built `CatalogueData` snapshot,
  raised by `Catalogue.Load*`, not a YAML/schema load-time failure from a separate store type.
- **Diagnostics**, isolated in `EnrolmentRules.Domain.Diagnostics`: `BuildInfo`, read by the
  CLI/Web `--version` surfaces and health endpoints — diagnostic metadata, not a mainline scenario
  type.
- **Collection-expression and value-equality helpers**, public so any assembly's collection
  expressions and record equality can target them, and kept in mainline `EnrolmentRules.Domain`
  because they are shared value-collection vocabulary, not plumbing a consumer opts into:
  `EquatableArray`, `EquatableArray`1`, `EquatableDictionary`2`, `EquatableDictionaryFactory`.
- **Attributes read by source generators or analyzers at the call site**: `LargeStructAttribute`.
- **Enum-name lookup used by rendering front ends** outside this repository's own CLI/Web
  projects: `EnumNames`.

## Deliberate construction-contract deviation

`Subject` and `EnrolmentPolicyId` are strongly-typed identifier wrappers, but deliberately do not
follow the generic "normalise or default on bad input" template: direct construction throws
`ArgumentNullException` for a null value and `ArgumentException` for invalid non-null text, rather
than silently mapping either to a default identifier. An enrolment or policy-selection typo must
fail at the boundary where it was made, not surface later as a mysterious "unknown subject" from
deep inside a workflow.

## Deliberate exclusions

The following implementation types are internal on purpose and are not part of the supported
surface:

- `RatingEvaluator`
- `EligibilityGate`
- `SubjectRating`
- `GcseFacts`
- `RatingFacts`
- `ConstraintPass`
- `Aggregator`
- `RuleSettings`

## Authoritative inventory

One fully-qualified type name per line, grouped by assembly. Every type `GetExportedTypes()`
returns for that assembly must appear in its block exactly once, and every line must name a type
that assembly actually exports; `PublicApiSurfaceTests.public_surface_matches_the_design_spec`
enforces both directions plus duplicate and unknown-label detection.

```types:EnrolmentRules.Domain
EnrolmentRules.Domain.AgeCalculator
EnrolmentRules.Domain.ALevelGrade
EnrolmentRules.Domain.AdjustmentKind
EnrolmentRules.Domain.Adjustment
EnrolmentRules.Domain.AdviceResult
EnrolmentRules.Domain.BatchOutcome
EnrolmentRules.Domain.Catalogue
EnrolmentRules.Domain.CatalogueData
EnrolmentRules.Domain.CatalogueDataException
EnrolmentRules.Domain.EnrolmentDataException
EnrolmentRules.Domain.EnrolmentResult
EnrolmentRules.Domain.EnrolmentSummary
EnrolmentRules.Domain.EnumNames
EnrolmentRules.Domain.ChoiceStatus
EnrolmentRules.Domain.ChosenSubjectStatus
EnrolmentRules.Domain.EntryEquivalent
EnrolmentRules.Domain.ExclusionPair
EnrolmentRules.Domain.ExplainedResult
EnrolmentRules.Domain.Explanation
EnrolmentRules.Domain.EquatableArray
EnrolmentRules.Domain.EquatableArray`1
EnrolmentRules.Domain.EquatableDictionary`2
EnrolmentRules.Domain.EquatableDictionaryFactory
EnrolmentRules.Domain.FinalProgramme
EnrolmentRules.Domain.GateAdvice
EnrolmentRules.Domain.GcseResult
EnrolmentRules.Domain.GcseScoreboard
EnrolmentRules.Domain.GcseSubjects
EnrolmentRules.Domain.GradeChange
EnrolmentRules.Domain.LargeStructAttribute
EnrolmentRules.Domain.LintFinding
EnrolmentRules.Domain.LintSeverity
EnrolmentRules.Domain.PredictedGrade
EnrolmentRules.Domain.PolicyThresholds
EnrolmentRules.Domain.PredictionModel
EnrolmentRules.Domain.PredictionModel+Coefficients
EnrolmentRules.Domain.Prerequisite
EnrolmentRules.Domain.PrerequisiteSatisfaction
EnrolmentRules.Domain.Qualification
EnrolmentRules.Domain.QualificationScale
EnrolmentRules.Domain.QualificationScaleEntry
EnrolmentRules.Domain.QualificationType
EnrolmentRules.Domain.Rating
EnrolmentRules.Domain.RatingExtensions
EnrolmentRules.Domain.RatingMeaning
EnrolmentRules.Domain.SubjectCriteria
EnrolmentRules.Domain.Recommendation
EnrolmentRules.Domain.RestudyBar
EnrolmentRules.Domain.StudentDocument
EnrolmentRules.Domain.StudentInput
EnrolmentRules.Domain.StudentProfile
EnrolmentRules.Domain.StudentValidator
EnrolmentRules.Domain.Subject
EnrolmentRules.Domain.SubjectAdvice
EnrolmentRules.Domain.SubjectExclusion
EnrolmentRules.Domain.SubjectMeta
EnrolmentRules.Domain.Thresholds
EnrolmentRules.Domain.TransitionEvidence
EnrolmentRules.Domain.UnsatGcseAdvice
EnrolmentRules.Domain.ValidationOutcome
EnrolmentRules.Domain.ValidatedEvaluation`1
EnrolmentRules.Domain.Serialization.BatchJsonContext
EnrolmentRules.Domain.Serialization.EnrolmentJsonContext
EnrolmentRules.Domain.Serialization.SubjectJsonConverter
EnrolmentRules.Domain.Serialization.YamlConverter
EnrolmentRules.Domain.Authoring.PolicyThresholdsException
EnrolmentRules.Domain.Authoring.PolicyThresholdsStore
EnrolmentRules.Domain.Authoring.QualificationScaleException
EnrolmentRules.Domain.Authoring.QualificationScaleStore
EnrolmentRules.Domain.RuntimeBinding.PolicyFacts
EnrolmentRules.Domain.Diagnostics.BuildInfo
```

```types:EnrolmentRules.Prediction
EnrolmentRules.Prediction.DfeTransitionMatrix
EnrolmentRules.Prediction.GradePredictor
EnrolmentRules.Prediction.TransitionMatrixException
```

```types:EnrolmentRules.Engine
EnrolmentRules.Engine.Authoring.CatalogueException
EnrolmentRules.Engine.Authoring.CatalogueStore
EnrolmentRules.Engine.Authoring.CriteriaExplainer
EnrolmentRules.Engine.Authoring.CriteriaNarrationException
EnrolmentRules.Engine.Authoring.ExpressionNarrator
EnrolmentRules.Engine.IEnrolmentCriteriaExplainer
EnrolmentRules.Engine.Authoring.WorkflowException
EnrolmentRules.Engine.Authoring.WorkflowLintException
EnrolmentRules.Engine.Authoring.WorkflowLinter
EnrolmentRules.Engine.Authoring.WorkflowProbeException
EnrolmentRules.Engine.Authoring.WorkflowSchemaException
EnrolmentRules.Engine.Authoring.WorkflowStore
EnrolmentRules.Engine.EnrolmentEngine
EnrolmentRules.Engine.IEnrolmentAdvisor
EnrolmentRules.Engine.IEnrolmentEngine
EnrolmentRules.Engine.IEnrolmentEngineFactory
EnrolmentRules.Engine.IEnrolmentEvaluator
EnrolmentRules.Engine.Hosting.DirectoryDataSource
EnrolmentRules.Engine.Hosting.EnrolmentEngineFactory
EnrolmentRules.Engine.Hosting.IEnrolmentDataSource
EnrolmentRules.Engine.Hosting.WorkflowContent
EnrolmentRules.Engine.Hosting.OverlayEnrolmentDataSource
EnrolmentRules.Engine.EnrolmentPolicy
EnrolmentRules.Engine.EnrolmentPolicyBuildException
EnrolmentRules.Engine.EnrolmentPolicyConfigurationException
EnrolmentRules.Engine.EnrolmentPolicyDefinition
EnrolmentRules.Engine.EnrolmentPolicyDescriptor
EnrolmentRules.Engine.EnrolmentPolicyId
EnrolmentRules.Engine.EnrolmentPolicyRegistry
EnrolmentRules.Engine.EnrolmentPolicyRegistryException
EnrolmentRules.Engine.IEnrolmentPolicyRegistry
EnrolmentRules.Engine.UnknownEnrolmentPolicyException
EnrolmentRules.Engine.PolicyComparisonResult
```

```types:EnrolmentRules.Extensions.DependencyInjection
EnrolmentRules.Extensions.DependencyInjection.EnrolmentEngineOptions
EnrolmentRules.Extensions.DependencyInjection.EnrolmentPolicyOptions
EnrolmentRules.Extensions.DependencyInjection.ServiceCollectionExtensions
```

The supported surface can grow only by updating this document and the public-surface test in the
same change.
