using System;
using System.Reflection;
using Microsoft.Win32;

namespace AntiDuneKeyboardDiddler
{
    /// <summary>
    /// Start-with-Windows, via the per-user Run key. A registry value is used rather than a
    /// shortcut in the Startup folder because it needs no COM, no shell scripting, and no
    /// administrator rights, and it is just as easy for the user to inspect or remove.
    /// </summary>
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "AntiDuneKeyboardDiddler";

        public static string ExecutablePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        private static string CommandLine
        {
            get { return "\"" + ExecutablePath + "\""; }
        }

        /// <summary>
        /// True only when the Run entry exists and points at this very executable, so that a
        /// copy moved to a new folder correctly reports itself as not registered.
        /// </summary>
        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    string value = key.GetValue(ValueName) as string;

                    return !string.IsNullOrEmpty(value)
                        && string.Equals(value.Trim(), CommandLine, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    if (enabled)
                    {
                        key.SetValue(ValueName, CommandLine, RegistryValueKind.String);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }

                Log.Write("start with Windows: " + (enabled ? "enabled" : "disabled"));

                return true;
            }
            catch (Exception exception)
            {
                Log.Write("could not change the start-with-Windows setting: " + exception.Message);

                return false;
            }
        }
    }
}
