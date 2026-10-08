using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// History of cleared maps: one record per clear, each holding a copy of that level's
    /// AreaProgressStore data as it was at the moment the ending started. Kept in its own file,
    /// separate from the in-progress AreaProgress file - that one is wiped on clear (the game's
    /// own clear deletes its saves, and SaveLubePatches follows along), this one never is.
    ///
    /// Only ever touched on a clear (Append, via GameCompletePatches) and when the title screen's
    /// mod settings menu is built (Load, via ClearedMapsMenu) - nothing here runs during play.
    /// Both happen on the main thread, so unlike AreaProgressStore this needs no locking.
    /// </summary>
    internal static class ClearedMapHistoryStore
    {
        private const string HistoryFileName = "F.AreaInfoDisplayOnPause.ClearedHistory.xml";

        private static readonly string s_historyPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), HistoryFileName);

        public sealed class ClearedArea
        {
            public int Start;
            public int Order;
            public int AttemptCount;
            public bool HasFullyCleared;
            public TimeSpan LapTime;
            public int BestScreenIndex;

            /// <summary>
            /// Display name captured at clear time - the title screen has no Location data
            /// loaded to resolve Start back into a name, and the level may not even be
            /// subscribed anymore by the time this is viewed.
            /// </summary>
            public string Name;
        }

        public sealed class ClearedRecord
        {
            public string LevelKey;
            public string LevelName;
            public DateTime ClearedAt;

            /// <summary>
            /// The run's final play time, as the vanilla post-ending stats screen showed it. Null
            /// for records written before this was tracked.
            /// </summary>
            public TimeSpan? ClearTime;

            /// <summary>In first-visit order.</summary>
            public List<ClearedArea> Areas = new List<ClearedArea>();
        }

        /// <summary>Every record, most recent clear first.</summary>
        public static List<ClearedRecord> Load()
        {
            var result = new List<ClearedRecord>();
            if (!File.Exists(s_historyPath))
            {
                return result;
            }
            XElement root = XDocument.Load(s_historyPath).Root;
            if (root == null)
            {
                return result;
            }
            foreach (XElement clearElement in root.Elements("Clear"))
            {
                var record = new ClearedRecord
                {
                    LevelKey = (string)clearElement.Attribute("key") ?? string.Empty,
                    LevelName = (string)clearElement.Attribute("name") ?? string.Empty,
                    ClearedAt = ParseDate((string)clearElement.Attribute("clearedAt")),
                };
                long? clearTicks = (long?)clearElement.Attribute("clearTicks");
                if (clearTicks.HasValue)
                {
                    record.ClearTime = TimeSpan.FromTicks(clearTicks.Value);
                }
                foreach (XElement areaElement in clearElement.Elements("Area"))
                {
                    record.Areas.Add(new ClearedArea
                    {
                        Start = (int?)areaElement.Attribute("start") ?? 0,
                        Order = (int?)areaElement.Attribute("order") ?? 0,
                        AttemptCount = (int?)areaElement.Attribute("attempts") ?? 0,
                        HasFullyCleared = (bool?)areaElement.Attribute("cleared") ?? false,
                        LapTime = TimeSpan.FromTicks((long?)areaElement.Attribute("lapTicks") ?? 0),
                        BestScreenIndex = (int?)areaElement.Attribute("bestScreenIndex") ?? 0,
                        Name = (string)areaElement.Attribute("name"),
                    });
                }
                result.Add(record);
            }
            result.Sort((a, b) => b.ClearedAt.CompareTo(a.ClearedAt));
            return result;
        }

        /// <summary>
        /// Adds record as the newest entry and writes the result straight to disk. There's no
        /// limit on how many are kept (ClearedMapsMenu pages through them instead).
        /// </summary>
        public static void Append(ClearedRecord record)
        {
            List<ClearedRecord> records = Load();
            records.Insert(0, record);

            var root = new XElement("ClearedHistory");
            foreach (ClearedRecord r in records)
            {
                XElement clearElement = new XElement("Clear",
                    new XAttribute("key", r.LevelKey),
                    new XAttribute("name", r.LevelName),
                    new XAttribute("clearedAt", r.ClearedAt.ToString("o", CultureInfo.InvariantCulture)));
                if (r.ClearTime.HasValue)
                {
                    clearElement.Add(new XAttribute("clearTicks", r.ClearTime.Value.Ticks));
                }
                foreach (ClearedArea area in r.Areas)
                {
                    XElement areaElement = new XElement("Area",
                        new XAttribute("start", area.Start),
                        new XAttribute("order", area.Order),
                        new XAttribute("attempts", area.AttemptCount),
                        new XAttribute("cleared", area.HasFullyCleared),
                        new XAttribute("lapTicks", area.LapTime.Ticks),
                        new XAttribute("bestScreenIndex", area.BestScreenIndex));
                    if (area.Name != null)
                    {
                        areaElement.Add(new XAttribute("name", area.Name));
                    }
                    clearElement.Add(areaElement);
                }
                root.Add(clearElement);
            }
            new XDocument(root).Save(s_historyPath);
        }

        private static DateTime ParseDate(string value)
        {
            if (value != null && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime date))
            {
                return date;
            }
            return DateTime.MinValue;
        }
    }
}
