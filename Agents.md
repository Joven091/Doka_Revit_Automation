# Agent Instructions: Revit 2024 Add-in (Modern .NET)

## Environment Context
- **Target Framework:** .NET 10.0
- **Revit Version:** 2024
- **IDE:** VS Code
- **Dependency Management:** NuGet (Nice3point.Revit.Api)
- **Architecture:** This project uses a modern .NET 10.0 loader to run within Revit 2024. 

## Coding Standards & Rules
1. **No Local Paths:** Never reference DLLs via absolute paths (e.g., C:\Program Files\...). Use NuGet packages exclusively.
2. **Revit API Safety:** - Always wrap model modifications in a `Transaction`.
   - Use `using` blocks for Transactions to ensure they are disposed of properly.
   - Always set `Transaction.Start()` and `Transaction.Commit()`.
3. **Async/Await:** Since this is .NET 10.0, feel free to use modern C# features, but remember that the Revit API is **not thread-safe**. All Revit API calls must happen on the Main Thread (Idling or External Events).
4. **Boilerplate:** When creating new commands, implement `IExternalCommand` and use the `[Transaction(TransactionMode.Manual)]` attribute.
5. **UI:** Prefer `TaskDialog` for simple user feedback.
6. **Custom UI:** Use WPF for complex user inputs. Ensure all WPF Windows are owned by the Revit main window handle to prevent them from disappearing behind the Revit UI.

## Build Instructions
- Use `dotnet build` to verify code changes.
- Do not attempt to "run" or "debug" Revit in the agent environment; only verify that the code compiles.

## Element Selection Patterns
- Prefer `FilteredElementCollector` for retrieving elements. 
- Use LINQ where it improves readability, as .NET 10.0 handles it efficiently.
