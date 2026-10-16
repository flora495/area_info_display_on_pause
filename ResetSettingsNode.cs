using BehaviorTree;
using JumpKing.Controller;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// The child of the "Reset to Default" button: puts every setting back to its default the
    /// moment Confirm is pressed (TextButton only runs it then), with no confirmation and no
    /// submenu, leaving the cursor on the settings list. Progress data and the cleared-map
    /// history aren't touched. Each settings item notices the change itself on its next Draw.
    /// </summary>
    internal sealed class ResetSettingsNode : IBTnode
    {
        protected override BTresult MyRun(TickData p_data)
        {
            ModEntry.Settings.ResetToDefault();
            ModEntry.SaveSettings();
            AreaTracker.ShowProgressionDetail = false;
            // Personal Best coming back on, or the detail going back to the normal display,
            // changes the pause-info text's line count. Harmless on the title screen.
            MenuFactoryPatches.RefreshDisplayFrame();
            ControllerManager.instance.MenuController.ConsumePadPresses();
            return BTresult.Success;
        }
    }
}
