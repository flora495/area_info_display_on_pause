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
            return ModEntry.Settings.IsEnabled;
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
