# Approved development plan and decision record

Supervisor: root agent. User authorization: the multi-agent review/release request
supplied on 28 October 2026. Installer target confirmed by the user: the existing
Revit Engineering Dashboard. This plan approves implementation, not packaging.

Baseline: commit `348f525ce019dd37a4aea38391ffc285baeec2fc`, tree
`171d0664aeda44df602bd66caddcd215b9a0086c`, preserved in its original checkout.
Development uses the isolated `codex/top50-engineering-review-20261028` worktree.
Existing dashboard, D3, Steel Shape, Revit import, updater and beam-worker work
must remain intact.

## Approved tasks

| ID | Evidence and decision | Acceptance criteria |
| --- | --- | --- |
| AP-01 | **High: fix identical-kit allocation (QA-003).** Walls 500, 500 and 1500 mm, catalog 500/750/1000, two identical sets: a generated 750×2 + 500×2 inventory admits a one-kit 1500 mm pattern but the unconstrained allocation uses both 750s for that wall. Fresh search then throws. Constrain the actual required-wall allocations to one kit before solving; preserve compatible patterns for other walls. | Fresh search returns a validated fixed offer without an internal-validation exception. The required longest walls use no more than one kit's count of each size. The independent witness is two allocation groups for the cited stock. Ordinary/non-identical searches retain their ordering and constraints. Tiny independent exhaustive kit cases pass. |
| AP-01b | **Bounded optimization usability: complete identical-kit seeds.** A later independent sweep finds four pre-existing no-offer cases among 286 controlled inputs, including 500/500/3000 mm under catalog 500/750/1000 with two sets. Rounding pooled stock into multiples can still leave each kit unable to form its required wall. Site and developer reviews support one narrow completion, not a generator rewrite. Keep every existing rounded candidate; grow its per-kit inventory to cover one retained pattern per required length, preferring fewest added panels, then added LM and a stable tie-break; append at most one completed candidate per original. | Recover the four independently feasible examples and validate their actual kit usage. Do not assert that the example requires two groups: stock with two kits each 1000×3 + 500×1 can form all three walls in one pooled allocation group. Preserve ordinary/percentage candidates and signatures, original candidates, allowed catalog, quantity-first tiers, four-pattern retention and fixed deterministic work. Record identical-only result/runtime changes. Completion remains heuristic; do not claim a global result beyond independent tiny proofs. |
| AP-02 | **High: use effective method settings (QA-001, DEV-01, SE-02).** The identical-set preference is hidden but still passed to percentage search. Apply it only to fixed-set mode, preserving the stored preference for switching back. | Fixed + identical → percentage optimizes; percentage does not claim identical kits; switching back retains the preference. Hidden fixed-only settings do not invalidate percentage caches or searches. |
| AP-03 | **High: validate cached results against project policy (QA-002, DEV-02).** Saved geometry may close while stock violates the allowed catalog, percentage band or required identical-set convention. Use project-context validation at fresh acceptance and restoration. | Contradictory cached results cannot appear current or independently validated. Preserve valid inputs/settings and explain that Optimize is needed when a policy-incompatible cache is discarded. Reject corrupt source/geometry atomically as before. Ordinary valid V1–V5 round trips and selection/history remain compatible. Expansion may admit out-of-band offers only under the existing fallback semantics; in-band cached alternatives retain priority. Required kits must have the correct count and actual per-required-wall usage. |
| AP-04 | **High: concise result feedback and model context (SE-01/03/04).** No-result/cancelled completion messages currently disappear. Preserve the compact native presentation while making relevant outcomes visible. | A first restrictive no-result, cancellation and stale/rejected cache have a readable explanation. Results indicate best-found/quantity-first basis without a global-optimum claim. Per-level/both-face connector headings stay; concise local text/tooltip explains sequential reuse and conditional adjustable substitution. Casting groups remain equipment allocations, not an approved pour sequence. Existing preset selector help describes its actual secondary metrics and that Fastest means fewer allocation groups, not measured construction duration. No restored large disclaimer footer, report tab, Excel action, extra setting or approval prompt. |
| AP-05 | **Bounded usability: validate discrete numeric settings (DEV-03, SE-05).** Fractional counts are currently cast to integers while the editor can continue showing a different number. | Whole-number fields reject fractional/out-of-range values with field-specific feedback and preserve the previous valid value/history state. Percentage inputs still accept decimals. Invalid catalog/mode/preset/filler/count settings fail with useful messages before search; no automatic geometry rounding or silent clamp. Preserve supported legacy values that satisfy the existing model. |
| AP-06 | **Release provenance and packaging (DEV-04), parent-owned.** Current helper manifests omit linked view/helper sources and some README text says V4 instead of V5. Pin the two Revit API packages to the already resolved baseline 2024.3.60; this does not upgrade them. | Record exact source, resolved dependencies, version, checksums and passed acceptance tests. Build the existing dashboard installer with its native OR-Tools and self-contained beam worker only after final supervisor approval. Verify isolated install, launch, planner operations, restart, backup/upgrade and uninstall on available Windows CI. No company publication, real-user installation or GitHub merge is implied. |

Agent 2 owns application implementation and focused regression changes. Agent 3
owns an independent durable oracle suite and final numerical/software validation.
Agent 1 owns a separate final practical assessment of current Windows renders and
the approved scenarios. The supervisor independently reviews code and evidence.
No implementing-agent-only acceptance is sufficient.

## Decisions on the remaining recommendations

- **Retain:** all three presets, Balanced default, two offering methods, automatic
  component counts, deterministic thorough work, quantity-first admission, maximum
  three displayed offers, two-face physical quantities, per-face LM and one-level
  connector stock with adjustable reuse. Independent connector arithmetic supports
  the existing stock formula; it is not a missing full-height feature.
- **Retain with clear limits:** four retained patterns and the existing candidate
  family. Small exhaustive tests expose an omitted feasible stock combination;
  the software already describes bounded best-found search. No unsupported claim
  of global optimality or infeasibility is approved. A solver rewrite is not
  justified by this release's defects.
- **Defer manual zones (Option B):** useful when actual project evidence requires
  defined concurrent fronts, but labels alone would not enforce stock. Existing
  unrestricted allocation plus explicit reuse limits is the coherent release
  scope (Option A). Separate project proposals can be consolidated externally.
- **Defer advanced sequencing (Option C):** no supplied programme, stripping-time,
  crew, crane or access data supports a scheduling engine.
- **Defer performance/history rewrites:** measure first; no demonstrated release
  blocker justifies changing the numeric model, finite work or history design.
- **Reject:** Excel/report restoration, automatic height/level multiplication,
  invented connector clearances, arbitrary stock caps, structural-approval claims,
  large persistent warning blocks and changes to the established quantity policy.
- **Requires project engineering outside this software:** actual connector
  assemblies, 350 mm filler applicability, short-panel connection clearances,
  walings/ties/pressure/height, unequal faces and real simultaneous site demand.

## Final release gate

Before installer compilation: all approved critical/high defects have reproducible
regression checks; independent small exact references, existing regressions and
repeatability pass; current WPF result/settings/group views are exercised on
Windows; site review accepts the bounded workflow and labels; supervisor signs
the validation report. Only then authorize the packaging job. A clean Windows
runner supports isolated package testing; an actual Revit 2024 session is a
separate acceptance limitation and must never be claimed from WPF stand-ins.
