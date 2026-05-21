// MetroTrilithon.Desktop の内製化 (Phase 1)
using System;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace MetroTrilithon.Desktop
{
    public class SystemEnvironment
    {
        public string OS { get; }
        public string OSVersion { get; }
        public string Architecture { get; }
        public string CPU { get; }
        public string TotalPhysicalMemorySize { get; }
        public string FreePhysicalMemorySize { get; }
        public string DotNetVersion { get; }
        public string ErrorMessage { get; }

        public SystemEnvironment()
        {
            try
            {
                using (var mc = new ManagementClass("Win32_OperatingSystem"))
                using (var mo = mc.GetInstances().OfType<ManagementObject>().FirstOrDefault())
                {
                    if (mo != null)
                    {
                        this.OS = mo["Caption"]?.ToString();
                        this.OSVersion = mo["Version"]?.ToString();
                        this.Architecture = mo["OSArchitecture"]?.ToString();
                        this.TotalPhysicalMemorySize = $"{mo["TotalVisibleMemorySize"]:N0} KB";
                        this.FreePhysicalMemorySize = $"{mo["FreePhysicalMemory"]:N0} KB";
                    }
                }
                using (var mc = new ManagementClass("Win32_Processor"))
                using (var mo = mc.GetInstances().OfType<ManagementObject>().FirstOrDefault())
                {
                    if (mo != null) this.CPU = mo["Name"]?.ToString();
                }
                this.DotNetVersion = Desktop.DotNetVersion.GetVersion();
            }
            catch (Exception ex)
            {
                this.ErrorMessage = ex.Message;
            }
        }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(this.ErrorMessage))
                return $"SystemEnvironment\n({this.ErrorMessage})";

            return $@"SystemEnvironment
OS:           {this.OS}
OSVersion:    {this.OSVersion}
Architecture: {this.Architecture}
Runtime:      {this.DotNetVersion}

CPU:         {this.CPU}
RAM (Total): {this.TotalPhysicalMemorySize}
RAM (Free):  {this.FreePhysicalMemorySize}";
        }
    }

    public class DotNetVersion
    {
        public static string GetVersion()
        {
            const string subkey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\";
            var version = "";
            using (var ndpKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(subkey))
            {
                if (ndpKey?.GetValue("Release") is int value)
                    version = GetVersionCore(value);
            }
            if (string.IsNullOrEmpty(version)) version = System.Environment.Version.ToString();
            return $".NET Framework Version: {version}";
        }

        private static string GetVersionCore(int releaseKey)
        {
            if (releaseKey >= 461808) return "4.7.2 or later";
            if (releaseKey >= 461308) return "4.7.1";
            if (releaseKey >= 460798) return "4.7";
            if (releaseKey >= 394802) return "4.6.2";
            if (releaseKey >= 394254) return "4.6.1";
            if (releaseKey >= 393295) return "4.6";
            if (releaseKey >= 379893) return "4.5.2";
            if (releaseKey >= 378675) return "4.5.1";
            if (releaseKey >= 378389) return "4.5";
            return "";
        }
    }
}
