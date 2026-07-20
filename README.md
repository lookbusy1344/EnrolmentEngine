# Enrolment Rules Engine - AI assisted enrolment decisions

![Green shoots](EnrolmentRules/Green_shoots.jpg)

This is a recreation of a proprietary project I developed a few years ago, to assist in enrolment decision-making and ensure policies were consistently followed. The real system was also capable of writing the complete enrolment package into the management information system, and printing forms for signature.

### [Green Shoots demo site](https://enrolment-web-716005672573.europe-west1.run.app)

**May take a couple of seconds to wake the docker image**

*Green Shoots* is the engine's front end demo: staff enter a student's GCSE results and any prior
qualifications, and instantly get a recommendation for every A-Level subject on offer, each with a
plain-English reason. It gives admissions and pastoral staff a consistent, defensible answer for
every student in seconds rather than a judgement call that varies by who's on duty, and a clear
audit trail for why a decision was made if it's ever challenged.

## A monotonic, rules-as-data engine for A-Level enrolment decisions

The full source code can be found in the [Project folder](EnrolmentRules/)

### AI in Production

This engine is an application of AI using multiple techniques: **statistical learning** paired with a **symbolic AI**
engine. Linear regression over GCSE results predicts each student's likely A-Level outcome
from historical attainment data - the statistical learning half.
That prediction then feeds a symbolic AI engine, which evaluates the institution's published
policy - entry thresholds, subject ratings, prerequisites, exclusions - as explicit,
human-readable rules. Every recommendation is completely reproducible and highly interpretable. Run the same student through
the same policy and you get the same answer, every time, with a plain-English reason attached -
none of the black-box guesswork that comes with a purely statistical model.

Managers have full control over rule application. They can be written as simple logical expressions.
