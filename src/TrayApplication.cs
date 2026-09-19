using System;
using System.Diagnostics;
using System.Drawing;
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
        private readonly ToolStripMenuItem enforceAlwaysItem;
        private readonly ToolStripMenuItem startWithWindowsItem;

        public TrayApplication(Options options)
        {
            this.options = options;

            guard = new Guard(options);
            guard.StateChanged += OnGuardStateChanged;
            guard.Notified += OnGuardNotified;

            idleIcon = TrayIcons.Idle();
            armedIcon = TrayIcons.Armed();

            statusItem = new ToolStripMenuItem("Starting...") { Enabled = false };

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

            var menu = new ContextMenuStrip();

            menu.Items.Add(statusItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Open log", null, OnOpenLog);
            menu.Items.Add("Show status...", null, OnShowStatus);
            menu.Items.Add("Remove stray layouts now", null, OnCleanupNow);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(enforceAlwaysItem);
            menu.Items.Add(startWithWindowsItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, OnExit);

            notifyIcon = new NotifyIcon
            {
                Icon = idleIcon,
                Text = "AntiDuneKeyboardDiddler",
                ContextMenuStrip = menu,
                Visible = true
            };

            notifyIcon.DoubleClick += OnShowStatus;

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
