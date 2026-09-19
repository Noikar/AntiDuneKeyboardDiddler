using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace AntiDuneKeyboardDiddler
{
    /// <summary>
    /// The watchdog itself. Driven one step at a time by <see cref="Tick"/> so it can be run
    /// from a UI timer without needing a thread of its own.
    /// </summary>
    internal sealed class Guard
    {
        private readonly Options options;

        private Dictionary<uint, string> expected = new Dictionary<uint, string>();
        private SortedDictionary<string, string> preloadSnapshot = new SortedDictionary<string, string>();
        private uint preferredHkl;

        private readonly Dictionary<uint, DateTime> lastUnloadAttempt = new Dictionary<uint, DateTime>();
        private readonly Dictionary<uint, bool> lastUnloadResult = new Dictionary<uint, bool>();
        private DateTime lastDefaultLanguageReset = DateTime.MinValue;
        private DateTime settleDeadline = DateTime.MinValue;

        private HashSet<uint> gameProcessIds = new HashSet<uint>();
        private List<Process> tracked = new List<Process>();
        private DateTime lastProcessScan = DateTime.MinValue;

        public Guard(Options options)
        {
            this.options = options;
        }

        /// <summary>Raised when the guard arms, disarms, or corrects something.</summary>
        public event EventHandler StateChanged;

        /// <summary>Raised with a sentence worth putting in front of the user.</summary>
        public event EventHandler<NotificationEventArgs> Notified;

        public bool IsArmed { get; private set; }

        public string ArmedDescription { get; private set; }

        /// <summary>
        /// True for a short while after a layout is evicted, during which any window not on
        /// the preferred layout is assumed to be fallout from the eviction rather than a
        /// deliberate choice by the user.
        /// </summary>
        public bool IsSettling
        {
            get { return DateTime.UtcNow < settleDeadline; }
        }

        public int NextIntervalMilliseconds
        {
            get
            {
                return IsArmed || options.EnforceAlways || IsSettling
                    ? options.ArmedPollMilliseconds
                    : options.IdlePollMilliseconds;
            }
        }

        // --- layout inspection ---------------------------------------------------

        public static List<uint> GetLoadedLayouts()
        {
            uint count = NativeMethods.GetKeyboardLayoutList(0, null);

            if (count == 0)
            {
                return new List<uint>();
            }

            var buffer = new IntPtr[count];

            NativeMethods.GetKeyboardLayoutList((int)count, buffer);

            return buffer.Select(ToHkl).ToList();
        }

        private static uint ToHkl(IntPtr handle)
        {
            return unchecked((uint)handle.ToInt64());
        }

        private static IntPtr ToHandle(uint hkl)
        {
            return new IntPtr(unchecked((int)hkl));
        }

        public static string Describe(uint hkl, Dictionary<uint, string> known)
        {
            string name;

            if (known != null && known.TryGetValue(hkl, out name))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:X8} ({1})", hkl, name);
            }

            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo((int)(hkl & 0xFFFF));

                return string.Format(CultureInfo.InvariantCulture, "{0:X8} ({1})", hkl, culture.Name);
            }
            catch (CultureNotFoundException)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:X8}", hkl);
            }
        }

        /// <summary>Just the human-readable part, for notifications.</summary>
        private string FriendlyName(uint hkl)
        {
            string name;

            if (expected.TryGetValue(hkl, out name))
            {
                return name;
            }

            try
            {
                return CultureInfo.GetCultureInfo((int)(hkl & 0xFFFF)).DisplayName;
            }
            catch (CultureNotFoundException)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:X8}", hkl);
            }
        }

        private List<uint> GetIntruders()
        {
            return GetLoadedLayouts().Where(hkl => !expected.ContainsKey(hkl)).ToList();
        }

        private static uint GetDefaultInputLanguage()
        {
            IntPtr value = IntPtr.Zero;

            if (NativeMethods.SystemParametersInfoW(NativeMethods.SPI_GETDEFAULTINPUTLANG, 0, ref value, 0))
            {
                return ToHkl(value);
            }

            return 0;
        }

        private static void SetDefaultInputLanguage(uint hkl)
        {
            IntPtr value = ToHandle(hkl);

            NativeMethods.SystemParametersInfoW(
                NativeMethods.SPI_SETDEFAULTINPUTLANG, 0, ref value, NativeMethods.SPIF_SENDCHANGE);
        }

        private static uint GetForegroundLayout()
        {
            IntPtr hWnd = NativeMethods.GetForegroundWindow();

            if (hWnd == IntPtr.Zero)
            {
                return 0;
            }

            uint processId;
            uint threadId = NativeMethods.GetWindowThreadProcessId(hWnd, out processId);

            return threadId == 0 ? 0 : ToHkl(NativeMethods.GetKeyboardLayout(threadId));
        }

        // --- process tracking ----------------------------------------------------

        private List<Process> FindGameProcesses()
        {
            var found = new List<Process>();

            foreach (Process process in Process.GetProcesses())
            {
                bool matched = false;

                try
                {
                    string name = process.ProcessName;

                    matched = options.WatchProcesses.Any(
                        hint => name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
                }
                catch (InvalidOperationException)
                {
                    // Process exited between enumeration and inspection.
                }

                if (matched)
                {
                    found.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }

            return found;
        }

        /// <summary>
        /// Anti-cheat services can refuse the query that backs HasExited. An unanswerable
        /// handle is treated as still running; the periodic full scan corrects it either way.
        /// </summary>
        private static bool HasExited(Process process)
        {
            try
            {
                return process.HasExited;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private void ResetCorrectionState()
        {
            lastUnloadAttempt.Clear();
            lastUnloadResult.Clear();
            lastDefaultLanguageReset = DateTime.MinValue;
            settleDeadline = DateTime.MinValue;
        }

        // --- enforcement ---------------------------------------------------------

        /// <summary>
        /// Puts every window whose layout matches <paramref name="needsRestoring"/> back onto
        /// the preferred one. Windows belonging to the game are skipped on purpose.
        /// </summary>
        private int RestoreWindows(Predicate<uint> needsRestoring)
        {
            int restored = 0;

            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                uint processId;
                uint threadId = NativeMethods.GetWindowThreadProcessId(hWnd, out processId);

                if (threadId == 0 || gameProcessIds.Contains(processId))
                {
                    return true;
                }

                uint current = ToHkl(NativeMethods.GetKeyboardLayout(threadId));

                if (current == 0 || !needsRestoring(current))
                {
                    return true;
                }

                bool posted = NativeMethods.PostMessageW(
                    hWnd,
                    NativeMethods.WM_INPUTLANGCHANGEREQUEST,
                    new IntPtr(NativeMethods.INPUTLANGCHANGE_SYSCHARSET),
                    ToHandle(preferredHkl));

                if (posted)
                {
                    restored++;
                }

                return true;
            }, IntPtr.Zero);

            return restored;
        }

        /// <summary>
        /// Attempts to remove the given layouts from the session. Returns true if at least one
        /// was actually removed, which is the moment the desktop needs settling afterwards.
        /// </summary>
        private bool TryUnload(IEnumerable<uint> intruders)
        {
            bool removedAny = false;

            // A layout cannot be unloaded while this thread is the one using it.
            NativeMethods.ActivateKeyboardLayout(ToHandle(preferredHkl), NativeMethods.KLF_SETFORPROCESS);

            foreach (uint intruder in intruders)
            {
                DateTime last;

                if (lastUnloadAttempt.TryGetValue(intruder, out last)
                    && (DateTime.UtcNow - last).TotalMilliseconds < options.UnloadRetryMilliseconds)
                {
                    continue;
                }

                lastUnloadAttempt[intruder] = DateTime.UtcNow;

                bool unloaded = NativeMethods.UnloadKeyboardLayout(ToHandle(intruder));
                int error = Marshal.GetLastWin32Error();

                removedAny = removedAny || unloaded;

                // A game that re-adds its layout every few seconds would otherwise fill the
                // log with identical failures, so only report when the outcome changes.
                bool previous;
                bool isRepeat = lastUnloadResult.TryGetValue(intruder, out previous) && previous == unloaded;

                lastUnloadResult[intruder] = unloaded;

                if (!isRepeat)
                {
                    Log.Write(string.Format(
                        CultureInfo.InvariantCulture,
                        "unload {0} -> {1}",
                        Describe(intruder, expected),
                        unloaded ? "removed" : "refused (error " + error + ")"));
                }
            }

            return removedAny;
        }

        private void RestorePreload()
        {
            SortedDictionary<string, string> current = LayoutRegistry.ReadPreload();

            bool same = current.Count == preloadSnapshot.Count
                && current.All(entry =>
                {
                    string value;

                    return preloadSnapshot.TryGetValue(entry.Key, out value)
                        && string.Equals(value, entry.Value, StringComparison.OrdinalIgnoreCase);
                });

            if (same)
            {
                return;
            }

            Log.Write("Preload changed: " + Format(current) + " - restoring " + Format(preloadSnapshot));
            LayoutRegistry.WritePreload(preloadSnapshot);
        }

        private static string Format(SortedDictionary<string, string> preload)
        {
            return string.Join(" ", preload.Select(entry => entry.Key + "=" + entry.Value).ToArray());
        }

        /// <summary>
        /// One enforcement pass. Returns true when something had to be corrected.
        /// </summary>
        private bool Enforce()
        {
            List<uint> intruders = GetIntruders();

            if (intruders.Count == 0)
            {
                // Evicting a layout drops everything that was using it onto the system default
                // input language, which is rarely the layout the user was actually on. For a
                // moment afterwards, keep putting it back rather than reading the fallback as
                // a deliberate choice.
                if (IsSettling)
                {
                    RestoreWindows(hkl => hkl != preferredHkl);

                    return true;
                }

                // Nothing is wrong, so follow whatever the user has chosen for themselves.
                uint foreground = GetForegroundLayout();

                if (foreground != 0 && expected.ContainsKey(foreground))
                {
                    preferredHkl = foreground;
                }

                return false;
            }

            var intruderSet = new HashSet<uint>(intruders);
            bool firstSighting = false;

            foreach (uint intruder in intruders)
            {
                if (!lastUnloadAttempt.ContainsKey(intruder))
                {
                    firstSighting = true;

                    Log.Write("intruder layout appeared: " + Describe(intruder, expected)
                        + " - restoring " + Describe(preferredHkl, expected));
                }
            }

            RestorePreload();

            // Unload before restoring, never after. Windows reassigns every thread still using
            // a layout as that layout goes away, so a restore done first is simply overwritten
            // by that fallback - which is how this ended up leaving the desktop on the system
            // default instead of the layout the user was on.
            bool removedAny = TryUnload(intruders);

            if (removedAny)
            {
                settleDeadline = DateTime.UtcNow.AddMilliseconds(options.SettleMilliseconds);
            }

            // After a successful eviction the damage is no longer confined to windows holding
            // the intruder, so sweep everything that is not on the preferred layout.
            int restored = removedAny
                ? RestoreWindows(hkl => hkl != preferredHkl)
                : RestoreWindows(intruderSet.Contains);

            // SPI_SETDEFAULTINPUTLANG with SPIF_SENDCHANGE broadcasts WM_SETTINGCHANGE to every
            // top-level window on the desktop. Doing that on every poll would hammer the whole
            // session, and a single app that is slow to answer would drag everything with it,
            // so it gets the same cooldown as the unload.
            if (intruderSet.Contains(GetDefaultInputLanguage())
                && (DateTime.UtcNow - lastDefaultLanguageReset).TotalMilliseconds >= options.UnloadRetryMilliseconds)
            {
                lastDefaultLanguageReset = DateTime.UtcNow;

                SetDefaultInputLanguage(preferredHkl);
                Log.Write("default input language reset to " + Describe(preferredHkl, expected));
            }

            if (restored > 0 && options.Verbose)
            {
                Log.Write("restored " + restored + " window(s)");
            }

            if (firstSighting && removedAny)
            {
                Notify("Keyboard layout put back",
                    "Removed the " + FriendlyName(intruders[0]) + " layout the game added, and put you back on "
                    + FriendlyName(preferredHkl) + ".");
            }

            return true;
        }

        private void RefreshBaseline()
        {
            expected = LayoutRegistry.GetExpectedLayouts();
            preloadSnapshot = LayoutRegistry.ReadPreload();

            uint foreground = GetForegroundLayout();

            if (foreground != 0 && expected.ContainsKey(foreground))
            {
                preferredHkl = foreground;
            }
            else if (!expected.ContainsKey(preferredHkl))
            {
                preferredHkl = expected.Keys.FirstOrDefault();
            }
        }

        private void Notify(string title, string message)
        {
            EventHandler<NotificationEventArgs> handler = Notified;

            if (handler != null)
            {
                handler(this, new NotificationEventArgs(title, message));
            }
        }

        private void RaiseStateChanged()
        {
            EventHandler handler = StateChanged;

            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        // --- driving -------------------------------------------------------------

        /// <summary>
        /// Establishes the baseline. Returns false when the registry holds no configured
        /// layouts at all, in which case guessing would do more harm than nothing.
        /// </summary>
        public bool Start()
        {
            RefreshBaseline();

            if (expected.Count == 0)
            {
                Log.Write("No configured layouts found in the registry - refusing to guess.");

                return false;
            }

            Log.Write("Guarding. Your layouts: "
                + string.Join(", ", expected.Select(entry => Describe(entry.Key, expected)).ToArray()));
            Log.Write("Watching for processes matching: " + string.Join(", ", options.WatchProcesses.ToArray()));

            return true;
        }

        /// <summary>One step of the watch loop.</summary>
        public void Tick()
        {
            // Enumerating every process on the machine several times a second is far too
            // expensive for a poll loop. While the game runs, just ask the handles already
            // held whether they have exited, and do a full scan only occasionally so that
            // processes started later (launcher, then the game itself) still get picked up.
            bool fullScanDue = !IsArmed
                || (DateTime.UtcNow - lastProcessScan).TotalMilliseconds >= options.IdlePollMilliseconds;

            if (fullScanDue)
            {
                lastProcessScan = DateTime.UtcNow;
                tracked.ForEach(process => process.Dispose());
                tracked = FindGameProcesses();
            }
            else
            {
                tracked.RemoveAll(process =>
                {
                    if (!HasExited(process))
                    {
                        return false;
                    }

                    process.Dispose();

                    return true;
                });
            }

            bool gameRunning = tracked.Count > 0;

            gameProcessIds = new HashSet<uint>(tracked.Select(process => (uint)process.Id));

            if (gameRunning && !IsArmed)
            {
                IsArmed = true;
                ArmedDescription = string.Join(", ", tracked.Select(p => p.ProcessName).Distinct().ToArray());

                ResetCorrectionState();
                RefreshBaseline();

                Log.Write("ARMED - "
                    + string.Join(", ", tracked.Select(p => p.ProcessName + " (pid " + p.Id + ")").ToArray())
                    + " is running. Holding " + Describe(preferredHkl, expected));

                RaiseStateChanged();
            }
            else if (!gameRunning && IsArmed)
            {
                IsArmed = false;
                ArmedDescription = null;
                gameProcessIds.Clear();

                Log.Write("Game exited - final cleanup.");
                ResetCorrectionState();
                Enforce();
                Log.Write("DISARMED - idle.");

                RaiseStateChanged();
            }

            // Keep working while settling, so that the eviction done as the game exits still
            // gets its layout restored rather than being left on the system default.
            if (IsArmed || options.EnforceAlways || IsSettling)
            {
                Enforce();
            }
        }

        public void Stop()
        {
            tracked.ForEach(process => process.Dispose());
            tracked.Clear();

            Log.Write("Stopped.");
        }

        // --- one-shot operations -------------------------------------------------

        /// <summary>
        /// Drops any layout that is loaded but not configured, and starts the settling window
        /// so the caller's polling puts the active layout back. Returns how many were evicted.
        /// </summary>
        public int CleanupOnce()
        {
            RefreshBaseline();

            if (expected.Count == 0)
            {
                Log.Write("No configured layouts found in the registry - refusing to touch anything.");

                return -1;
            }

            List<uint> intruders = GetIntruders();

            if (intruders.Count == 0)
            {
                Log.Write("Nothing to clean up: only your configured layouts are loaded.");

                return 0;
            }

            Log.Write("Cleaning up " + intruders.Count + " stray layout(s).");

            var intruderSet = new HashSet<uint>(intruders);

            // Only reset the default input language if an intruder has taken it over. The user
            // may well have deliberately defaulted to one of their own layouts.
            if (intruderSet.Contains(GetDefaultInputLanguage()))
            {
                SetDefaultInputLanguage(preferredHkl);
                Log.Write("default input language reset to " + Describe(preferredHkl, expected));
            }

            RestorePreload();

            if (TryUnload(intruders))
            {
                settleDeadline = DateTime.UtcNow.AddMilliseconds(options.SettleMilliseconds);
            }

            RestoreWindows(hkl => hkl != preferredHkl);

            Notify("Stray layouts removed",
                "Put you back on " + FriendlyName(preferredHkl) + ".");

            return intruders.Count;
        }

        public string BuildStatusText()
        {
            expected = LayoutRegistry.GetExpectedLayouts();

            var text = new StringBuilder();

            text.AppendLine("Configured layouts (from HKCU\\Keyboard Layout):");

            foreach (var entry in expected)
            {
                text.AppendLine("  " + Describe(entry.Key, expected));
            }

            text.AppendLine();
            text.AppendLine("Currently loaded:");

            foreach (uint hkl in GetLoadedLayouts())
            {
                text.AppendLine("  " + Describe(hkl, expected) + (expected.ContainsKey(hkl) ? "  [ok]" : "  [INTRUDER]"));
            }

            text.AppendLine();
            text.AppendLine("Foreground window layout: " + Describe(GetForegroundLayout(), expected));
            text.AppendLine("Default input language:   " + Describe(GetDefaultInputLanguage(), expected));
            text.AppendLine("Preload:                  " + Format(LayoutRegistry.ReadPreload()));

            List<Process> games = FindGameProcesses();

            text.AppendLine("Watched processes:        "
                + (games.Count == 0
                    ? "none running (hints: " + string.Join(", ", options.WatchProcesses.ToArray()) + ")"
                    : string.Join(", ", games.Select(p => p.ProcessName + " (pid " + p.Id + ")").ToArray())));

            games.ForEach(process => process.Dispose());

            return text.ToString();
        }
    }

    internal sealed class NotificationEventArgs : EventArgs
    {
        public NotificationEventArgs(string title, string message)
        {
            Title = title;
            Message = message;
        }

        public string Title { get; private set; }

        public string Message { get; private set; }
    }
}
