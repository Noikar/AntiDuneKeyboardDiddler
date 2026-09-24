using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace AntiDuneKeyboardDiddler
{
    /// <summary>
    /// Works out which input locales the user has actually configured, by reading the
    /// same registry data that the Windows language settings write.
    /// </summary>
    internal static class LayoutRegistry
    {
        private const string PreloadKey = @"Keyboard Layout\Preload";
        private const string SubstitutesKey = @"Keyboard Layout\Substitutes";
        private const string LayoutsKey = @"SYSTEM\CurrentControlSet\Control\Keyboard Layouts";

        /// <summary>
        /// Reads HKCU\Keyboard Layout\Preload as an ordered slot to KLID map ("1" =&gt; "00000809").
        /// </summary>
        public static SortedDictionary<string, string> ReadPreload()
        {
            var preload = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PreloadKey))
            {
                if (key == null)
                {
                    return preload;
                }

                foreach (string name in key.GetValueNames())
                {
                    preload[name] = Convert.ToString(key.GetValue(name));
                }
            }

            return preload;
        }

        public static void WritePreload(SortedDictionary<string, string> preload)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(PreloadKey))
            {
                if (key == null)
                {
                    return;
                }

                // Drop slots that are not in the snapshot, then rewrite the ones that are.
                foreach (string name in key.GetValueNames())
                {
                    if (!preload.ContainsKey(name))
                    {
                        key.DeleteValue(name, false);
                    }
                }

                foreach (var entry in preload)
                {
                    key.SetValue(entry.Key, entry.Value, RegistryValueKind.String);
                }
            }
        }

        private static Dictionary<string, string> ReadSubstitutes()
        {
            var substitutes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SubstitutesKey))
            {
                if (key == null)
                {
                    return substitutes;
                }

                foreach (string name in key.GetValueNames())
                {
                    substitutes[name] = Convert.ToString(key.GetValue(name));
                }
            }

            return substitutes;
        }

        /// <summary>
        /// Derives the input locale identifier (HKL) that a Preload entry gets when Windows
        /// loads it.
        ///
        /// The low word is the language id of the entry as it appears in Preload, and survives
        /// substitution. The high word comes from the substituted layout: 0xF000 or-ed with its
        /// "Layout Id" when it has one, otherwise its own language id.
        ///
        ///   00000409 -> 00020409 (Layout Id 0001)  =&gt;  F001:0409  United States-International
        ///   00000809 -> 0000041d (no Layout Id)    =&gt;  041D:0809  Swedish, listed under en-GB
        ///   00000409 -> no substitute              =&gt;  0409:0409  plain US, what the game loads
        ///
        /// Getting this wrong means mistaking one of the user's own layouts for an intruder,
        /// so it is worth the extra argument.
        /// </summary>
        public static uint KlidToHkl(string preloadKlid, string effectiveKlid)
        {
            uint languageId =
                uint.Parse(preloadKlid, NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 0xFFFF;

            uint high =
                uint.Parse(effectiveKlid, NumberStyles.HexNumber, CultureInfo.InvariantCulture) & 0xFFFF;

            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(LayoutsKey + "\\" + effectiveKlid))
            {
                string layoutId = key == null ? null : key.GetValue("Layout Id") as string;

                if (!string.IsNullOrEmpty(layoutId))
                {
                    high = 0xF000u | uint.Parse(layoutId, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }
            }

            return (high << 16) | languageId;
        }

        public static string DescribeKlid(string klid)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(LayoutsKey + "\\" + klid))
            {
                string text = key == null ? null : key.GetValue("Layout Text") as string;

                return string.IsNullOrEmpty(text) ? klid : text;
            }
        }

        /// <summary>
        /// Turns a PreferredLayout setting into one of the user's configured HKLs, or 0 when it
        /// names nothing recognizable. Three spellings are accepted, because three different
        /// places show the user three different things:
        ///
        ///   F0010409                    the HKL, as the log and the status window print it
        ///   00000409                    the KLID, as it appears in the registry
        ///   United States-International the layout name, as Windows shows it
        ///
        /// The KLID form works because the low word of an HKL is the language id of the Preload
        /// entry and survives substitution - see <see cref="KlidToHkl"/>. It is only honored
        /// when exactly one configured layout has that language id; two layouts for the same
        /// language (US and Dvorak, say) make the setting ambiguous, and holding the wrong one
        /// all session is worse than falling back to detection.
        /// </summary>
        public static uint ResolvePreferred(string setting, Dictionary<uint, string> expected)
        {
            if (string.IsNullOrEmpty(setting) || expected == null)
            {
                return 0;
            }

            string text = setting.Trim();

            if (text.Length == 0)
            {
                return 0;
            }

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(2);
            }

            uint value;

            if (uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
            {
                if (expected.ContainsKey(value))
                {
                    return value;
                }

                var sameLanguage = new List<uint>();

                foreach (uint hkl in expected.Keys)
                {
                    if ((hkl & 0xFFFF) == (value & 0xFFFF))
                    {
                        sameLanguage.Add(hkl);
                    }
                }

                if (sameLanguage.Count == 1)
                {
                    return sameLanguage[0];
                }
            }

            // Names second, and exact before partial, so that "Swedish" cannot be swallowed by
            // a longer layout that merely contains it.
            foreach (var entry in expected)
            {
                if (string.Equals(entry.Value, text, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.Key;
                }
            }

            uint partial = 0;
            int matches = 0;

            foreach (var entry in expected)
            {
                if (entry.Value != null
                    && entry.Value.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    partial = entry.Key;
                    matches++;
                }
            }

            return matches == 1 ? partial : 0;
        }

        /// <summary>
        /// The set of HKLs the user has deliberately configured. Anything else that turns up
        /// loaded was added by something other than the user.
        /// </summary>
        public static Dictionary<uint, string> GetExpectedLayouts()
        {
            var expected = new Dictionary<uint, string>();
            Dictionary<string, string> substitutes = ReadSubstitutes();

            foreach (var entry in ReadPreload())
            {
                string preloadKlid = entry.Value;

                if (string.IsNullOrEmpty(preloadKlid))
                {
                    continue;
                }

                string effectiveKlid = preloadKlid;
                string substitute;

                if (substitutes.TryGetValue(preloadKlid, out substitute) && !string.IsNullOrEmpty(substitute))
                {
                    effectiveKlid = substitute;
                }

                // Input methods (IMEs) start with E0 and do not follow the layout-id rule.
                // Leave them be rather than guessing wrong and calling one an intruder.
                if (preloadKlid.StartsWith("E0", StringComparison.OrdinalIgnoreCase)
                    || effectiveKlid.StartsWith("E0", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    uint hkl = KlidToHkl(preloadKlid, effectiveKlid);

                    expected[hkl] = DescribeKlid(effectiveKlid);
                }
                catch (FormatException)
                {
                    // Malformed KLID in the registry; skip it rather than fail the whole scan.
                }
            }

            return expected;
        }
    }
}
