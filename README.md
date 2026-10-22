# Doka engineering calculators

Formwork calculators for Autodesk Revit 2024, opened through the **Engineering Dashboard** command.

| Calculator | Features | Documentation |
| --- | --- | --- |
| Beam/H20 | Props, towers, main and cross beams, transferred slab loads, drawings and member diagrams | [Beam calculator](docs/beam-calculator.md), [towers](docs/beam-towers.md), [beam/slab networks](docs/beam-slab-transfer.md) |
| Slab | Floor props, primary and secondary H20s, deflection and response diagrams | [Slab calculator](docs/slab-calculator-fixes.md), [prop capacities](docs/prop-capacity-sources.md) |
| D3 slab | Tower layouts, H20 stock planning and drawing workspace | [D3 slab](docs/d3-slab.md) |
| Dome | Beam analysis, support spacing and diagrams | [Dome calculator](docs/dome-calculator.md) |
| Backpropping | Staged floor loads and prop capacity checks | [Backpropping](docs/backpropping.md) |

Calculator source lives in `src/Models`, `src/ViewModels`, `src/Views` and `src/Dome`. The Revit command and application loader launch the calculators, import calculated values into Revit and load their dependencies. `tests`, `docs` and `.github/workflows` support calculator verification and maintenance.

## Build

Use the .NET 10 SDK. The add-in targets .NET Framework 4.8 and obtains the Revit 2024 APIs and framework reference assemblies through NuGet. Keep `Microsoft.NETFramework.ReferenceAssemblies` in the projects.

```powershell
dotnet build Doka_Revit_Automation.sln -c Release --warnaserror
```

## Validate

Run the calculator regressions on Windows. The dome/slab tests and UI smoke projects use WPF; the net48 executables need the .NET Framework 4.8 runtime.

```powershell
dotnet run --project tests/Beam.Tests/Beam.Tests.csproj -c Release
dotnet run --project tests/Slab.Tests/Slab.Tests.csproj -c Release
dotnet run --project tests/D3Slab.Tests/D3Slab.Tests.csproj -c Release
dotnet run --project tests/Dome.Tests/Dome.Tests.csproj -c Release
dotnet run --project tests/Backpropping.Tests/Backpropping.Tests.csproj -c Release
```

The GitHub Windows workflow also runs the calculator UI smoke tests and net48 beam regressions. See [calculator UI](docs/calculator-ui.md) and [cross-beam validation](docs/cross-beam-validation.md) for additional checks and model limitations.

## Install in Revit

Build Release, then compile `installer.iss` with Inno Setup on Windows. It packages the root `MyRevitAddin.addin` manifest and the DLLs in `src/bin/Release/net48`, producing an installer in `artifacts/installer`. The installer writes to the current user's Revit 2024 add-ins directory.

For manual installation, copy the manifest and the Release DLLs together into `%APPDATA%\Autodesk\Revit\Addins\2024`. The existing assembly names and calculator registration IDs are retained for installation compatibility.

## Try the beam calculator without Revit

On Windows 10/11 with .NET Framework 4.8, download the **beam-calculator-standalone-preview** artifact from a successful **Dome calculator and existing calculator checks** workflow run. Extract the entire ZIP and double-click **start-preview.cmd**. The preview opens the calculator, including D3 geometry, calculation, sections, settings and response diagrams, without copying anything into Revit's add-ins folder. Importing model data or writing results into Revit still requires the add-in.

To launch from source with the .NET 10 SDK:

```powershell
./scripts/start-beam-preview.ps1
```

The preview uses the production calculator views and numerical models with the existing test host. It does not replace Revit integration testing.

## Create a drawing from Revit selection

Select beams, slabs, columns and walls, then use **From Revit…** in the D3 beam workspace. Review the preview before replacing the drawing. See [supported layouts and workflow](docs/revit-selection-import.md).
