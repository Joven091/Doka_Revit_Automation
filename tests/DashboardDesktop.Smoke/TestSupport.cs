using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

internal static class TestSupport
{
    internal static int Checks;
    internal static Assembly Product;
    internal static string Output;
    internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=32*1024*1024,RecursionLimit=100};
    internal static void Check(bool condition,string description)
    {if(!condition)throw new InvalidOperationException(description);Checks++;Console.WriteLine("PASS "+description);}
    internal static Type Type(string name)=>Product.GetType(name,true);
    internal static object New(string name,params object[] args)=>Activator.CreateInstance(Type(name),args);
    internal static object Get(object target,string name)
    {
        var type=target as Type??target.GetType();var instance=target is Type?null:target;
        var property=type.GetProperty(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
        return property!=null?property.GetValue(instance):type.GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).GetValue(instance);
    }
    internal static void Set(object target,string name,object value)
    {
        var type=target as Type??target.GetType();var instance=target is Type?null:target;
        var property=type.GetProperty(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
        if(property!=null)property.SetValue(instance,value);else type.GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).SetValue(instance,value);
    }
    internal static object Call(object target,string name,params object[] args)
    {
        var type=target as Type??target.GetType();
        var method=type.GetMethods(BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length);
        try{return method.Invoke(target is Type?null:target,args);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
    }
    internal static void Pump()=>Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(()=>{}));
    internal static void Wait(Func<bool> done,string description,int seconds=30)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        while(!done()){Pump();Thread.Sleep(10);if(clock.Elapsed.TotalSeconds>seconds)throw new TimeoutException(description);}
        Pump();
    }
    internal static IEnumerable<T> Find<T>(DependencyObject parent) where T:DependencyObject
    {
        var result=new List<T>();var seen=new HashSet<DependencyObject>();Action<DependencyObject> visit=null;
        visit=node=>{if(node==null||!seen.Add(node))return;if(node is T typed)result.Add(typed);foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())visit(child);if(node is Visual||node is System.Windows.Media.Media3D.Visual3D)for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)visit(VisualTreeHelper.GetChild(node,i));};visit(parent);return result;
    }
    internal static void Click(Window owner,string name){((Button)owner.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
    internal static void Click(Button button){button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
    internal static IDictionary<string,object> Parse(string path)
    {
        // Match the production metadata reader, including atomic rename sharing.
        // This remains a single read: no sharing/content errors are retried.
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
        using(var reader=new StreamReader(stream))return (IDictionary<string,object>)Json.DeserializeObject(reader.ReadToEnd());
    }
    internal static IDictionary<string,object> Map(object value)=>(IDictionary<string,object>)value;
    internal static IEnumerable<object> Items(object value)=>((IEnumerable)value).Cast<object>();
    internal static IDictionary<string,string> Fields(object calculation)=>Items(Map(calculation)["Inputs"]).Select(Map).ToDictionary(row=>(string)row["Name"],row=>(string)row["Value"]);
    internal static byte[] SerializeProduction(object value)
    {
        var method=Type("EngineeringDashboard.DesktopHost.BridgeProtocol").GetMethod("Encode").MakeGenericMethod(value.GetType());return (byte[])method.Invoke(null,new[]{value});
    }
    internal static string WriteProductionCheckpoint(object value,string root)
    {
        string directory=Path.Combine(root,"Sessions");Directory.CreateDirectory(directory);string path=Path.Combine(directory,Guid.NewGuid().ToString("N")+".json");File.WriteAllBytes(path,SerializeProduction(value));return path;
    }
}
