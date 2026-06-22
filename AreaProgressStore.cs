using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using JumpKing.MiscSystems.LocationText;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Per-area progress (first-visit order, attempt count, whether the area has been fully
    /// cleared at least once), keyed by level and by the area's start screen. Persisted to its
    /// own XML file kept in sync with the game's own save lifecycle via SaveLubePatches.
    /// </summary>
    internal static class AreaProgressStore
    {
        private sealed class AreaEntry
        {
            public int Order;
            public int AttemptCount;
            public bool HasFullyCleared;
        }

        private sealed class LevelEntry
        {
            public readonly Dictionary<int, AreaEntry> Areas = new Dictionary<int, AreaEntry>();
            public int NextOrder;
        }

        private static Dictionary<string, LevelEntry> s_levels = new Dictionary<string, LevelEntry>();
        private static bool s_dirty;

        public static void Load(string path)
        {
            s_levels = new Dictionary<string, LevelEntry>();
            s_dirty = false;
            if (!File.Exists(path))
            {
                return;
            }
            XElement root = XDocument.Load(path).Root;
            if (root == null)
            {
                return;
            }
            foreach (XElement levelElement in root.Elements("Level"))
            {
                string key = (string)levelElement.Attribute("key");
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }
                LevelEntry levelEntry = new LevelEntry
                {
                    NextOrder = (int?)levelElement.Attribute("nextOrder") ?? 0
                };
                foreach (XElement areaElement in levelElement.Elements("Area"))
                {
                    int start = (int?)areaElement.Attribute("start") ?? 0;
                    levelEntry.Areas[start] = new AreaEntry
                    {
                        Order = (int?)areaElement.Attribute("order") ?? 0,
                        AttemptCount = (int?)areaElement.Attribute("attempts") ?? 0,
                        HasFullyCleared = (bool?)areaElement.Attribute("cleared") ?? false,
                    };
                }
                s_levels[key] = levelEntry;
            }
        }

        public static void Save(string path)
        {
            if (!s_dirty)
            {
                return;
            }
            XElement root = new XElement("AreaProgress");
            foreach (KeyValuePair<string, LevelEntry> levelPair in s_levels)
            {
                XElement levelElement = new XElement("Level",
                    new XAttribute("key", levelPair.Key),
                    new XAttribute("nextOrder", levelPair.Value.NextOrder));
                foreach (KeyValuePair<int, AreaEntry> areaPair in levelPair.Value.Areas)
                {
                    levelElement.Add(new XElement("Area",
                        new XAttribute("start", areaPair.Key),
                        new XAttribute("order", areaPair.Value.Order),
                        new XAttribute("attempts", areaPair.Value.AttemptCount),
                        new XAttribute("cleared", areaPair.Value.HasFullyCleared)));
                }
                root.Add(levelElement);
            }
            new XDocument(root).Save(path);
            s_dirty = false;
        }

        public static void Clear()
        {
            s_levels = new Dictionary<string, LevelEntry>();
            s_dirty = true;
        }

        /// <summary>
        /// Called whenever the resolved area changes during play. Registers brand-new areas, or
        /// bumps the attempt count when re-entering an already-known area from one with an
        /// earlier first-visit order (i.e. climbing back up after falling down). Also doubles as
        /// the level-start "quiet" resolve when called with previousStart: null, since a null
        /// previousStart never triggers the attempt-count bump on an already-known area.
        ///
        /// The very first area has no earlier-order area below it to climb up from, so under
        /// the order-comparison rule its attempt count would only ever be set once and never
        /// bumped again. isFirstArea carves out that one area: falling all the way back down to
        /// it (from anywhere) counts as a new attempt too, since that's what "starting over"
        /// looks like for it.
        /// </summary>
        public static void OnEnterArea(string levelKey, int newStart, int? previousStart, bool isFirstArea)
        {
            LevelEntry levelEntry = GetOrCreateLevel(levelKey);
            if (!levelEntry.Areas.TryGetValue(newStart, out AreaEntry newEntry))
            {
                levelEntry.Areas[newStart] = new AreaEntry { Order = levelEntry.NextOrder++, AttemptCount = 1, HasFullyCleared = false };
                s_dirty = true;
                return;
            }
            if (!previousStart.HasValue)
            {
                return;
            }
            if (isFirstArea)
            {
                newEntry.AttemptCount++;
                s_dirty = true;
                return;
            }
            if (levelEntry.Areas.TryGetValue(previousStart.Value, out AreaEntry previousEntry)
                && previousEntry.Order < newEntry.Order)
            {
                newEntry.AttemptCount++;
                s_dirty = true;
            }
        }

        public static void MarkCleared(string levelKey, int start)
        {
            LevelEntry levelEntry = GetOrCreateLevel(levelKey);
            if (!levelEntry.Areas.TryGetValue(start, out AreaEntry entry))
            {
                return;
            }
            if (!entry.HasFullyCleared)
            {
                entry.HasFullyCleared = true;
                s_dirty = true;
            }
        }

        /// <summary>
        /// Catches up areas that were already passed before this mod's tracking started for
        /// this level (e.g. the mod was just installed mid-playthrough, or this is a resumed
        /// save and the level-start quiet resolve only registers the current area).
        /// </summary>
        public static void RestoreClearedOnResume(string levelKey, Location[] sortedLocations, int screenIndex1)
        {
            LevelEntry levelEntry = GetOrCreateLevel(levelKey);
            foreach (Location location in sortedLocations)
            {
                if (location.end < screenIndex1
                    && levelEntry.Areas.TryGetValue(location.start, out AreaEntry entry)
                    && !entry.HasFullyCleared)
                {
                    entry.HasFullyCleared = true;
                    s_dirty = true;
                }
            }
        }

        public static int GetAttemptCount(string levelKey, int start)
        {
            if (s_levels.TryGetValue(levelKey, out LevelEntry levelEntry)
                && levelEntry.Areas.TryGetValue(start, out AreaEntry entry))
            {
                return entry.AttemptCount;
            }
            return 0;
        }

        public static bool IsCleared(string levelKey, int start)
        {
            return s_levels.TryGetValue(levelKey, out LevelEntry levelEntry)
                && levelEntry.Areas.TryGetValue(start, out AreaEntry entry)
                && entry.HasFullyCleared;
        }

        public readonly struct AreaSummary
        {
            public AreaSummary(int start, int order, int attemptCount)
            {
                Start = start;
                Order = order;
                AttemptCount = attemptCount;
            }

            public int Start { get; }
            public int Order { get; }
            public int AttemptCount { get; }
        }

        /// <summary>
        /// Every area in levelKey that's been reached at least once, in first-visit order.
        /// </summary>
        public static List<AreaSummary> GetVisitedAreas(string levelKey)
        {
            var result = new List<AreaSummary>();
            if (s_levels.TryGetValue(levelKey, out LevelEntry levelEntry))
            {
                foreach (KeyValuePair<int, AreaEntry> pair in levelEntry.Areas)
                {
                    result.Add(new AreaSummary(pair.Key, pair.Value.Order, pair.Value.AttemptCount));
                }
            }
            result.Sort((a, b) => a.Order.CompareTo(b.Order));
            return result;
        }

        private static LevelEntry GetOrCreateLevel(string levelKey)
        {
            if (!s_levels.TryGetValue(levelKey, out LevelEntry levelEntry))
            {
                levelEntry = new LevelEntry();
                s_levels[levelKey] = levelEntry;
            }
            return levelEntry;
        }
    }
}
