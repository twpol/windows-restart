using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
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
        public event EventHandler<EventEventArgs> RaiseEvent;

        public Monitor()
        {
        }

        public void Execute()
        {
            if (HasTcbPrivilege())
                RunAsConsoleUser();
            else
                CollectData();
        }

        public void CollectData()
        {
            var data = new Dictionary<string, object> {
                { "meta.local_hostname", Dns.GetHostName() },
                { "service_name", "windows-restart" },
                { "name", "monitor" },
            };

            if (OperatingSystem.IsWindows())
            {
                data["uptime_ms"] = PInvoke.GetTickCount64();

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

                if (0 == PInvoke.SHQueryUserNotificationState(out var notificationState))
                {
                    data["notification_state"] = notificationState;
                    data["notification_state.name"] = Enum.GetName(typeof(QUERY_USER_NOTIFICATION_STATE), notificationState);
                }

                var lii = new LASTINPUTINFO()
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)),
                };
                if (PInvoke.GetLastInputInfo(out lii) && lii.dwTime > 0)
                {
                    data["user_idle_ms"] = PInvoke.GetTickCount() - lii.dwTime;
                }

                using (var sessionManager = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
                {
                    var pending = sessionManager.GetValue("PendingFileRenameOperations") as string[];
                    if (pending != null)
                    {
                        data["pending_renames.count"] = pending.Length;
                    }
                }

                try
                {
                    dynamic systemInfo = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.SystemInfo", true));
                    data["auto_update.restart_required"] = systemInfo.RebootRequired;

                    dynamic autoUpdate = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.AutoUpdate", true));
                    data["auto_update.last_check_date"] = DateTime.SpecifyKind(autoUpdate.Results.LastSearchSuccessDate, DateTimeKind.Utc).ToString("o");
                    data["auto_update.last_install_date"] = DateTime.SpecifyKind(autoUpdate.Results.LastInstallationSuccessDate, DateTimeKind.Utc).ToString("o");
                    data["auto_update.enabled"] = autoUpdate.ServiceEnabled;
                    data["auto_update.settings.notification_level"] = autoUpdate.Settings.NotificationLevel;
                    data["auto_update.settings.read_only"] = autoUpdate.Settings.ReadOnly;
                    data["auto_update.settings.required"] = autoUpdate.Settings.Required;
                    data["auto_update.settings.scheduled_installation_day"] = autoUpdate.Settings.ScheduledInstallationDay;
                    data["auto_update.settings.scheduled_installation_time"] = autoUpdate.Settings.ScheduledInstallationTime;
                    data["auto_update.settings.include_recommended_updates"] = autoUpdate.Settings.IncludeRecommendedUpdates;
                    data["auto_update.settings.non_administrators_elevated"] = autoUpdate.Settings.NonAdministratorsElevated;
                    data["auto_update.settings.featured_updates_enabled"] = autoUpdate.Settings.FeaturedUpdatesEnabled;
                }
                catch (Exception error)
                {
                    data["auto_update.error"] = error.Message + "\n" + error.StackTrace;
                }
            }

            RaiseEvent(this, new EventEventArgs(JsonSerializer.Serialize(data)));
        }

        unsafe bool HasTcbPrivilege()
        {
            if (!PInvoke.LookupPrivilegeValue(null, "SeTcbPrivilege", out var tcbPrivilege)) return false;

            if (!PInvoke.OpenProcessToken(PInvoke.GetCurrentProcess(), Native.TOKEN_QUERY, out var processToken)) return false;

            try
            {
                var privilegeSet = new PRIVILEGE_SET()
                {
                    PrivilegeCount = 1,
                };
                privilegeSet.Privilege[0].Luid = tcbPrivilege;
                if (!PInvoke.PrivilegeCheck(new CloseHandleSafeHandle(processToken), ref privilegeSet, out var checkResult) || checkResult == 0) return false;

                return true;
            }
            finally
            {
                PInvoke.CloseHandle(new HANDLE(processToken));
            }
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
