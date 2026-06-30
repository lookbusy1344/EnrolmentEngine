# Rule Authoring Guide

A practical guide to writing and changing rules in EnrolmentRules. Routine configuration is
**entirely a YAML process** — the **rules-as-data YAML** plus the three data tables — and that is
what this guide covers, along with every datum a rule can read: GCSE grades, the average GCSE score,
the predicted A-level grades, and the DfE transition-matrix probabilities. Writing rules needs a
little comfort with simple expressions (`>=`, `&&`) and an engine-driven test for every changed rule;
it needs no C# unless the change extends a compiled vocabulary or introduces a new evaluator shape.

If you only read one thing first, read [Where a rule belongs](#1-where-a-rule-belongs): putting a
rule in the wrong file is the single most common mistake.

The appendix, [How the engine applies your rules](#appendix-how-the-engine-applies-your-rules-background),
is background — what the engine *does* with your rules. It is code you never edit; read it only to
understand or debug an effect.

Recommended reading order:

1. [Technical reference](technical-reference.md) for setup, CLI usage, input shape, and library
   usage.
2. [Configuration reference](configuration-reference.md) for the field-level map of the editable
   YAML/JSON surfaces.
3. [Guided walk-through](walkthrough.md) for the end-to-end pipeline and decision tables.
4. This guide for changing workflow/data rules safely.
5. [Engine-choice rationale](engine-choice.md) if you want the RulesEngine vs NRules/RETE design
   argument.

---

## Table of contents

1. [Where a rule belongs](#1-where-a-rule-belongs)
2. [The data available to rules](#2-the-data-available-to-rules)
3. [Writing engine rules (YAML)](#3-writing-engine-rules-yaml)
4. [Changing policy data (no code)](#4-changing-policy-data-no-code)
5. [Validating your changes](#5-validating-your-changes)
6. [Worked examples](#6-worked-examples)
7. [Common pitfalls](#7-common-pitfalls)
8. [Quick reference](#8-quick-reference)
- [Appendix: How the engine applies your rules](#appendix-how-the-engine-applies-your-rules-background)

---

## 1. Where a rule belongs

Four data locations hold all the rules; the choice between them is decided by *what the rule reads*.

| If the rule…                                                              | It goes in                 |
|---------------------------------------------------------------------------|----------------------------|
| decides **one** student in isolation (eligibility, a subject's base tier) | `workflows/*.yaml`         |
| is a **number** a rule reads                                              | `data/thresholds.yaml`     |
| says **which** subjects relate (clashes, prerequisites, weights, bars)    | `data/catalogue.yaml`      |
| is the cross-type grade ordering for prior qualifications                 | `data/qualifications.yaml` |

One case is **not** a rule you write: anything that needs to know how a *different* subject was rated
— exclusions, prerequisites, the optional green cap. A YAML rule cannot read another rule's outcome,
so the engine handles cross-subject decisions itself (background in the
[appendix](#appendix-how-the-engine-applies-your-rules-background)). See
[the engine-choice rationale](engine-choice.md) for why.

**Decision shortcut**

```
Does the rule need to look at another subject's rating?
├── Yes → not a YAML rule — the engine handles this (see appendix)
└── No  → Is it a single per-student true/false or tier decision?
         ├── Yes → workflow YAML (reads numbers from thresholds.yaml)
         └── No, it's just a number or a relationship → data YAML
                  ├── a tuning number          → thresholds.yaml
                  ├── a subject relationship    → catalogue.yaml
                  └── a grade-scale equivalence → qualifications.yaml
```

### Lifecycle of one rating — where your rule fires

A rule change only makes sense if you know *when* it runs. The engine applies a fixed pipeline to
each student:

```
1. Predict          raw GCSEs → predicted A-level points + DfE evidence + age
2. Eligibility gate  workflows/eligibility.yaml   ── ineligible? → every subject Red, STOP ↓
3. Base ratings      workflows/subject-ratings.yaml → one green/amber/red per subject
4. Constraint pass   cross-subject downgrades folded in (most-severe-wins)
5. Green cap         (optional, off by default) — no-op unless a cap is set
6. Summarise         UCAS tariff, green/amber counts, ranking
```

Consequences worth internalising before you author:

- **The gate short-circuits.** An ineligible student is red in *every* subject with the gate's reason,
  and the rating workflow **never runs**. A rating rule cannot rescue an ineligible student.
- **Downgrade-only, after the base.** Every stage after (3) can only *worsen* a rating. Your YAML rule
  sets the ceiling; the constraint pass can only lower it. Nothing upgrades.
- **The green cap is optional and off by default.** In normal operation stage (5) does nothing and every
  green stays green — the engine reports what a student is *allowed* to study, it does not force an
  "optimal" shortlist. Only when an admin sets `max_green_choices` does the cap engage, demoting the
  lowest-weighted surplus greens to amber. Treat "surplus green" as a rarely-used policy lever, not a
  normal outcome.

---

## 2. The data available to rules

Every value a rule can read, where it comes from, and which rules can see it.

### 2.1 The raw student input

A student document has this shape:

```json
{
  "student": {
    "id": "S1001",
    "date_of_birth": "2008-09-01",
    "gcses": { "maths": 8, "english_language": 7, "physics": 7 },
    "hobbies": ["plays_piano", "chess_club"],
    "chosen_a_levels": ["maths"],
    "prior_qualifications": [
      { "subject": "applied_science", "type": "btec_diploma", "grade": "distinction" }
    ]
  }
}
```

- `gcses` — a `subject → grade` map on the **GCSE 1–9 scale**. An absent key means *not taken*; it
  reads as `0` (below the lowest grade), so a missing GCSE can never satisfy a threshold.
- `hobbies` — opaque free-form activity tags, matched by **prefix** for own-time requirements and
  vetoes (e.g. `plays_` matches `plays_piano`).
- `chosen_a_levels` — A-levels the student has already committed to. Constraint *triggers*, not
  filters: they drive prerequisites (`requires: chosen`) and prior-choice exclusions.
- `prior_qualifications` — typed Level-3 facts (`subject`/`type`/`grade`) that can open an entry
  path, lift the prediction, or trip a restudy bar.
- `date_of_birth` — the raw fact; **age is derived**, not stored (see below).

GCSE subject keys are validated against the list of known GCSE subjects — an unknown key is rejected
immediately rather than silently scoring a wrong red.

### 2.2 The derived prediction profile

Before the rules run, each student's raw facts are turned into a predicted profile, which the rating
rules read. It carries:

| Field               | Meaning                                                                                 |
|---------------------|-----------------------------------------------------------------------------------------|
| `AverageGcseScore`  | Mean over the **present** GCSEs, on the 1–9 scale. The single regression feature.       |
| `PredictedGrades`   | One continuous A-level points prediction per subject, on the `ALevelGrade` scale.       |
| `TransitionEvidence`| One DfE transition-matrix row per subject for the student's prior-attainment band.       |
| `Age`               | Whole years as of the run's reference date, from `date_of_birth`. Drives age-gated entry.|
| `Hobbies`, `ChosenALevels`, `PriorQualifications` | Carried through unchanged for the constraint pass.    |

#### The predicted grades (regression)

Each subject's prediction is a **fixed one-feature linear regression** mapping the average GCSE score
to predicted A-level points:

```
points = clamp(slope · averageGcseScore + intercept, U, A*)
```

The **coefficients live in the catalogue** (`data/catalogue.yaml`, the `regression: { slope,
intercept }` block per subject), so retuning a prediction line is a data edit. The output is on the
**A-level points scale** (`ALevelGrade`: A\*=6 … U=0), which is distinct from the GCSE 1–9 input scale
— never conflate them.

> Maths and Further Maths carry divergent lines: Further Maths is steeper, so for the
> strongest students it out-predicts Maths. That crossover is what makes the Further-Maths-green /
> Maths-red prerequisite scenario reachable.

#### The DfE transition-matrix probabilities

A local projection of the official **DfE 16-to-18 transition matrices (2019)**, stored as a CSV
extract at `data/dfe-transition-matrices/`. The student's average GCSE score is bucketed into a
**prior-attainment band** (`< 1`, `1 to < 2`, …, `>=9`), and the matching row gives the per-grade
probabilities. `facts.DfeProbabilityAtOrAbove("maths", ALevelGrade.B)` sums the columns at or above a
grade — i.e. `P(B) + P(A) + P(A*)`.

This is **evidence**, distinct from the point prediction: the regression says *what grade we expect*;
the matrix says *how likely a grade-or-better is for students who started here*. A rating tier
typically requires **both** — predicted points above the bar **and** the national probability above
a floor. A subject with no matrix row returns `0.0`, so it can never clear a probability-gated tier.

### 2.3 What each rule can see

| Datum                          | Rating rule | Eligibility rule | Constraint pass |
|--------------------------------|:-----------:|:----------------:|:---------------:|
| GCSE grade by subject          | `facts.Gcse(key)` | `lookup.Grade(key)`  | yes     |
| Average GCSE score             | `facts.Average`   | —                    | yes     |
| Predicted A-level points       | `facts.Predicted(key)` | —               | via base rating |
| DfE probability at-or-above    | `facts.DfeProbabilityAtOrAbove(key, grade)` | — | via base rating |
| Age                            | `facts.Age`       | —                    | yes     |
| Entry equivalent (prior qual)  | `facts.HasEntryEquivalent(key)` | —      | yes     |
| All GCSEs (for pass-count)     | —                 | `gcses` array        | —       |
| Sibling subjects' ratings      | **never**         | **never**            | yes     |
| Hobbies / chosen / prior quals | —                 | —                    | yes     |

Rating rules read everything off `facts`; eligibility rules read `lookup` (single-subject grades),
`gcses` (the array, for counts), and `policy`.

---

## 3. Writing engine rules (YAML)

Engine rules are **expressions** stored in `workflows/`. The loader supplies the surrounding
boilerplate; you write the `Expression`.

### 3.1 The two workflows

- **`workflows/eligibility.yaml`** — the whole-student gate. Exactly three rules, in order:
  `EnglishLanguagePass`, `MathsPass`, `EnoughPasses`. The linter enforces that set and order.
- **`workflows/subject-ratings.yaml`** — three rules per subject, named `<subject>:<rating>`, in
  severity order **green → amber → red**, where `red` is the unconditional `true` catch-all.

### 3.2 The objects and members you can call

**`facts`** — the rating workflow's single input:

| Member                                          | Returns  | Meaning                                              |
|-------------------------------------------------|----------|------------------------------------------------------|
| `facts.Gcse("maths")`                           | `int`    | GCSE grade (0 if not taken)                          |
| `facts.Predicted("maths")`                      | `double` | Predicted A-level points (U if unmodelled)           |
| `facts.DfeProbabilityAtOrAbove("maths", g)`     | `double` | DfE P(≥ g) (0 if no row)                              |
| `facts.HasEntryEquivalent("biology")`           | `bool`   | A prior qualification satisfies entry                 |
| `facts.Average`                                 | `double` | Mean GCSE score                                       |
| `facts.Age`                                     | `int`    | Whole years at reference date                         |
| `facts.TopEntry` / `StrongEntry` / `StandardEntry` | `int` | Entry thresholds (forwarded from policy)              |
| `facts.FurtherMathsAverageEntry` / `HumanitiesAverageEntry` | `double` | Average-based entry bars                  |
| `facts.MinDfeGreenProbabilityAtOrAbove` / `…Amber…` | `double` | DfE probability floors                            |
| `facts.AdultAge`                                | `int`    | Adult-age cutoff for age-gated entry                  |

**`lookup`** and **`policy`** — the eligibility workflow's inputs:

| Member                  | Returns | Meaning                                  |
|-------------------------|---------|------------------------------------------|
| `lookup.Grade("maths")` | `int`   | GCSE grade (0 if not taken)              |
| `gcses`                 | array   | All GCSEs — iterate with LINQ for counts |
| `policy.PassGrade`      | `int`   | Pass grade (4)                           |
| `policy.MinPasses`      | `int`   | Minimum passes for eligibility           |
| `policy.*`              | —       | Same threshold surface as `facts.*`      |

**`ALevelGrade`** — the grade constants for the predicted/probability bars: `ALevelGrade.AStar`, `.A`,
`.B`, `.C`, `.D`, `.E`, `.U` (and `.Min` / `.Max`).

> **Do not put bare numbers in a rule.** Thresholds come from `policy.*` / `facts.*` (sourced from
> `thresholds.yaml`); grade bars come from `ALevelGrade.*`. A literal `4` or `5.0` in an expression
> is a magic number and will not survive review.

### 3.3 Rule patterns

A standard subject tier reads as *entry gate AND predicted-points bar AND DfE-probability floor*:

```yaml
  - RuleName: 'physics:green'
    SuccessEvent: 'Entry met; predicted A-level grade at or above the green threshold'
    Expression: >-
      facts.Gcse("maths") >= facts.TopEntry
      && facts.Gcse("physics") >= facts.StrongEntry
      && facts.Predicted("physics") >= ALevelGrade.B
      && facts.DfeProbabilityAtOrAbove("physics", ALevelGrade.B) >= facts.MinDfeGreenProbabilityAtOrAbove
```

The amber tier loosens the bars (e.g. `ALevelGrade.C` and `MinDfeAmberProbabilityAtOrAbove`); the red
tier is always the literal catch-all:

```yaml
  - RuleName: 'physics:red'
    SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
    Expression: >-
      true
```

Other shipped patterns worth copying:

- **Average-based entry** (no single GCSE gate): `facts.Average >= facts.HumanitiesAverageEntry && …`
  (History).
- **Age-gated entry** with a ternary: `facts.Gcse("art") >= (facts.Age >= facts.AdultAge ? facts.TopEntry : facts.StrongEntry)` (Art).
- **Entry equivalents** as an OR with the GCSE path:
  `(facts.Gcse("biology") >= facts.StrongEntry && facts.Gcse("chemistry") >= facts.StandardEntry || facts.HasEntryEquivalent("biology")) && …`.
- **A `LocalParams` sub-expression** for a reused value (eligibility's pass count):

```yaml
  - RuleName: 'EnoughPasses'
    LocalParams:
      - Name: 'passCount'
        Expression: >-
          gcses.Count(g => g.Grade >= policy.PassGrade)
    Expression: >-
      passCount >= policy.MinPasses
```

### 3.4 "First hit wins"

The engine runs **every** rule; it does not stop at the first match. It then takes the **first
successful rule in declared order** per subject. Because a green-eligible student also satisfies the
amber expression, the green-before-amber ordering is what makes green win — which is exactly why the
linter enforces green → amber → red ordering. Author your tiers so each is a *superset-eligible*
loosening of the one above it.

### 3.5 Always lint a rule change

A workflow expression is not compile-time checked with the C# project. Startup probe-evaluation does
reject an invalid member binding such as `facts.Avrage`, but a well-formed string key can still carry
the wrong meaning. **Run `--lint-workflows` after every edit** to catch both member and vocabulary
mistakes before startup or student evaluation (see
[validating your changes](#5-validating-your-changes)).

---

## 4. Changing policy data (no code)

Most policy changes are a one-line YAML edit, validated at startup. No rebuild is needed, but a
running engine holds an immutable snapshot: restart or explicitly construct a new engine to load the
changed files.

### 4.1 `data/thresholds.yaml` — the tuning knobs

The numbers the rules and constraint pass read. Retune a pass grade, an entry tier, a DfE floor, or
the amber tariff factor here:

```yaml
pass_grade: 4
top_entry: 7
min_dfe_green_probability_at_or_above: 0.60
amber_tariff_factor: 0.5
# max_green_choices: 4   # optional, normally omitted — see the green-cap note below
```

`max_green_choices` is the **optional green cap** and is normally omitted (as in the shipped file): with
it absent the cap is disabled and every green stays green. Add it — a positive integer — only when policy
demands an auto-enrol ceiling, in which case the lowest-UCAS-weight surplus greens are demoted to amber.

### 4.2 `data/catalogue.yaml` — subject relationships

The single source of truth for the subject-relationship rules and the regression coefficients. Per subject:

```yaml
  - subject: further_maths
    ucas_weight: 56
    regression: { slope: 1.00, intercept: -2.00 }   # the prediction line
    prerequisites:
      - any_of: [ maths ]
        requires: chosen          # only a committed choice counts; default 'qualifying' also accepts green/amber
        # severity: red           # default red (hard); 'amber' is advisory
    exclusions:
      - { other: german, severity: red }            # symmetric — declare on both sides
    required_activities: [ plays_ ]                  # absence ⇒ amber
    blocking_activities: [ plays_trombone ]          # presence ⇒ red veto
    entry_equivalents:
      - { subject: applied_science, type: btec_diploma, min_grade: distinction }
    restudy_bar:
      types: [ a_level ]
      severity: red
```

Load-time validation enforces coverage and **exclusion symmetry** (a clash declared on one side only
fails startup), so the file cannot drift into an inconsistent state silently. What each of these
fields *does* to a rating is in the [appendix](#appendix-how-the-engine-applies-your-rules-background).

### 4.3 `data/qualifications.yaml` — the cross-type grade scale

The grade ordering and A-level-points equivalences used when prior qualifications are compared
(A-level vs BTEC vs NVQ tokens). Edit here to add or retune grades for an existing type. The type
vocabulary itself is the compiled `QualificationType` enum and is repeated in the schemas; adding a
new type requires coordinated C#, schema, and test changes.

### 4.4 Adding a whole new subject

A subject is data-driven — there is no fixed list in code to extend. To add one:

1. Add its block (weight, regression, any relationships) to `data/catalogue.yaml`.
2. Add its three `<subject>:green|amber|red` rules to `workflows/subject-ratings.yaml`.
3. Add any required grade mapping for an existing qualification type to `data/qualifications.yaml`.
4. Add a DfE matrix row if the subject should be probability-gated.
5. Write engine-driven boundary tests for all three tiers.
6. Run `--lint-workflows`, then evaluate a sample student with `--explain-text` to confirm each tier
   behaves as intended.

One exception needs a developer: if the subject is also a brand-new **GCSE** key (used in a
`facts.Gcse(...)` call), that key must be added to the known-GCSE list in code. The technical
reference's [Custom A-level subjects](technical-reference.md#custom-a-level-subjects) section covers
that wiring.

---

## 5. Validating your changes

A rule expression is not compile-time checked with the C# project, so validity is **layered**. Rule
authors use linting and explanation while developing, then add or update an engine-driven test before
the change is accepted; startup and the wider suite provide further independent protection.

| Layer                    | Catches                                                                 |
|--------------------------|-------------------------------------------------------------------------|
| `--lint-workflows`       | Missing/duplicate tiers, tier mis-ordering, off-vocabulary references, eligibility shape |
| `--explain-text`         | Whether a rule actually produces the rating you intended, on a sample student |
| Startup validation       | Structural defects, invalid member bindings, and malformed expressions — the engine refuses to start |
| Engine-driven rule tests | The rule's intended pass/fail behavior at and around every relevant boundary |
| The wider test suite     | End-to-end drift, data contracts, and random-input safety across every subject |

### What the linter checks

`--lint-workflows` is the **cheapest check** and needs no student input, so it catches a typo before
any student exercises it. Run it after every workflow edit:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --lint-workflows
# or lint a candidate set before shipping it:
dotnet run --project src/EnrolmentRules.Cli -- --lint-workflows path/to/workflows
```

It checks:

- **Member references** — every `facts.`/`lookup.`/`policy.` member in an expression must actually
  exist. Catches `facts.Avrage`.
- **Subject keys** — the string literal in `facts.Gcse(...)`, `lookup.Grade(...)`,
  `facts.Predicted(...)`, `facts.DfeProbabilityAtOrAbove(...)` must be in the right vocabulary. The
  two vocabularies differ: GCSE keys (includes `english_language`, excludes `further_maths`) vs
  A-level subject keys (the catalogue). Catches a misspelt key like `facts.Gcse("physis")`.
- **Subject-rating shape** — every subject has exactly one green, one amber, one red rule; they are
  ordered green → amber → red; and red is the unconditional `true` catch-all.
- **Eligibility shape** — exactly `EnglishLanguagePass`, `MathsPass`, `EnoughPasses`, in order.

Exit code `5` on any error finding, `0` when clean.

After adding the rule tests, run the bounded project gate:

```bash
dotnet build EnrolmentRules.slnx -warnaserror
dotnet format EnrolmentRules.slnx
dotnet test EnrolmentRules.slnx
```

### The schema files

The data shapes are themselves authoritative JSON Schemas, validated at startup — consult them when
unsure of a field name or allowed value rather than guessing:

- `workflows/workflow.schema.json` — rule/workflow structure
- `data/catalogue.schema.json` — subject relationships, regression, bars
- `data/thresholds.schema.json` — the tuning knobs
- `data/qualifications.schema.json` — the cross-type grade scale

### Debugging an unexpected result

When a student is rated something you didn't expect, don't read the YAML and guess — ask the engine.
The CLI explain modes show **which rule fired and which override applied**, which pinpoints the file
at fault:

```bash
# Rule provenance + overrides, as JSON:
dotnet run --project src/EnrolmentRules.Cli -- --explain examples/student.json
# Same, as Markdown prose:
dotnet run --project src/EnrolmentRules.Cli -- --explain-text examples/student.json
# Counterfactual "what would change the outcome" advice:
dotnet run --project src/EnrolmentRules.Cli -- --advise examples/student.json
```

Reading the output: if the **base rating** is wrong, the fix is in the rating workflow or a threshold;
if the base is right but an **override** demoted the subject, the fix is in the catalogue. A
surprising amber on an otherwise-strong subject is most often an exclusion loser, or — if the green
cap has been enabled — the cap itself; both are visible in `--explain`.

---

## 6. Worked examples

### 6.1 Tighten a DfE probability floor (data only)

Raise the green DfE floor from 0.60 to 0.65. Edit `data/thresholds.yaml`:

```yaml
min_dfe_green_probability_at_or_above: 0.65
```

No workflow change — every `:green` tier reads `facts.MinDfeGreenProbabilityAtOrAbove`. Confirm the
effect with `--explain-text`: a student who was green at 0.62 now comes out amber.

### 6.2 Add a new subject clash (data only)

Make History and Music mutually exclusive (amber). In `data/catalogue.yaml`, add to **both** subjects
(symmetry is enforced):

```yaml
  - subject: history
    exclusions:
      - { other: art, severity: amber }
      - { other: music, severity: amber }   # new
  - subject: music
    exclusions:
      - { other: history, severity: amber } # new
```

The mutual-exclusion handling picks it up; the lower UCAS weight loses. No workflow change.

### 6.3 Add a new rating tier rule (YAML)

A new subject `geography` needs tiers. Add to `workflows/subject-ratings.yaml`:

```yaml
  - RuleName: 'geography:green'
    SuccessEvent: 'Entry met; predicted A-level grade at or above the green threshold'
    Expression: >-
      facts.Average >= facts.HumanitiesAverageEntry
      && facts.Predicted("geography") >= ALevelGrade.B
      && facts.DfeProbabilityAtOrAbove("geography", ALevelGrade.B) >= facts.MinDfeGreenProbabilityAtOrAbove
  - RuleName: 'geography:amber'
    SuccessEvent: 'Entry met; predicted A-level grade at or above the amber threshold'
    Expression: >-
      facts.Average >= facts.HumanitiesAverageEntry
      && facts.Predicted("geography") >= ALevelGrade.C
      && facts.DfeProbabilityAtOrAbove("geography", ALevelGrade.C) >= facts.MinDfeAmberProbabilityAtOrAbove
  - RuleName: 'geography:red'
    SuccessEvent: 'Entry requirement unmet or predicted grade below the amber threshold'
    Expression: >-
      true
```

Add the `geography` block to `data/catalogue.yaml` (weight + regression), then run
`--lint-workflows` and check a sample student with `--explain-text`.

### 6.4 Add a new *kind* of relationship — out of scope

Not an authoring task. Adding a relationship *type* that does not exist yet — not a new instance of an
existing one — is a code change, rare and not part of normal operation. Almost always the policy *can*
be expressed by an existing catalogue field, as in [the subject-clash example](#62-add-a-new-subject-clash-data-only)
— prefer that. The existing types are described in the
[appendix](#appendix-how-the-engine-applies-your-rules-background).

---

## 7. Common pitfalls

The mistakes that pass a casual review — worth a deliberate check.

- **Boundary direction.** Tiers use `>=` (at-or-above). A `>` where a `>=` was meant silently moves
  every on-the-threshold student down a tier. Always check exactly *at* the boundary, not just around it.
- **The two subject vocabularies differ.** GCSE keys (`facts.Gcse`, `lookup.Grade`) include
  `english_language` but **not** `further_maths`; A-level keys (`facts.Predicted`,
  `facts.DfeProbabilityAtOrAbove`) are the catalogue subjects. Using the wrong vocabulary is a lint
  error — run `--lint-workflows` to catch it before a student does.
- **A typo'd subject key is a lint error.** `facts.Gcse("physis")` compiles and would otherwise
  rate the student incorrectly, but the linter flags it as off-vocabulary — and that lint now runs
  inside `CreateAsync`, so such a workflow fails startup rather than shipping. `--lint-workflows` is
  the cheaper pre-check that surfaces the same finding without booting an engine, so never skip it.
- **Magic numbers.** A literal `4` or `5.0` or `0.6` in a rule will not survive review — pull it
  from `policy.*` / `facts.*` (sourced from `thresholds.yaml`) or `ALevelGrade.*`.
- **Scale confusion.** GCSE grades are `1–9`; predicted points are `ALevelGrade` (`A*=6 … U=0`).
  Comparing a predicted value against a GCSE threshold (or vice versa) is nonsense.
- **Exclusion symmetry.** A clash declared on one subject only fails startup validation. Always edit
  *both* subjects in `catalogue.yaml`.
- **Tier ordering and the red catch-all.** Rules must be green → amber → red, with red as literal
  `true`. Author each tier as a *loosening* of the one above, or "first-hit-wins" gives green to a
  student who should be amber. The linter enforces the shape but not the *semantics* of the loosening —
  check that with `--explain-text`.
- **Reason strings are user-facing.** The `SuccessEvent` text surfaces verbatim in `--explain-text`.
  Write it as you'd want an admin to read it.
- **Don't reach across rules in YAML.** If a rating rule seems to need another subject's outcome, it
  is handled by the engine's constraint pass, not a YAML rule. There is no `facts.RatingOf(...)`.

---

## 8. Quick reference

**Which file by what the rule reads**

- one student, true/false or tier → `workflows/*.yaml`
- a number → `data/thresholds.yaml`
- a subject relationship → `data/catalogue.yaml`
- a grade equivalence → `data/qualifications.yaml`
- compares siblings → handled by the engine (see appendix)

**Rating-rule surface** — `facts.Gcse(k)`, `facts.Predicted(k)`,
`facts.DfeProbabilityAtOrAbove(k, g)`, `facts.HasEntryEquivalent(k)`, `facts.Average`, `facts.Age`,
`facts.TopEntry`/`StrongEntry`/`StandardEntry`, `facts.*AverageEntry`, `facts.MinDfe*ProbabilityAtOrAbove`,
`facts.AdultAge`, `ALevelGrade.*`.

**Eligibility-rule surface** — `lookup.Grade(k)`, `gcses` (array), `policy.PassGrade`,
`policy.MinPasses`, `policy.*`.

**Scales** — GCSE input is **1–9**; predicted A-level points are **`ALevelGrade` A\*=6 … U=0**. Never
conflate them. No bare numbers — use named thresholds and `ALevelGrade.*`.

**Invariants** — tiers ordered green → amber → red; red is `true`; exclusions symmetric; every
adjustment downgrades only.

**Commands**

```bash
dotnet run --project src/EnrolmentRules.Cli -- --lint-workflows                 # validate the rules
dotnet run --project src/EnrolmentRules.Cli -- --explain-text examples/student.json   # see the effect
```

---

## Appendix: How the engine applies your rules (background)

This is **background, not authoring** — code you never edit. Read it to understand what a catalogue
edit will *do* to a rating, or to debug an unexpected downgrade.

After the base green/amber/red rating, the engine applies the cross-subject relationships. Every
adjustment **only ever downgrades**, and they compose by **most-severe-wins** — so the order they
apply in never changes the outcome. The shipped relationship types:

| Relationship            | Effect            | Trigger                                                                 |
|-------------------------|-------------------|-------------------------------------------------------------------------|
| Prerequisites           | → group severity  | A qualifying subject's dependency group is unmet (`any_of` / `requires`) |
| Mutual exclusions       | → pair severity   | Both sides of a clash pair qualify; the **lower UCAS weight** loses      |
| Prior-choice exclusions | → pair severity   | A committed `chosen_a_levels` choice excludes a qualifying subject       |
| Own-time                | → amber           | A qualifying subject's `required_activities` prefix is absent from hobbies|
| Vetoes                  | → red             | A `blocking_activities` prefix is present in hobbies                      |
| Restudy bars            | → bar severity    | A held prior qualification of a barred `type` in the same subject         |

Each of these reads the catalogue — none hardcodes policy. To add a clash, a prerequisite, or a bar
you edit [`data/catalogue.yaml`](#42-datacatalogueyaml--subject-relationships); you never touch the
code. Adding a genuinely new *kind* of relationship (one the six above cannot express) is a code
change with its own design discussion — rare, and out of scope for this guide.

Finally the engine aggregates over the surviving ratings: it computes the UCAS tariff and green/amber
summary (using `ucas_weight` and `amber_tariff_factor`), and applies the **optional green cap**. The
cap is off in normal operation (`max_green_choices` unset, so every green stays green); when an admin
sets it, the cap keeps only the highest-weighted greens and demotes the surplus to amber.

---

See also: [technical reference](technical-reference.md) · [pipeline walk-through](walkthrough.md) ·
[engine-choice rationale](engine-choice.md).
