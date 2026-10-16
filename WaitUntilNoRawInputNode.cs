using BehaviorTree;
using JumpKing.Controller;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Runs right before CaptureDetailShortcutNode: waits until the Confirm press used to pick
    /// "Detail Shortcut" has been released, so that press itself isn't captured as the new
    /// binding. Same role as the vanilla controls screen's WaitUntilNoInput (copied from
    /// hide_pause_window, where it's been verified in-game).
    /// </summary>
    internal sealed class WaitUntilNoRawInputNode : IBTnode
    {
        protected override BTresult MyRun(TickData p_data)
        {
            PadInstance main = ControllerManager.instance.GetMain();
            if (!main.IsValid || !main.IsConnected)
            {
                return BTresult.Failure;
            }
            if (main.GetPad().GetPressedButtons().Length != 0)
            {
                return BTresult.Running;
            }
            return BTresult.Success;
        }
    }
}
