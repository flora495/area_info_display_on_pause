using JumpKing.PauseMenu.BT.Actions;

namespace AreaInfoDisplayOnPause
{
    public sealed class PersonalBestToggle : ITextToggle
    {
        public PersonalBestToggle()
            : base(ModEntry.Settings.PersonalBestEnabled)
        {
        }

        protected override bool CanChange()
        {
            // Has no effect while Progression Detail is showing its own attempt count/PB line.
            return !AreaTracker.ShowProgressionDetail;
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
