# Extracting DfE transition-matrix rows

How to regenerate or extend
`data/dfe-transition-matrices/gce-a-level-2019-transition-probabilities.csv` from the official
Department for Education workbook. The committed CSV is a narrow extract; the full workbook is
intentionally not committed (see `SOURCE.md` in that directory).

## Source

- Publication: *Ready reckoner and transition matrices for 16 to 18: 2019*
- Workbook: `TMs_2019A_live_final.xlsx`
- URL: <https://assets.publishing.service.gov.uk/media/5e25971940f0b62c45460061/TMs_2019A_live_final.xlsx>
- Qualification extracted: **111 — GCE A level**

The download must run outside the build sandbox (network). The cleanest path is to fetch it
yourself in-session with the `!` prefix rather than via a sandboxed tool call.

## Workbook structure

`.xlsx` is a zip of XML — no `openpyxl` needed, stdlib `zipfile` + `xml.etree` suffices. Cell
text comes from `xl/sharedStrings.xml` (string cells carry `t="s"` and a `<v>` index into it;
numeric cells hold the raw value string directly in `<v>`).

Sheets (from `xl/workbook.xml` → resolve `r:id` via `xl/_rels/workbook.xml.rels`):

| Sheet name                     | File          | Use                                     |
|--------------------------------|---------------|-----------------------------------------|
| `All Data - Percentages`       | `sheet4.xml`  | the transition probabilities (one row per qual × subject × prior-attainment band) |
| `Qualification & Subject Lookup` | `sheet5.xml`  | subject-number → subject-name (cols F, G) |
| `QualSub`                      | `sheet6.xml`  | which subject numbers exist per qualification |

### Percentages sheet column map (the non-obvious part)

Key columns: `A` = Qualification Number (`111` for A level), `B` = Subject Number,
`D` = Prior_Band, `E` = Row ID, `F`..`CB` = grade-percentage columns.

The grade columns are **not** contiguous and the row-3 header labels are misleading (they cover
all qualification grade scales stacked into one sheet). For **A level**, the columns that hold the
A*/A/B/C/D/E/U distribution were confirmed by reconciling against an already-committed row
(`biology`, band `7 to < 8`):

| A-level grade | Column |
|---------------|--------|
| A*            | `F`    |
| A             | `AP`   |
| B             | `AS`   |
| C             | `AV`   |
| D             | `AY`   |
| E             | `BI`   |
| U             | `CB`   |

A blank cell means that grade was suppressed/zero in the source — keep it **empty** in the CSV
(the committed file does this, e.g. `0.4,,0.2,...`), do not write `0`.

## CSV output contract

- Header: `subject,dfe_qualification_number,dfe_subject_number,dfe_subject_name,prior_attainment_band,probability_u,probability_e,probability_d,probability_c,probability_b,probability_a,probability_a_star`
  — note the probability columns run **U → A\*** (reverse of the workbook's grade order).
- Emit only the bands actually populated for a subject, in ascending order:
  `< 1, 1 to < 2, 2 to < 3, 3 to < 4, 4 to < 5, 5 to < 6, 6 to < 7, 7 to < 8, 8 to < 9, >=9`.
- Preserve the raw workbook value strings verbatim (this is why the file carries
  scientific-notation values like `4.1666...E-2`).
- **Line endings are LF** (normalised via `.gitattributes`); emit LF when regenerating.
- **The loader validates the output**, so a regenerated file that breaks the contract fails engine
  startup with a `TransitionMatrixException` rather than loading silently: the header must match the
  string above exactly, there must be at least one data row, every `subject`/`prior_attainment_band`
  pair must be unique, each probability must parse to a finite value in `[0, 1]` (a blank counts as
  zero), and each row's seven probabilities must sum to 1. Blank only genuinely-zero source cells —
  blanking a small non-zero value would drop the row total below 1 and be rejected.
- `subject` is our snake_case id; map it to the workbook's `dfe_subject_number`/name. The
  subjects modelled by this project and their DfE numbers:

  | snake_case          | DfE no. | DfE name                  |
  |---------------------|---------|---------------------------|
  | maths               | 12210   | Mathematics               |
  | further_maths       | 12330   | Mathematics (Further)     |
  | physics             | 11210   | Physics                   |
  | chemistry           | 11110   | Chemistry                 |
  | biology             | 11010   | Biology                   |
  | english_language    | 15030   | English Language          |
  | english_literature  | 15110   | English Literature        |
  | french              | 15650   | French                    |
  | german              | 15670   | German                    |
  | physical_education  | 17210   | Physical Education/Sports Studies |
  | computer_studies    | 12610   | Computer Studies/Computing |
  | history             | 14010   | History                   |
  | music               | 17010   | Music                     |
  | art                 | 13510   | Art & Design              |
  | economics           | 14410   | Economics                 |
  | geography           | 13910   | Geography                 |
  | psychology          | 14850   | Psychology                |
  | sociology           | 14890   | Sociology                 |
  | business_studies    | 13210   | Business Studies:Single   |
  | politics            | 14830   | Government & Politics      |
  | religious_studies   | 14610   | Religious Studies         |
  | drama               | 15210   | Drama & Theatre Studies   |
  | media_studies       | 15350   | Media/Film/Tv Studies     |
  | law                 | 14770   | Law                       |
  | spanish             | 15750   | Spanish                   |
  | design_technology   | 19080   | D&T Product Design        |

## Small-cell suppression (top band only)

The `>=9` prior-attainment band is a tiny cohort (a straight-9s GCSE average), so its row is often
based on a handful of students and is statistically noisy — e.g. geography's `>=9` cell is **2
students** with `P(≥B)=0.50`, which would rank a top student *below* the green DfE floor purely on
sample noise. To avoid that, the **`>=9` band row is suppressed when it represents fewer than 5
students** (counts read from the workbook's `All Data - Student Numbers` sheet, `sheet3.xml`, same
column map). The loader (`DfeTransitionMatrix.FindEvidence`) then falls back to the adjacent
well-sampled `8 to < 9` band, which is what a top student is scored against. The fallback is
observable, not silent: the returned `TransitionEvidence` sets `RequestedBand` to the band asked
for and flags `Imputed = true` (with `PriorAttainmentBand` still naming the band the probabilities
describe), so a top student scored against `8 to < 9` is visibly marked as borrowed.

Suppression is confined to the **top** band: lower bands keep their small cells (e.g. biology's
`< 1` cell is `n=2` and is retained), because they sit below the entry gates and never feed the
"top student is green everywhere" invariant. Subjects suppressed at `>=9` under the n<5 rule:
`music` (1), `computer_studies` (2), `geography` (2), `politics` (2), `business_studies` (3),
`spanish` (3), `english_literature` (4). The threshold (5) is the only knob; a regeneration script
reads both sheets, applies it to the `>=9` row, and re-emits every subject in catalogue order.

## Validation

Each row's probabilities (treating blanks as 0) must sum to `1.0` within `1e-6`. The loader
(`DfeTransitionMatrix`) rejects rows that do not have exactly 12 fields, and a subject absent from
the file falls back to empty evidence — DfE probability `0`, which pins every rating to red. So an
omitted or malformed subject is silently wrong at the policy layer, not a load error; the sum check
above is the guard.
