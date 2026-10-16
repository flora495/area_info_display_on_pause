using BehaviorTree;
using JumpKing.PauseMenu;
using JumpKing.PauseMenu.BT;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// The pause menu's "Show Progression Detail" button: opens the current run's per-area
    /// breakdown in centered pages (PagedTextMenu). The popup is drawn over the mod settings list
    /// rather than hiding it - hiding it would need a patch on the engine's CreateModOptions,
    /// which broke other mods' settings screens before (NOTES.md).
    /// </summary>
    internal static class ProgressionDetailMenu
    {
        private const string Title = "Progression Detail";

        public static TextButton Create(object factory)
        {
            // Same frame as ClearedMapsMenu's detail screen, so both breakdowns look alike.
            GuiFormat format = ClearedMapsMenu.CreateCenteredFormat();
            format.all_margin /= 2;
            format.all_padding = format.all_padding / 2 + 1;
            format.element_margin = 2;

            DetailHost host = new DetailHost(format);
            MenuFactoryPatches.AddDrawable(factory, host);
            return new TextButton("Show Progression Detail", host);
        }

        /// <summary>
        /// The pause menu's mod settings are built only once per level, but the breakdown keeps
        /// growing - and with it the number of pages, which a MenuSelector can't drop or add
        /// items for safely once built (DisableMenuItem can index out of range in FixIndex). So
        /// the pages are rebuilt from scratch each time this is opened. The engine only registers
        /// a TextButton's MenuSelector child for drawing, and only when the settings are built,
        /// so this registers itself instead and draws whichever pages are current.
        /// </summary>
        private sealed class DetailHost : IBTnode, JumpKing.Util.IDrawable
        {
            private readonly GuiFormat m_format;
            private MenuSelector m_firstPage;

            public DetailHost(GuiFormat format)
            {
                m_format = format;
            }

            protected override void OnNewRun()
            {
                base.OnNewRun();
                m_firstPage = PagedTextMenu.Create(m_format, Title, AreaTracker.GetProgressionDetailText());
            }

            protected override BTresult MyRun(TickData p_data)
            {
                return m_firstPage.Run(p_data);
            }

            public void Draw()
            {
                if (m_firstPage != null)
                {
                    PagedTextMenu.DrawChain(m_firstPage);
                }
            }
        }
    }
}
