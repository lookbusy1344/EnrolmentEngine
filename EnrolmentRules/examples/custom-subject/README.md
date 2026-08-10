# Custom subject example

This folder is a concrete Phase 3 reference for adding an A-level subject by data only: `philosophy`,
a subject the shipped catalogue and workflows do not define.

It shows the three pieces that must move together:

1. `data/catalogue.append.yaml`
   Add the per-subject metadata row: priority weight, regression coefficients, and any exclusions /
   prerequisites / own-time policy. `dfe_evidence: false` opts the subject out of the DfE
   transition-matrix coverage guard (`DfeTransitionMatrix.ValidateCoverage`) — this example's tiers
   never call `facts.DfeProbabilityAtOrAbove`, so no matching CSV rows are required. A subject whose
   tiers do call it should omit this flag (it defaults to `true`) and ship matrix rows instead.
2. `workflows/subject-ratings.append.yaml`
   Add the subject's `green` / `amber` / `red` workflow rules.
3. `student.json`
   A sample student that should receive a green recommendation for the new subject.

The files here are append snippets, not standalone replacements for the shipped catalogue or
workflow files. They are intended to be merged into:

- `data/catalogue.yaml`
- `workflows/subject-ratings.yaml`

You can lint the workflow snippet against its matching data by creating a temporary tree with
adjacent `data/` and `workflows/` directories, then running:

```bash
dotnet run --project src/EnrolmentRules.Cli -- --lint-workflows /path/to/workflows
```

The CLI will load the sibling `data/` directory automatically for linting.
