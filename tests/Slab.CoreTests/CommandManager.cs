using System;
namespace System.Windows.Input { public static class CommandManager { public static event EventHandler RequerySuggested { add {} remove {} } public static void InvalidateRequerySuggested() {} } }
