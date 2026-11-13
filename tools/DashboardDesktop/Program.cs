using System;
using EngineeringDashboard.DesktopHost;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)=>DashboardDesktopApplication.Run(args);
}
