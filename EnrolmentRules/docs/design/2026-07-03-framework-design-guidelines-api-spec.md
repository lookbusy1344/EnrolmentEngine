# EnrolmentRules API surface specification — 2026-07-03

This note records the supported public surface after the FDG remediation. It is the decision
record for which scenarios stay public, which move into explicit hosting/authoring namespaces, and
which compiled machinery becomes internal implementation detail.

The principle is simple: mainline consumers should discover the engine first; advanced consumers
should opt into hosting or authoring namespaces explicitly; implementation machinery should not be
part of the documented public API.

## Scenario 1 — Evaluate one student

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

## Scenario 2 — Validate a document at an HTTP boundary

Consumer shape:

```csharp
var validation = StudentValidator.Validate(student, catalogue, scale);
if (validation.Count > 0)
{
    return Results.BadRequest(validation);
}
```

Required types:

- `EnrolmentRules.Domain.StudentValidator`
- `EnrolmentRules.Domain.ValidationOutcome`
- `EnrolmentRules.Domain.CatalogueData`
- `EnrolmentRules.Domain.QualificationScale`
- `EnrolmentRules.Domain.StudentInput`

Non-goals:

- throwing on malformed student documents when the caller explicitly wants structured validation;
- bypassing the catalogue/scale snapshot the engine is actually using.

## Scenario 3 — Register the engine with dependency injection

Consumer shape:

```csharp
services.AddEnrolmentEngine(options =>
{
    options.UseWorkflowsDirectory(workflowsDirectory);
    options.UseDataDirectory(dataDirectory);
});
```

Required types:

- `EnrolmentRules.Extensions.DependencyInjection.ServiceCollectionExtensions`
- `EnrolmentRules.Extensions.DependencyInjection.EnrolmentEngineOptions`
- `EnrolmentRules.Engine.EnrolmentEngine`
- `EnrolmentRules.Engine.IEnrolmentEngine`
- `EnrolmentRules.Engine.IEnrolmentEvaluator`
- `EnrolmentRules.Engine.IEnrolmentAdvisor`

Non-goals:

- exposing the reload proxy or factory implementation as a required consumer dependency;
- forcing hosts to rebuild the container for policy refreshes.

## Scenario 4 — Reload policy without rebuilding the host

Consumer shape:

```csharp
var factory = provider.GetRequiredService<IEnrolmentEngineFactory>();
factory.Reload();
var engine = factory.Current;
```

Required types:

- `EnrolmentRules.Engine.IEnrolmentEngineFactory`
- `EnrolmentRules.Engine.Hosting.EnrolmentEngineFactory`

Non-goals:

- direct use of the proxy that DI registers behind the interface;
- reimplementing the bootstrap recipe in host code.

## Scenario 5 — Supply embedded or remote policy data

Consumer shape:

```csharp
public sealed class EmbeddedDataSource : IEnrolmentDataSource
{
    public IReadOnlyList<WorkflowContent> OpenWorkflows() => ...
    public Stream OpenWorkflowSchema() => ...
    public Stream OpenCatalogue() => ...
    // remaining streams...
}
```

Required types:

- `EnrolmentRules.Engine.Hosting.IEnrolmentDataSource`
- `EnrolmentRules.Engine.Hosting.WorkflowContent`
- `EnrolmentRules.Engine.Hosting.DirectoryDataSource`
- `EnrolmentRules.Engine.EnrolmentEngine`
- `EnrolmentRules.Engine.Hosting.EnrolmentEngineFactory`

Non-goals:

- constraining policy loading to the filesystem;
- exposing evaluation internals as part of the data-source abstraction.

## Scenario 6 — Author and lint policy files

Consumer shape:

```csharp
var workflows = WorkflowStore.LoadAndValidate(workflowsDirectory);
var catalogue = CatalogueStore.LoadAndValidate(dataDirectory, scale);
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

Non-goals:

- direct public consumption of rating, constraint, or aggregation machinery;
- expecting the authoring layer to be the runtime evaluation surface.

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

The supported surface can grow only by updating this document and the public-surface test in the
same change.
