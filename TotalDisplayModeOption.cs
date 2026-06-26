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

        // The settings menu's frame is sized once from whichever option's text is current when
        // it's (re)built, and never resizes again as the player cycles through the options live -
        // so if it's built while showing the shortest text ("Never") and the player then cycles
        // to the longest ("After Clear"), the text overflows the frame. Padding every variant to
        // the same overall length keeps the measured text width constant across all three, so the
        // frame ends up sized for the longest one no matter which option happened to be current
        // when it was built. The padding goes on the *left* of the value word (not the right), so
        // the ">" cycle arrow the engine draws right after the returned text stays snug against the
        // word itself instead of trailing off after a run of invisible padding spaces - any slack
        // shows up between "Total:" and the value instead.
        private const string Prefix = "Show Total: ";
        private static readonly int ValuePadLength = "After Clear".Length + 2;

        protected override string CurrentOptionName()
        {
            switch ((TotalDisplayMode)base.CurrentOption)
            {
                case TotalDisplayMode.Always:
                    return Prefix + "Always".PadLeft(ValuePadLength);
                case TotalDisplayMode.Never:
                    return Prefix + "Never".PadLeft(ValuePadLength);
                default:
                    return Prefix + "After Clear".PadLeft(ValuePadLength);
            }
        }

        protected override void OnOptionChange(int option)
        {
            ModEntry.Settings.DisplayMode = (TotalDisplayMode)option;
            ModEntry.SaveSettings();
        }
    }
}
