# Dashboard updates while Revit stays open

This development branch separates the existing Dashboard UI/calculations from a small, stable Revit2024/.NET Framework4.8 integration DLL. It keeps the existing Dashboard commands, tools, access approval, project files, numerical engines and .NET10 beam worker. The UI remains launched from Revit.

**One migration requires closing Revit.** The old MyRevitAddin.dll is already loaded into Revit and cannot be unloaded. After migrating, compatible Dashboard updates close/reopen only Doka.EngineeringDashboard.exe. Changes to the stable Revit module or its protocol still require a Revit restart. No updater closes, kills, or reopens Revit or a model.

## Team flow

1. Open Engineering Dashboard from Revit, then Check for updates.
2. Click Update Dashboard for a published compatible release.
3. The ZIP is copied locally and every payload file is verified. The current Dashboard stays available during the download.
4. Working inputs, unsaved edits, source mappings, selected tool and project path are checkpointed. Calculations/Revit operations must finish first; unsupported open dialogs or uncommitted editor states refuse the update with a reason.
5. Only the Dashboard restarts. A startup receipt is written after reconnection and restoration. The helper displays completion; D3/Top50 calculation outputs are regenerated with Optimize from the restored inputs.

A failed start restores the previous version pointer and attempts to reopen it with the same recovery checkpoint. The previous payload and checkpoint are retained. A failed new process is asked to close cooperatively; a second Dashboard is not launched while it remains alive. Updates use one helper and one update window; cancellation markers prevent a delayed helper from applying at a later normal close.

## Packages and publication

`dashboard-latest.json` describes a versioned Dashboard ZIP, product `Doka.EngineeringDashboard.Desktop`, schema/protocol1. This is separate from legacy `latest.json`, which continues to describe an EXE for changes requiring a Revit restart. Never rename one feed as the other.

`Build-DashboardLiveUpdate.ps1 -OutputDirectory <new local folder> -Version 1.2.0` compiles the actual external entry, helper, bridge, package utility and matching self-contained beam worker. It validates the ZIP and produces review-only bootstrap files. It creates no installer and performs no installation or company publication.

After release acceptance, the owner publishes with `Publish-DashboardUpdate.ps1 -FeedDirectory <release>/feed`. It copies the immutable ZIP first, verifies the network copy, then atomically replaces dashboard-latest.json. The configured share remains `\\DOKAAREDUBA\ApplDat\MEA\ENG\Joven\BeamCalculator\Updates`; the company network/VPN and write access are required. Team members only need read access. An unavailable share does not prevent normal calculation.

## One-time migration and recovery

The review kit contains `bootstrap/Initialize-DashboardLiveUpdate.ps1`. The eventual installer must perform this same checked migration. **Do not distribute this development kit as an approved team installer.** It has not been accepted in an actual Revit2024 session.

The migration refuses running Revit/Dashboard processes, managed/custom or duplicate registrations, and downgrades. It retains the three existing command GUIDs, backs up replaced integration files, preserves credentials/access/update settings/projects, activates a verified runtime, and points MyRevitAddin.addin to Doka.RevitBridge.dll. Failures restore changed files and use a generation-checked runtime rollback. Old calculator DLLs are retained as backup material but are not loaded by the bridge.

Payloads live in `%LOCALAPPDATA%\DokaRevitAutomation\Dashboard\versions\<version>-<hash12>`; the atomic `active.json` pointer chooses future launches. Running versions are never overwritten/deleted. Root/Sessions holds bounded recovery checkpoints; Root/handoffs contains update plans and status logs. Neither contains sign-in tokens. Named-pipe capability secrets exist only in process memory/environment for the current Revit session. The pipe permits the current Windows user, denies network access, verifies peer PID/start and installed payload, and only accepts bounded typed operations.

All Revit API work runs through the stable module's ExternalEvent. Reads return detached data; helper writes check the original document and initiating process, complete native selection before starting a Transaction, and retain atomic per-instance SubTransactions. Disconnects invalidate queued requests. A running native selection is cancelled with Esc; the app cannot silently interrupt Revit's native command.

## Acceptance limits

An approved migration installer is built separately with `Build-DashboardInstaller.ps1`, using the complete verified output of `Build-DashboardLiveUpdate.ps1` from the same source commit. It uses the existing checked migration and preserves the normal per-user application identity. `Test-DashboardInstaller.ps1` exercises the actual EXE and uninstaller in guarded temporary folders, including upgrades, preserved data, process guards and rejected paths. The installer workflow requires an explicit build request; ordinary development changes do not authorize installers.

The checked runtime migration finishes before Setup records its uninstall entry. If that final Setup phase fails, the verified runtime and upgrade backups remain in place, and Setup instructs the user to rerun the same installer to repair its registration. A successful runtime migration is not silently reversed.

Pure transaction tests and cross-compilation do not prove actual Revit operation. The independent native smoke uses the actual production EXE with a test-owned pipe peer, not Autodesk Revit. Actual Revit2024 modelling coexistence, selection/highlighting/helper transactions, document switches, first migration on a company machine and company-share publication require host acceptance before team release. This branch does not merge prior draft PRs or publish to the company folder.
