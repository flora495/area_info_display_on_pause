using JumpKing.PauseMenu.BT.Actions;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Swaps the pause-info box between the current area's normal info and a per-area breakdown
    /// (attempt count + the time it was first reached, with the personal-best area marked) of
    /// every area reached so far this run. Toggling needs to immediately re-measure the
    /// pause-info frame (see MenuFactoryPatches.RefreshDisplayFrame) since the two modes can have
    /// very different line counts/lengths, and there's no pause-open event to hang that off of
    /// here - the player is already mid-pause when they press this.
    /// </summary>
    public sealed class ProgressionDetailToggle : ITextToggle
    {
        public ProgressionDetailToggle()
            : base(AreaTracker.ShowProgressionDetail)
        {
        }

        protected override string GetName()
        {
            return "Progression Detail";
        }

        protected override void OnToggle()
        {
            AreaTracker.ShowProgressionDetail = base.toggle;
            MenuFactoryPatches.RefreshDisplayFrame();
        }
    }
}
