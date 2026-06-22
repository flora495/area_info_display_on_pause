using JumpKing.PauseMenu.BT.Actions;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Swaps the pause-info box between the current area's normal info and a list of every area
    /// reached so far this run, with attempt counts. Toggling needs to immediately re-measure the
    /// pause-info frame (see MenuFactoryPatches.RefreshDisplayFrame) since the two modes can have
    /// very different line counts/lengths, and there's no pause-open event to hang that off of
    /// here - the player is already mid-pause when they press this.
    /// </summary>
    public sealed class AreaHistoryToggle : ITextToggle
    {
        public AreaHistoryToggle()
            : base(AreaTracker.ShowHistory)
        {
        }

        protected override bool CanChange()
        {
            return ModEntry.Settings.IsEnabled;
        }

        protected override string GetName()
        {
            return "Area History";
        }

        protected override void OnToggle()
        {
            AreaTracker.ShowHistory = base.toggle;
            MenuFactoryPatches.RefreshDisplayFrame();
        }
    }
}
