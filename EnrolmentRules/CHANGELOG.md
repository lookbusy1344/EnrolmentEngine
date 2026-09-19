# Changelog

## 1.4.0 — 2026-09-19

- GCSE subject vocabulary moves from a hardcoded C# array to `data/gcse-subjects.yaml`, closing the
  last vocabulary source that required a code change and release; adds the missing `spanish` GCSE
  key alongside the existing A-level Spanish subject.
- The policy set is discovered from `policies/<id>/policy.yaml` manifests instead of being
  hard-coded in each host; adding a policy is now a directory to drop in, not a code change in the
  CLI and web hosts.
- `/api/enrolment/*` rate-limits by a trusted client-address partition (1000 requests/10s, 429 with
  `Retry-After`; direct connection by default, explicit opt-in for Google Cloud's external load
  balancer), and responses now carry `X-Content-Type-Options`, `Referrer-Policy`, and a CSP scoped
  to the app's own origins.
- The Standard policy's advisor pipeline budget is capped instead of running unbounded, bounding a
  best-first search over the shared engine against pathological input.
- `--batch` no longer aborts the whole run on one bad line — a per-line evaluation failure is
  isolated to that line's outcome instead of surfacing as an unhandled exception.
- The advisor's restudy-bar detection matches on the adjustment kind instead of parsing reason
  text.
- Web: fixed a family of policy-switch and reset bugs where a previous policy's basket comparison
  could leak into the newly-selected policy or a freshly started session.
- Web: the masthead canopy leaves pivot on their own bounding box, fixing an intermittent overflow
  past the band's edge.
- Web: options responses (grades, subjects, hobbies) are cached per policy and read the engine's
  own reference date, instead of rebuilding on every request off a separately-injected clock.
- Dependency updates (NuGet and npm), pnpm pin bump.

## 1.3.0 — 2026-08-25

- The masthead is now a full-bleed soil band: a deep-evergreen ground with the brand mark and
  wordmark scaled up and locked together over an A-Level decision engine tagline, so the header
  reads as the shoot growing out of the earth rather than sitting loose on the paper page.
- Behind it, a canopy of the brand mark's own leaf blows across the band. The arrangement is
  scattered afresh on every page load, and each leaf drifts, tumbles and fades on its own slow
  cycle in one of three parallax depths. Held quieter below Bootstrap's 768px breakpoint, where
  the band is barely wider than the wordmark, and still under `prefers-reduced-motion`.
- Removed the Razor Pages front end entirely — `EnrolmentRules.Web` now serves the Vue app at
  both `/` and `/app`, with no server-rendered form flow, PRG cookies, or dynamic/server-rendered
  mode switch. Multi-instance deploys no longer need sticky routing either.
- Grade picker: hover feedback on the flattened button row at wide breakpoints, tighter touch
  snapping on the drum and the wide row, and Remove buttons now match the grade wheel's height
  across every facts row.
- Hobby picker no longer offers a bare catalogue prefix (e.g. "Plays") as if it were a hobby.

## 1.2.0 — 2026-08-21

- GCSE grades are now picked from a spin wheel instead of a row of grade buttons, with the
  Remove button laid out alongside it across phone, tablet, and desktop breakpoints.
- Hardened the `CodeStyle_*` file-length and method-length architecture guards (measured in
  code lines, not physical lines), and completed RulesEngine's isolation within the Engine
  project.

## 1.1.2 — 2026-08-18

- "What's open to you" cards and basket pills now both sort green, then amber, then red,
  alphabetical within each colour (previously engine priority-weight order for the cards, and a
  plain valid/invalid split for the basket).

## 1.1.1 — 2026-08-16

- `/razor` and `/app` now share one `localStorage`-backed facts store, so edits in either front
  end carry over to the other and survive closing the browser.
- Basket display groups valid choices before invalid ones, alphabetical within each group.
- Assorted UX fixes: over-limit warning wording, `/razor` empty-basket confirmation matches
  `/app`'s design, GCSE grade picker stays on one row in phone landscape.
- Hardened the `CodeStyle_*` architecture guard tests: Razor is parsed with the SDK's own
  Roslyn/Razor compiler instead of scanned with regex, and the struct-size scan covers every
  project under `src`.

## 1.1.0 — 2026-08-15

- Multi-policy support: a policy registry with non-destructive comparison between policies, and a
  new Elite auxiliary policy (full fourteen-subject range, top-N eligibility gate).
- Policy selection exposed through the CLI, the web API, and both Razor and Vue front ends.
- Final programme selections are validated end-to-end (`EvaluateValidated`/`ExplainValidated`), and
  a committed choice can no longer be reported red
- Basket UX: live GCSE scoreboard, per-choice remove, empty-basket icon, English
  Language/Maths pinned to the top of the GCSE picker.

## 1.0 — 2026-08-10

- Initial release: eligibility gate and per-subject entry/rating rules as RulesEngine YAML
  workflows, with GCSE averaging + linear-regression prediction upstream and a cross-subject
  constraint pass (prerequisites, exclusions, own-time, vetoes, green cap) downstream.
- `EnrolmentRules.Cli` for single/batch student evaluation, and `EnrolmentRules.Web` — a Razor
  Pages front end plus a Vue front end, both backed by anonymous `localStorage` facts editing with
  no server-side session.
- UCAS tariff summary, ranked shortlist, and full explanation output.
- Golden-file and invariant/property test suite driven through the engine; startup probe-evaluation
  and JSON-Schema validation guard the untyped rule data.
