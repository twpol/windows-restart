using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;

namespace Windows_Restart
{
    class EventEventArgs : EventArgs
    {
        public EventEventArgs(string json)
        {
            Json = json;
        }

        public string Json { get; set; }
    }

    class Monitor
    {
        public event EventHandler<EventEventArgs> RaiseEvent;

        public Monitor()
        {
        }

        public void Execute()
        {
            var data = new Dictionary<string, object> {
                { "meta.local_hostname", Dns.GetHostName() },
                { "service_name", "windows-restart" },
                { "name", "monitor" },
            };

            if (0 == Native.CallNtPowerInformation(PowerInformationLevel.SystemExecutionState, null, 0, out ExecutionState state, 8))
            {
                data["execution_state"] = state;
                data["execution_state.display_required"] = (state & ExecutionState.DisplayRequired) != 0;
                data["execution_state.system_required"] = (state & ExecutionState.SystemRequired) != 0;
                data["execution_state.awaymode_required"] = (state & ExecutionState.AwaymodeRequired) != 0;
                data["execution_state.user_present"] = (state & ExecutionState.UserPresent) != 0;
            }

            RaiseEvent(this, new EventEventArgs(JsonSerializer.Serialize(data)));
        }
    }
}
