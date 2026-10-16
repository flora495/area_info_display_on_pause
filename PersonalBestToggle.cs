using JumpKing.PauseMenu.BT.Actions;

namespace AreaInfoDisplayOnPause
{
    public sealed class PersonalBestToggle : ITextToggle
    {
        public PersonalBestToggle()
            : base(ModEntry.Settings.PersonalBestEnabled)
        {
        }

        public override void Draw(int x, int y, bool selected)
        {
            // Picks up Reset to Default, the only way the setting changes without this toggle.
            if (base.toggle != ModEntry.Settings.PersonalBestEnabled)
            {
                OverrideToggle(ModEntry.Settings.PersonalBestEnabled);
            }
            base.Draw(x, y, selected);
        }

        protected override string GetName()
        {
            return "Personal Best";
        }

        protected override void OnToggle()
        {
            ModEntry.Settings.PersonalBestEnabled = base.toggle;
            ModEntry.SaveSettings();
            MenuFactoryPatches.RefreshDisplayFrame();
        }
    }
}
