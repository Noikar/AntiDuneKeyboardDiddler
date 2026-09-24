using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AntiDuneKeyboardDiddler
{
    internal sealed class Options
    {
        // Case-insensitive substrings matched against running process names.
        public List<string> WatchProcesses = new List<string> { "DuneSandbox" };

        // How often to look for the game while idle, and how often to enforce while it runs.
        public int IdlePollMilliseconds = 2000;
        public int ArmedPollMilliseconds = 150;

        // Do not retry unloading the same intruder more often than this. If the game keeps
        // re-adding the layout there is nothing to gain from a tight fight over it.
        public int UnloadRetryMilliseconds = 3000;

        // How long to keep putting the layout back after an intruder is evicted. Removing a
        // layout drops every thread that was using it onto the system default, and the
        // requests that undo that are asynchronous, so the desktop needs a moment to settle.
        public int SettleMilliseconds = 2000;

        // How often, while the game runs, to check that the rest of the desktop is still on
        // the preferred layout and put back anything that has drifted. Cheap when nothing is
        // wrong: it reads each window's layout and posts nothing unless one is off.
        public int HoldSweepMilliseconds = 500;

        // The layout to hold, pinned rather than detected. Accepts the input locale identifier
        // as the log and status window print it ("F0010409"), the KLID Windows uses in the
        // registry ("00000409"), or the layout's name ("United States-International").
        //
        // Empty means work it out from the foreground window, which is only as good as what
        // Windows happens to have put there - see the comment on Guard.Enforce.
        public string PreferredLayout = string.Empty;

        // Ask GitHub once at startup whether there is a newer release, and say so with a tray
        // balloon if there is. Nothing is ever downloaded or installed; the menu item checks on
        // demand whether this is on or off.
        public bool CheckForUpdates = true;

        public bool EnforceAlways = false;
        public bool Verbose = false;

        // Show a tray balloon when the layout is put back, so the correction is not silent.
        public bool Notify = true;

        private string path;

        public static Options Load(string path)
        {
            var options = new Options();

            options.path = path;

            if (!File.Exists(path))
            {
                return options;
            }

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();

                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("["))
                {
                    continue;
                }

                int separator = line.IndexOf('=');

                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();

                switch (key.ToLowerInvariant())
                {
                    case "watchprocesses":
                        options.WatchProcesses = value
                            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(item => item.Trim())
                            .Where(item => item.Length > 0)
                            .ToList();
                        break;

                    case "idlepollmilliseconds":
                        int.TryParse(value, out options.IdlePollMilliseconds);
                        break;

                    case "armedpollmilliseconds":
                        int.TryParse(value, out options.ArmedPollMilliseconds);
                        break;

                    case "unloadretrymilliseconds":
                        int.TryParse(value, out options.UnloadRetryMilliseconds);
                        break;

                    case "settlemilliseconds":
                        int.TryParse(value, out options.SettleMilliseconds);
                        break;

                    case "holdsweepmilliseconds":
                        int.TryParse(value, out options.HoldSweepMilliseconds);
                        break;

                    case "preferredlayout":
                        options.PreferredLayout = value;
                        break;

                    case "checkforupdates":
                        options.CheckForUpdates = IsTrue(value);
                        break;

                    case "enforcealways":
                        options.EnforceAlways = IsTrue(value);
                        break;

                    case "verbose":
                        options.Verbose = IsTrue(value);
                        break;

                    case "notify":
                        options.Notify = IsTrue(value);
                        break;
                }
            }

            return options;
        }

        /// <summary>
        /// Rewrites a single setting in place, preserving the comments around it, so that a
        /// toggle made from the tray menu survives a restart. Settings toggled before the file
        /// exists are simply not persisted; the defaults still apply.
        /// </summary>
        public void Save(string key, bool value)
        {
            Save(key, value ? "true" : "false");
        }

        /// <summary>See the bool overload; this is the one that does the work.</summary>
        public void Save(string key, string value)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                bool replaced = false;

                for (int index = 0; index < lines.Length; index++)
                {
                    string trimmed = lines[index].TrimStart();

                    if (trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                    {
                        continue;
                    }

                    int separator = trimmed.IndexOf('=');

                    if (separator <= 0)
                    {
                        continue;
                    }

                    if (trimmed.Substring(0, separator).Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[index] = key + " = " + value;
                        replaced = true;
                        break;
                    }
                }

                File.WriteAllLines(path, replaced ? lines : lines.Concat(
                    new[] { key + " = " + value }).ToArray());
            }
            catch (IOException)
            {
                // A settings file that cannot be written is not worth interrupting the user for.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static bool IsTrue(string value)
        {
            return value.Equals("1", StringComparison.OrdinalIgnoreCase)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }
    }
}
