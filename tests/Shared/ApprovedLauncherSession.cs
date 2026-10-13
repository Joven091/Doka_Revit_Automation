using System;
using System.Reflection;

// Test-only fixture for launcher/modal ownership checks. AccessClient authorization
// is tested separately with scripted HTTP responses in Access.Tests. This class is
// linked only into test executables and restores process-local state on disposal.
internal sealed class ApprovedLauncherSession : IDisposable
{
    private readonly FieldInfo approval;
    private readonly bool previous;
    internal ApprovedLauncherSession(Assembly addin)
    {
        var type=addin.GetType("MyRevitAddin.Access.AccessWindow",true);
        approval=type.GetField("approvedForRevitSession",BindingFlags.Static|BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The launcher approval fixture no longer matches the access gate.");
        previous=(bool)approval.GetValue(null);
        approval.SetValue(null,true);
    }
    public void Dispose()=>approval.SetValue(null,previous);
}
