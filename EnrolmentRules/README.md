# EnrolmentRules - A monotonic, rules-as-data engine for A-Level enrolment decisions

This is a recreation of a proprietary project I developed about a decade ago, to assist in enrolment decision-making and ensure policies were
consistently followed. The real system was also capable of writing the complete enrolment package into the management information system, and printing
forms for signature.

EnrolmentRules is a decision-support system for A-Level enrolment. It gives staff a consistent,
explainable recommendation for each student and subject while keeping the final decision visible
and accountable.

## What It Does

For each student, the system first checks whether they meet the institution's overall entry policy.
It then assesses every available A-Level subject and gives it a clear recommendation:

- **Green** - the student meets the normal requirements.
- **Amber** - enrolment may be appropriate but needs staff review or authorisation.
- **Red** - the published requirements are not met.

The system provides explainable recommendations based on specific policy conditions. These results can either serve as 1) advisory support for staff
judgment or 2) enforced as a hard gate to guarantee consistent decision-making. The original system evolved from 1 to 2, with management override (not
replicated here).

## Information It Can Consider

The decision can draw on more than a simple grade threshold. Depending on the institution's policy,
it can consider:

- GCSE results in individual subjects and the student's overall attainment.
- Predicted A-Level outcomes and published evidence about likely progression from GCSE to A-Level.
- The student's age at the date of assessment, where different entry arrangements apply to adults.
- Relevant qualifications already achieved, including equivalent routes into a subject.
- A-Level choices already made, including combinations that are not permitted or not recommended.
- Subject prerequisites, conflicting subjects, previous study and restrictions on repeating a
  qualification.
- Relevant activities or experience, such as evidence that an own-time practical requirement can
  be met.

This allows the institution to apply the whole policy consistently, while still distinguishing
routine cases from those that genuinely require professional judgement.

## What It Delivers

- **Consistent decisions** - the same published policy is applied to every student.
- **Clear recommendations** - subjects are rated green, amber, or red, with reasons staff can review.
- **Faster enrolment** - straightforward choices can be identified immediately; exceptions are
  directed to an authorised colleague.
- **Transparent decisions** - explanations show how student information and policy led to the
  outcome.
- **Better policy control** - routine policy changes can be reviewed and tested independently of the
  surrounding application.

## Designed For Policy Change

Most changes do not require the application itself to be redesigned. Entry thresholds, subject
ratings, qualification equivalents, prerequisites, exclusions and other routine policy settings are
held as configuration. This makes policy easier to review, version, test and deploy through a
controlled process.

Configuration is checked when it is loaded, and policy changes are backed by automated tests. A
running service can also reload an approved policy set without replacing the software around it.

## Designed For Integration

EnrolmentRules is a reusable decision library rather than a fixed application with one user
interface. An organisation can place the same decision-making capability behind:

- a staff-facing website;
- a student or staff mobile app;
- a desktop enrolment application;
- a command-line or batch-processing tool;
- an internal service used by a management information system or other existing software.

This separation means the presentation can change without duplicating the enrolment policy. A web
page, mobile app and back-office process can all receive the same recommendation from the same policy
and student information.

## Quality And Assurance

The project is maintained to a high engineering standard. Automated static analysis examines every
software build for correctness, consistency and common defects, and warnings are treated as failures
rather than being allowed to accumulate.

A robust automated test suite covers individual components, complete end-to-end decisions and the
policy rules themselves. Configuration is validated before use, and representative outcomes are
checked against approved examples, reducing the risk that a software or policy change silently alters
an enrolment recommendation.

## Project Status

This open-source reference implementation recreates the core decision-support ideas of a production
enrolment system using modern .NET. It is stateless, designed for integration, and licensed under MIT.

For setup, architecture, input formats, command-line usage, and library integration, see the
[technical reference](docs/technical-reference.md). The [guided walk-through](docs/walkthrough.md)
explains the decision process from first principles.

## License

MIT
