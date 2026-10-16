using JumpKing.PauseMenu.BT.Actions;

namespace AreaInfoDisplayOnPause
{
    public sealed class AttemptCounterToggle : ITextToggle
    {
        public AttemptCounterToggle()
            : base(ModEntry.Settings.AttemptCounterEnabled)
        {
        }

        public override void Draw(int x, int y, bool selected)
        {
            // Picks up Reset to Default, the only way the setting changes without this toggle.
            if (base.toggle != ModEntry.Settings.AttemptCounterEnabled)
            {
                OverrideToggle(ModEntry.Settings.AttemptCounterEnabled);
            }
            base.Draw(x, y, selected);
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
