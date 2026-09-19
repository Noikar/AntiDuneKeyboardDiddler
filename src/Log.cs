using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace AntiDuneKeyboardDiddler
{
    internal static class Log
    {
        private static readonly object gate = new object();

        /// <summary>
        /// Where the log actually ended up. Null only if nowhere was writable.
        /// </summary>
        public static string Path { get; private set; }

        /// <summary>
        /// Writes to <paramref name="preferredPath"/> if that directory is writable, otherwise
        /// falls back to LocalAppData. The fallback matters once the tool starts with Windows:
        /// it may well be sitting somewhere the user cannot write to, and the tray menu still
        /// has to be able to open a log.
        /// </summary>
        public static void Initialize(string preferredPath)
        {
            Path = TryUse(preferredPath) ?? TryUse(System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AntiDuneKeyboardDiddler",
                "AntiDuneKeyboardDiddler.log"));
        }

        private static string TryUse(string candidate)
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(candidate);

                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Keep the log from growing without bound across many sessions.
                if (File.Exists(candidate) && new FileInfo(candidate).Length > 512 * 1024)
                {
                    File.Copy(candidate, candidate + ".old", true);
                    File.Delete(candidate);
                }

                File.AppendAllText(candidate, string.Empty, Encoding.UTF8);

                return candidate;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public static void Write(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                + "  " + message;

            lock (gate)
            {
                Console.WriteLine(line);

                if (Path == null)
                {
                    return;
                }

                try
                {
                    File.AppendAllText(Path, line + Environment.NewLine, Encoding.UTF8);
                }
                catch (IOException)
                {
                    // A locked log file is not worth crashing the guard over.
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
