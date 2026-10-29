# Independent native desktop acceptance

Build this test and the production `tools/DashboardDesktop` entry for native
Windows. Run on an interactive Windows desktop with the WebView2 runtime:

```powershell
.\DashboardDesktop.Smoke.exe C:\payload\Doka.EngineeringDashboard.exe C:\evidence\dashboard-desktop
```

For the actual Windows helper/update path, supply two complete production payload
folders built with increasing versions (for example 1.2.0 and 1.2.1):

```powershell
.\DashboardDesktop.Smoke.exe --helper-update C:\payload-1.2.0 C:\payload-1.2.1 C:\evidence\dashboard-helper
```

Each folder must contain `dashboard-package.json` matching every file, including
the production entry, resource DLL, update helper, native solver and beam worker.
The harness independently verifies and archives these files, creates an isolated
old installation and local candidate feed, then uses the old Dashboard's actual
**Update Dashboard** button. It checks the real helper readiness, old process
exit, candidate activation, exact new process receipt and restored live unsaved
Backpropping inputs while the same local bridge peer remains alive. It closes the
helper and candidate cooperatively after success. `helper-update-evidence.txt`
records process IDs, versions and recovery/receipt paths.

On failure, callback exceptions are written to stdout before owned-child cleanup.
`failure-diagnostics` captures the owned processes' UI Automation window trees
(names, IDs, classes, enabled/read-only state), product `Logs/*.txt` and handoff
`update.log` / `helper-startup.log` files. UIA collection is bounded and never
inspects unrelated PIDs.

Windows has executed **351 update-contract checks** and **80 native desktop
checks** successfully. The actual helper integration test has reached capture of
live unsaved inputs; helper readiness is still under investigation. A successful
complete helper update and actual Revit host acceptance have not been established.

The harness loads the **desktop variant from that exact production payload** for
WPF session tests, and separately launches **that exact production EXE** for
startup and UI Automation checks. It never substitutes an older standalone test
entry. Auth replies and selected geometry come from a local test-owned named pipe.
The peer verifies protocol/session/token metadata; the production client also
checks that pipe's actual Windows server PID and start time.

Coverage includes retained D3 / slab inputs, source provenance, project-specific
stock settings, one-click beam / Top50 import, unsaved planner edits and project
path, frozen checkpoint capture/cancellation, busy operation and open-dialog
refusal, verbatim incomplete backpropping and weld drafts, inactive Steel Shape
sections, recalculation notices, locked features, production startup receipts and
external-process restoration. The test uses isolated recovery/settings folders
and never writes the company update share, a production project or an installer.
Failure cleanup may terminate only the child EXE this test launched; this is not
the product updater's process policy.

Passing these checks does **not** establish Revit2024 host acceptance. Actual
ExternalEvent dispatch, coexistence with active modelling, Revit helper parameter
transactions, document switches, live geometry and company-share/installer tests
remain separate acceptance gates. On Linux this test can only be cross-built.
