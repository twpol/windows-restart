using System;
using System.Collections.Generic;
using System.ComponentModel;
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

        const string HKCU_Windows_Restart = @"Software\Windows Restart";
        const string KeyRestartRequiredSince = "Restart Required Since";

        public Monitor()
        {
        }

        public void Execute()
        {
            var data = HasTcbPrivilege() ? GetConsoleUserData() : GetData();
            ApplyPersistentData(data);
            RaiseEvent(this, new EventEventArgs(JsonSerializer.Serialize(data)));
        }

        public Dictionary<string, object> GetData()
        {
            var data = new Dictionary<string, object> {
                { "meta.local_hostname", Dns.GetHostName() },
                { "meta.local_platform", Environment.OSVersion.Platform.ToString() },
                { "meta.local_version", Environment.OSVersion.Version.ToString() },
                { "meta.local_os", Environment.OSVersion.ToString() },
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

            return data;
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

        unsafe Dictionary<string, object> GetConsoleUserData()
        {
            var executableFilePath = Process.GetCurrentProcess().MainModule.FileName;

            nint token = 0;
            if (!PInvoke.WTSQueryUserToken(PInvoke.WTSGetActiveConsoleSessionId(), ref token)) throw new Win32Exception(Marshal.GetLastWin32Error());

            try
            {
                var tempFile = Path.GetTempFileName();
                var tempDir = Path.GetTempFileName();
                File.Delete(tempDir);
                try
                {
                    using (var stdout = new FileStream(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Inheritable))
                    {
                        var si = new STARTUPINFOW();
                        si.cb = (uint)Marshal.SizeOf(si);
                        si.hStdOutput = new HANDLE(stdout.SafeFileHandle.DangerousGetHandle());
                        si.dwFlags = Native.STARTF_USESTDHANDLES;
                        Environment.SetEnvironmentVariable("TMP", tempDir);
                        Environment.SetEnvironmentVariable("TEMP", tempDir);
                        if (!PInvoke.CreateProcessAsUser(new CloseHandleSafeHandle(token, false), null, $"\"{executableFilePath}\" --once", null, null, true, 0, null, null, si, out var pi)) throw new Win32Exception(Marshal.GetLastWin32Error());
                        if (0 != PInvoke.WaitForSingleObject(new CloseHandleSafeHandle(pi.hProcess), 10000)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    }
                    return JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(tempFile));
                }
                finally
                {
                    File.Delete(tempFile);
                }
            }
            finally
            {
                PInvoke.CloseHandle(new HANDLE(token));
            }
        }

        void ApplyPersistentData(Dictionary<string, object> data)
        {
            if (OperatingSystem.IsWindows() && data.ContainsKey("auto_update.restart_required"))
            {
                var restartRequired = data["auto_update.restart_required"].ToString() == "True";
                using (var key = Registry.CurrentUser.CreateSubKey(HKCU_Windows_Restart, true))
                {
                    var restartRequiredSince = key.GetValue(KeyRestartRequiredSince) as long? ?? 0;

                    if (restartRequired && restartRequiredSince == 0)
                    {
                        restartRequiredSince = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        key.SetValue(KeyRestartRequiredSince, restartRequiredSince, RegistryValueKind.QWord);
                    }
                    else if (!restartRequired && restartRequiredSince != 0)
                    {
                        restartRequiredSince = 0;
                        key.DeleteValue(KeyRestartRequiredSince);
                    }

                    data["auto_update.restart_required_since"] = restartRequiredSince > 0 ? DateTimeOffset.FromUnixTimeSeconds(restartRequiredSince).UtcDateTime.ToString("o") : null;
                }
            }
        }
    }
}
