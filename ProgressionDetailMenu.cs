using JumpKing.PauseMenu;
using JumpKing.PauseMenu.BT;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// The pause menu's "Show Progression Detail" button: opens the current run's per-area
    /// breakdown in a centered popup. The engine's MenuFactory.TryCreateModSetting registers the
    /// button's child MenuSelector for drawing on its own (same as ClearedMapsMenu). The popup is
    /// drawn over the mod settings list rather than hiding it - hiding it would need a patch on the
    /// engine's CreateModOptions, which broke other mods' settings screens before (NOTES.md).
    /// </summary>
    internal static class ProgressionDetailMenu
    {
        public static TextButton Create()
        {
            // Same frame as ClearedMapsMenu's detail screen, so both breakdowns look alike.
            GuiFormat format = ClearedMapsMenu.CreateCenteredFormat();
            format.all_margin /= 2;
            format.all_padding = format.all_padding / 2 + 1;
            format.element_margin = 2;

            MenuSelector detail = new RemeasuringMenuSelector(format);
            detail.AddChild(new AreaInfoTextInfo(AreaTracker.GetProgressionDetailText));
            // Initialize() adds the back button, which is needed here: AreaInfoTextInfo is
            // UnSelectable, and a MenuSelector with nothing selectable crashes in FixIndex as soon
            // as it's opened.
            detail.Initialize();
            return new TextButton("Show Progression Detail", detail);
        }

        /// <summary>
        /// The pause menu's mod settings are built only once per level, but the breakdown keeps
        /// growing, so the frame is re-measured each time it's opened. The back button is
        /// already among the children by then, hence Initialize(false).
        /// </summary>
        private sealed class RemeasuringMenuSelector : MenuSelector
        {
            public RemeasuringMenuSelector(GuiFormat format)
                : base(format)
            {
            }

            protected override void OnNewRun()
            {
                base.OnNewRun();
                Initialize(false);
            }
        }
    }
}
