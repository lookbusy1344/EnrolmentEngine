# EnrolmentRules — a guided walk-through

This document explains how the project works from the ground up. It assumes **no prior
knowledge of Microsoft RulesEngine**. By the end you should understand what the rule engine
is, what it does (and does *not* do) here, how the whole pipeline fits together,
and how to run it and change it safely.

It is a companion to the other project docs:

- [`README.md`](../README.md) — the quick start, input shape, CLI examples, and library usage.
- [`docs/rule-authoring.md`](rule-authoring.md) — the practical guide to changing workflow and
  policy YAML safely.
- [`docs/engine-choice.md`](engine-choice.md) — why this project uses Microsoft RulesEngine rather
  than NRules/RETE.
- [`docs/plans/rulesengine-csharp-enrolment-plan.md`](plans/rulesengine-csharp-enrolment-plan.md) —
  the original phased design rationale.

This walk-through sits in the middle: enough conceptual grounding to read the code with
confidence.

---

## 1. The problem in one paragraph

Given a student's **GCSE results** (subject → grade, on the 1–9 scale), any typed
**prior qualifications** (for example A-levels or BTECs), and a few non-academic **hobby tags**,
decide for each A-level subject whether the student is a **green**
(pre-approved — may enrol with no authorisation), **amber** (needs authorisation), or **red**
(not permitted) candidate — and explain why. This is **advisory**: it recommends, it does not
enrol anyone. The student still chooses which subjects to take; green just means *if* they pick
it, no staff sign-off is required. Some decisions depend only on that one student's facts
(e.g. "is the Maths GCSE good enough?"). Others depend on *all* the subject decisions together
(e.g. "you can't pre-approve more than four subjects", or "History and Art clash on the
timetable"). That split — per-subject vs.
cross-subject — is the single most important idea in the whole design, and it dictates which
half lives in the rule engine and which half is plain C#.

---

## 2. What is Microsoft RulesEngine?

[Microsoft RulesEngine](https://github.com/microsoft/RulesEngine) is a small, open-source .NET
library for running **business rules that are stored as data instead of compiled into code**.

The mental model is short:

- You write rules as **lambda-expression strings** inside a workflow file. RulesEngine's native
  payload is JSON; this project authors the same structure as YAML. For example, the string
  `"lookup.Grade(\"maths\") >= policy.PassGrade"`.
- At runtime you hand the engine the **named workflow** plus one or more **input objects**.
- The engine compiles each expression string into a real .NET lambda (the first time it runs
  it), evaluates it against your inputs, and returns a **result tree** — one node per rule —
  telling you which rules succeeded (`IsSuccess`), the human-readable `SuccessEvent` attached
  to a passing rule, and any error message.

```csharp
var engine  = new RulesEngine.RulesEngine([workflow]);     // build once
var results = await engine.ExecuteAllRulesAsync(            // returns List<RuleResultTree>
    "eligibility",                                          // the workflow name
    new RuleParameter("lookup", gcseAccessor));             // your input object(s)

foreach (var r in results)
    Console.WriteLine($"{r.Rule.RuleName}: {r.IsSuccess}");
```

### Why use it at all?

The headline benefit is that **policy lives in editable YAML data files, not in a recompiled binary**.
A college administrator (in principle) could change "5 GCSE passes required" to "6" by editing
`workflows/eligibility.yaml` and reloading — no code change, no rebuild, no redeploy. That
"rules-as-data" property is the entire reason this engine was chosen for the table-shaped part
of the problem.

### Two constraints of RulesEngine that shape everything here

Two properties are inherent to the engine, and the architecture is built around them.

1. **Rules are untyped.** An expression string like `lookup.Grade("maths") >= policy.PassGrade`
   is just text until it runs. A typo (`mathss`), a flipped comparison (`<=` instead of `>=`),
   or a wrong field name **compiles in C# fine and silently produces the wrong answer**. There
   is no compiler to catch it. We mitigate this with several independent safety layers
   (see [defence in depth](#7-why-you-can-trust-untyped-rules-defence-in-depth)).

2. **A rule cannot see other rules' results.** Within one evaluation, rules are independent —
   a rule cannot ask "did the Maths rule pass?" or "how many subjects went green?". So any
   decision that needs to look across subjects **cannot be expressed as a rule** and must be
   ordinary C# code running *after* the engine. This is why the project is explicitly
   **two-layer**.

---

## 3. The two-layer architecture

```
   raw student JSON
         │
         ▼
┌───────────────────┐   plain C#, upstream of the engine
│  1. Prediction    │   averages GCSEs + regression + DfE transition probabilities
└───────────────────┘
         │  StudentProfile (predicted points + transition evidence per subject) + raw GCSEs
         ▼
┌───────────────────┐   Microsoft RulesEngine — "rules as data" (the editable YAML)
│  2. Engine tables │   • eligibility gate     (workflows/eligibility.yaml)
│                   │   • per-subject ratings  (workflows/subject-ratings.yaml)
└───────────────────┘
         │  base green/amber/red per subject
         ▼
┌───────────────────┐   plain C#, downstream of the engine
│  3. Constraint    │   prerequisites, exclusions, own-time rules, vetoes, the optional green cap,
│     pass + caps   │   tariff/summary — all the *cross-subject* decisions
└───────────────────┘
         │
         ▼
   EnrolmentResult (the document you get back)
```

**The engine only ever does step 2** — the "table half": independent, per-student-fact
decisions. Steps 1 and 3 are normal, fully-typed, unit-testable C# because they either involve
arithmetic (prediction) or need to see all subjects at once (constraints) — neither of which
the engine can do well.

The code maps onto the diagram cleanly:

| Layer | Project | Key types |
| --- | --- | --- |
| 1. Prediction | `EnrolmentRules.Prediction` | `GradePredictor`, `PredictionModel`, `DfeTransitionMatrix` |
| 2. Engine tables | `EnrolmentRules.Engine` + `workflows/` | `WorkflowStore`, `RatingEvaluator`, the two YAML workflows |
| 3. Constraints | `EnrolmentRules.Engine` | `ConstraintPass`, `Aggregator` |
| Façade over all 3 | `EnrolmentRules.Engine` | `EnrolmentEngine` |
| Shared vocabulary | `EnrolmentRules.Domain` | `Subject`, `Rating`, `Thresholds`, `ALevelGrade`, `Catalogue`, the result records |

### Where each rule actually lives

The rules themselves are all editable data — but "it's all one YAML file" is a trap. The rules are
spread across **five different data files** (two YAML workflows plus two policy tables and one
qualification-scale table, on three different evaluators), and then there is C#. The C# is *not*
rules: it is the fixed set of relationship *types* a rule can take. Keep these buckets straight and
the rest of the project reads easily:

| Bucket | Home | Evaluated by | Holds |
| --- | --- | --- | --- |
| **Per-student rules** | `workflows/eligibility.yaml`, `workflows/subject-ratings.yaml` | Microsoft **RulesEngine** (lambda strings) | The eligibility gate and each subject's green/amber/red base tier. A pure function of *one* student's facts. |
| **Tuning values** | `data/thresholds.yaml` | Plain **data**, schema-validated by `PolicyThresholdsStore` → `PolicyThresholds` | The numeric knobs the lambdas read as `policy.PassGrade` / `facts.TopEntry` and the host pass reads directly: pass grade, min passes, entry thresholds, DfE floors, adult age, the optional green cap (normally unset), amber tariff factor. |
| **Qualification scale** | `data/qualifications.yaml` | Plain **data**, schema-validated by `QualificationScaleStore` → `QualificationScale` | Grade ordering within each qualification type, plus the A-level-points equivalence used when prior qualifications lift a subject's prediction. |
| **Subject-relationship rules** | `data/catalogue.yaml` | Plain **data**, schema-validated by `CatalogueStore` — *not* RulesEngine | UCAS weights, exclusion pairs, own-time/veto prefixes, prerequisites, entry equivalents, restudy bars, per-subject regression — which subjects relate to which. |
| **Relationship types + scale invariants** | `ConstraintPass`, `Aggregator`, `PredictionModel`/`ALevelGrade` math, `Thresholds` | Compiled **C#** | Not rules — the fixed *types* a rule can take and how they apply (a rule cannot read sibling rule outcomes), plus the GCSE-scale invariants. Changed only to add a new *kind* of relationship — rare, not part of normal operation. |

The one-line model: **what** to decide for one student → workflow YAML; the *numbers* it turns on →
thresholds YAML; **how prior-qualification grades compare** → qualifications YAML; **which** subjects
relate → catalogue YAML. Those are all rules, all editable data. Only the fixed *types* of relation
those rules can express → C#.

---

## 4. Step-by-step: how one student flows through

This is the order in `EnrolmentEngine.RunAsync`. The order is fixed because each step consumes
the previous step's output.

### Step 1 — Prediction (`GradePredictor.Predict`)

Pure host-side projection, no engine. `Predict` takes the student plus a **reference ("as-of")
date** — the prediction stage is the one place age is materialised, and age depends on *when* the
student is assessed, so the date is passed in explicitly rather than read from the wall clock
(the CLI supplies today; tests and golden files pin a fixed date). This keeps the whole pipeline a
deterministic function of `(document, as-of date)`.

- **Average GCSE score** = the mean of the present GCSE grades (absent subjects are simply not
  counted).
- **Age in whole years** = derived from the input `date_of_birth` as of the reference date
  (`AgeCalculator.WholeYears`, birthday-aware, clamped at zero). This is the only derived signal
  that is not a function of the GCSEs; it feeds age-gated entry rules such as Art's.
- **Simulated ALIS-style predicted A-level points per subject** = a fixed one-feature linear
  regression, `points = slope · average + intercept`, clamped to the A-level scale. The
  slope/intercept for each subject are loaded from the catalogue as `PredictionModel.Coefficients`.
  A matching prior qualification can raise that subject's predicted points to the best configured
  entry-equivalent value from `data/qualifications.yaml`, but it never lowers a stronger regression
  prediction.
- **DfE transition-matrix evidence per subject** = the same average GCSE score is mapped to a
  Department for Education prior-attainment band (`7 to < 8`, `8 to < 9`, etc.). For each
  modelled A-level subject, the project looks up the national probability distribution for
  grades `U`, `E`, `D`, `C`, `B`, `A`, `A*` from the local DfE extract.

Two scales are in play and must not be confused:

- **GCSE scale**: integers `1`–`9` (the input).
- **A-level points scale** (`ALevelGrade`): `A*=6, A=5, B=4, C=3, D=2, E=1, U=0` (the
  prediction target the rule tiers compare against).

The output is a `StudentProfile`. This, plus the raw GCSEs, is what gets handed to the engine.

`StudentProfile` contains:

| Field | Meaning |
| --- | --- |
| `Id` | Student identifier carried through from the input. |
| `AverageGcseScore` | Continuous mean over present GCSEs. |
| `PredictedGrades[]` | One linear-regression point prediction per catalogue subject. |
| `TransitionEvidence[]` | One DfE probability row per catalogue subject, at the student's prior-attainment band. |
| `Age` | Whole years derived from the input `date_of_birth` as of the run's reference date; drives age-gated entry rules. |
| `ChosenALevels[]` | Already-committed A-level choices used later by host constraints. |
| `Hobbies[]` | Opaque activity tags used later by host constraints. |
| `PriorQualifications[]` | Typed prior study carried through for entry-equivalent uplift upstream and restudy-bar checks downstream. |

The two prediction/evidence sources are different:

| Source | What it produces | How it is used |
| --- | --- | --- |
| Simulated ALIS-style regression + entry-equivalent uplift | One continuous A-level point prediction per subject. | Workflow tiers compare it with grade thresholds such as `A`, `B`, or `C`. |
| DfE national transition matrices | One probability distribution per subject and prior-attainment band. | Workflow rules can ask for probabilities such as `P(A or above)`. |

The ALIS-style model is a simple local approximation of the kind of prior-attainment-based
line a sixth-form admissions process might use: each subject has a fixed slope/intercept, and
the only feature is the student's average GCSE score. It is **simulated** — useful for a stable
demo expert system and for exercising subject-specific boundaries, but not trained from a
vendored ALIS dataset.

The DfE source is real, local, and narrow:

- `data/dfe-transition-matrices/gce-a-level-2019-transition-probabilities.csv` is a normalized
  extract from the official DfE workbook "Transition matrices 16 to 18: 2019".
- GOV.UK publication: `https://www.gov.uk/government/publications/16-to-18-level-3-value-added-ready-reckoner`.
- Source workbook attachment:
  `https://assets.publishing.service.gov.uk/media/5e25971940f0b62c45460061/TMs_2019A_live_final.xlsx`.
- `data/dfe-transition-matrices/SOURCE.md` records the GOV.UK page, attachment URL, source
  sheet, and extraction scope.
- The full workbook is not committed. Runtime code reads only the compact CSV rows required by
  this project's twenty-six subjects. See `docs/dfe-matrix-extraction.md` for the column mapping
  and the small-cell suppression rule applied to the noisy top (`>=9`) band.

The DfE matrix is probability evidence, not another regression. A transition matrix says, for
example, "among students nationally in prior-attainment band `7 to < 8` taking A-level
Mathematics, these proportions achieved `U/E/D/C/B/A/A*`".
`TransitionEvidence.ProbabilityAtOrAbove(A)` then sums the `A` and `A*` columns. That value can
be used directly in workflow YAML. Some DfE subject/band combinations are absent from the
source workbook because the national row is too sparse; `DfeTransitionMatrix` handles that by
falling back to the nearest lower populated band for the same subject, and only returns zero
evidence if the subject has no usable DfE row at or below the requested band.

What DfE means by prior attainment is slightly richer than this project's current input model.
For A-level/academic value-added measures, DfE bases prior attainment on **GCSE grades only**,
not all KS4 qualifications. It applies the KS4 point scale for the relevant reporting year,
discounts repeated/same-subject entries, uses the best eligible result for a subject, ignores
post-16 resits/additional qualifications, weights by qualification size, and averages the
included points. Students are then grouped with others of similar prior attainment for the
same qualification/subject.

This project approximates that DfE prior-attainment score as `AverageGcseScore`: the plain mean
of present GCSE grades. That is a reasonable match for the simplified student document because
every input GCSE is size `1`, there are no duplicate entries, no resits, and no non-GCSE KS4
qualifications. If the input model grows to include those facts, the DfE banding function should
move from simple mean GCSE grade to the fuller DfE prior-attainment calculation.

> Detail worth knowing: Further Maths has a *steeper* regression line than Maths
> (`slope 1.00` vs `0.80`). For the very strongest students, predicted Further Maths can exceed
> predicted Maths — which is exactly how a student can end up Further-Maths-green while
> Maths-red. Under the shipped `requires: chosen` rule the prerequisite is reachable more often
> still: any green/amber Further Maths whose Maths is not a committed choice is forced red in step 3,
> regardless of how Maths itself rates.

#### Prior qualifications (Level 3): the two opposite rules

`prior_qualifications` is typed prior study — mostly **Level 3** (A-levels, BTECs, NVQs) — carried
separately from the 1–9 GCSEs. The closed set of types lives in `data/qualifications.yaml`, where
each grade has an ordinal (for "at least grade X" checks) and an **A-level-points equivalence** on
the same `A*=6 … U=0` scale prediction uses:

| Type | Grades (low → high) | Points equivalence |
| --- | --- | --- |
| `gcse` | `1 … 9` | `0.0, 0.5, 1.0, 2.0, 3.0, 4.0, 5.0, 5.5, 6.0` |
| `a_level` | `u … a_star` | `0.0 … 6.0` |
| `btec_extended_certificate` | `pass, merit, distinction, distinction_star` | `2.0, 3.0, 5.0, 6.0` |
| `btec_diploma` | `pass, merit, distinction, distinction_star` | `3.0, 4.0, 5.0, 6.0` |
| `nvq` | `level_1, level_2, level_3` | `1.0, 2.0, 3.0` |

A prior qualification feeds two **mirror-image** per-subject rules, both data in `data/catalogue.yaml`:

- **Entry equivalent — the only rule that *opens* a path (upstream, here in step 1).** A subject can
  list `entry_equivalents`, each a `{ subject, type, min_grade }` triple. A held qualification of the
  same subject and type at or above `min_grade` then (a) makes `facts.HasEntryEquivalent("<subject>")`
  true — an OR-branch on the subject's workflow entry clause, so the GCSE entry requirement can be met
  a different way — and (b) lifts that subject's predicted points to the best satisfying
  qualification's equivalence (`Math.Max(regression, equivalence)`, so it never *lowers* a stronger
  line). It opens reachability; it never on its own clears the green prediction/probability bars.
  *Shipped:* Biology accepts `applied_science` BTEC Diploma at Distinction+ — that student clears
  Biology entry without the Chemistry GCSE and is predicted at ≥ 5.0 points (grade A).
- **Restudy bar — a pure downgrade (downstream, in step 3).** A subject can declare a
  `restudy_bar: { types, severity }`; holding a prior qualification *in that same subject* of a listed
  type downgrades it to `severity` (default **red**). Like a veto it fires even on an already-red base,
  so the reason names the bar. *Shipped:* Biology bars a prior `a_level` at red — already holding
  A-level Biology forces red on re-enrolment, and `--advise` reports it as a hard non-GCSE blocker.

So an entry equivalent is the one prior-qualification lever that can make a subject *more* reachable;
a restudy bar only ever takes reachability away. The bar is revisited as a constraint-pass rule in
step 3 below.

### Step 2a — Eligibility gate (engine, `workflows/eligibility.yaml`)

A small workflow of three independent rules:

| Rule | Expression (paraphrased) |
| --- | --- |
| `EnglishLanguagePass` | English Language GCSE ≥ `policy.PassGrade` (4) |
| `MathsPass` | Maths GCSE ≥ `policy.PassGrade` (4) |
| `EnoughPasses` | count of GCSEs at grade ≥ 4 is ≥ `policy.MinPasses` (5) |

The engine reports each rule's pass/fail. `RatingEvaluator.EvaluateEligibilityAsync` then
*assembles the verdict in host code*: if any rule failed, the student is ineligible and the
failed rules' messages become the reasons (kept in declared order). Note this assembly is host
code precisely because the engine can't aggregate sibling rules.

A subtlety in how inputs are shaped: counting passes needs to iterate a **collection**, but
checking a single subject needs a **keyed lookup**. So the engine is given two inputs —
`gcses` (an array, which `EnoughPasses` counts over) and `lookup` (a `GcseFacts` accessor, on
which the single-subject rules call `lookup.Grade("maths")`). An absent subject returns `0`,
which can never satisfy a threshold.

### Step 2b — Per-subject ratings (engine, `workflows/subject-ratings.yaml`)

For each of the twenty-six subjects there are **three ordered rules**: `:green`, `:amber`, `:red`.
Each green/amber rule combines an **entry requirement** (the right GCSEs) with two pieces of
evidence about likely A-level performance: the simulated ALIS-style point prediction and the
DfE national transition-matrix probability at the same grade threshold. The `:red` rule is
literally `"true"` — a catch-all that always matches.

Example for Physics:

```jsonc
"physics:green": "facts.Gcse(\"maths\") >= facts.TopEntry && facts.Gcse(\"physics\") >= facts.StrongEntry && facts.Predicted(\"physics\") >= ALevelGrade.B && facts.DfeProbabilityAtOrAbove(\"physics\", ALevelGrade.B) >= facts.MinDfeGreenProbabilityAtOrAbove"
"physics:amber": "facts.Gcse(\"maths\") >= facts.TopEntry && facts.Gcse(\"physics\") >= facts.StrongEntry && facts.Predicted(\"physics\") >= ALevelGrade.C && facts.DfeProbabilityAtOrAbove(\"physics\", ALevelGrade.C) >= facts.MinDfeAmberProbabilityAtOrAbove"
"physics:red":   "true"
```

Maths uses the same shape with a higher green grade threshold:

```jsonc
"maths:green":
  "facts.Gcse(\"maths\") >= facts.TopEntry
   && facts.Predicted(\"maths\") >= ALevelGrade.A
   && facts.DfeProbabilityAtOrAbove(\"maths\", ALevelGrade.A)
        >= facts.MinDfeGreenProbabilityAtOrAbove"
```

### Rule patterns at a glance

The shipped workflows use five main rule shapes:

```yaml
# 1. A simple threshold check
WorkflowName: 'eligibility'
Rules:
  - RuleName: 'MathsPass'
    SuccessEvent: 'GCSE Maths at pass grade or above'
    Expression: >-
      lookup.Grade("maths") >= policy.PassGrade
```

```yaml
# 2. A combined subject rule
WorkflowName: 'subject-ratings'
Rules:
  - RuleName: 'physics:green'
    SuccessEvent: 'Entry met; predicted A-level grade at or above the green threshold'
    Expression: >-
      facts.Gcse("maths") >= facts.TopEntry &&
      facts.Gcse("physics") >= facts.StrongEntry &&
      facts.Predicted("physics") >= ALevelGrade.B &&
      facts.DfeProbabilityAtOrAbove("physics", ALevelGrade.B)
        >= facts.MinDfeGreenProbabilityAtOrAbove
```

```yaml
# 3. A rule with a local parameter
WorkflowName: 'eligibility'
Rules:
  - RuleName: 'EnoughPasses'
    LocalParams:
      - Name: 'passCount'
        Expression: >-
          gcses.Count(g => g.Grade >= policy.PassGrade)
    Expression: >-
      passCount >= policy.MinPasses
```

```yaml
# 4. A catch-all red rule
WorkflowName: 'subject-ratings'
Rules:
  - RuleName: 'history:red'
    Expression: >-
      true
```

```yaml
# 5. An age-gated threshold (the entry bar branches on a derived per-student signal)
WorkflowName: 'subject-ratings'
Rules:
  - RuleName: 'art:green'
    SuccessEvent: 'Entry met (age-gated GCSE threshold); predicted A-level grade at or above the green threshold'
    Expression: >-
      facts.Gcse("art") >= (facts.Age >= facts.AdultAge ? facts.TopEntry : facts.StrongEntry) &&
      facts.Predicted("art") >= ALevelGrade.B &&
      facts.DfeProbabilityAtOrAbove("art", ALevelGrade.B)
        >= facts.MinDfeGreenProbabilityAtOrAbove
```

`facts.Age` is the whole-years age derived in step 1. The gate *policy* — adults (≥ `AdultAge`)
must reach `TopEntry`, younger students only `StrongEntry` — lives entirely in the rule data; host
code only exposes the value. This is the template for any new per-student attribute: derive it in
prediction, expose it on `RatingFacts`, and decide with it in the YAML.

The five evaluation stages are fixed, each consuming the previous one's output:

1. **Prediction** shapes the student into `StudentProfile` plus raw GCSE accessors.
2. **Eligibility gate** checks `EnglishLanguagePass`, `MathsPass`, and `EnoughPasses`.
3. **Per-subject ratings** evaluate ordered rule sets such as `physics:green`, `physics:amber`,
   and the catch-all `physics:red`.
4. **Cross-subject constraint pass** applies prerequisite, exclusion, own-time, and veto
   adjustments, moving a subject from green to amber or red on a cross-subject violation.
5. **Aggregation** computes the summary counts and projected tariff (and applies the green cap only
   if explicitly enabled — by default it does nothing and every green stays green).

Three concrete rule patterns show up again and again:

- A single threshold check, such as `MathsPass`.
- A combined entry/prediction/probability rule, such as `physics:green`.
- A catch-all red rule, such as `physics:red` or `history:red`.

The important reading rule is:

- green means the entry requirement, prediction threshold, and probability threshold all passed;
- amber means the entry requirement passed, but the higher green bar did not;
- red is the fallback, or a host-code downgrade when prerequisites fail.

#### Entry requirement vs. tier — a common point of confusion

The entry requirement is **not a separate gate sitting alongside green and amber. It is the
shared first clause inside both tiers.** Compare the two Physics expressions above: their entry
clauses
(`facts.Gcse("maths") >= TopEntry && facts.Gcse("physics") >= StrongEntry`) are *identical* —
only the predicted-grade and DfE-probability bars differ (≥B/green-probability for green,
≥C/amber-probability for amber). So amber is a strict **superset** of green: a green-eligible
student also satisfies amber (and the always-true red), and the first-hit-wins scan is what
collapses that overlap into one tier.

So **to land on a subject as green or amber, a student must clear at least the amber bar — and
clearing amber already includes having met the entry requirement** (an unmet entry clause makes
*both* the amber and green rules fail, so the student drops to red). The mental split is:

- the **entry requirement** answers *"is the student allowed onto this subject at all?"*;
- the **green/amber split** answers *"how confident are we they will succeed, given they're
  allowed on?"* — green = strong evidence (≥B and ≥ green probability), amber = weaker but
  acceptable evidence (≥C and ≥ amber probability), so amber needs authorisation;
- **red** is *"no tier matched"* (entry unmet or evidence too weak), or a downstream host-code
  downgrade.

Two things still gate enrolment around this per-subject rating:

1. **Upstream:** the whole-student eligibility gate. If it fails, the per-subject workflow never
   runs and every subject is emitted red — so amber-or-better only matters once the gate is passed.
2. **Downstream:** the cross-subject constraint pass (and, if enabled, the optional green cap) can
   still downgrade a subject that cleared its amber/green bar — an unmet prerequisite forces red, and
   a mutual exclusion or own-time miss can knock green to amber. A red-severity exclusion can also
   force red when the rule says so. The green cap is off by default, so normally nothing demotes a
   subject purely for being "surplus".

Each non-red tier requires three independent facts:

| Fact | Source |
| --- | --- |
| Subject entry requirement is met | Raw input GCSEs and average score via `GcseFacts` / `RatingFacts.Average`. |
| Linear prediction clears the tier grade | `PredictionModel` via `PredictedGrades[]`. |
| National DfE probability at or above that grade clears the tier probability threshold | `DfeTransitionMatrix` via `TransitionEvidence[]`. |

If the profile contains no DfE evidence for a subject, `RatingFacts` returns probability `0.0`,
so a probability-gated green or amber rule cannot pass accidentally.

**"First hit wins" is enforced by host code, not the engine.** The engine runs *every* rule
and returns all results — there is no built-in short-circuit. A green-eligible student also
satisfies the amber rule (and the always-true red rule). So `RatingEvaluator.EvaluateRatingsAsync`
scans the results **in declared order and takes the first success** per subject. Authoring the
rules green → amber → red is therefore the *intended policy*, and the host scan is what
*realises* it.

> The expressions call methods like `facts.Gcse(...)` and reference types like `Thresholds`.
> RulesEngine only lets a lambda touch types you have explicitly whitelisted. That whitelist is
> `RuleSettings.Default` (`CustomTypes = [GcseFacts, PolicyFacts, RatingFacts, Thresholds, ALevelGrade]`). If
> you write a rule that calls into a new type, you must add it there or the rule won't compile.

`RatingFacts` is the boundary between rules-as-data and typed C#:

| Workflow call | Host-side meaning |
| --- | --- |
| `facts.Gcse("maths")` | Absent-safe raw GCSE lookup; missing subjects read as `0`. |
| `facts.Predicted("physics")` | Continuous A-level point prediction; missing predictions read as `U`. |
| `facts.DfeProbabilityAtOrAbove("maths", ALevelGrade.A)` | DfE transition probability at or above the requested grade; missing evidence reads as `0.0`. |
| `facts.HasEntryEquivalent("biology")` | True when `prior_qualifications` contains a catalogue-configured matching subject/type/grade pair for that subject. |
| `facts.Average` | The student's mean GCSE score. |

The result of step 2 is one **base** `SubjectRating { Subject, Rating, Reason }` per subject.
`StudentProfile.ChosenALevels` also rides along unchanged, but it is only consumed by the
host constraint pass in step 3.

### Step 2c — Eligibility short-circuit

If the gate said *ineligible*, the per-subject workflow is **never run**; instead every
subject is emitted red with the gate's reason. The subject list comes from the loaded catalogue
snapshot, so adding a subject to the catalogue cannot accidentally bypass the gate.

### Step 3 — Cross-subject constraint pass (host code, `ConstraintPass`)

This is the part the engine fundamentally cannot do, because it must read *all* the base
ratings at once. It is a pure function `(ratings, profile) → Adjustment[]`. Six rules:

- **Prerequisite → amber or red.** Each dependent subject carries one or more dependency *groups* in
  the catalogue (`prerequisites`); every group must hold (groups are AND-ed). A group has `any_of`
  (the alternatives, OR-ed — satisfied if *any one* holds), an optional `severity` (**red**, the default,
  for a hard requirement; **amber** for an advisory one), and an optional `requires` mode deciding what
  counts as satisfying an alternative: **`qualifying`** (the default) — rating green/amber this run *or*
  being a committed `chosen_a_levels` choice (it is enough to be *viable*); **`chosen`** — *only* a
  committed choice counts, so a subject that merely rates well does not satisfy it. Unmet groups compose
  by most-severe-wins. The shipped policy has one: Further Maths hard-requires Maths and requires it to
  have been **chosen** (`any_of: [maths]`, `requires: chosen`):

  ```yaml
  - subject: further_maths
    ucas_weight: 56
    prerequisites:
      - any_of: [maths]
        requires: chosen
  ```

  So a student whose Maths rates green but who has *not* committed to it (`chosen_a_levels` does not
  include `maths`) is forced **red** on Further Maths — and, because no GCSE change can make Maths a
  committed choice, the counterfactual advisor (step 5) reports Further Maths as unreachable rather than
  proposing a grade bump. Switching the rule back to `requires: qualifying` would instead let a
  green/amber Maths satisfy it; switching `severity` to `amber` would make the dependency advisory
  (demote to amber, not red). A prerequisite only ever *gates* — it can keep Further Maths out of green,
  never put it in.
- **Mutual exclusion → amber or red.** For each clashing pair in the catalogue (here **History ↔ Art**
  and the illustrative **French ↔ German** clash), the rule's configured severity applies when
  the pair qualifies: amber pairs require both to be green, red pairs block either green or amber
  on the lower-weight side.
- **Prior-choice exclusion → amber or red.** A committed `ChosenALevels` choice always wins, so any
  qualifying subject that the chosen A-level excludes is downgraded to that exclusion's severity.
- **Own-time → amber.** A subject that requires a non-academic activity (here **Music** requires
  a `plays_*` hobby) but whose student lacks it is demoted to **amber**.
- **Veto → red.** A subject barred outright by an incompatible activity tag (here an illustrative
  **`plays_trombone`** bar on Music) is forced **red**, overriding its entry/green/amber tier
  entirely. It is own-time's mirror — same `Hobbies` input, but *presence* triggers a hard red
  rather than *absence* demoting to amber — and unlike the other rules it fires even on an
  already-red base, so the explanation cites the specific bar instead of "entry unmet".
- **Restudy bar → amber or red.** A subject can declare that already holding a prior qualification
  in the same subject bars or advises against re-enrolment. It is a monotone downgrade, so it runs
  downstream with vetoes and exclusions, not in the workflow tiers. Unlike entry equivalents, it
  never improves a rating.

Every adjustment **only ever downgrades** (green→amber→red, never the reverse). That
monotonicity is why the pass is single-shot and order-independent: the rules read only
the *base* ratings, never each other's output, so they commute. `ConstraintPass.Apply` then
folds the base rating and any adjustments together by **most-severe-wins**.

The net effect is one unambiguous precedence ladder. Read it top-down and stop at the first row
that matches:

| Order | Check | Result |
| --- | --- | --- |
| 1 | Eligibility gate fails | **Red** — every subject, nothing below can lift it |
| 2 | Per-subject **veto** (incompatible activity tag) | **Red** — overrides the tier entirely |
| 3 | Unmet **hard** prerequisite, or a red-severity exclusion | **Red** — overrides the tier |
| 4 | **Green** tier matches (entry + prediction + green probability) | **Green** — may still demote to amber via an amber exclusion, an unmet advisory (amber) prerequisite, an own-time miss, or (if enabled) the optional green cap |
| 5 | **Amber** tier matches (entry + prediction + amber probability) | **Amber** |
| 6 | Nothing matched | **Red** — the catch-all fallback |

Mechanically the engine emits the green/amber/red base tier first (its first-hit-wins scan is
what makes green beat amber) and the host pass then applies the red/amber downgrades; the table
above is the *net* precedence after `Apply`. Promotion never happens.

### Step 4 — Aggregation and the optional green cap (host code, `Aggregator`)

This runs **after** the constraint pass:

- **Summary.** Green/amber counts and a **projected UCAS tariff** = full `UcasWeight` per green
  + the loaded `AmberTariffFactor` (half) per amber. Weights live in `Catalogue`.
- **Ranking.** Subjects ordered green → amber → red, ties broken by descending UCAS weight.
- **Green cap (optional, off by default).** `MaxGreenChoices` is normally unset in
  `data/thresholds.yaml`, so the cap does nothing and every green stays green — the system reports
  what a student is *allowed* to study, not an enforced shortlist. If an admin sets it to a positive
  integer and more than that many subjects survive green, the lowest-UCAS-weight surplus greens are
  demoted to amber with reason "exceeds auto-enrol cap". It must run here because it counts the greens
  that *survived* the constraint downgrades.

Any cap demotions are recorded as more `Adjustment`s, so the final `Adjustment` trail explains *every*
downgrade — constraint pass and (when enabled) cap alike.

### The output

`EnrolmentEngine` projects all of the above into an `EnrolmentResult`:

```jsonc
{
  "eligible": true,
  "eligibility_reasons": [],
  "recommendations": [ { "subject": "maths", "rating": "green", "reason": "..." }, ... ],
  "summary": { "green_count": 4, "amber_count": 5, "projected_tariff": 300 },
  "adjustments": [ { "subject": "art", "from": "green", "to": "amber", "reason": "Mutual exclusion with history — authorisation required" }, ... ]
}
```

`--explain` produces a richer `ExplainedResult` that additionally names, per subject, the
deciding engine rule, the base rating it gave, the predicted points the tier matched on, and
every host-code override. `--explain-text` renders the same object as plain Markdown prose for
advisors who want something readable in a terminal or pipe. Both views are pure projections of
a *single* evaluation — nothing is re-run.

`--advise` uses the same engine-backed pipeline to propose the smallest GCSE grade changes that
would lift each amber/red subject, or clear the eligibility gate for an ineligible student. The
per-subject search only ever raises GCSEs the student *already sat* — a grade bump is actionable
advice, "go and take another GCSE from scratch" is not — so a subject gated on a qualification the
student never took (e.g. French A-level wanting a French GCSE) is reported unreachable for that entry
reason rather than by inventing a new qualification. (The eligibility gate-clearing bundle is the one
exception: English, Maths and any extra passes needed to clear the gate are genuinely required, so it
may propose sitting them.) A restudy bar is likewise a hard non-GCSE blocker the advisor reports
rather than suggesting grade changes that cannot remove it.

The held-only restriction can be lifted for diagnosis: the `advice_considers_unsat_gcses` knob in
`data/thresholds.yaml` (off by default), or the per-call `considerUnsatGcses` argument / CLI
`--advise --all-gcses` flag, reverts to the old, much heavier search that also proposes sitting
brand-new GCSEs — handy for understanding *why* a subject is unreachable, not for normal operation.

---

## 5. How to interact with it

### From the command line

All commands run from the `EnrolmentRules/` directory. The modes you reach for most:

```bash
# Prediction profile only:
dotnet run --project src/EnrolmentRules.Cli -- examples/student.json
# The traffic-light table:
dotnet run --project src/EnrolmentRules.Cli -- --table examples/student.json
# An explained result (rule provenance + overrides), as Markdown prose:
dotnet run --project src/EnrolmentRules.Cli -- --explain-text examples/student.json
```

The rest — `--json`, `--explain`, `--advise`, `--batch`, `--lint-workflows` — plus the full
exit-code table and the input shape are in [`README.md`](../README.md). Single-student modes also
accept a YAML document (chosen by file extension); `--batch` is JSONL only.

### From code

The whole pipeline is behind one façade. Build the engine **once** with
`EnrolmentEngine.CreateAsync` (loading + schema-validating + probe-compiling the workflows,
catalogue and thresholds), then reuse it across students — it is stateless and parallel-safe. The
reference ("as-of") date is read at this edge, so the engine stays a pure function of
`(document, as-of date)`:

```csharp
using EnrolmentRules.Engine;

var asOf      = DateOnly.FromDateTime(DateTime.Today);
var enrolment = await EnrolmentEngine.CreateAsync("workflows/", "data/", asOf);

EnrolmentResult result    = await enrolment.EvaluateAsync(student);
ExplainedResult explained = await enrolment.ExplainAsync(student);
```

For long-running hosts (live clock per evaluation), the `IEnrolmentEngine` abstraction, DI
registration via `AddEnrolmentEngine`, and `Stream`-based overloads, see the
[library usage section in `README.md`](../README.md#using-enrolmentrules-as-a-library).

---

## 6. How to change the rules

**Routine configuration is entirely a YAML process** — no normal-operation change touches compiled
code. The first three paths below are that everyday config (the three data homes). The last two —
adding a new relationship *type* or a new per-student attribute — are **engine evolution, not
configuration**: code changes that extend what the system can express, rare and out of scope for
normal operation. They are listed only to mark the boundary.

**Retuning a numeric knob (the "tuning-data" path).** Edit `data/thresholds.yaml`. For example,
to require 6 GCSE passes instead of 5, set `min_passes: 6` — the `EnoughPasses` expression already
reads `policy.MinPasses`, so no expression or code change is needed. The file is loaded and
schema-validated at startup (`PolicyThresholdsStore`) and threaded in as `PolicyThresholds`, so the
new value flows to *both* the eligibility lambda and the host pass (optional green cap, tariff, advice). No
recompile — but **you must add or update a test that drives the change through the engine** (see
[defence in depth](#7-why-you-can-trust-untyped-rules-defence-in-depth)).

**Changing a tier's shape (the "rules-as-data" path).** Edit the YAML in `workflows/`. To make
Physics need a predicted A for green, change `physics:green` in `subject-ratings.yaml`.

> The numeric knobs live in `data/thresholds.yaml`, loaded once as `PolicyThresholds` and read by
> the lambdas as `policy.*` (eligibility) / `facts.*` (ratings) and by the host pass directly —
> there is no compiled policy constant to keep in step (only the GCSE-scale invariants
> `Thresholds.Min`/`MaxGcseGrade` remain in code). Tests pin against the loaded file. The loader
> injects the boilerplate `RuleExpressionType = LambdaExpression` for actual rules, so authors do
> not need to repeat it.

**Changing a subject relationship (the everyday, data-file path).** Edit `data/catalogue.yaml` —
weights, exclusion pairs, own-time/veto activity prefixes, prerequisites, entry equivalents, restudy
bars. The file is loaded and schema-validated at startup (`CatalogueStore`), with coverage and
exclusion-symmetry enforced as load-time invariants. For example, the History ↔ Art exclusion is a
data entry, so adding a new clashing pair is a one-line YAML edit, not a code change. This is how
you change *which* subjects relate and *how strictly* — the common case.

**Adding a new relationship *type* (engine evolution, not config).** Only when you need a kind of
relation that does not exist yet — beyond prerequisite, mutual exclusion, prior-choice exclusion,
own-time, veto, and restudy bar — do you touch `ConstraintPass` / `Aggregator`. The existing six
types cover the policy you can express as data; a seventh is a structural change to the engine, not a
routine edit, and not the kind of thing that changes within an academic year. This is a project
change, out of scope for normal operation — see the
[relationship-types section](rule-authoring.md#4-relationship-types-how-they-apply) of the
rule-authoring guide for how the existing types apply, and raise a new one for discussion.

**Adding a new per-student attribute (engine evolution, not config).** Likewise a code change, not
configuration. A signal a rule reads but the document does not yet carry follows a fixed path: add
the raw fact to `StudentInput` (and the validator, if
required at the boundary); derive or carry it onto `StudentProfile` in `GradePredictor.Predict`
(anything date- or context-dependent, like age, takes the reference date already threaded there);
expose it on `RatingFacts` so the lambda can read `facts.X`; then write the gate in the YAML.
`date_of_birth` → `facts.Age` is the worked example — host code plumbs the value, the workflow
decides the policy.

**Changing DfE probability policy.** There are three different levers; keep them separate:

| Change | Where |
| --- | --- |
| Use a different probability threshold | `min_dfe_green_probability_at_or_above` in `data/thresholds.yaml` (the lambdas read `facts.MinDfe*ProbabilityAtOrAbove`). |
| Change which tiers use DfE probabilities | `workflows/subject-ratings.yaml`, using `facts.DfeProbabilityAtOrAbove(...)`, plus engine tests. |
| Refresh or remap the national data | `data/dfe-transition-matrices/` and `DfeTransitionMatrix`, plus source/provenance and CSV contract tests. |

Do not make workflow YAML parse CSV, inspect DfE subject codes, or calculate prior-attainment
bands. The workflow should only consume already-shaped facts. Data loading, band mapping, and
missing-row behaviour belong in typed host code.

The guiding principle: **per-student-fact rules go in the workflows; subject-relationship rules go
in the catalogue, and the host code applies them.** If you find yourself wanting a rule to read
another rule's result, that relation belongs in the catalogue and is applied by `ConstraintPass`,
not the engine — and only a brand-new *type* of relation requires touching that code.

---

## 7. Why you can trust untyped rules: defence in depth

Because workflow expressions are untyped strings (RulesEngine constraint #1), the project
guards rule correctness with **seven independent layers**. No single layer is trusted alone, and
**no rule is ever signed off by eyeballing JSON**:

| Layer | What it catches | When |
| --- | --- | --- |
| **JSON-Schema validation** | Structurally broken workflow (missing field, wrong type) | startup (`WorkflowStore`) |
| **Probe-evaluation** | Lambda compile/binding errors — typo'd field, bad expression | startup (`WorkflowStore`) |
| **Per-rule engine tests** | A rule's real pass/fail vs. intent, including DfE probability gates | CI |
| **DfE extract tests** | A known official CSV row maps to the expected band/probability | CI |
| **Golden-file tests** | Whole-pipeline regressions (predict → engine → constraints → cap) | CI |
| **Property/invariant tests** | Never-throws, always-valid rating, and catalogue invariants over random students | CI |
| **Drift tests** | Catalogue/workflow divergence — a subject with no rule group, an unnamed rule | CI |

The two **startup** layers deserve a closer look, because they are what turn RulesEngine's
weaknesses into loud boot-time failures rather than silent mis-enrolments:

- **Schema validation** (`workflow.schema.json`) checks *structure* only — that each rule has a
  name and a string expression, etc. It cannot see *inside* a lambda string, so a typo'd field
  passes schema.
- **Probe-evaluation** is the real lambda guard. RulesEngine compiles expressions lazily on
  first execution, so a bad field reference would otherwise only blow up when that rule first
  fires for some unlucky student. `WorkflowStore.ProbeCompileAsync` therefore **runs every
  workflow once at startup against a fully-populated canonical student**, forcing eager
  compilation. A typo'd field or malformed expression becomes a `WorkflowProbeException` at
  boot.

The practical rule for contributors: **treat workflow YAML as executable code.** A workflow
change is a code change, and it must keep all seven layers green
(`dotnet build -warnaserror`, `dotnet format`, `dotnet test`).

---

## 8. Where to read next

- `src/EnrolmentRules.Engine/EnrolmentEngine.cs` — the façade; read `RunAsync` to see the
  pipeline order in one place.
- `src/EnrolmentRules.Engine/WorkflowStore.cs` — loading, schema validation, and probe
  compilation.
- `src/EnrolmentRules.Engine/RatingEvaluator.cs` — how engine results become base ratings, and
  the input-shaping (`GcseFacts`, `RatingFacts`).
- `src/EnrolmentRules.Prediction/DfeTransitionMatrix.cs` and `data/dfe-transition-matrices/` —
  the DfE probability-evidence loader and its local source extract.
- `src/EnrolmentRules.Engine/ConstraintPass.cs` and `Aggregator.cs` — the host-code half.
- `workflows/eligibility.yaml` and `workflows/subject-ratings.yaml` — the rules-as-data.
- `tests/EnrolmentRules.Tests/` — one `PhaseNTests.cs` per pipeline stage, plus the golden-file
  and invariant suites; the best worked examples of expected behaviour.
