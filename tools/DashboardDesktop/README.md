# Engineering Dashboard desktop host

This executable runs the existing .NET Framework 4.8 WPF calculators and Top50 UI outside the Revit process. The calculation sources and existing .NET 10 beam worker remain the same. The stable Revit integration performs selection, highlighting and helper-line writes on Revit's UI thread through a local named pipe.

Build with:

```powershell
dotnet build tools/DashboardDesktop/Doka.DashboardDesktop.csproj -c Release
```

The project compiles `src/MyRevitAddin.csproj` with `DashboardDesktopBuild=true`. That variant excludes Revit entry points/readers and replaces the calculator handlers with bridge adapters. The resource DLL keeps its `MyRevitAddin.dll` name for existing WPF pack URIs. The ordinary add-in build remains available and unchanged by default. Do not distribute Revit API DLLs in the desktop payload.

Launch the executable through the stable Revit integration. It supplies a process/session-bound authenticated pipe environment; starting the EXE independently does not grant access. `--updates` opens only the update page and retains the existing ability to check updates without calculator sign-in. `--restore-session <checkpoint>` is used by the update helper.

Recovery checkpoints use schema 1 under `DashboardPaths.CheckpointsDirectory`. They contain tool inputs, source identities, original Top50 project path/dirty flag, current page, Backpropping draft text and Steel Shape browser state. They contain no access credentials. The source version is informational so compatible new versions can restore the previous version's work.

An update is refused while a solver/Revit request is active, drawing commands/dialogs remain unfinished, an input cannot be committed, or an active embedded browser cannot preserve its state. After successful capture, editing remains disabled until shutdown or `CancelUpdateRestart`. Capturing alone never bypasses unsaved-project prompts. Only `BeginUpdateRestart` enables the checkpointed shutdown path.

Optimized results are discarded on restoration and require Optimize again. Classic Beam and Slab calculators recalculate automatically from the restored inputs in the new process. The startup receipt is written only after restoration, including Steel Shape browser initialization, succeeds. This lets the update helper roll back to the previous payload if the new Dashboard cannot start.

The initial stable-integration installation, or a later change to that integration, requires a Revit restart. Subsequent compatible desktop-only updates restart the Dashboard while leaving Revit open.
