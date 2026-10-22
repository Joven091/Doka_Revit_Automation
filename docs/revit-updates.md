# Revit 2024 updates from the company folder

This feature updates the **Revit add-in only**. Standalone previews keep their existing distribution process. No GitHub account or stored GitHub token is needed on the team's computers.

The first release shows **D3 slab** and **Dome Calculator** as disabled, grey **Coming soon** choices. Dokaflex slab, beam (including D3 beam shoring), and backpropping remain available.

## First installation

1. Download and extract the `revit2024-team-release` artifact from the **Revit 2024 team installer** workflow on main. It includes the latest calculations and the updater; the first combined release is 1.1.2. The release owner runs `Publish-to-company.cmd` while connected to the company network with write access. The script publishes the versioned installer first and `latest.json` last.
2. Team members copy `S:\MEA\ENG\Joven\BeamCalculator\Updates\DokaRevitAutomation-1.1.2.exe` to their own computer, close Revit, and run it under their own Windows account. Administrator elevation is not required. Do not run it as a different administrator account: installation is per user.
3. Reopen Revit 2024. Open **Engineering Dashboard → Check for updates**. In the beam drawing workspace, use **File → Check for updates**. **Add-Ins → External Tools → Check for Doka updates** also works independently of calculator sign-in.
4. Once a newer release is published, select **Install after Revit closes**. The installer is copied locally and checked. A small updater window waits while Revit remains open. Save and close all your Revit windows when convenient; then reopen Revit after installation completes.

The updater never closes Revit or its models. A designer can cancel before installation begins. If they log off or cancel while waiting, they can check again next time. There is no Windows service or startup task.

## Publish a reviewed release

The permanent source is:

`\\DOKAAREDUBA\ApplDat\MEA\ENG\Joven\BeamCalculator\Updates`

This is the same location as `S:\MEA\ENG\Joven\BeamCalculator\Updates`. The UNC path works without the S: mapping. Ask IT to give the team **read** access and release owners **write** access. Folder permissions control who can publish executable updates; the checksum detects incomplete or changed copies.

Build a reviewed installer with a version higher than the team's current version. On a Windows release machine:

```powershell
dotnet build src/MyRevitAddin.csproj -c Release /p:Version=1.1.3
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer.iss
.\scripts\Publish-RevitUpdate.ps1 `
  -Installer .\artifacts\installer\Install_DokaRevitAutomation.exe `
  -Version 1.1.3 `
  -ReleaseNotes 'Describe the approved changes here.'
```

The script validates the installer version, copies it with a versioned name, verifies the copy, then atomically replaces `latest.json` last. It prevents simultaneous publishing, reusing a version for different contents, and advertising an older release. Leave old versioned installers in place while users may still be preparing updates. Do not copy a partial installer over an existing version or edit the live manifest manually.

## Test before publishing to the team

The separate CI artifact `revit2024-updater-test-kit` is an integration-test fixture with 1.1.0/1.1.1 installers and a `test-feed`. Use `revit2024-team-release` to distribute the combined release to the team; an older test-kit download does not contain subsequent calculation changes.

Test on a spare Windows account/computer first. Copy `test-feed` into a temporary company folder. After installing the bootstrap, edit the installed `update-settings.json` to point `sourceDirectory` to that test folder, keeping JSON backslashes escaped. Test checking, preparing, cancelling, closing Revit, reopening, and checking the displayed version. Only publish a reviewed release to the permanent company folder when that trial passes. Custom update settings are preserved during updates; restore the permanent UNC path after the trial.

Cloud CI exercises the real installers on a disposable Windows profile, including a process named Revit holding the add-in DLL open. This verifies deferred installation and version changes, but is not an Autodesk Revit integration test or a test of access to your company share.

## Files and troubleshooting

- Installation remains `%APPDATA%\Autodesk\Revit\Addins\2024`.
- Local staging and logs: `%LOCALAPPDATA%\DokaRevitAutomation\Updates\<version>-<id>`. `updater.log` records waiting/installing; `install.log` contains installer details. Old completed/cancelled staging folders can be removed once no updater is using them.
- No company network/VPN: checking reports that the folder is unavailable. The installed tool continues to work. Once preparation completes, installation no longer depends on the share.
- Wrong checksum, incompatible Revit version, or invalid metadata: the release is rejected before installation.
- Multiple Revit windows: the updater waits for all Revit processes in the user's session and checks add-in DLL file locks. Another update cannot run concurrently for this installation. Reopening Revit before installation starts causes a retry or a clear failure; it is never forcibly closed.
- Other install locations (machine-wide or custom): automatic installation is refused; IT must update those installations.
- An interrupted or failed installer is not reported as successful. Read the logs and rerun a reviewed installer with Revit closed. This first implementation does not provide an automatic rollback.

The installer replaces add-in binaries and the existing public access configuration as it did previously. Saved calculation files, user authentication state, and custom updater settings are not reset. Updates are applied only after a user clicks the install button; this version does not silently install at startup.
