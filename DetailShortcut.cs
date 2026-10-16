using JumpKing.Controller;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Detects presses of the Detail Shortcut. Polled by PauseInfoFrame, which the engine only
    /// ticks while paused, so this never runs during gameplay.
    /// </summary>
    internal static class DetailShortcut
    {
        public const string KeyboardSaveIdentifier = "pc_keyboard_jump_king";

        private static bool s_lastPressed;

        /// <summary>
        /// True only on the frame the shortcut goes from released to pressed. A press that was
        /// already held when SuppressCurrentPress was last called has to be released first.
        /// </summary>
        public static bool WasJustPressed()
        {
            bool pressed = IsPressed();
            bool justPressed = pressed && !s_lastPressed;
            s_lastPressed = pressed;
            return justPressed;
        }

        /// <summary>
        /// Treats the shortcut as already held, so whatever's down right now (the button used to
        /// open pause, or the one just bound) only counts once it's released and pressed again.
        /// </summary>
        public static void SuppressCurrentPress()
        {
            s_lastPressed = true;
        }

        /// <summary>
        /// Checks every connected device rather than just ControllerManager.GetMain(): one
        /// physical controller can show up as both an XInput and a DirectInput device, and the
        /// "main" one may not be the side the button was bound on (found in hide_pause_window's
        /// in-game testing). Bails out before touching any device while nothing is bound.
        /// </summary>
        private static bool IsPressed()
        {
            int[] keyboard = ModEntry.Settings.KeyboardDetailShortcut;
            int[] pad = ModEntry.Settings.PadDetailShortcut;
            if (keyboard.Length == 0 && pad.Length == 0)
            {
                return false;
            }
            foreach (PadInstance instance in ControllerManager.instance.GetConnectedPads())
            {
                if (!instance.IsValid)
                {
                    continue;
                }
                bool isKeyboard = instance.GetPad().GetSaveIdentifier() == KeyboardSaveIdentifier;
                if (ContainsAny(instance.GetPad().GetPressedButtons(), isKeyboard ? keyboard : pad))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsAny(int[] pressed, int[] bind)
        {
            for (int i = 0; i < pressed.Length; i++)
            {
                for (int j = 0; j < bind.Length; j++)
                {
                    if (pressed[i] == bind[j])
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
