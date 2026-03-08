# Agent Instructions: Revit 2024 Add-in 

## Environment Context
- **Target Framework:** .NET Framework 4.8 (`net48`)
- **Compiler:** .NET 10 SDK (using `Microsoft.NETFramework.ReferenceAssemblies`)
- **Revit Version:** 2024
- **IDE:** VS Code
- **Dependency Management:** NuGet (Nice3point.Revit.Api)

## CRITICAL BUILD RULE
**DO NOT remove the `Microsoft.NETFramework.ReferenceAssemblies` package from the `.csproj`.** The host machine only has the .NET 10 SDK installed and relies entirely on this package to build the `net48` target. 

## Coding Standards & Rules
1. **No Local Paths:** Never reference DLLs via absolute paths (e.g., C:\Program Files\...). Use NuGet packages exclusively.
2. **Revit API Safety:** - Always wrap model modifications in a `Transaction`.
   - Use `using` blocks for Transactions to ensure they are disposed of properly.
   - Always set `Transaction.Start()` and `Transaction.Commit()`.
3. **Threading:** The Revit API is **not thread-safe**. All Revit API calls must happen on the Main Thread. Do not use `async/await` for Revit database calls.
4. **Boilerplate:** When creating new commands, implement `IExternalCommand` and use the `[Transaction(TransactionMode.Manual)]` attribute.

## Element Selection Patterns
- Prefer `FilteredElementCollector` for retrieving elements. 
- Always include null checks when querying parameters or geometry.
