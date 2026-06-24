using JumpKing.PauseMenu.BT.Actions;

namespace AreaInfoDisplayOnPause
{
    public sealed class TotalDisplayModeOption : IOptions
    {
        public TotalDisplayModeOption()
            : base(3, (int)ModEntry.Settings.DisplayMode, EdgeMode.Clamp)
        {
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
            ModEntry.Settings.DisplayMode = (TotalDisplayMode)option;
            ModEntry.SaveSettings();
        }
    }
}
