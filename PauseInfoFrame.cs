using BehaviorTree;
using JumpKing.PauseMenu;
using JumpKing.PauseMenu.BT;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// The pause-info DisplayFrame (built by MenuFactoryPatches.CreatePauseInfoPrefix), plus the
    /// Detail Shortcut check. PauseManager ticks this every frame while paused - including while
    /// a submenu is open, since it sits under a StaticNode in the pause tree's BTsimultaneous -
    /// and never while unpaused, so the check costs nothing during gameplay. No Harmony patch
    /// needed for it either.
    /// </summary>
    internal sealed class PauseInfoFrame : DisplayFrame
    {
        public PauseInfoFrame(GuiFormat p_format)
            : base(p_format, BTresult.Running)
        {
        }

        protected override BTresult MyRun(TickData p_data)
        {
            if (DetailShortcut.WasJustPressed())
            {
                AreaTracker.ShowProgressionDetail = !AreaTracker.ShowProgressionDetail;
                // The two modes can differ a lot in line count/length.
                Initialize();
            }
            return base.MyRun(p_data);
        }
    }
}
