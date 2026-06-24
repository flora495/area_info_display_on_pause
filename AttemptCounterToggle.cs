using JumpKing.PauseMenu.BT.Actions;

namespace AreaInfoDisplayOnPause
{
    public sealed class AttemptCounterToggle : ITextToggle
    {
        public AttemptCounterToggle()
            : base(ModEntry.Settings.AttemptCounterEnabled)
        {
        }

        protected override bool CanChange()
        {
            // Has no effect while Progression Detail is showing its own attempt count/PB line.
            return !AreaTracker.ShowProgressionDetail;
        }

        protected override string GetName()
        {
            return "Attempt Counter";
        }

        protected override void OnToggle()
        {
            ModEntry.Settings.AttemptCounterEnabled = base.toggle;
            ModEntry.SaveSettings();
        }
    }
}
