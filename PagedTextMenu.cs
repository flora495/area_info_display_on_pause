using System.Collections.Generic;
using BehaviorTree;
using JumpKing;
using JumpKing.PauseMenu;
using JumpKing.PauseMenu.BT;
using LanguageJK;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// A multi-line breakdown (Progression Detail, or a cleared map's history) as a chain of
    /// centered pages, each short enough to stay on screen. A page that fits on its own is just
    /// the text and the back button; otherwise each page gets a "Title 1/N" header and
    /// Previous/Next, laid out the same way as ClearedMapsMenu's list pages: Next opens the
    /// following page nested under it, and Previous (or cancel) backs out of it.
    ///
    /// Without this, the whole breakdown was one item: past ~25 areas the frame ran off screen,
    /// and with JKRuntime (Workshop 3793086563) installed nothing showed at all - its
    /// NativeMenuViewport takes over drawing any MenuSelector that leaves its safe area, scrolls
    /// it item by item to keep the selected one (the back button) in view, and so skips an item
    /// taller than the viewport entirely, leaving only the frame and its "2/2" item counter.
    /// </summary>
    internal static class PagedTextMenu
    {
        /// <summary>
        /// Tallest frame that stays inside JKRuntime's safe area (12, 12, 456, 336) when centered
        /// with ClearedMapsMenu.CreateCenteredFormat's margins halved (8px) - every format passed
        /// in here is built that way. It also keeps the frame on the 480x360 screen without
        /// JKRuntime.
        /// </summary>
        private const int MaxFrameHeight = 336;

        public static MenuSelector Create(GuiFormat format, string title, string text)
        {
            return CreatePage(format, title, Paginate(format, text), 0);
        }

        /// <summary>
        /// Draws a chain built by Create, for callers that build it after the engine's own
        /// drawables registration has already happened (see ProgressionDetailMenu). Each page
        /// only draws while it's open and none of its own pages are, so drawing all of them is
        /// safe.
        /// </summary>
        public static void DrawChain(MenuSelector page)
        {
            page.Draw();
            foreach (IBTnode child in page.Children)
            {
                if (child is IBTMenuDecorator decorator && decorator.Child is MenuSelector next)
                {
                    DrawChain(next);
                }
            }
        }

        private static MenuSelector CreatePage(GuiFormat format, string title, List<string> pages, int page)
        {
            MenuSelector menu = new ClearedMapsMenu.ParentHidingMenuSelector(format);
            if (pages.Count > 1)
            {
                menu.AddChild(new TextInfo($"{title} {page + 1}/{pages.Count}", Color.Gray));
            }
            string pageText = pages[page];
            menu.AddChild(new AreaInfoTextInfo(() => pageText));
            if (page > 0)
            {
                menu.AddChild(new TextButton(language.PAGINATION_PREVIOUS, new MenuSelectorBack(menu)));
            }
            if (page < pages.Count - 1)
            {
                menu.AddChild(new TextButton(language.PAGINATION_NEXT, CreatePage(format, title, pages, page + 1)));
            }
            // Initialize() adds the back button, which is needed here: the text items are
            // UnSelectable, and a MenuSelector with nothing selectable crashes in FixIndex as
            // soon as it's opened.
            menu.Initialize();
            return menu;
        }

        /// <summary>
        /// Splits text into pages by whole lines, measuring with the same font and the same
        /// height sum GuiFormat.CalculateBounds uses, so each page's frame stays within
        /// MaxFrameHeight. Space for the header and both Previous and Next is reserved on every
        /// page once there's more than one, so the split doesn't depend on which page it is.
        /// </summary>
        private static List<string> Paginate(GuiFormat format, string text)
        {
            SpriteFont font = Game1.instance.contentManager.font.MenuFont;
            int backHeight = Game1.instance.contentManager.gui.BackButton.source.Height;

            int singlePageMax = MaxFrameHeight - format.padding.height - backHeight - format.element_margin;
            if (MeasureHeight(font, text) <= singlePageMax)
            {
                return new List<string> { text };
            }

            int headerHeight = MeasureHeight(font, "0/0");
            int buttonHeight = System.Math.Max(MeasureHeight(font, language.PAGINATION_PREVIOUS), MeasureHeight(font, language.PAGINATION_NEXT));
            int bodyMax = MaxFrameHeight - format.padding.height - headerHeight - buttonHeight * 2 - backHeight
                - format.element_margin * 4;

            var pages = new List<string>();
            string current = null;
            foreach (string line in text.Split('\n'))
            {
                string candidate = current == null ? line : current + "\n" + line;
                // Always at least one line per page, even if a single line is somehow too tall.
                if (current != null && MeasureHeight(font, candidate) > bodyMax)
                {
                    pages.Add(current);
                    current = line;
                }
                else
                {
                    current = candidate;
                }
            }
            pages.Add(current);
            return pages;
        }

        /// <summary>
        /// For the pause-info box's Progression Detail (the Detail Shortcut), which can't page
        /// like a menu can and grows upward from the bottom of the screen - left alone, a long
        /// breakdown would push its most important lines (pb/current/latest areas, at the top)
        /// off screen. Instead, whole lines are dropped from the bottom (the oldest areas) and
        /// replaced with a "... +N more" line, so the result's height stays within maxHeight.
        /// Measures only once when the text already fits, which is the usual case.
        /// </summary>
        public static string TruncateToHeight(string text, int maxHeight)
        {
            SpriteFont font = Game1.instance.contentManager.font.MenuFont;
            if (MeasureHeight(font, text) <= maxHeight)
            {
                return text;
            }

            string[] lines = text.Split('\n');
            string kept = lines[0];
            int keptCount = 1;
            for (int i = 1; i < lines.Length; i++)
            {
                string candidate = kept + "\n" + lines[i];
                if (MeasureHeight(font, candidate + "\n" + MoreLine(lines.Length - i - 1)) > maxHeight)
                {
                    break;
                }
                kept = candidate;
                keptCount = i + 1;
            }
            return kept + "\n" + MoreLine(lines.Length - keptCount);
        }

        private static string MoreLine(int hiddenCount)
        {
            return $"... +{hiddenCount} more";
        }

        // Same rounding as the items' own GetSize (Vector2.ToPoint truncates).
        private static int MeasureHeight(SpriteFont font, string text)
        {
            return (int)font.MeasureString(text).Y;
        }
    }
}
