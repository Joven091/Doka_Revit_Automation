#if DASHBOARD_DESKTOP
using System;
using System.IO;
using EngineeringDashboard.DesktopHost;

namespace EngineeringDashboard.SteelShape
{
    public sealed partial class SteelShapeWindow
    {
        internal bool RestartBusy=>!Initialization.IsCompleted;
        internal string CaptureRestartState()
        {
            if(!Initialization.IsCompleted||!Initialization.Result||Browser==null)throw new InvalidOperationException("Steel Shape is not ready to preserve its inputs. Finish opening it or return to the Dashboard before updating.");
            var result=DesktopRuntime.Wait(Browser.ExecuteScriptAsync("(()=>{states.set(current,readInputs());return JSON.stringify({version:1,current,states:Array.from(states.entries())});})()"));
            string state=BridgeProtocol.FromJson<string>(result);if(string.IsNullOrEmpty(state))throw new InvalidDataException("Steel Shape inputs could not be captured.");return state;
        }
        internal void RestoreRestartState(string state)
        {
            if(!DesktopRuntime.Wait(Initialization)||Browser==null)throw new InvalidOperationException("Steel Shape could not restore its inputs: the embedded browser failed to open.");
            string script="(()=>{const saved=JSON.parse("+BridgeProtocol.Json(state)+");if(saved.version!==1||!Array.isArray(saved.states)||typeof saved.current!=='string')throw Error('Invalid Steel Shape restart state');engine.shape(saved.current);for(const row of saved.states){if(!Array.isArray(row)||row.length!==2||!row[1]||typeof row[1]!=='object')throw Error('Invalid shape state');engine.shape(row[0]);}states.clear();for(const row of saved.states)states.set(row[0],row[1]);$('input-fields').replaceChildren();selectCalculator(saved.current);return true;})()";
            var restored=DesktopRuntime.Wait(Browser.ExecuteScriptAsync(script));if(restored!="true")throw new InvalidDataException("Steel Shape did not accept the saved input state.");
        }
    }
}
#endif
