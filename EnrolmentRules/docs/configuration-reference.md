# Configuration Reference

This document is the field-level reference for every editable YAML/JSON surface that shapes
EnrolmentRules behaviour.

It complements, rather than replaces:

- [`technical-reference.md`](technical-reference.md) for setup, CLI usage, and the student input
  shape.
- [`rule-authoring.md`](rule-authoring.md) for the workflow for changing rules safely.
- [`walkthrough.md`](walkthrough.md) for the end-to-end pipeline and decision semantics.

## Scope

The editable surfaces fall into three groups:

| Surface | Files | Purpose |
| --- | --- | --- |
| Runtime policy data | `data/*.yaml`, `workflows/*.yaml` | The rules and thresholds the engine actually loads at startup. |
| Validation contracts | `data/*.schema.json`, `workflows/workflow.schema.json` | JSON Schema files that define the allowed structure of the YAML files. |
| Extension and input examples | `examples/**/*.yaml`, `examples/**/*.json` | Authoring examples, append snippets, and sample student documents. |

If you are changing live policy, the files that matter most are:

- `data/catalogue.yaml`
- `data/thresholds.yaml`
- `data/qualifications.yaml`
- `workflows/eligibility.yaml`
- `workflows/subject-ratings.yaml`

## Runtime Policy Files

### `data/thresholds.yaml`

Numeric tuning knobs read by the workflow expressions and host-side aggregation/advice code.

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `pass_grade` | integer `1..9` | yes | Inclusive GCSE pass boundary. The eligibility workflow compares English Language, Maths, and the count of all supplied GCSE grades against this value; a grade equal to it passes. It does not set subject-specific entry requirements. |
| `min_passes` | integer `>= 1` | yes | Inclusive number of GCSE entries at or above `pass_grade` required by the `EnoughPasses` eligibility rule. Duplicate subjects cannot inflate the count because GCSE input is a subject-keyed map. |
| `top_entry` | integer `1..9` | yes | Named high-selectivity GCSE boundary exposed to subject-rating expressions as `facts.TopEntry`. It has no intrinsic ordering relationship with the other entry fields: workflow expressions choose where and how to apply it. |
| `strong_entry` | integer `1..9` | yes | Named medium-selectivity GCSE boundary exposed as `facts.StrongEntry`. A subject may test one or several GCSEs against it, or ignore it entirely. |
| `standard_entry` | integer `1..9` | yes | Named baseline GCSE boundary exposed as `facts.StandardEntry`. It does not automatically apply to every subject; only expressions that reference it are affected. |
| `further_maths_average_entry` | number `0..9` | yes | Inclusive whole-profile GCSE-average boundary exposed as `facts.FurtherMathsAverageEntry`. Despite the policy-oriented name, it affects whichever workflow expressions reference it. |
| `humanities_average_entry` | number `0..9` | yes | Inclusive whole-profile GCSE-average boundary exposed as `facts.HumanitiesAverageEntry`, commonly used for subjects whose entry decision is based on overall attainment rather than one GCSE. |
| `min_dfe_green_probability_at_or_above` | number `0..1` | yes | Inclusive probability floor exposed as `facts.MinDfeGreenProbabilityAtOrAbove`. A workflow normally compares it with the student's DfE probability of achieving a specified A-level grade or better; it has no effect unless the expression performs that comparison. |
| `min_dfe_amber_probability_at_or_above` | number `0..1` | yes | Inclusive probability floor for amber expressions, exposed as `facts.MinDfeAmberProbabilityAtOrAbove`. Values are proportions (`0.50` means 50%), not percentages. |
| `adult_age` | integer `>= 1` | yes | Inclusive whole-years age boundary exposed as `facts.AdultAge`. Age is calculated on the evaluation's as-of date; workflows decide whether being at, above, or below the boundary is acceptable. |
| `max_green_choices` | integer `>= 1` | no | Maximum number of subjects allowed to remain green after all constraint downgrades. When exceeded, the lowest-`priority_weight` greens are demoted to amber; omit the field to disable this pass. |
| `amber_score_factor` | number `0..1` | yes | Multiplier applied to each final amber subject's `priority_weight` when calculating `programme_priority_score`. Green contributes full weight and red contributes zero; this field changes only the aggregate score, not a rating. |
| `advice_considers_unsat_gcses` | boolean | no | Controls the advisor's candidate GCSE set. When `true`, it may propose adding a GCSE absent from the student's input; when `false` or omitted, it may only raise supplied GCSEs. It never changes ordinary evaluation. |
| `advice_max_grade_cost` | integer `>= 1` | no | Upper bound on a proposal's total grade-step cost: the sum of every proposed GCSE increase, with a newly added GCSE costed from the advisor's baseline. Candidates over the bound are not explored. Defaults to `12` when omitted. |
| `advice_max_subjects_changed` | integer `>= 1` | no | Upper bound on the number of distinct GCSE keys altered by one proposal, independent of how many grade steps each alteration costs. Defaults to `3` when omitted. |
| `advice_max_pipeline_evaluations` | integer `>= 1` | no | Budget for complete evaluation-pipeline executions during one advice search. Reaching it returns deterministic partial advice marked as truncated; omit for no evaluation-count limit. |

Notes:

- The entry thresholds are policy values, not hard-coded semantics. Workflow YAML decides which one a subject reads.
- `max_green_choices` is intentionally absent in the shipped file. The normal mode is uncapped greens.
- The `advice_*` values affect only the counterfactual advisor, not ordinary enrolment evaluation.

Example:

```yaml
pass_grade: 4
min_passes: 5
top_entry: 7
strong_entry: 6
standard_entry: 5
further_maths_average_entry: 7.0
humanities_average_entry: 5.0
min_dfe_green_probability_at_or_above: 0.60
min_dfe_amber_probability_at_or_above: 0.50
adult_age: 19
amber_score_factor: 0.5
```

What this means in practice:

- a GCSE grade of `4` counts as a pass,
- a student needs at least `5` passes to clear the eligibility gate,
- a subject can choose to require `7`, `6`, or `5` in a GCSE depending on how selective it is,
- a green tier usually needs stronger DfE evidence than an amber tier.

### `data/catalogue.yaml`

The subject catalogue is the data source for prediction coefficients, cross-subject relationships,
entry equivalents, restudy bars, and subject priority weighting.

Top-level shape:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `subjects` | array of subject entries | yes | Complete, non-empty catalogue of A-level subjects. Subject ids must be unique, and startup validation cross-checks this set against the subjects represented by the rating workflow. Array order does not determine recommendation order. |

Each subject entry:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `subject` | `snake_case` string | yes | Canonical, case-sensitive subject identifier used in rule names, workflow fact lookups, results, relationships, and student choices. It must be unique and match the schema's lowercase identifier pattern. |
| `priority_weight` | positive integer | yes | Subject policy priority. It contributes to `programme_priority_score`, orders subjects within a rating, decides the loser in a mutual exclusion, and—only when the optional green cap is enabled—decides which surplus greens are demoted. It is not a UCAS tariff value. Mutually excluding subjects must have distinct weights. |
| `regression` | object | yes | Coefficients for converting the student's average GCSE score into this subject's predicted A-level points before prior-qualification uplift and workflow rating. |
| `exclusions` | array | no | Pairwise timetable or policy clashes. The reciprocal subject must declare the same pair and severity. A red clash applies when both subjects qualify as green/amber; an amber clash applies only when both are green. Compiled machinery downgrades the lower-priority subject. |
| `required_activities` | array of strings | no | Acceptable hobby-tag prefixes for an own-time requirement. At least one student hobby must start with at least one configured prefix; otherwise the subject is downgraded to amber. An empty/omitted list creates no requirement. |
| `blocking_activities` | array of strings | no | Hobby-tag prefixes that veto the subject. Any student hobby starting with any configured prefix produces a red downgrade, even if a required-activity prefix also matches. |
| `prerequisites` | array | no | Conjunctive list of dependency groups: every group must be satisfied, while the subjects inside one group's `any_of` are alternatives. Each unmet group produces its configured downgrade. |
| `entry_equivalents` | array | no | Alternative prior-qualification routes recognised for this A-level. A matching qualification at or above `min_grade` can both satisfy `facts.HasEntryEquivalent(subject)` and lift the upstream prediction. |
| `restudy_bar` | object | no | Same-subject prior-qualification restriction applied downstream. If the student already holds one of the listed qualification types in this catalogue subject, the final rating is downgraded to the configured severity. |

`regression` object:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `slope` | number | yes | Multiplier applied to the student's average GCSE score in the linear model: `predicted points = slope × average + intercept`. The regression result is clamped to the A-level `0..6` points range, then a stronger qualifying prior-equivalent value may replace it. |
| `intercept` | number | yes | Constant added by that linear model. Negative values lower the prediction across the range; positive values raise it. |

`exclusions` entries:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `other` | `snake_case` subject id | yes | Id of the other catalogue subject in the clash. Self-references and unknown ids are rejected, and the other subject must contain the reciprocal declaration. |
| `severity` | `amber` or `red` | yes | Maximum rating retained by the losing subject when the clash applies. `amber` preserves it as a caution; `red` blocks it. Reciprocal entries must agree. |

`prerequisites` entries:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `any_of` | non-empty array of subject ids | yes | Alternative catalogue subjects for one prerequisite group. One alternative is sufficient; separate prerequisite entries are all required. Unknown subjects and a dependency on the owning subject are rejected. |
| `severity` | `amber` or `red` | no | Maximum rating retained when this group is unmet. Omission defaults to `red`, making the prerequisite blocking. |
| `requires` | `qualifying` or `chosen` | no | Satisfaction mode. `qualifying` accepts a green/amber result (as it stands *after* the dependency's own veto / restudy bar / exclusion downgrades) or a chosen A-level; `chosen` requires a committed `chosen_a_levels` entry. |

`entry_equivalents` entries:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `subject` | string | yes | Prior-qualification subject token to match, for example `applied_science`. This need not be an A-level catalogue id; matching is ordinal and case-insensitive. |
| `type` | known qualification type | yes | Required qualification type. The type selects the grade scale used to interpret `min_grade` and the student's grade. |
| `min_grade` | string | yes | Inclusive minimum grade token on the selected type's scale. Qualification ordering is determined by `ordinal`, not lexical grade-name ordering. Unknown grade tokens fail startup validation. |

`restudy_bar` object:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `types` | non-empty array of qualification types | yes | Qualification types that trigger the bar when a student's prior-qualification `subject` equals this catalogue subject. Grade is irrelevant: holding any recognised grade of a listed type is enough. |
| `severity` | `amber` or `red` | no | Maximum final rating after a match. Omission defaults to `red`. |

Notes:

- Subject ids are data-driven for A-level subjects. Adding a subject here is the first half of a data-only subject addition.
- `exclusions`, `prerequisites`, `required_activities`, `blocking_activities`, `entry_equivalents`, and `restudy_bar` all operate downstream in compiled machinery; this file decides the policy data they read.
- Structural validity comes from JSON Schema; broader invariants such as exclusion symmetry and coverage are enforced at load time in code.

Examples:

Minimal subject row:

```yaml
subjects:
  - subject: maths
    priority_weight: 50
    regression:
      slope: 0.80
      intercept: -1.00
```

Subject row with relationships:

```yaml
subjects:
  - subject: further_maths
    priority_weight: 56
    regression: { slope: 1.00, intercept: -2.00 }
    prerequisites:
      - any_of: [ maths ]
        requires: chosen

  - subject: music
    priority_weight: 36
    regression: { slope: 0.85, intercept: -1.70 }
    required_activities: [ plays_ ]
    blocking_activities: [ plays_trombone ]

  - subject: biology
    priority_weight: 44
    regression: { slope: 0.90, intercept: -2.30 }
    entry_equivalents:
      - subject: applied_science
        type: btec_diploma
        min_grade: distinction
    restudy_bar:
      types: [ a_level ]
      severity: red
```

How to read those examples:

- `further_maths` requires `maths` to appear in `chosen_a_levels`, not merely to rate green or amber.
- `music` expects at least one hobby tag starting with `plays_`, but `plays_trombone` is an explicit veto.
- `biology` can accept a strong enough `btec_diploma` in `applied_science` as an alternative entry path, and bars re-study for someone who already has an A-level in Biology.

### `data/qualifications.yaml`

The typed qualification scale. This controls grade ordering within each qualification type and the
A-level-points equivalence used for prior-qualification uplift.

Top-level shape:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `qualifications` | array of type entries | yes | Complete set of qualification scales. Every compiled qualification type must occur exactly once and contain at least one grade; missing type coverage is rejected at startup. Array order has no evaluation meaning. |

Each qualification type entry:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `type` | qualification type | yes | Compiled qualification-family identifier used by student input, entry equivalents, and restudy bars. A type selects its own independent grade vocabulary and ordering. |
| `grades` | non-empty array of grade entries | yes | Complete grade lookup table for this type. The array's physical order is irrelevant; `ordinal` supplies the ordering, and grade tokens and ordinals must each be unique within the type. |

Each grade entry:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `grade` | string | yes | Non-blank external token accepted in `student.prior_qualifications[].grade` and `entry_equivalents[].min_grade`, for example `distinction` or `a_star`. It is resolved only within the enclosing qualification type. |
| `ordinal` | integer `>= 0` | yes | Relative rank used for inclusive minimum-grade comparisons within this type. Higher means stronger; values need not be contiguous, but duplicate ordinals are rejected. Ordinals are never compared across types. |
| `equivalence` | number `0..6` | yes | Value on the A-level points scale (`U = 0` through `A* = 6`). For a satisfied entry-equivalent route, this value can raise—but never lower—the subject's regression prediction. It does not establish grade ordering; `ordinal` does that. |

Known qualification types in the shipped schema:

- `gcse`
- `a_level`
- `btec_extended_certificate`
- `btec_diploma`
- `nvq`

Notes:

- This file defines ordering and equivalence, not which subject a qualification applies to. Subject-specific use lives in `data/catalogue.yaml`.
- Adding a new grade to an existing qualification type is a data change.
- Adding a brand-new qualification type is not a data-only change; the type vocabulary is also compiled and repeated in schema.

Example:

```yaml
qualifications:
  - type: a_level
    grades:
      - grade: u
        ordinal: 0
        equivalence: 0.0
      - grade: c
        ordinal: 3
        equivalence: 3.0
      - grade: a
        ordinal: 5
        equivalence: 5.0

  - type: btec_diploma
    grades:
      - grade: pass
        ordinal: 0
        equivalence: 3.0
      - grade: merit
        ordinal: 1
        equivalence: 4.0
      - grade: distinction
        ordinal: 2
        equivalence: 5.0
```

How to read that example:

- within `a_level`, `a` is stronger than `c` because its `ordinal` is higher,
- a `btec_diploma` `distinction` is treated as equivalent to `5.0` A-level points for uplift and threshold checks,
- the meaning of `distinction` itself comes from this file, not from `catalogue.yaml`.

### `workflows/eligibility.yaml`

The whole-student gate. If any gate condition fails, the student is ineligible and every subject is
returned red without running `subject-ratings`.

Top-level fields:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `WorkflowName` | string | yes | Unique name by which compiled machinery selects this workflow. The shipped gate must be named `eligibility`; renaming it without changing the host lookup prevents evaluation. |
| `Rules` | array of rule objects | yes | Non-empty ordered gate conditions. All shipped eligibility rules must succeed for the student to proceed to subject rating; failures are collected into the ineligibility result. |

Rule object fields used here:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `RuleName` | string | yes | Unique, stable identifier within the workflow. For the shipped gate it also keys the compiled failure-reason projection, so changing a standard name is a behavioral change rather than a cosmetic edit. |
| `SuccessEvent` | string | no | Human-readable text attached by RulesEngine on success. Eligibility output does not use it as the failure explanation, but it remains useful workflow metadata and must accurately describe the expression. |
| `ErrorMessage` | string | no | Permitted by the schema but not used by the shipped workflows. Eligibility failure reasons are projected from the loaded `data/thresholds.yaml` values in compiled code (keyed by rule name), not read from this field, so the explanation cannot drift from the threshold that actually fired. |
| `Expression` | string | yes | C#-style lambda body compiled and evaluated by RulesEngine against the workflow inputs. It must return Boolean; startup probe-evaluation catches syntax/member errors but cannot prove that the policy comparison is logically correct. |
| `LocalParams` | array | no | Rule-local named expressions evaluated for this rule and made available to its main `Expression`. Use them to name or reuse intermediate calculations; names must be unique within the rule. |

`LocalParams` entries:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `Name` | string | yes | Identifier bound to the local result and referenced directly from the parent rule expression, such as `passCount`. It must be a valid, unique expression identifier. |
| `Expression` | string | yes | C#-style expression evaluated against the same workflow inputs as the parent rule. Its inferred result type becomes the type of `Name`. |

The shipped gate contains three ordered rules:

1. `EnglishLanguagePass`
2. `MathsPass`
3. `EnoughPasses`

Notes:

- These expressions read `lookup`, `gcses`, and `policy`, not `facts`.
- The workflow is intentionally small and rigid; tests and linter expectations assume the standard gate shape.

Example:

```yaml
WorkflowName: eligibility
Rules:
  - RuleName: EnglishLanguagePass
    SuccessEvent: GCSE English Language at pass grade or above
    Expression: >-
      lookup.Grade("english_language") >= policy.PassGrade

  - RuleName: EnoughPasses
    SuccessEvent: Enough GCSE passes for eligibility
    LocalParams:
      - Name: passCount
        Expression: >-
          gcses.Count(g => g.Grade >= policy.PassGrade)
    Expression: >-
      passCount >= policy.MinPasses
```

How to read that example:

- `lookup.Grade("english_language")` fetches the student's English Language GCSE,
- `policy.PassGrade` comes from `data/thresholds.yaml`,
- `LocalParams` lets the workflow compute `passCount` once and reuse it in the rule.
- The rules carry no `ErrorMessage`: a failed rule's reason text is projected from the loaded
  thresholds in compiled code (keyed by `RuleName`), so changing `pass_grade` or `min_passes`
  updates the explanation as well as the verdict.

### `workflows/subject-ratings.yaml`

The per-subject base-tier workflow. Every subject normally appears as a three-rule ordered block:
`green`, `amber`, then `red`.

Top-level fields are the same as `eligibility.yaml`:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `WorkflowName` | string | yes | Unique name used by compiled machinery to select the rating workflow. The shipped value must remain `subject-ratings` unless the host lookup changes with it. |
| `Rules` | array of rule objects | yes | Ordered rules covering every catalogue subject with exactly one `green`, `amber`, and `red` rule. Startup linting validates coverage, tier names, order, and catch-all shape. |

Rule object fields used here:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `RuleName` | string | yes | Required `<subject>:<tier>` identity, for example `physics:green`. The subject must exist in the catalogue and the tier must be `green`, `amber`, or `red`; this name is parsed to build the result. |
| `SuccessEvent` | string | no | Explanation copied into the subject result when this rule is the winning base tier. A later constraint downgrade replaces it with the adjustment reason, so this text should explain only the base rule. |
| `Expression` | string | yes | Boolean C#-style expression evaluated against one immutable `facts` view. All three rules are evaluated, after which the first successful tier in workflow order wins; the red expression must therefore remain an unconditional `true` fallback. |

Common expression inputs in this workflow:

| Member | Meaning |
| --- | --- |
| `facts.Gcse("subject")` | Looks up a GCSE grade by subject using case-insensitive matching. Returns `0` when absent, so expressions must not mistake a missing GCSE for a supplied low grade when that distinction matters. |
| `facts.Predicted("subject")` | Returns predicted A-level points on the `0..6` scale after taking the greater of the clamped regression prediction and any qualifying entry-equivalent uplift. |
| `facts.DfeProbabilityAtOrAbove("subject", ALevelGrade.X)` | Returns the transition-matrix probability, as a `0..1` proportion, of grade `X` or better for the subject at the student's GCSE-average evidence band. |
| `facts.HasEntryEquivalent("subject")` | Returns `true` when at least one prior qualification matches one of that catalogue subject's entry-equivalent routes by subject, type, and inclusive minimum ordinal. |
| `facts.Average` | Arithmetic mean of all supplied GCSE grades. Missing subjects are not zero-filled; an empty GCSE map produces `0.0`. |
| `facts.Age` | Student's age in completed years on the evaluation as-of date. A missing date of birth produces `0`, although normal document validation requires the field. |
| `facts.TopEntry`, `facts.StrongEntry`, `facts.StandardEntry` | Direct projections of the three named GCSE boundaries in `data/thresholds.yaml`; they acquire meaning only through the comparisons written in a rule. |
| `facts.FurtherMathsAverageEntry`, `facts.HumanitiesAverageEntry` | Direct projections of the named GCSE-average boundaries. Their names are policy conventions, not restrictions on which subject rules can reference them. |
| `facts.MinDfeGreenProbabilityAtOrAbove`, `facts.MinDfeAmberProbabilityAtOrAbove` | Direct projections of the configured `0..1` DfE probability floors for green and amber expressions. |
| `facts.AdultAge` | Direct projection of the configured whole-years adult-age boundary; the expression supplies the comparison operator. |

Notes:

- The engine evaluates all rules, then takes the first successful tier for a subject. Ordering is therefore semantic, not cosmetic.
- The `red` rule is expected to be an unconditional `true` catch-all.
- This workflow sets the base tier only. Cross-subject downgrades are applied later from `data/catalogue.yaml`.

Example:

```yaml
WorkflowName: subject-ratings
Rules:
  - RuleName: physics:green
    SuccessEvent: Entry met; predicted A-level grade at or above the green threshold
    Expression: >-
      facts.Gcse("maths") >= facts.TopEntry
      && facts.Gcse("physics") >= facts.StrongEntry
      && facts.Predicted("physics") >= ALevelGrade.B
      && facts.DfeProbabilityAtOrAbove("physics", ALevelGrade.B) >= facts.MinDfeGreenProbabilityAtOrAbove

  - RuleName: physics:amber
    SuccessEvent: Entry met; predicted A-level grade at or above the amber threshold
    Expression: >-
      facts.Gcse("maths") >= facts.TopEntry
      && facts.Gcse("physics") >= facts.StrongEntry
      && facts.Predicted("physics") >= ALevelGrade.C
      && facts.DfeProbabilityAtOrAbove("physics", ALevelGrade.C) >= facts.MinDfeAmberProbabilityAtOrAbove

  - RuleName: physics:red
    SuccessEvent: Entry requirement unmet or predicted grade below the amber threshold
    Expression: >-
      true
```

How to read that example:

- a strong Physics recommendation needs both GCSE entry conditions and post-prediction evidence,
- the amber rule is a looser version of the green rule,
- the unconditional red rule means every student will match some tier even if the stricter rules fail.

## Validation Schema Files

These JSON files are not runtime policy. They are the structural contracts the loaders use to reject
bad YAML before evaluation starts.

### `data/thresholds.schema.json`

Defines:

- the required threshold keys,
- numeric ranges such as GCSE `1..9` and probability `0..1`,
- which advice-related fields are optional.

Change this only when the shape of `data/thresholds.yaml` changes.

### `data/catalogue.schema.json`

Defines:

- the `subjects` array shape,
- allowed subject-id pattern,
- allowed exclusion/prerequisite shapes,
- allowed qualification-type vocabulary for `entry_equivalents` and `restudy_bar`,
- optional versus required subject fields.

This schema does not enforce every business invariant. For example, exclusion symmetry is checked in
compiled code, not in the schema.

### `data/qualifications.schema.json`

Defines:

- the top-level `qualifications` array,
- grade-entry structure,
- qualification-type vocabulary,
- `equivalence` range on the A-level-points scale.

Coverage and duplicate-grade/ordinal checks happen in code after schema validation.

### `workflows/workflow.schema.json`

Defines the generic RulesEngine workflow document shape shared by `eligibility.yaml` and
`subject-ratings.yaml`.

Relevant top-level fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `WorkflowName` | yes | Unique workflow identifier used for RulesEngine execution and host lookup. |
| `WorkflowsToInject` | no | Names of other workflows for RulesEngine to execute as dependencies of this workflow. The project does not lint or rely on composition, so introducing it requires integration tests and host-semantics review. |
| `GlobalParams` | no | Named expressions available to every rule in the workflow. They are evaluated in workflow scope; shipped workflows use rule-local parameters instead. |
| `Rules` | yes | Non-empty array of top-level expression or composite rules. Array order is preserved and is semantically significant for subject-tier selection. |

Relevant rule fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `RuleName` | yes | Identifier unique within the workflow. Project code parses eligibility names and subject-rating `<subject>:<tier>` names, so those conventions are contractual. |
| `Enabled` | no | RulesEngine execution toggle. A disabled rule cannot succeed; disabling a shipped tier also violates the project's expected complete three-tier policy even if the generic schema accepts it. |
| `ErrorMessage` | no | Permitted by the schema but not used by shipped workflows; eligibility failure reasons are projected from thresholds in compiled code. |
| `SuccessEvent` | no | Text associated with successful evaluation. Subject-rating results expose this as their base explanation; eligibility failures do not. |
| `Operator` | no | RulesEngine composite operator applied to child `Rules`, for example `And` or `Or`. The schema accepts any string and therefore does not validate the operator vocabulary; shipped workflows use flat lambda expressions instead. |
| `RuleExpressionType` | no | Expression dialect selector. The only schema-supported value is `LambdaExpression`; normalization supplies it for rules with an `Expression`. |
| `Expression` | conditional | Boolean lambda expression for a leaf rule. Required when the rule does not contain child `Rules`; project startup probe-evaluates it to force compilation. |
| `WorkflowsToInject` | no | Names of workflows injected from this rule. This RulesEngine composition surface is accepted structurally but unused by project policy. |
| `LocalParams` | no | Name/expression pairs scoped to this rule and evaluated before its main expression. |
| `Rules` | conditional | Child-rule array for a composite rule, combined by `Operator`. A rule uses either a leaf `Expression` or nested rules according to the schema constraints. |
| `Actions` | no | RulesEngine success/failure action configuration. The engine has no configured action implementations, so this is outside the supported project subset. |
| `Properties` | no | Free-form JSON metadata retained on the rule. Project evaluation, explanations, and linting do not consume it. |

For this project, the practical subset is much smaller than the full schema surface:

- top-level `WorkflowName`
- top-level `Rules`
- per-rule `RuleName`
- per-rule `SuccessEvent`
- per-rule `Expression`
- optional `LocalParams`

## Extension and Example Files

### `examples/custom-subject/data/catalogue.append.yaml`

Append-only snippet showing the minimum `data/catalogue.yaml` row for a custom subject. It is not a
standalone catalogue file.

Fields shown:

| Field | Meaning |
| --- | --- |
| `subject` | Canonical id for the new subject. The appended workflow's three `RuleName` prefixes must use exactly the same id. |
| `priority_weight` | Positive policy weight used for aggregate scoring, same-rating ordering, mutual-exclusion resolution, and optional green-cap demotion. It must differ from the weight of any subject this one excludes. |
| `regression.slope` / `regression.intercept` | Coefficients of the average-GCSE-to-A-level-points prediction line. Choose these from an evidenced model; the engine clamps the result to `0..6`. |

### `examples/custom-subject/workflows/subject-ratings.append.yaml`

Append-only snippet showing the matching `green` / `amber` / `red` workflow rules for the custom
subject. It is not a standalone workflow file.

Example:

```yaml
  - RuleName: 'drama:green'
    SuccessEvent: 'Entry met; predicted A-level grade at or above the green threshold'
    Expression: >-
      facts.Average >= facts.HumanitiesAverageEntry && facts.Predicted("drama") >= ALevelGrade.B
  - RuleName: 'drama:amber'
    SuccessEvent: 'Entry met; predicted A-level grade at or above the amber threshold'
    Expression: >-
      facts.Average >= facts.HumanitiesAverageEntry && facts.Predicted("drama") >= ALevelGrade.C
  - RuleName: 'drama:red'
    SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
    Expression: >-
      true
```

This is the workflow half of “add a new subject by data”. The catalogue row defines what `drama`
is; this append snippet defines how `drama` gets rated.

### `examples/student.json` and `examples/student.yaml`

Sample student documents, not engine configuration. They are still editable JSON/YAML surfaces that
matter when integrating the CLI or a host application.

Student fields:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `student.id` | string | yes | Opaque correlation identifier copied unchanged to the evaluation result. It does not participate in any decision and need not encode personal information. |
| `student.gcses` | object | yes | Case-insensitive subject-to-integer-grade map on the GCSE `1..9` scale. Present entries drive the average and pass count; an absent subject is returned as grade `0` by workflow lookup. |
| `student.chosen_a_levels` | array of subject ids | no | Catalogue subjects to which the student is already committed. They can satisfy `chosen` and `qualifying` prerequisites and activate prior-choice exclusions; they are not automatically added to the recommendation set. |
| `student.hobbies` | array of strings | no | Activity tags compared case-insensitively with catalogue prefix rules. One tag may satisfy a required prefix and/or trigger a blocking prefix; omit for no declared activities. |
| `student.prior_qualifications` | array | no | Existing typed qualifications. Matching entries can open an entry-equivalent route, raise its subject prediction to the configured equivalence, or trigger a same-subject restudy bar. |
| `student.date_of_birth` | `YYYY-MM-DD` string | yes | Calendar date used with the evaluation's as-of date to calculate completed years for `facts.Age`. It is not interpreted relative to the machine's current date inside a run. |

Prior-qualification entries:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `subject` | string | yes | Qualification's subject token, for example `applied_science`. Entry-equivalent and restudy matching are ordinal and case-insensitive; the token is otherwise free-form. |
| `type` | known qualification type | yes | Qualification family selecting the relevant scale in `data/qualifications.yaml`. It must also be permitted by the compiled/schema vocabulary. |
| `grade` | string | yes | Grade token resolved within the selected type. It supplies both the type-local `ordinal` for minimum-grade checks and the A-level-points `equivalence` for uplift; unknown tokens are rejected. |

Example:

```yaml
student:
  id: S-1001
  date_of_birth: "2009-09-01"
  gcses:
    maths: 8
    english_language: 7
    biology: 6
    chemistry: 6
    music: 5
  chosen_a_levels:
    - maths
  hobbies:
    - plays_piano
  prior_qualifications:
    - subject: applied_science
      type: btec_diploma
      grade: distinction
```

How to read that example:

- the student has the GCSE data used by the eligibility and subject-rating workflows,
- `chosen_a_levels: [maths]` can satisfy a `requires: chosen` prerequisite such as Further Maths,
- `plays_piano` satisfies Music's `required_activities: [plays_]`,
- the `btec_diploma` in `applied_science` can unlock Biology's `entry_equivalents` rule.

## Change Boundaries

Routine policy changes are data-only when they stay within the existing vocabularies and shapes.

Examples of data-only changes:

- retuning a threshold in `data/thresholds.yaml`,
- changing subject clashes or prerequisites in `data/catalogue.yaml`,
- adding a grade to an existing qualification type in `data/qualifications.yaml`,
- changing a subject's workflow expression in `workflows/subject-ratings.yaml`,
- adding a new A-level subject by updating both `data/catalogue.yaml` and `workflows/subject-ratings.yaml`.

Examples that are not data-only:

- adding a brand-new qualification type,
- adding a brand-new relationship type beyond the existing prerequisite/exclusion/own-time/veto/restudy shapes,
- changing the GCSE input vocabulary in compiled code,
- changing evaluation semantics in the constraint pass or aggregation.

## Safe Editing Checklist

1. Put the change in the right file.
2. Keep subject ids and qualification types consistent across files.
3. Preserve the `green -> amber -> red` ordering in `subject-ratings.yaml`.
4. Keep exclusions symmetric in `data/catalogue.yaml`.
5. Run the workflow linter after workflow edits.
6. Run the normal build/format/test gate before committing.
