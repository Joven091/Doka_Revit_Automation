Verified installer release 1.1.16.0
Installer: DokaEngineeringDashboard_Top50_Setup_1.1.16.exe
Bytes: 15521073
SHA256: 54456abf3156b8a5cd8bb1a6642f503b9209a6a1d7dc2a1e561d7fed713372f5
Source base: PR168 bb37debee36f9d6a9dbce79bc41f3d47bdb35b6b, uncommitted Top50/installer overlay in revit-top50-pr168-review.
The selection reader fixes, complete diagnostic IDs, newer beam callback and all existing calculator entries are retained. Version advanced from1.1.15 to1.1.16.

Prior approach inspected: actual task01a13db5-a288-73a6-b986-6f29164eb051, .github/workflows/revit-release.yml, installer.iss, updater tests/docs. GitHub run40324479032 artifact12650447826 (revit2024-team-release) is unexpired and identifies bb37debe; no hosted workflow was started. The same Inno Setup packaging technology was reused locally with additional safety checks. Original installer.iss/workflow remain unchanged; new installer-top50.iss is the review entry point.

Build: .NET SDK10.0.110, net48/x64/win-x64, Release, zero warnings/errors. Inno Setup6.7.3 compile succeeded without warnings. Native eight-DLL solver payload and managed dependencies included; host Revit API DLLs excluded. No Python or AutoCAD is needed by the installed tool.

Final installer verification:54 lifecycle checks plus5 guard checks pass. Fresh install hashes match shipping DLLs; all three manifest Assembly entries are absolute/correct; actual installed payload imports the supplied DXF, optimizes/independently validates fresh results, exports Excel and constructs the compiled dashboard/five views without generated binding redirects. The compatibility resolver rejects unrelated callers/threads. Upgrade1.1.15 file-layout fixture preserves settings, saved-project and unrelated-file sentinels; backup hashes match previous files. Uninstall removes registration/binaries while retaining settings/projects/unrelated files/backups. Isolated file restoration reproduces the prior assembly hash. Duplicate IDs, path escape, newer version and custom registration are rejected. A locally compiled disposable process named Revit.exe blocks install/uninstall; uninstall succeeds after its exit.

59 production reader-path checks also pass on net48 with test-only Revit stand-ins (199,200,201,432,1001 selections; all IDs/calling thread preserved). Prior integration verification remains136 native WPF checks,33 core checks plus discovery,411 beam import regressions. The final installed1.1.16 shipping-assembly smoke ran separately. Solver/source behavior unchanged by the installer/version work.

Limits: lifecycle upgrades use a copied1.1.15 file layout, not a real Revit-host upgrade. Isolated mode registers no Windows Apps entry and never writes to actual Revit add-in folders; normal Apps registration and Revit2024 host behavior remain unrun. Production directory and uninstall-registration fingerprints stayed unchanged. No CUA retry or security bypass occurred. Early test failures in backup timestamp/error-dialog handling were corrected before the final executable and59 passing installer checks.

Normal installer requires Revit2024 detected at its standard installation or registry location. Current computer has only Revit2023; no production install was attempted. Normal installation is a user action, not part of this delivery. Existing custom/machine-wide Dashboard installations require reviewed migration. Original numerical parity remains unresolved; recorded S3 and fresh general search are intentionally not conflated.

Library create confirmed succeeded: libfile_491188cdb3a8819192dcb372a18879fb; file_000000000f288243820ccf9439f91642; version0; exact filename and15521073 bytes. The required local metadata helper was invoked immediately and failed because Windows Python has no os.setxattr. The Library upload itself succeeded; local extended-attribute identity was NOT persisted. No helper patch, bypass, duplicate create or transfer retry was performed.

Doka Engineering Dashboard 1.1.16 — integrated Top50 review release

INSTALL
1. Download DokaEngineeringDashboard_Top50_Setup_1.1.16.exe to the computer running Revit2024.
2. Save your models and close every Revit window.
3. Run the installer under your usual Windows account and follow its wizard. Administrator elevation is not required.
4. Reopen Revit2024, open Engineering Dashboard, then Top50.

No ZIP extraction, VS Code build or manual manifest editing is required for this EXE.
Requires Autodesk Revit2024 x64. Revit2023 is unsupported; the installer does not install Revit. This current validation computer has only Revit2023, so normal production installation was not attempted.

The installer supports the standard per-user target %APPDATA%\Autodesk\Revit\Addins\2024. It registers absolute DLL paths automatically, includes every native/managed dependency, preserves existing access/update settings and saved projects, and refuses live Revit processes, newer versions, duplicate Dashboard IDs, custom assembly registrations and unsupported directory overrides.

BACKUP / ROLLBACK
Before an upgrade, existing DLLs, manifest, updater and settings are copied into %LOCALAPPDATA%\DokaRevitAutomation\Backups\1.1.16-<timestamp>. The installer log records the exact backup directory and checksums. Backups remain after uninstall.
To roll back: close Revit, uninstall this review release from Windows Apps, then reinstall your previously trusted calculator installer. Saved projects/settings are retained. If that prior installer is unavailable, retain the backup and request a reviewed restoration; do not mix DLL versions. Backup creation is not automatic rollback. Do not run two Dashboard registrations concurrently.

REVIEW LIMITS
Original V1 appearance/workflow is reconstructed; exact original solver equivalence remains unresolved. Fresh search results differ from the recording. No golden output is compiled into production. Actual Revit2024 startup, access gate, native-library coexistence and user acceptance remain unrun. This is a review release, not a company-published or approved engineering release. No production installation, PR merge, push, paid CI or company-feed publication occurred.
