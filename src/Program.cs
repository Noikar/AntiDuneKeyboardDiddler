using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AntiDuneKeyboardDiddler
{
    internal static class Program
    {
        private const string InstanceMutexName = @"Local\AntiDuneKeyboardDiddler.SingleInstance";

        [STAThread]
        private static int Main(string[] args)
        {
            string exeDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            Options options = Options.Load(Path.Combine(exeDirectory, "AntiDuneKeyboardDiddler.ini"));

            bool status = false;
            bool cleanup = false;

            foreach (string arg in args)
            {
                switch (arg.ToLowerInvariant().TrimStart('-', '/'))
                {
                    case "status":
                        status = true;
                        break;

                    case "cleanup":
                        cleanup = true;
                        break;

                    case "enforce-always":
                    case "enforcealways":
                        options.EnforceAlways = true;
                        break;

                    case "verbose":
                        options.Verbose = true;
                        break;

                    case "help":
                    case "?":
                        ShowUsage();
                        return 0;

                    default:
                        ShowMessage("Unknown argument: " + arg);
                        return 2;
                }
            }

            Log.Initialize(Path.Combine(exeDirectory, "AntiDuneKeyboardDiddler.log"));

            if (status)
            {
                AttachToParentConsole();
                Console.WriteLine(new Guard(options).BuildStatusText());

                return 0;
            }

            if (cleanup)
            {
                AttachToParentConsole();

                return RunCleanup(options);
            }

            // One tray icon is the point; a second copy would fight the first over the same
            // layouts. Started again by hand, the newcomer simply steps aside.
            bool isFirstInstance;

            using (new Mutex(true, InstanceMutexName, out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    ShowMessage("AntiDuneKeyboardDiddler is already running - look for it in the "
                        + "notification area, next to the clock.");

                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplication(options));
            }

            return 0;
        }

        /// <summary>
        /// Runs a cleanup and keeps polling until the desktop has settled back onto the right
        /// layout, since this process is about to exit and nothing else will do it.
        /// </summary>
        private static int RunCleanup(Options options)
        {
            var guard = new Guard(options);

            guard.Notified += (sender, e) => Console.WriteLine(e.Message);

            int removed = guard.CleanupOnce();

            if (removed <= 0)
            {
                return removed < 0 ? 1 : 0;
            }

            while (guard.IsSettling)
            {
                guard.Tick();
                Thread.Sleep(options.ArmedPollMilliseconds);
            }

            return 0;
        }

        /// <summary>
        /// This is a windows-subsystem application, so it has no console of its own. When it is
        /// started from a terminal for --status or --cleanup, borrow that terminal's console so
        /// the output has somewhere to go; otherwise fall back to a message box.
        /// </summary>
        private static bool consoleAttached;

        private static bool AttachToParentConsole()
        {
            if (consoleAttached)
            {
                return true;
            }

            if (!NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS))
            {
                return false;
            }

            consoleAttached = true;

            try
            {
                var standardOutput = new StreamWriter(Console.OpenStandardOutput());

                standardOutput.AutoFlush = true;

                Console.SetOut(standardOutput);
            }
            catch (IOException)
            {
            }

            return true;
        }

        private static void ShowUsage()
        {
            ShowMessage(
                "AntiDuneKeyboardDiddler " + UpdateCheck.Current
                + " - keeps a game from hijacking your keyboard layout."
                + Environment.NewLine + Environment.NewLine
                + "  (no arguments)     Sit in the notification area and guard while the game runs."
                + Environment.NewLine
                + "  --status           Show configured vs. loaded layouts, then exit."
                + Environment.NewLine
                + "  --cleanup          Remove stray layouts once, then exit."
                + Environment.NewLine
                + "  --enforce-always   Guard continuously, not just while the game runs."
                + Environment.NewLine
                + "  --verbose          Log every individual correction."
                + Environment.NewLine + Environment.NewLine
                + "Right-click the tray icon for the log, the current status, and the "
                + "start-with-Windows switch."
                + Environment.NewLine
                + "Settings live in AntiDuneKeyboardDiddler.ini next to the executable.");
        }

        /// <summary>
        /// Prints to the parent console when there is one, and otherwise shows a dialog, so
        /// that double-clicking the executable never fails silently.
        /// </summary>
        private static void ShowMessage(string message)
        {
            if (AttachToParentConsole())
            {
                Console.WriteLine(message);

                return;
            }

            MessageBox.Show(message, "AntiDuneKeyboardDiddler", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
