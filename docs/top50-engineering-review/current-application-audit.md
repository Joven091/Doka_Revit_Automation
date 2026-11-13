# Current application audit

Baseline reviewed on 28 October 2026: application 1.1.26, commit
`348f525ce019dd37a4aea38391ffc285baeec2fc`, tree
`171d0664aeda44df602bd66caddcd215b9a0086c`. This includes the preserved, reviewed
beam-worker work from draft PR174. It does not claim that work has merged into
main. The original checkout remains unchanged; this review uses a separate
worktree. The user confirmed the final target is Top50 inside the existing Revit
Engineering Dashboard.

## Functionality and verified baseline

Top50 is a native WPF planner shared by the net48 Revit dashboard and a portable
test host. DXF, reviewed manual walls and detached Revit wall selections feed the
same integer-micrometre calculation. Revit uses the longer straight forming face;
both original face measurements and source identities remain available for
review/refresh/highlight. Actual Revit host behavior is a separate acceptance
item, not proved by stand-ins or a desktop renderer.

Inventory candidates use catalog cutoffs and retained panel multisets. OR-Tools
9.12.4544 allocates each included wall exactly once under per-size reusable-stock,
internal-filler, group-count and longest-wall simultaneity constraints. Up to four
patterns are retained per wall length/catalog. Fixed deterministic work and a
single native search worker support repeatability. Pattern cache and robust
cancellation fixes from the previous audit are retained.

All presets admit the fewest validated panel-count tier first. Within that tier,
Pareto preference and the selected preset compare LM/types/groups or normalized
secondary costs. Economical does not calculate rental prices; Fastest does not
calculate site duration. Both fixed-longest and percentage-band offering methods
are useful inventory references, with different simultaneous-demand assumptions.

Physical panel Qty covers both faces; offered LM remains per face. Connector
stock covers one level on both faces. The adjustable stock is the maximum filler
demand of any group; standard stock completes the largest total joint demand,
allowing spare adjustable extensions at direct joints under the established
conditional assembly assumption. Independent exhaustive procurement arithmetic
supports this formula. Wall height and applicable levels remain the user's
engineering calculation.

Native Import/Settings/Results/Casting groups, compact inventory/connectors,
utilization, schematics, saved source-backed projects, undo/redo/delete and
diagnosis work and should remain. Reports/Excel are unavailable in the product.
Dormant legacy workbook code is not being exposed or turned into a new feature.

Baseline evidence:

- Independent QA: 8,416 assertions, including 756 complete tiny allocation cases,
  256 connector procurement vectors and a separately proved kit witness.
- Existing quantity/history/persistence suite: 397 checks pass before changes.
- Recorded 63-wall distribution: 46 physical panels, 99.25 LM per face and 10
  mathematical groups; independent returned-arrangement checks pass. Fresh
  reversed-order/German-culture processes reproduce the tested signature.
- Production and standalone cross-builds: zero warnings/errors. Package cache was
  used with command-only `NuGetAudit=false` after a proxy vulnerability-query
  failure; repository audit policy was not weakened.
- Current Windows baseline artifact 12839322417, from tested source
  `799b61f2b35015131863695491bd74d3b6dcaa96`, has the same Top50 sources as the
  baseline. SHA-256 `7b4fc8ce4af69c934ee557e8ab3c3c0a6da061f5a5d6bdd0042ff26837afe468`.
  Site review inspected all nine settings/results/casting renders at three
  scales. These are actual WPF captures, not an actual Revit session.

## Findings and disposition

Three reproducible defects are confirmed: a hidden identical-set preference
blocks percentage mode; project-policy-incompatible saved results can appear
fresh; and a fresh identical-kit solve can pool panels across kits for a required
wall and reject its own output despite a valid constrained allocation. The last
case needs a model constraint, not weaker validation.

No-result/completion messages disappear in the baseline UI. Conditional connector
substitution, sequential reuse and preset limitations need concise local context.
Fractional counts can be silently truncated while a different number remains in
the editor. These are bounded usability corrections, not new planning modules.

An additional controlled sweep exposes four pre-existing identical-kit seed
omissions. A narrow per-kit seed completion is approved with unchanged ordinary
searches and explicit retained-space limits; it is an optimization-quality
improvement, not a claim the old no-result message proved infeasibility.

Manual zones were independently compared with unrestricted allocation and full
sequencing. Unrestricted preliminary allocation remains the release scope, with
explicit sequential-reuse limits. Zone labels alone would not enforce concurrent
stock. Advanced sequencing lacks the programme and resource inputs needed for a
justified implementation. History/performance rewrites also lack measured release
impact and are deferred.

The finite candidate family can omit feasible arrangements. The original saved
Top50 solver's exact parity remains unresolved. Neither a retained-model Optimal
status nor geometric fit is global optimization, structural approval, connector
assembly approval or a verified pour plan. Actual site constraints and unverified
connector clearance details remain outside this planner's calculation claim.

Detailed evidence and decisions:

- [Site engineering assessment](site-assessment.md)
- [Independent baseline QA](baseline-qa.md)
- [Architecture baseline](architecture-baseline.md)
- [Approved plan and decision record](approved-development-plan.md)

Installer creation stays closed until implemented-source numerical, Windows and
site acceptance are complete and the supervisor records final approval.
