These fixtures replay all three diagnostics captured on 2026-11-03 with MyRevitAddin
1.2.3. They contain the **reviewed** detached geometry, exact search-start project
inputs, each combined part's reported dimensions/crossings, and active beam spans.
`SourceSha256` identifies the immutable report bytes; `extract.py <reports-dir>`
regenerates the JSON without rounding or simplifying geometry.

| Scenario | Source beams / slabs / boundaries | Calculation parts | Slab-area crossings | Report still running after |
| --- | --- | --- | --- | --- |
| 1 | 23 / 12 / 12 | 5 slab areas + 6 column-to-column beams | 3 + 1 + 1 + 1 + 1 | 313.785 s |
| 2 | 10 / 5 / 4 | 1 slab area + 2 column-to-column beams | 2 | 75.026 s |
| 3 | 20 / 12 / 6 | 1 slab area + 4 column-to-column beams | 9 | 329.426 s |

The reports show an in-flight wrapper and stale, unsolved child models. The
search-start project settings are 70 kN, custom H20 enabled for Both, ignore uplift,
DoubleH20, L/270 members, L/350 plywood, doka-18mm-9732-103, 150 mm stock setbacks and
end overhang. Scenario 1 permits standalone 600 towers; scenarios 2/3 exclude them.
The child defaults of 30 kN and catalogue-only stock are not the search settings.
No successful solution snapshot or completed timing can be inferred from these
reports.

Build and run from the repository root with the .NET 10 SDK:

```sh
dotnet build tests/Beam.Tests/Beam.Tests.csproj -c Release
dotnet build tools/BeamSolver.Worker/BeamSolver.Worker.csproj -c Release
dotnet tests/Beam.Tests/bin/Release/net10.0/Beam.Tests.dll \
  --reported-scenario-fixtures --output artifacts/fixtures
dotnet tests/Beam.Tests/bin/Release/net10.0/Beam.Tests.dll \
  --reported-scenarios --scenarios 1,2,3 --mode worker \
  --worker tools/BeamSolver.Worker/bin/Release/net10.0/Doka.BeamSolver.dll \
  --dotnet /absolute/path/to/dotnet --output artifacts/full-worker
```

The worker and harness must be built from the same source tree; the production
protocol checks source identity. Worker mode requires every final part to report
the .NET 10 worker and rejects fallback. `--mode in-process` explicitly uses the
production local solver within this net10.0 harness. Its diagnostic says ".NET
Framework fallback" because that is the production fallback label, but the actual
runtime is recorded in JSON. Keep worker and in-process timing results separate.

Each scenario calls `OptimizeTowersAsync()` through the normal combined model,
including every imported part, production validation, coordination retries and
final combined clash checks. Independent final checks cover individual leg
reactions, member strength/shear/deflection, force/moment conservation, fresh
analysis of exported member loads/supports, all crossing results, stock rules and
physical crossing clearances. The timed interval includes those final checks.
Import/fixture reconstruction has its own setup time. No elapsed-time budget,
cancellation token or first-solution shortcut is set. A result over 30 seconds is
reported as `MeetsTarget: false`; the solver still runs to completion.

Outputs include source-hashed input XML, JSON with timing/acceptance/quality
snapshots, final diagnostics and a live child diagnostic every 10 seconds.
Snapshots expose legs, groups, brace and symmetry preferences, timber, fallback
slab counts, crossing arrangements and a deterministic network fingerprint for
old/new comparisons. Run scenarios serially on otherwise idle CPU; do not compare
contended timings. JSON validity failures cause a nonzero process exit.

`--require-target` additionally makes any completed scenario over 30 seconds
cause a nonzero exit, after all requested scenarios have finished. The JSON still
records the complete result. Each result records its production engine identity.
Part snapshots retain solved children even if the combined wrapper rejects the
final coordination; every available child is validated before the combined
failure is reported.

For hotspot investigation, `--reported-parts --scenario 1 --parts 1,2` runs the
selected one-based calculation parts with the same worker/mode/output options.
It creates children through production `DrawingModel`/`CopyProjectInputs` and
uses their normal `OptimizeTowersAsync` path. Outputs are explicitly marked
`Scope: SinglePartDiagnostic` and `FullScenarioAcceptance: false`: neighboring
parts and coordination are omitted, so these timings cannot satisfy acceptance.
`--reported-part-fixtures --scenario 1 --parts 1,2` checks that path without
running the solver. `--live-combined-diagnostics` checks live saved/running child
selection, project settings, optional worker telemetry and coordination-retry
identity without running any solver.

`--crossing-load-paths` checks small exported-result fixtures for shared primary,
dedicated and mixed end-zone support paths. Full replay uses the same check:
external main-receiver bearings plus the final dedicated primary case must equal
the crossing load, with dedicated primary self-weight removed. Intermediate
dedicated secondary bearings and historical reaction envelopes are excluded.

`--receiver-geometry-cache` prepares a small fixed receiver plan directly and
checks exact geometry/edge reuse, retained failures, a fresh leg-capacity context,
exception retry, cancellation before a cache hit, and 4096-entry eviction. It
does not run a network search.

Standalone validation separates the supported stock length from the full
concrete span. Primary reactions carry the covered UDL and primary self-weight;
the two external column-side end strips account for the remaining concrete
force. Their lengths must remain symmetric and within the entered allowance,
with no internal stock gaps. Each left/right stock run is independently checked
by dense analysis, including exported leg reactions and member responses.
`--standalone-reported-validation` checks the reported 4098.170620 mm beam and
3800 mm stock: 141.816001 kN on primaries plus 11.127728 kN on external end strips.

`--adaptive-receiver-proof` exercises a small actual optimizer counterexample:
a broad ceiling returns a count-3, twelve-leg receiver, while the tighter ceiling
refines to count 4 and finds eight legs. It also inspects production proof
eligibility and verifies that restricted fixed geometry cannot consume or
publish unrestricted receiving-search evidence.

Production `CopyProjectInputs` does not propagate `SlabStripWidthMm`,
`NumberOfProps`, or the selected prop type into detached calculation children.
Those fields are preserved in the top-level fixture inputs. They do not change
these tower calculations: every standalone beam has no adjacent slab and network
slabs use explicit bay geometry. The harness retains the production propagation
behavior.

Every completed replay also writes `scenarioN.evidence.json` and a matching
`scenarioN.evidence/` folder. Each child retains its complete final applied
`BeamSolverReply` graph, a reconstructed post-search `BeamSolverRequest`, input
XML and diagnostic inputs. These use the existing bounded `BeamWire` whitelist,
including numerical coefficients, crossing members, bearings and solids. Failed
children retain their errors and part identities. Report SHA256, production
engine identity, embedded source hashes and per-file checksums accompany the
graphs. Coordination obstacles are cleared by production after a retry, so the
saved requests are explicitly reconstructed inputs for verification.

Writing and immediate read-back validation are outside `TotalSeconds` and
reported as `EvidenceWriteAndReadValidationSeconds`. `EvidenceFailure` records
an artifact failure and makes the command fail without changing engineering
validity. The original timed validation still includes all final physical
checks, including every exported main/crossing solid pair and concrete clearance.

Recheck saved results without running the solver:

```sh
dotnet tests/Beam.Tests/bin/Release/net10.0/Beam.Tests.dll \
  --reported-scenarios --verify-evidence \
  --input artifacts/full-worker/scenario2.evidence.json
```

The checker must use the matching production sources: `BeamWire` rejects a
different engine identity. New tests can be built against a frozen copy of those
production sources. The reader recreates all fixture parts and inputs, compares
the saved request bytes, validates the wire result and reruns current engineering
and combined-clash checks. It writes `.evidence.json.revalidated.json`, always
marked `SavedResultRevalidation` and `FullScenarioAcceptance: false`; its time
cannot satisfy the search target. Single-part diagnostics save the same evidence
format with their restricted scope. `--reported-scenarios
--reported-evidence-checks` runs small round-trip, corruption, source/fixture
identity, complete-part and independent numerical checks without a search.
