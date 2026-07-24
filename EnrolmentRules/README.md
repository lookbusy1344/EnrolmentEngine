# Enrolment Rules Engine - AI assisted enrolment decisions

![Green shoots](Green_shoots.jpg)

This is a recreation of a proprietary project I developed a few years ago, to assist in enrolment decision-making and ensure policies were
consistently followed. The real system was also capable of writing the complete enrolment package into the management information system, and printing
forms for signature.

## A monotonic, rules-as-data engine for A-Level enrolment decisions

EnrolmentRules is a decision-support system for A-Level enrolment. It gives staff a consistent, explainable recommendation for each student and
subject while keeping the final decision visible and accountable.

### [Green Shoots demo site](https://enrolment-web-716005672573.europe-west1.run.app)

**May take a couple of seconds to wake the docker image**, be patient!

*Green Shoots* is the engine's front end demo: staff enter a student's GCSE results and any prior qualifications, and instantly get a recommendation
for every A-Level subject on offer, each with a plain-English reason. It gives admissions and pastoral staff a consistent, defensible answer for every
student in seconds rather than a judgement call that varies by who's on duty, and a clear audit trail for why a decision was made if it's ever
challenged.

The system is anonymous, with no sign-in. Hosted on Google Cloud Run and scaled to zero, so the first request after an idle period **takes a second or
two to wake** the instance. See [docs/deployment.md](docs/deployment.md)
to run it locally or host your own.

## What the Engine Does

For each student, the system first checks whether they meet the institution's overall entry policy. It then assesses every available A-Level subject
and gives it a clear recommendation:

- **Green** - the student meets the normal requirements.
- **Amber** - enrolment may be appropriate but needs staff review or authorisation.
- **Red** - the published requirements are not met.

The system provides explainable recommendations based on specific policy conditions. These results can either serve as 1) advisory support for staff
judgment or 2) enforced as a hard gate to guarantee consistent decision-making. The original system evolved from 1 to 2, with management override (not
replicated here).

## AI in Production

This engine is an application of AI using multiple techniques: **statistical learning** paired with a **symbolic AI**
engine. Linear regression over GCSE results predicts each student's likely A-Level outcome from historical attainment data - the statistical learning
half. That prediction then feeds a symbolic AI engine, which evaluates the institution's published policy - entry thresholds, subject ratings,
prerequisites, exclusions - as explicit, human-readable rules. Every recommendation is completely reproducible and highly interpretable. Run the same
student through the same policy and you get the same answer, every time, with a plain-English reason attached - none of the black-box guesswork that
comes with a purely statistical model.

Managers have full control over rule application. They can be written as simple logical expressions.

## Information It Can Consider

The decision can draw on more than a simple grade threshold. Depending on the institution's policy, it can consider:

- GCSE results in individual subjects and the student's overall attainment.
- Predicted A-Level outcomes and published evidence about likely progression from GCSE to A-Level.
- The student's age at the date of assessment, where different entry arrangements apply to adults.
- Relevant qualifications already achieved, including equivalent routes into a subject.
- A-Level choices already made, including combinations that become unavailable once another subject in the clash has been chosen.
- Subject prerequisites, conflicting subjects, previous study and restrictions on repeating a qualification.
- Relevant activities or experience, such as evidence that an own-time practical requirement can be met.

This allows the institution to apply the whole policy consistently, while still distinguishing routine cases from those that genuinely require
professional judgement. Subjects in an exclusion pair remain available until one side is actually chosen; red recommendations are not selectable in
the reference web UI.

## What It Delivers

- **Consistent decisions** - the same published policy is applied to every student.
- **Clear recommendations** - subjects are rated green, amber, or red, with reasons staff can review.
- **Faster enrolment** - straightforward choices can be identified immediately; exceptions are directed to an authorised colleague.
- **Transparent decisions** - explanations show how student information and policy led to the outcome.
- **Better policy control** - routine policy changes can be reviewed and tested independently of the surrounding application.

## Designed For Policy Change

Most changes do not require the application itself to be redesigned. Entry thresholds, subject ratings, qualification equivalents, prerequisites,
exclusions and other routine policy settings are held as configuration. This makes policy easier to review, version, test and deploy through a
controlled process.

To make that concrete, here is the complete policy for one course, **A-Level Further Maths**, exactly as it ships. The traffic-light rating rules live
in
`workflows/subject-ratings.yaml` as logical expressions the engine evaluates per student:

```yaml
# workflows/subject-ratings.yaml — the green / amber / red tiers for Further Maths.
# Entry is met by a top-grade GCSE Maths (facts.TopEntry) with an overall GCSE average of 7.0+, OR
# an equivalent prior qualification
- RuleName: 'further_maths:green'
  Expression: >-
    (facts.HasEntryEquivalent("further_maths") || (facts.Gcse("maths") >= facts.TopEntry && facts.Average >= 7.0))
    && facts.Predicted("further_maths") >= ALevelGrade.D
    && facts.DfeProbabilityAtOrAbove("further_maths", ALevelGrade.D) >= facts.MinDfeGreenProbabilityAtOrAbove
- RuleName: 'further_maths:amber'
  Expression: >-
    (facts.HasEntryEquivalent("further_maths") || (facts.Gcse("maths") >= facts.TopEntry && facts.Average >= 7.0))
    && facts.Predicted("further_maths") >= ALevelGrade.E
    && facts.DfeProbabilityAtOrAbove("further_maths", ALevelGrade.E) >= facts.MinDfeAmberProbabilityAtOrAbove
- RuleName: 'further_maths:red'
  Expression: 'true'
```

Cross-subject relationships — how a course interacts with the rest of a student's basket — live in `data/catalogue.yaml`:

```yaml
# data/catalogue.yaml — the same course's relationships and prediction model.
- subject: further_maths
  priority_weight: 56
  regression: { slope: 1.00, intercept: -2.00 }   # predicts the A-level grade from GCSE attainment
  prerequisites:
    - any_of: [ maths ]        # Further Maths requires Maths...
      requires: chosen         # ...actually chosen this year (not merely available)
  entry_equivalents:
    - { subject: maths, type: a_level, min_grade: d }   # ...or a prior A-level Maths at grade D+
```

Nothing above is compiled in: an admissions lead can raise the entry bar, relax the prerequisite, or add an equivalent route by editing these two
files. Configuration is checked when it is loaded, and policy changes are backed by automated tests. A running service can also reload an approved
policy set without replacing the software around it.

## Designed For Integration

The engine is a reusable decision library rather than a fixed application with one user interface. An organisation can place the same decision-making
capability behind:

- an intranet or website similar to [Green Shoots demo site](https://enrolment-web-716005672573.europe-west1.run.app)
- a student or staff mobile app
- a desktop enrolment application
- a command-line or batch-processing tool
- an internal service used by your existing MIS software

This separation means the presentation can change without duplicating the enrolment policy. A web page, mobile app and back-office process can all
receive the same recommendation from the same policy and student information.

`src/EnrolmentRules.Web` is a small reference implementation of the website option. Run it locally with `./scripts/run-web.sh`, which builds the
generated Vue assets and then watches both the C# and Vue sources, rebuilding each on change; run the focused web gate with `./scripts/verify-web.sh`.
See
[Web Interface](docs/technical-reference.md#web-interface) for details and Rider debugging setup. To run it as a container (OrbStack locally, or a
free container host), see the technical
[deployment guide](docs/deployment.md).

## Quality And Assurance

The project is maintained to a high engineering standard. Automated static analysis examines every software build for correctness, consistency and
common defects, and warnings are treated as failures rather than being allowed to accumulate.

A robust automated test suite covers individual components, complete end-to-end decisions and the policy rules themselves. Configuration is validated
before use, and representative outcomes are checked against approved examples, reducing the risk that a software or policy change silently alters an
enrolment recommendation.

## Project Status

This open-source reference implementation recreates the core decision-support ideas of a production enrolment system using modern .NET. It is
stateless, designed for integration, and licensed under MIT.

For setup, architecture, input formats, command-line usage, and library integration, see the
[technical reference](docs/technical-reference.md). The [guided walk-through](docs/walkthrough.md)
explains the decision process from first principles.

## License

MIT
