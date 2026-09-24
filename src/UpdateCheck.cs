using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace AntiDuneKeyboardDiddler
{
    /// <summary>
    /// Asks GitHub whether there is a newer release than the running build. It downloads
    /// nothing and changes nothing; the most it ever does is report, and the user goes to the
    /// releases page themselves.
    /// </summary>
    internal static class UpdateCheck
    {
        private const string Repository = "Noikar/AntiDuneKeyboardDiddler";

        private const string LatestReleaseApi =
            "https://api.github.com/repos/" + Repository + "/releases/latest";

        /// <summary>Where the user is sent when there is something to download.</summary>
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases/latest";

        /// <summary>
        /// The running build's version, trimmed to three fields so that the assembly's
        /// 1.0.2.0 and the tag v1.0.2 compare equal rather than the tag looking older.
        /// </summary>
        public static Version Current
        {
            get
            {
                Version version = Assembly.GetExecutingAssembly().GetName().Version;

                return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
            }
        }

        /// <summary>
        /// Runs the check. Blocking, so call it from a background thread. It never throws:
        /// a failed check is an ordinary result with <see cref="UpdateCheckResult.Error"/> set,
        /// because being offline is not a problem worth an exception.
        /// </summary>
        public static UpdateCheckResult Run()
        {
            try
            {
                // 3072 is Tls12, spelled numerically because the enum member is absent on the
                // older frameworks this still compiles against. Or-ed in rather than assigned,
                // so nothing else the process configured is thrown away.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;

                var request = (HttpWebRequest)WebRequest.Create(LatestReleaseApi);

                // GitHub rejects requests without a user agent outright.
                request.UserAgent = "AntiDuneKeyboardDiddler/" + Current;
                request.Accept = "application/vnd.github+json";
                request.Timeout = 8000;
                request.ReadWriteTimeout = 8000;

                string body;

                using (WebResponse response = request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream))
                {
                    body = reader.ReadToEnd();
                }

                // One field out of the response is all this needs, and a regex keeps the whole
                // feature to two files with no serializer reference. Tag names cannot contain a
                // quote, so there is nothing here for an escape sequence to hide in.
                Match match = Regex.Match(body, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");

                if (!match.Success)
                {
                    return new UpdateCheckResult(Current, null, null, "GitHub returned no release tag.");
                }

                string tag = match.Groups[1].Value;
                Version latest = ParseTag(tag);

                if (latest == null)
                {
                    return new UpdateCheckResult(
                        Current, null, tag, "Could not read a version number out of the tag \"" + tag + "\".");
                }

                return new UpdateCheckResult(Current, latest, tag, null);
            }
            catch (WebException exception)
            {
                return new UpdateCheckResult(Current, null, null, exception.Message);
            }
            catch (IOException exception)
            {
                return new UpdateCheckResult(Current, null, null, exception.Message);
            }
            catch (NotSupportedException exception)
            {
                return new UpdateCheckResult(Current, null, null, exception.Message);
            }
        }

        /// <summary>
        /// "v1.0.2" and "1.0.2-beta1" both come out as 1.0.2. Returns null when the tag does
        /// not start with a version number at all, which is not worth guessing about.
        /// </summary>
        private static Version ParseTag(string tag)
        {
            string text = tag.Trim();

            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(1);
            }

            int suffix = text.IndexOfAny(new[] { '-', '+', ' ' });

            if (suffix >= 0)
            {
                text = text.Substring(0, suffix);
            }

            Version version;

            if (!Version.TryParse(text, out version))
            {
                return null;
            }

            // A two-field tag parses with Build = -1, which the Version constructor rejects.
            return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
        }
    }

    /// <summary>
    /// The outcome of one check. Built on a background thread and read from the UI timer, so
    /// it carries no mutable state.
    /// </summary>
    internal sealed class UpdateCheckResult
    {
        public UpdateCheckResult(Version current, Version latest, string tag, string error)
        {
            Current = current;
            Latest = latest;
            Tag = tag;
            Error = error;
        }

        public Version Current { get; private set; }

        /// <summary>The newest released version, or null when the check did not get that far.</summary>
        public Version Latest { get; private set; }

        /// <summary>The tag as GitHub spells it, for showing the user something they will recognize.</summary>
        public string Tag { get; private set; }

        /// <summary>Null when the check succeeded.</summary>
        public string Error { get; private set; }

        public bool Failed
        {
            get { return Error != null; }
        }

        public bool UpdateAvailable
        {
            get { return Latest != null && Latest > Current; }
        }
    }
}
