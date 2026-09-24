using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace AntiDuneKeyboardDiddler
{
    /// <summary>
    /// The tray presence. The guard is stepped from a UI timer rather than a background
    /// thread, which keeps every touch of the icon and menu on the thread that owns them
    /// without any marshalling; each step is a handful of cheap Win32 calls.
    /// </summary>
    internal sealed class TrayApplication : ApplicationContext
    {
        private readonly Options options;
        private readonly Guard guard;
        private readonly NotifyIcon notifyIcon;
        private readonly Timer timer;
        private readonly Icon idleIcon;
        private readonly Icon armedIcon;

        private readonly ToolStripMenuItem statusItem;
        private readonly ToolStripMenuItem preferredLayoutItem;
        private readonly ToolStripMenuItem enforceAlwaysItem;
        private readonly ToolStripMenuItem startWithWindowsItem;
        private readonly ToolStripMenuItem checkForUpdatesItem;

        // Written by the thread pool, read by the poll timer. The timer already runs on the UI
        // thread, which is the only one allowed to touch the menu and the balloon, so handing
        // the result over through a field saves marshalling it back by hand.
        private volatile UpdateCheckResult updateResult;

        private bool updateCheckRunning;
        private bool updateCheckIsManual;
        private bool balloonOpensReleases;
        private DateTime startupCheckDue = DateTime.MaxValue;

        public TrayApplication(Options options)
        {
            this.options = options;

            guard = new Guard(options);
            guard.StateChanged += OnGuardStateChanged;
            guard.Notified += OnGuardNotified;

            idleIcon = TrayIcons.Idle();
            armedIcon = TrayIcons.Armed();

            statusItem = new ToolStripMenuItem("Starting...") { Enabled = false };

            // Filled in when the menu opens, so that a layout added or removed in Windows
            // settings shows up without restarting the tray.
            preferredLayoutItem = new ToolStripMenuItem("Preferred layout");

            enforceAlwaysItem = new ToolStripMenuItem("Guard even when the game is not running")
            {
                CheckOnClick = true,
                Checked = options.EnforceAlways
            };
            enforceAlwaysItem.CheckedChanged += OnEnforceAlwaysChanged;

            startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
            {
                CheckOnClick = true,
                Checked = Startup.IsEnabled()
            };
            startWithWindowsItem.CheckedChanged += OnStartWithWindowsChanged;

            checkForUpdatesItem = new ToolStripMenuItem("Check for updates...", null, OnCheckForUpdates);

            var menu = new ContextMenuStrip();

            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open log", null, OnOpenLog);
            menu.Items.Add("Show status...", null, OnShowStatus);
            menu.Items.Add("Remove stray layouts now", null, OnCleanupNow);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(preferredLayoutItem);
            menu.Items.Add(enforceAlwaysItem);
            menu.Items.Add(startWithWindowsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(checkForUpdatesItem);
            menu.Items.Add("Exit", null, OnExit);

            menu.Opening += OnMenuOpening;

            notifyIcon = new NotifyIcon
            {
                Icon = idleIcon,
                Text = "AntiDuneKeyboardDiddler",
                ContextMenuStrip = menu,
                Visible = true
            };

            notifyIcon.DoubleClick += OnShowStatus;
            notifyIcon.BalloonTipClicked += OnBalloonTipClicked;

            if (!guard.Start())
            {
                notifyIcon.ShowBalloonTip(
                    8000,
                    "Nothing to guard",
                    "No keyboard layouts are configured in the registry, so there is nothing to protect.",
                    ToolTipIcon.Warning);
            }

            timer = new Timer { Interval = options.IdlePollMilliseconds };
            timer.Tick += OnTimerTick;
            timer.Start();

            // Not immediately: started at sign-in, this is often running before the network is,
            // and a check that fails because nothing is connected yet is worse than no check.
            if (options.CheckForUpdates)
            {
                startupCheckDue = DateTime.UtcNow.AddSeconds(15);
            }

            UpdateDisplay();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            try
            {
                guard.Tick();
            }
            catch (Exception exception)
            {
                // A watchdog that dies silently is worse than one that complains, but it must
                // not take the tray icon down with it either.
                Log.Write("error during poll: " + exception);
            }

            if (DateTime.UtcNow >= startupCheckDue)
            {
                startupCheckDue = DateTime.MaxValue;

                BeginUpdateCheck(false);
            }

            UpdateCheckResult result = updateResult;

            if (result != null)
            {
                updateResult = null;

                ShowUpdateCheckResult(result);
            }

            timer.Interval = Math.Max(50, guard.NextIntervalMilliseconds);
        }

        private void OnGuardStateChanged(object sender, EventArgs e)
        {
            UpdateDisplay();
        }

        private void OnGuardNotified(object sender, NotificationEventArgs e)
        {
            if (options.Notify)
            {
                balloonOpensReleases = false;

                notifyIcon.ShowBalloonTip(4000, e.Title, e.Message, ToolTipIcon.Info);
            }
        }

        private void UpdateDisplay()
        {
            bool armed = guard.IsArmed;

            statusItem.Text = armed
                ? "Guarding - " + guard.ArmedDescription + " is running"
                : "Idle - waiting for the game";

            notifyIcon.Icon = armed ? armedIcon : idleIcon;

            // The tray tooltip is capped at 63 characters; anything longer is simply dropped.
            string tooltip = armed ? "AntiDuneKeyboardDiddler - guarding" : "AntiDuneKeyboardDiddler - idle";

            notifyIcon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
        }

        private void OnOpenLog(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(Log.Path))
            {
                MessageBox.Show(
                    "No log file could be written, so there is nothing to open.",
                    "AntiDuneKeyboardDiddler",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                return;
            }

            try
            {
                if (!File.Exists(Log.Path))
                {
                    File.WriteAllText(Log.Path, string.Empty);
                }

                // notepad explicitly, rather than the shell default: .log is often unassociated,
                // which would otherwise show the user a "how do you want to open this" dialog.
                Process.Start("notepad.exe", "\"" + Log.Path + "\"");
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "Could not open the log:" + Environment.NewLine + exception.Message,
                    "AntiDuneKeyboardDiddler",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void OnShowStatus(object sender, EventArgs e)
        {
            MessageBox.Show(
                guard.BuildStatusText() + Environment.NewLine + "Log: " + (Log.Path ?? "(none)"),
                "AntiDuneKeyboardDiddler - status",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OnCleanupNow(object sender, EventArgs e)
        {
            int removed = guard.CleanupOnce();

            if (removed == 0)
            {
                notifyIcon.ShowBalloonTip(
                    3000,
                    "Nothing to remove",
                    "Only your own layouts are loaded.",
                    ToolTipIcon.Info);
            }

            // The settling window is handled by the timer, which speeds up on its own.
            timer.Interval = Math.Max(50, guard.NextIntervalMilliseconds);
        }

        private void OnMenuOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var layouts = guard.RefreshLayouts();

            string held;

            if (!layouts.TryGetValue(guard.PreferredLayout, out held))
            {
                held = "unknown";
            }

            preferredLayoutItem.Text = "Preferred layout: " + held
                + (guard.PinnedLayout == 0 ? " (detected)" : " (pinned)");

            preferredLayoutItem.DropDownItems.Clear();

            var automatic = new ToolStripMenuItem("Detect it from the foreground window")
            {
                Checked = guard.PinnedLayout == 0,
                Tag = (uint)0
            };

            automatic.Click += OnPreferredLayoutClicked;

            preferredLayoutItem.DropDownItems.Add(automatic);
            preferredLayoutItem.DropDownItems.Add(new ToolStripSeparator());

            foreach (var entry in layouts)
            {
                var item = new ToolStripMenuItem(entry.Value)
                {
                    Checked = guard.PinnedLayout == entry.Key,
                    Tag = entry.Key
                };

                item.Click += OnPreferredLayoutClicked;

                preferredLayoutItem.DropDownItems.Add(item);
            }
        }

        private void OnPreferredLayoutClicked(object sender, EventArgs e)
        {
            uint hkl = (uint)((ToolStripMenuItem)sender).Tag;

            guard.Pin(hkl);

            // The HKL rather than the layout name: names are read out of this machine's own
            // layout registry and are localized, so a name would not travel with the INI.
            options.PreferredLayout = hkl == 0
                ? string.Empty
                : hkl.ToString("X8", CultureInfo.InvariantCulture);

            options.Save("PreferredLayout", options.PreferredLayout);
        }

        private void OnCheckForUpdates(object sender, EventArgs e)
        {
            BeginUpdateCheck(true);
        }

        /// <summary>
        /// Starts a check on a pool thread. A manual one reports whatever it finds, including
        /// "you are up to date"; the startup one only speaks up when there is something to say.
        /// </summary>
        private void BeginUpdateCheck(bool manual)
        {
            if (updateCheckRunning)
            {
                return;
            }

            updateCheckRunning = true;
            updateCheckIsManual = manual;
            updateResult = null;

            checkForUpdatesItem.Enabled = false;
            checkForUpdatesItem.Text = "Checking for updates...";

            System.Threading.ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    updateResult = UpdateCheck.Run();
                }
                catch (Exception exception)
                {
                    // An exception left to escape a pool thread takes the process down with it,
                    // and a failed update check is not worth losing the guard over. It also has
                    // to produce a result either way, or the menu item stays greyed out.
                    updateResult = new UpdateCheckResult(UpdateCheck.Current, null, null, exception.Message);
                }
            });
        }

        private void ShowUpdateCheckResult(UpdateCheckResult result)
        {
            updateCheckRunning = false;

            checkForUpdatesItem.Enabled = true;
            checkForUpdatesItem.Text = "Check for updates...";

            if (result.Failed)
            {
                Log.Write("update check failed: " + result.Error);

                if (updateCheckIsManual)
                {
                    MessageBox.Show(
                        "Could not reach GitHub to check for updates:" + Environment.NewLine
                            + Environment.NewLine + result.Error,
                        "AntiDuneKeyboardDiddler",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return;
            }

            if (!result.UpdateAvailable)
            {
                Log.Write("update check: " + result.Current + " is the latest release.");

                if (updateCheckIsManual)
                {
                    MessageBox.Show(
                        "You are on the latest release (" + result.Current + ").",
                        "AntiDuneKeyboardDiddler",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            Log.Write("update available: " + result.Tag + ", running " + result.Current);

            if (updateCheckIsManual)
            {
                DialogResult answer = MessageBox.Show(
                    "You are running " + result.Current + ", and " + result.Tag + " is available."
                        + Environment.NewLine + Environment.NewLine
                        + "Open the download page?",
                    "AntiDuneKeyboardDiddler - update available",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (answer == DialogResult.Yes)
                {
                    OpenReleasesPage();
                }

                return;
            }

            balloonOpensReleases = true;

            notifyIcon.ShowBalloonTip(
                8000,
                "Update available",
                result.Tag + " is out and you are running " + result.Current + ". Click here to download it.",
                ToolTipIcon.Info);
        }

        /// <summary>
        /// Balloons are also used for layout corrections, which must not send anyone to a
        /// browser, so only the one raised by the update check arms this.
        /// </summary>
        private void OnBalloonTipClicked(object sender, EventArgs e)
        {
            if (!balloonOpensReleases)
            {
                return;
            }

            balloonOpensReleases = false;

            OpenReleasesPage();
        }

        private void OpenReleasesPage()
        {
            try
            {
                Process.Start(UpdateCheck.ReleasesPage);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "Could not open the download page:" + Environment.NewLine + exception.Message
                        + Environment.NewLine + Environment.NewLine + UpdateCheck.ReleasesPage,
                    "AntiDuneKeyboardDiddler",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void OnEnforceAlwaysChanged(object sender, EventArgs e)
        {
            options.EnforceAlways = enforceAlwaysItem.Checked;
            options.Save("EnforceAlways", options.EnforceAlways);

            Log.Write("guard even when the game is not running: " + options.EnforceAlways);

            timer.Interval = Math.Max(50, guard.NextIntervalMilliseconds);
        }

        private void OnStartWithWindowsChanged(object sender, EventArgs e)
        {
            if (!Startup.SetEnabled(startWithWindowsItem.Checked))
            {
                MessageBox.Show(
                    "Could not change the start-with-Windows setting. The log has the details.",
                    "AntiDuneKeyboardDiddler",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);

                startWithWindowsItem.CheckedChanged -= OnStartWithWindowsChanged;
                startWithWindowsItem.Checked = Startup.IsEnabled();
                startWithWindowsItem.CheckedChanged += OnStartWithWindowsChanged;
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (timer != null)
                {
                    timer.Stop();
                    timer.Dispose();
                }

                if (guard != null)
                {
                    guard.Stop();
                }

                if (notifyIcon != null)
                {
                    // Without this the icon lingers in the tray until the mouse passes over it.
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }

                if (idleIcon != null)
                {
                    idleIcon.Dispose();
                }

                if (armedIcon != null)
                {
                    armedIcon.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
