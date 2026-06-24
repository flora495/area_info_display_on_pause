using System;
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
            };
        }

        public void Save(string path)
        {
            new XDocument(new XElement("Settings",
                new XElement("DisplayMode", DisplayMode),
                new XElement("AttemptCounterEnabled", AttemptCounterEnabled),
                new XElement("PersonalBestEnabled", PersonalBestEnabled))).Save(path);
        }

        private static TotalDisplayMode ParseDisplayMode(string value)
        {
            if (value != null && Enum.TryParse(value, out TotalDisplayMode mode))
            {
                return mode;
            }
            return TotalDisplayMode.AfterClear;
        }
    }
}
