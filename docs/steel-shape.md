# Steel Shape in Revit 2024

Open **Steel Shape** from the existing dashboard. The page contains the sixteen approved steel section tools, live results, dimension schematics and optional weld edge selection. Fillet/butt and straight/L connections use millimetres. Click an outline edge to include/exclude it, or enter a partial welded length. The original section schematic stays visible.

The page and catalogue tables are embedded in the add-in. They require no website access. Microsoft Edge WebView2 hosts the page; if the runtime cannot initialize, **Open in browser** writes the same versioned offline page to the user's local application-data folder. There are no Revit database changes in this window. Existing access controls apply to its dashboard entry.

## Verification

`tools/SteelShape` preserves the standalone source, supplier-table audit and independent arithmetic, weld, DOM and real-browser tests. Its generated HTML must match `src/SteelShape/SteelShape.html` byte for byte. Three user-provided rectangular hollow section examples are retained; live parity for every supplier calculator is not established.

`tests/SteelShape.HostSmoke` exercises the actual WPF/WebView2 host, embedded resource, interactive weld calculations, main section schematic and missing-runtime fallback on Windows. Installer tests repeat the smoke against the installed payload and retain the existing Top50 solve/diagnosis/export checks. These are not actual Revit host acceptance.

The team installer retains the latest beam import fixes and the native Top50 planner/diagnosis work. Top50 ranking, search and numerical rules are unchanged by Steel Shape. The original Top50 solver's exact parity remains unresolved.

Build and test locally with Python 3, Node and .NET 10:

```
npm ci --prefix tools/SteelShape
python tools/SteelShape/build.py src/SteelShape/SteelShape.html
node tools/SteelShape/test-engine.cjs
node tools/SteelShape/test-weld.cjs
node tools/SteelShape/test-selection.cjs
node tools/SteelShape/test-ui.cjs src/SteelShape/SteelShape.html
node tools/SteelShape/test-browser.cjs src/SteelShape/SteelShape.html
dotnet build tests/SteelShape.HostSmoke -c Release --warnaserror
```

On Windows, set `STEEL_TEST_CHROMIUM` to an installed Chrome/Chromium executable for the browser tests. The host smoke needs the Microsoft Edge WebView2 runtime. The compiled add-in targets .NET Framework 4.8, x64, Revit 2024.
