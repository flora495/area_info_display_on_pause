using JumpKing.PauseMenu.BT.Actions;

namespace AreaInfoDisplayOnPause
{
    public sealed class TotalDisplayModeOption : IOptions
    {
        public TotalDisplayModeOption()
            : base(3, (int)ModEntry.Settings.DisplayMode, EdgeMode.Clamp)
        {
        }

        public override void Draw(int x, int y, bool selected)
        {
            // Picks up Reset to Default, the only way the setting changes without this option.
            if (base.CurrentOption != (int)ModEntry.Settings.DisplayMode)
            {
                base.CurrentOption = (int)ModEntry.Settings.DisplayMode;
            }
            base.Draw(x, y, selected);
        }

        protected override bool CanChange()
        {
            return true;
        }

        protected override string CurrentOptionName()
        {
            switch ((TotalDisplayMode)base.CurrentOption)
            {
                case TotalDisplayMode.Always:
                    return "Show Total: Always";
                case TotalDisplayMode.Never:
                    return "Show Total: Never";
                default:
                    return "Show Total: After Clear";
            }
        }

        protected override void OnOptionChange(int option)
        {
            // Draw syncing to an already-saved reset lands here too; nothing new to save then.
            if (ModEntry.Settings.DisplayMode == (TotalDisplayMode)option)
            {
                return;
            }
            ModEntry.Settings.DisplayMode = (TotalDisplayMode)option;
            ModEntry.SaveSettings();
        }
    }
}
