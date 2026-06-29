# Configuration Reference

This document is the field-level reference for every editable YAML/JSON surface that shapes
EnrolmentRules behaviour.

It complements, rather than replaces:

- [`README.md`](../README.md) for setup, CLI usage, and the student input shape.
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
| `pass_grade` | integer `1..9` | yes | GCSE grade treated as a pass by the eligibility gate. |
| `min_passes` | integer `>= 1` | yes | Minimum count of GCSE passes required for whole-student eligibility. |
| `top_entry` | integer `1..9` | yes | Highest GCSE threshold used by stricter subject-entry rules. |
| `strong_entry` | integer `1..9` | yes | Middle GCSE threshold used by many subject-entry rules. |
| `standard_entry` | integer `1..9` | yes | Lowest normal GCSE threshold used by subject-entry rules. |
| `further_maths_average_entry` | number `0..9` | yes | Minimum average GCSE score for Further Maths workflow rules. |
| `humanities_average_entry` | number `0..9` | yes | Minimum average GCSE score for humanities-style workflow rules. |
| `min_dfe_green_probability_at_or_above` | number `0..1` | yes | Minimum DfE `P(grade or above)` for a green tier. |
| `min_dfe_amber_probability_at_or_above` | number `0..1` | yes | Minimum DfE `P(grade or above)` for an amber tier. |
| `adult_age` | integer `>= 1` | yes | Age cutoff used by age-gated workflow rules such as Art. |
| `max_green_choices` | integer `>= 1` | no | Optional cap on surviving green subjects after constraints. Omit to disable the green cap. |
| `amber_tariff_factor` | number `0..1` | yes | Weighting factor when amber results contribute to summary tariff figures. |
| `advice_considers_unsat_gcses` | boolean | no | Diagnostic advisor switch. `true` allows advice to suggest sitting a brand-new GCSE. |
| `advice_max_grade_cost` | integer `>= 1` | no | Maximum total GCSE grade uplift the advisor may propose in one search. |
| `advice_max_subjects_changed` | integer `>= 1` | no | Maximum number of distinct GCSE subjects the advisor may change in one proposal. |
| `advice_max_pipeline_evaluations` | integer `>= 1` | no | Optional hard cap on full pipeline runs per `--advise` call. Omit for unlimited. |

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
amber_tariff_factor: 0.5
```

What this means in practice:

- a GCSE grade of `4` counts as a pass,
- a student needs at least `5` passes to clear the eligibility gate,
- a subject can choose to require `7`, `6`, or `5` in a GCSE depending on how selective it is,
- a green tier usually needs stronger DfE evidence than an amber tier.

### `data/catalogue.yaml`

The subject catalogue is the data source for prediction coefficients, cross-subject relationships,
entry equivalents, restudy bars, and UCAS weighting.

Top-level shape:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `subjects` | array of subject entries | yes | One entry per A-level subject known to the engine. |

Each subject entry:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `subject` | `snake_case` string | yes | Subject identifier used across workflows, results, and input documents. |
| `ucas_weight` | positive integer | yes | Ranking/tie-break weight used by aggregation and optional green-cap demotion. |
| `regression` | object | yes | Subject-specific linear prediction coefficients. |
| `exclusions` | array | no | Timetable or policy clashes with other subjects. Must be declared symmetrically on both sides. |
| `required_activities` | array of strings | no | Hobby/activity prefixes required for the subject's own-time rule. Missing prefix match downgrades to amber. |
| `blocking_activities` | array of strings | no | Hobby/activity prefixes that veto the subject outright. Matching prefix downgrades to red. |
| `prerequisites` | array | no | Dependency groups that must be satisfied by other subjects or chosen A-levels. |
| `entry_equivalents` | array | no | Prior qualifications that can satisfy the subject's entry path. |
| `restudy_bar` | object | no | Prior-qualification types that bar re-studying the same subject. |

`regression` object:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `slope` | number | yes | Multiplier in the subject's linear prediction line. |
| `intercept` | number | yes | Offset in the subject's linear prediction line. |

`exclusions` entries:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `other` | `snake_case` subject id | yes | The clashing subject. |
| `severity` | `amber` or `red` | yes | Downgrade severity when both subjects survive the base rating stage. |

`prerequisites` entries:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `any_of` | non-empty array of subject ids | yes | Alternative subjects that satisfy this prerequisite group. |
| `severity` | `amber` or `red` | no | Downgrade severity when the group is unmet. Defaults to the engine's hard requirement behaviour. |
| `requires` | `qualifying` or `chosen` | no | Satisfaction mode. `qualifying` accepts a green/amber result or a chosen A-level; `chosen` requires a committed `chosen_a_levels` entry. |

`entry_equivalents` entries:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `subject` | string | yes | Prior-qualification subject that counts for equivalence, for example `applied_science`. |
| `type` | known qualification type | yes | Qualification vocabulary shared with `data/qualifications.yaml`. |
| `min_grade` | string | yes | Lowest prior-qualification grade that qualifies for this equivalent path. |

`restudy_bar` object:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `types` | non-empty array of qualification types | yes | Prior-qualification types that trigger the restudy check for the same subject. |
| `severity` | `amber` or `red` | no | Downgrade severity. If omitted, engine defaults apply. |

Notes:

- Subject ids are data-driven for A-level subjects. Adding a subject here is the first half of a data-only subject addition.
- `exclusions`, `prerequisites`, `required_activities`, `blocking_activities`, `entry_equivalents`, and `restudy_bar` all operate downstream in compiled machinery; this file decides the policy data they read.
- Structural validity comes from JSON Schema; broader invariants such as exclusion symmetry and coverage are enforced at load time in code.

Examples:

Minimal subject row:

```yaml
subjects:
  - subject: maths
    ucas_weight: 50
    regression:
      slope: 0.80
      intercept: -1.00
```

Subject row with relationships:

```yaml
subjects:
  - subject: further_maths
    ucas_weight: 56
    regression: { slope: 1.00, intercept: -2.00 }
    prerequisites:
      - any_of: [ maths ]
        requires: chosen

  - subject: music
    ucas_weight: 36
    regression: { slope: 0.85, intercept: -1.70 }
    required_activities: [ plays_ ]
    blocking_activities: [ plays_trombone ]

  - subject: biology
    ucas_weight: 44
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
| `qualifications` | array of type entries | yes | One entry per known qualification type. |

Each qualification type entry:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `type` | qualification type | yes | One of the compiled qualification vocab values. |
| `grades` | non-empty array of grade entries | yes | Ordered grade scale for that qualification type. |

Each grade entry:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `grade` | string | yes | External grade token, for example `distinction` or `a_star`. |
| `ordinal` | integer `>= 0` | yes | Monotone ordering within that qualification type. Higher means stronger. |
| `equivalence` | number `0..6` | yes | A-level-points equivalence used when prior qualifications lift prediction or satisfy thresholds. |

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
| `WorkflowName` | string | yes | Workflow identifier. Shipped value: `eligibility`. |
| `Rules` | array of rule objects | yes | Ordered RulesEngine rules for the gate. |

Rule object fields used here:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `RuleName` | string | yes | Stable rule identifier. |
| `SuccessEvent` | string | no | Human-readable reason emitted when the rule passes. |
| `ErrorMessage` | string | no | Human-readable reason emitted when the rule fails. |
| `Expression` | string | yes | Lambda expression evaluated by RulesEngine. |
| `LocalParams` | array | no | Named sub-expressions computed before the main expression. |

`LocalParams` entries:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `Name` | string | yes | Local variable name visible to the rule expression. |
| `Expression` | string | yes | Lambda expression that computes the local value. |

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
    ErrorMessage: GCSE English Language below the pass grade
    Expression: >-
      lookup.Grade("english_language") >= policy.PassGrade

  - RuleName: EnoughPasses
    SuccessEvent: Enough GCSE passes for eligibility
    ErrorMessage: Fewer than the required number of GCSE passes
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

### `workflows/subject-ratings.yaml`

The per-subject base-tier workflow. Every subject normally appears as a three-rule ordered block:
`green`, `amber`, then `red`.

Top-level fields are the same as `eligibility.yaml`:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `WorkflowName` | string | yes | Workflow identifier. Shipped value: `subject-ratings`. |
| `Rules` | array of rule objects | yes | Ordered RulesEngine rules covering all subjects. |

Rule object fields used here:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `RuleName` | string | yes | Conventionally `<subject>:<tier>`, for example `physics:green`. |
| `SuccessEvent` | string | no | Human-readable explanation for why this tier matched. |
| `Expression` | string | yes | RulesEngine lambda expression. |

Common expression inputs in this workflow:

| Member | Meaning |
| --- | --- |
| `facts.Gcse("subject")` | GCSE grade for a subject, or `0` if absent. |
| `facts.Predicted("subject")` | Predicted A-level points for a subject. |
| `facts.DfeProbabilityAtOrAbove("subject", ALevelGrade.X)` | DfE probability evidence for grade `X` or above. |
| `facts.HasEntryEquivalent("subject")` | Whether prior qualifications satisfy the subject's equivalent-entry path. |
| `facts.Average` | Average GCSE score. |
| `facts.Age` | Whole-years age on the run's as-of date. |
| `facts.TopEntry`, `facts.StrongEntry`, `facts.StandardEntry` | Threshold values forwarded from `data/thresholds.yaml`. |
| `facts.FurtherMathsAverageEntry`, `facts.HumanitiesAverageEntry` | Average-based thresholds from `data/thresholds.yaml`. |
| `facts.MinDfeGreenProbabilityAtOrAbove`, `facts.MinDfeAmberProbabilityAtOrAbove` | Probability floors from `data/thresholds.yaml`. |
| `facts.AdultAge` | Adult-age cutoff from `data/thresholds.yaml`. |

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
| `WorkflowName` | yes | Workflow identifier. |
| `WorkflowsToInject` | no | RulesEngine workflow composition hook. Not used by the shipped files. |
| `GlobalParams` | no | Workflow-scoped named expressions. Not used by the shipped files. |
| `Rules` | yes | Top-level rule array. |

Relevant rule fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `RuleName` | yes | Stable rule identifier. |
| `Enabled` | no | RulesEngine toggle. Not used by shipped workflows. |
| `ErrorMessage` | no | Failure text for gate-style rules. |
| `SuccessEvent` | no | Success text. |
| `Operator` | no | Composite-rule operator. Not used by shipped workflows. |
| `RuleExpressionType` | no | Schema permits `LambdaExpression`; loader fills this automatically for real rules. |
| `Expression` | conditional | Required for expression rules. |
| `WorkflowsToInject` | no | Nested workflow injection hook. Not used by shipped workflows. |
| `LocalParams` | no | Rule-scoped named expressions. |
| `Rules` | conditional | Child rules for composite rules. |
| `Actions` | no | RulesEngine action hook. Not used by shipped workflows. |
| `Properties` | no | Arbitrary metadata object. Not used by shipped workflows. |

For this project, the practical subset is much smaller than the full schema surface:

- top-level `WorkflowName`
- top-level `Rules`
- per-rule `RuleName`
- per-rule `SuccessEvent`
- per-rule `ErrorMessage` in eligibility
- per-rule `Expression`
- optional `LocalParams`

## Extension and Example Files

### `examples/custom-subject/data/catalogue.append.yaml`

Append-only snippet showing the minimum `data/catalogue.yaml` row for a custom subject. It is not a
standalone catalogue file.

Fields shown:

| Field | Meaning |
| --- | --- |
| `subject` | New subject id. |
| `ucas_weight` | Aggregation weight for the new subject. |
| `regression.slope` / `regression.intercept` | Prediction coefficients for the new subject. |

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
| `student.id` | string | yes | Student identifier carried into results. |
| `student.gcses` | object | yes | GCSE subject-to-grade map on the `1..9` scale. |
| `student.chosen_a_levels` | array of subject ids | no | Already-committed A-levels used by downstream constraints. |
| `student.hobbies` | array of strings | no | Activity tags matched by prefix for own-time and veto rules. |
| `student.prior_qualifications` | array | no | Prior typed qualifications used for entry equivalents, uplift, and restudy bars. |
| `student.date_of_birth` | `YYYY-MM-DD` string | yes | Date from which run-time age is derived. |

Prior-qualification entries:

| Field | Type | Required | Meaning |
| --- | --- | --- | --- |
| `subject` | string | yes | Qualification subject, for example `applied_science`. |
| `type` | known qualification type | yes | Qualification vocabulary shared with `data/qualifications.yaml`. |
| `grade` | string | yes | Grade token valid for that qualification type. |

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
