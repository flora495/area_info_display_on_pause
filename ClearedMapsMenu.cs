using System;
using System.Collections.Generic;
using CultureInfo = System.Globalization.CultureInfo;
using JumpKing.PauseMenu;
using JumpKing.PauseMenu.BT;
using LanguageJK;
using Microsoft.Xna.Framework;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// The title-screen-only "Cleared Maps" button (registered via [MainMenuItemSetting], which
    /// the pause menu's own mod settings never list): a list of ClearedMapHistoryStore's records,
    /// each opening a Progression-Detail-style breakdown of that clear.
    ///
    /// Built once each time the title screen's menus are, which is also the only time the
    /// history file is read. The engine's MenuFactory.TryCreateModSetting registers every nested
    /// MenuSelector reachable through TextButtons for drawing, so plain nesting is all it takes.
    /// </summary>
    internal static class ClearedMapsMenu
    {
        private const string ButtonText = "Cleared Maps";
        private const string NoClearsText = "No cleared maps yet";

        /// <summary>
        /// Shown instead of the live display's "pb: ..." line: the map was beaten, so pointing at
        /// whichever area happened to be the PB at that moment isn't meaningful anymore.
        /// </summary>
        private const string BeatenPbText = "pb: Beaten";

        private const string ClearTimeLabel = "clear: ";

        /// <summary>Longer level names are cut short so the list frame stays within the screen.</summary>
        private const int MaxLevelNameLength = 24;

        /// <summary>
        /// Records per list page. The history has no size limit, so it's split into pages the
        /// same way the More Saves mod's own "Load ... Save" lists are: each page's "Next" opens
        /// the following page nested under it, and "Previous" (or cancel) simply backs out of it.
        /// </summary>
        private const int PageSize = 8;

        /// <summary>
        /// A MenuSelector that stops drawing itself while one of its own items has a submenu open
        /// (the next list page, or a record's detail). Every screen here is centered, so without
        /// this the page underneath would show around the edges of whatever opened on top of it.
        /// </summary>
        private sealed class ParentHidingMenuSelector : MenuSelector
        {
            public ParentHidingMenuSelector(GuiFormat format)
                : base(format)
            {
            }

            public override void Draw()
            {
                if (m_last_child_result != BehaviorTree.BTresult.Running)
                {
                    base.Draw();
                }
            }
        }

        // Same values as the engine's own (internal) PauseManager.GUI_FORMAT, but centered on
        // screen: the mod settings popup these open from sits low and to the right on the title
        // screen, which leaves too little room for a full list page or area breakdown.
        private static GuiFormat CreateCenteredFormat()
        {
            return new GuiFormat
            {
                anchor_bounds = new Rectangle(0, 0, 480, 360),
                anchor = new Vector2(0.5f, 0.5f),
                all_margin = 16,
                element_margin = 8,
                all_padding = 16,
            };
        }

        public static TextButton Create()
        {
            List<ClearedMapHistoryStore.ClearedRecord> records = ClearedMapHistoryStore.Load();
            return new TextButton(ButtonText, CreateListPage(records, 0));
        }

        private static MenuSelector CreateListPage(List<ClearedMapHistoryStore.ClearedRecord> records, int page)
        {
            GuiFormat listFormat = CreateCenteredFormat();
            listFormat.element_margin = 4;
            MenuSelector list = new ParentHidingMenuSelector(listFormat);

            if (records.Count == 0)
            {
                list.AddChild(new TextInfo(NoClearsText, Color.Gray));
                list.Initialize();
                return list;
            }

            int pageCount = (records.Count + PageSize - 1) / PageSize;
            if (pageCount > 1)
            {
                list.AddChild(new TextInfo($"{ButtonText} {page + 1}/{pageCount}", Color.Gray));
            }
            int end = Math.Min(records.Count, (page + 1) * PageSize);
            for (int i = page * PageSize; i < end; i++)
            {
                list.AddChild(new TextButton(GetListLabel(records[i]), CreateDetail(records[i])));
            }
            if (page > 0)
            {
                list.AddChild(new TextButton(language.PAGINATION_PREVIOUS, new MenuSelectorBack(list)));
            }
            if (end < records.Count)
            {
                list.AddChild(new TextButton(language.PAGINATION_NEXT, CreateListPage(records, page + 1)));
            }
            list.Initialize();
            return list;
        }

        private static string GetListLabel(ClearedMapHistoryStore.ClearedRecord record)
        {
            string name = record.LevelName;
            if (name.Length > MaxLevelNameLength)
            {
                name = name.Substring(0, MaxLevelNameLength - 3) + "...";
            }
            return $"{name} {record.ClearedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}";
        }

        private static MenuSelector CreateDetail(ClearedMapHistoryStore.ClearedRecord record)
        {
            // Matches MenuFactoryPatches.CreatePauseInfoPrefix's own frame, so the breakdown is
            // laid out the same way it was in the pause screen.
            GuiFormat detailFormat = CreateCenteredFormat();
            detailFormat.all_margin /= 2;
            detailFormat.all_padding = detailFormat.all_padding / 2 + 1;
            detailFormat.element_margin = 2;

            string text = GetDetailText(record);
            MenuSelector detail = new MenuSelector(detailFormat);
            detail.AddChild(new AreaInfoTextInfo(() => text));
            detail.Initialize();
            return detail;
        }

        /// <summary>
        /// Same layout as AreaTracker's live Progression Detail text, minus its "current: ..."
        /// line (there's no current position for a finished run), with BeatenPbText in place of
        /// the PB, and the run's clear time right under it.
        /// </summary>
        private static string GetDetailText(ClearedMapHistoryStore.ClearedRecord record)
        {
            string text = BeatenPbText;
            if (record.ClearTime.HasValue)
            {
                text += "\n" + ClearTimeLabel + FormatClearTime(record.ClearTime.Value);
            }
            List<ClearedMapHistoryStore.ClearedArea> areas = record.Areas;
            areas.Sort((a, b) => a.Order.CompareTo(b.Order));
            for (int i = areas.Count - 1; i >= 0; i--)
            {
                ClearedMapHistoryStore.ClearedArea area = areas[i];
                string name = area.Name ?? area.Start.ToString();
                text += "\n" + AreaTracker.FormatProgressionDetailLine(name, area.AttemptCount, area.LapTime);
            }
            return text;
        }

        /// <summary>
        /// Mirrors the vanilla post-ending stats screen's TimeInfo.CreateLabelWithMs(stats, 15)
        /// (StatsScreen.MakeLines), so this reads exactly as it did there: "MM:SS.mmm" under 15
        /// minutes, "Xh Ym Zs mmmms" otherwise. That method's own long form also prefixes the
        /// localised "Time" label (language.TIMEINFO_TIME); ClearTimeLabel stands in for it here
        /// instead, to match the "pb: " line above.
        /// </summary>
        private static string FormatClearTime(TimeSpan time)
        {
            if (time.TotalMinutes < 15)
            {
                return $"{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
            }
            return $"{(int)time.TotalHours}h {time.Minutes}m {time.Seconds}s {time.Milliseconds:000}ms";
        }
    }
}
