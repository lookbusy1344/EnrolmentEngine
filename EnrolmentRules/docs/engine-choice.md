# Engine Choice: RulesEngine vs NRules/RETE

A reasonable question, given how the cross-subject pass has grown: would a production-rules engine
like **NRules** (a RETE implementation, used by the sibling `EnrolmentSystem` project) have been the
better foundation? With hindsight, **no** - and the reasons are structural, not incidental.

RETE exists to make *forward-chaining inference over mutable working memory* fast. Its incremental
match network pays off precisely when:

- rules assert new facts that fire further rules - cyclic, multi-pass derivation;
- the same facts are matched against a large rule set with shared conditions;
- working memory mutates and you want to avoid re-evaluating everything on each change.

None of those hold here.

## Why RETE Does Not Fit This Domain

**The problem is acyclic and monotone by construction.** Every adjustment only ever *downgrades* a
rating; nothing a rule produces feeds back as input to another rule. There is no fixpoint to iterate
toward, so RETE's defining feature - incremental rematch as facts change - has nothing to optimise.
A single ordered pass already computes the answer; most-severe-wins composes trivially.

**There is no working memory.** Each student is evaluated as a pure function of immutable facts.
RETE's agenda, conflict resolution, and truth maintenance are machinery for state this domain
does not have. We would carry the cost of a fact session per evaluation, retraction
semantics, salience, and conflict-resolution reasoning for capabilities we would never exercise.

**The hard part is relational aggregation over the result set, not rule matching.** The UCAS tariff,
exclusion/prerequisite resolution, and the optional green cap all compare a subject against its siblings. RETE
can express cross-fact joins, but a join network over every subject pair is a heavyweight,
error-prone way to write what is, in compiled C#, a sort and a fold (internal engine machinery).

RulesEngine's inability to read sibling-rule outcomes forced that logic into typed, tested,
debuggable code, which is where this relational, set-level logic belongs regardless of engine.
NRules would have permitted smearing it back into rules; that is a temptation, not a feature. The
per-subject/cross-subject boundary is the single most valuable structural decision in this codebase.

## Why RulesEngine Fits

What this project needs from a rules engine is narrow and unary: **hot-swappable,
non-programmer-editable, per-student decision tables**.

RulesEngine's rules-as-data YAML serves that directly. Policy ships as data, is schema-validated and
probe-compiled at startup, and can be retuned without a rebuild. NRules rules are C#/DSL compiled
into the assembly; gaining RETE would have cost the data/code separation that is the point of the
engine layer.

## The Real Trade-Off

RulesEngine's lambdas are **untyped strings**. A typo'd field or transposed comparison can compile
and silently mis-enrol. That is why the correctness strategy is layered:

| Layer                    | Catches                                                                 |
|--------------------------|-------------------------------------------------------------------------|
| JSON-Schema validation   | Structural workflow defects.                                            |
| Startup probe evaluation | Lambda binding and compilation errors.                                  |
| Engine-driven rule tests | Threshold and rule intent regressions, including DfE probability gates. |
| DfE extract tests        | A known official CSV row maps to the expected band and probability.     |
| Golden-file tests        | End-to-end result drift.                                                |
| Property/invariant tests | Random-input safety and catalogue invariants.                           |
| Drift tests              | Catalogue/workflow mismatch, missing subject rules, unnamed rules.      |

NRules would catch more at compile time. That is a genuine point in its favour, and if the
per-student rules were themselves complex or interdependent it might tip the balance. They are not:
they are flat unary table lookups, and the test layers neutralise the untyped-lambda risk cheaply.
We pay a known, bounded testing cost to keep policy as hot-swappable data.

## When To Revisit This

The verdict holds while the per-student rules stay flat and unary. If they become genuinely
interdependent, or the pipeline starts deriving intermediate facts that feed further rules toward a
fixpoint, the calculus changes. That is when NRules/RETE would deserve a real second look.

Until that signal appears, swapping engines would buy machinery the domain does not use at the cost
of the data/code separation it relies on.
