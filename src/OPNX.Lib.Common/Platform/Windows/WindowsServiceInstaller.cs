using Microsoft.Win32;
using OPNX.Lib.Common.Logging;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace OPNX.Lib.Common.Platform.Windows
{
    [SupportedOSPlatform("windows")]
    public static class WindowsServiceInstaller
    {
        public static bool InstallService(
            string servicePath,
            string serviceName,
            string displayName = "",
            string description = "",
            bool delayAutoStart = true)
        {
            try
            {
                if (!IsElevated() || !File.Exists(servicePath))
                    return false;

                string arguments = $"create \"{serviceName}\" binpath= \"{servicePath}\" type= own start= auto";
                if (!string.IsNullOrEmpty(displayName))
                    arguments += $" displayname= \"{displayName}\"";

                RunAsAdministrator("sc.exe", arguments);

                if (!string.IsNullOrEmpty(description))
                    RunAsAdministrator("sc.exe", $"description \"{serviceName}\" \"{description}\"");

                if (delayAutoStart)
                    SetDelayedAutoStart(serviceName);

                return true;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to install the service. ServiceName={serviceName}, Error={ex.Message}.");
                return false;
            }
        }

        public static bool UninstallService(string serviceName)
        {
            try
            {
                if (!IsElevated())
                    return false;

                RunAsAdministrator("sc.exe", $"delete \"{serviceName}\"");
                return true;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to uninstall the service. ServiceName={serviceName}, Error={ex.Message}.");
                return false;
            }
        }

        public static bool IsElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void SetDelayedAutoStart(string serviceName)
        {
            using var serviceKey = Registry.LocalMachine.OpenSubKey(@"System\CurrentControlSet\Services\" + serviceName, true);
            if (serviceKey is null)
                return;

            serviceKey.SetValue("Start", 2);
            if (Environment.OSVersion.Version.Major >= 6)
                serviceKey.SetValue("DelayedAutostart", 1, RegistryValueKind.DWord);
        }

        private static void RunAsAdministrator(string filePath, string arguments)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = filePath,
                Arguments = arguments,
                Verb = "runas",
                UseShellExecute = true
            };

            try
            {
                using Process process = new()
                {
                    EnableRaisingEvents = true,
                    StartInfo = startInfo
                };

                process.Start();
                process.WaitForExit();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                LogManager.Error($"The administrator permission request was denied. Error={ex.Message}.");
            }
        }
    }
}
