using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.Sdk;

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
        public bool CaptureConsoleUser { get; set; }

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

            var tickCount = PInvoke.GetTickCount();
            data["uptime_ms"] = tickCount;

            if (0 == Native.CallNtPowerInformation(POWER_INFORMATION_LEVEL.SystemExecutionState, null, 0, out ExecutionState state, 8))
            {
                data["execution_state"] = state;
                data["execution_state.display_required"] = (state & ExecutionState.DisplayRequired) != 0;
                data["execution_state.system_required"] = (state & ExecutionState.SystemRequired) != 0;
                data["execution_state.awaymode_required"] = (state & ExecutionState.AwaymodeRequired) != 0;
                data["execution_state.user_present"] = (state & ExecutionState.UserPresent) != 0;
            }

            if (PInvoke.ProcessIdToSessionId(PInvoke.GetCurrentProcessId(), out var sessionId))
            {
                data["session.id"] = sessionId;
            }

            var lii = new LASTINPUTINFO()
            {
                cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)),
            };
            if (PInvoke.GetLastInputInfo(out lii) && lii.dwTime > 0)
            {
                data["user_idle_ms"] = tickCount - lii.dwTime;
            }

            RaiseEvent(this, new EventEventArgs(JsonSerializer.Serialize(data)));

            if (CaptureConsoleUser) RunAsConsoleUser();
        }

        unsafe void RunAsConsoleUser()
        {
            var executableFilePath = Process.GetCurrentProcess().MainModule.FileName;

            nint token = 0;
            if (!PInvoke.WTSQueryUserToken(PInvoke.WTSGetActiveConsoleSessionId(), ref token)) return;

            try
            {
                var tempFile = Path.GetTempFileName();
                var tempDir = Path.GetTempFileName();
                File.Delete(tempDir);
                using (var stdout = new FileStream(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Inheritable))
                {
                    var si = new STARTUPINFOW();
                    si.hStdOutput = new HANDLE(token);
                    Environment.SetEnvironmentVariable("TMP", tempDir);
                    Environment.SetEnvironmentVariable("TEMP", tempDir);
                    if (!PInvoke.CreateProcessAsUser(new CloseHandleSafeHandle(token, false), null, $"\"{executableFilePath}\" --once", null, null, true, 0, null, null, si, out var pi)) return;
                    if (0 != PInvoke.WaitForSingleObject(new CloseHandleSafeHandle(pi.hProcess), 10000)) return;
                }
                RaiseEvent(this, new EventEventArgs(File.ReadAllText(tempFile)));
                File.Delete(tempFile);
            }
            finally
            {
                PInvoke.CloseHandle(new HANDLE(token));
            }
        }
    }
}
