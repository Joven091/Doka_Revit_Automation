# Dome Calculator (Beta)

The fourth launcher option is separated from Slab, Beam and Backpropping by an extra gap and divider. It uses the shared calculator theme and displays Beta in the launcher, input window, diagrams and copied summary.

This ports the approved Formwork Load Calculator from Joven091/My-App (PR #17) into the Revit 2024 / .NET Framework 4.8 add-in. Source lives under `src/Dome` in its own namespace. It uses the existing OxyPlot 2.1.2 dependency and does not start another WPF Application or require a separate executable. Closing it returns to the launcher; inputs persist for the Revit session. Diagram ownership follows the calculator window rather than Application.Current.MainWindow.

## Workflow

Enter tributary width and secondary beam length in metres, combined load in kN/m², and concrete section areas in m². Add slab/beam contributions as needed. Review spacings & diagrams opens the recommended primary-support sequence and secondary spacing prominently above all four plots. Each support has its reaction; exact extrema, limits, sign conventions and the response-scale toggle are retained. Hover a reaction card for its position.

## Scope

Dome is the requested calculator name; the underlying analysis remains a straight continuous secondary beam under averaged uniform load, not a curved shell/dome solver. It does not modify Revit elements. Constant EI = 449.8756 kNm², bearing-only supports, symmetric overhangs and the original discrete span/spacing search are retained. Acceptance uses exact extrema and rejects uplift. Legacy limits remain 11 kN shear, 5 kNm moment and absolute 5 mm deflection. The product/design basis and other formwork components require separate verification. Beta identifies this newly integrated calculator.

## Verification

```powershell
dotnet build src/MyRevitAddin.csproj -c Release --warnaserror
dotnet run --project tests/Dome.Tests/Dome.Tests.csproj -c Release
```

The net48 runner references the actual add-in assembly. It covers analytical benchmarks, the 143-case layout scan, input errors, WPF bindings, support reactions, extrema, display-scale invariance and the no-scroll diagram layout. It exercises the real launcher click handler, nested modal ownership, return navigation and persisted input state. Set DOME_PREVIEW_DIR to an output directory to render the launcher, input window and spacing/diagram dashboard. Revit-host execution remains a separate manual check in Revit 2024.

Local validation: full add-in Release build passes with zero warnings/errors; Dome passes 349,296 assertions; Beam passes 4,374 checks and Backpropping passes 188. The unchanged Slab.Tests project (which does not compile the new Dome files or launcher) currently fails its existing “six governs deflection within 5+” assertion. This is outside the Dome integration and is not included in the new CI job.
