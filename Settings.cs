using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace AreaInfoDisplayOnPause
{
    public enum TotalDisplayMode
    {
        Always,
        Never,
        AfterClear
    }

    public sealed class Settings
    {
        public TotalDisplayMode DisplayMode { get; set; } = TotalDisplayMode.AfterClear;
        public bool AttemptCounterEnabled { get; set; } = true;
        public bool PersonalBestEnabled { get; set; } = true;

        /// <summary>
        /// Keyboard key (Microsoft.Xna.Framework.Input.Keys value) that toggles the pause-info box
        /// between the normal display and Progression Detail. Empty while unbound (the default).
        /// Always replaced with a new array, never mutated in place - DetailShortcutButton spots
        /// a rebind/reset by reference comparison.
        /// </summary>
        public int[] KeyboardDetailShortcut { get; set; } = new int[0];

        /// <summary>Same as KeyboardDetailShortcut, for controllers (device-specific button number).</summary>
        public int[] PadDetailShortcut { get; set; } = new int[0];

        public static Settings Load(string path)
        {
            if (!File.Exists(path))
            {
                return new Settings();
            }
            XElement root = XDocument.Load(path).Root;
            return new Settings
            {
                DisplayMode = ParseDisplayMode((string)root.Element("DisplayMode")),
                AttemptCounterEnabled = (bool?)root.Element("AttemptCounterEnabled") ?? true,
                PersonalBestEnabled = (bool?)root.Element("PersonalBestEnabled") ?? true,
                KeyboardDetailShortcut = ParseKeys((string)root.Element("KeyboardDetailShortcut")),
                PadDetailShortcut = ParseKeys((string)root.Element("PadDetailShortcut")),
            };
        }

        public void Save(string path)
        {
            new XDocument(new XElement("Settings",
                new XElement("DisplayMode", DisplayMode),
                new XElement("AttemptCounterEnabled", AttemptCounterEnabled),
                new XElement("PersonalBestEnabled", PersonalBestEnabled),
                new XElement("KeyboardDetailShortcut", string.Join(",", KeyboardDetailShortcut)),
                new XElement("PadDetailShortcut", string.Join(",", PadDetailShortcut)))).Save(path);
        }

        /// <summary>
        /// Puts every setting back to its default. Doesn't save - the caller does that through
        /// ModEntry.SaveSettings, since only ModEntry knows the settings file's path.
        /// </summary>
        public void ResetToDefault()
        {
            DisplayMode = TotalDisplayMode.AfterClear;
            AttemptCounterEnabled = true;
            PersonalBestEnabled = true;
            KeyboardDetailShortcut = new int[0];
            PadDetailShortcut = new int[0];
        }

        private static TotalDisplayMode ParseDisplayMode(string value)
        {
            if (value != null && Enum.TryParse(value, out TotalDisplayMode mode))
            {
                return mode;
            }
            return TotalDisplayMode.AfterClear;
        }

        /// <summary>
        /// Comma-separated numbers, same format as hide_pause_window's key settings. Anything
        /// unreadable is skipped rather than thrown on, so a missing or hand-broken entry just
        /// reads as unbound.
        /// </summary>
        private static int[] ParseKeys(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return new int[0];
            }
            var keys = new List<int>();
            foreach (string part in value.Split(','))
            {
                if (int.TryParse(part.Trim(), out int key))
                {
                    keys.Add(key);
                }
            }
            return keys.ToArray();
        }
    }
}
