# Changelog

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
