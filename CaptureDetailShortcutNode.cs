using BehaviorTree;
using JumpKing.Controller;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Saves the next single button pressed on the most recently used device as the Detail
    /// Shortcut. Keyboards and everything else (controllers) go into separate slots, since a
    /// button number means something different on each kind of device (copied from
    /// hide_pause_window's CaptureToggleKeyNode). The button label picks the change up on its
    /// own (see DetailShortcutButton), so nothing needs updating here.
    /// </summary>
    internal sealed class CaptureDetailShortcutNode : IBTnode
    {
        protected override BTresult MyRun(TickData p_data)
        {
            PadInstance main = ControllerManager.instance.GetMain();
            if (!main.IsValid || !main.IsConnected)
            {
                return BTresult.Failure;
            }
            int[] pressed = main.GetPad().GetPressedButtons();
            if (pressed.Length == 0)
            {
                return BTresult.Running;
            }

            string identifier = main.GetPad().GetSaveIdentifier();
            if (identifier == DetailShortcut.KeyboardSaveIdentifier)
            {
                ModEntry.Settings.KeyboardDetailShortcut = new[] { pressed[0] };
            }
            else
            {
                // Set before the array: the button label rebuilds on the array's reference change.
                ModEntry.Settings.PadDetailShortcutIsXInput = DetailShortcut.IsXInput(identifier);
                ModEntry.Settings.PadDetailShortcut = new[] { pressed[0] };
            }
            ModEntry.SaveSettings();
            // The button just bound is still held down; without this, the pause-info frame would
            // read it as a fresh press next frame and toggle the detail straight away.
            DetailShortcut.SuppressCurrentPress();
            return BTresult.Success;
        }
    }
}
