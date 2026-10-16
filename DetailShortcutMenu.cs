using BehaviorTree;
using JumpKing.PauseMenu;
using JumpKing.PauseMenu.BT;
using JumpKing.PauseMenu.BT.Actions.BindController;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// The "Detail Shortcut (KB: ... / Pad: ...)" item, shown in both the main and pause menus'
    /// mod settings: selecting it opens the engine's own "press a button to bind" BindButtonFrame
    /// (10-second auto-cancel included). Copied from hide_pause_window's HideWindowKeyMenu, which
    /// has been verified in-game.
    /// </summary>
    internal static class DetailShortcutMenu
    {
        public static TextButton Create(object factory)
        {
            BTsequencor captureFlow = new BTsequencor();
            captureFlow.AddChild(new WaitUntilNoRawInputNode());
            captureFlow.AddChild(new CaptureDetailShortcutNode());

            BindButtonFrame frame = new BindButtonFrame(ClearedMapsMenu.CreateCenteredFormat(), captureFlow);
            frame.AddChild(new TextButton("Press a key or button to bind...", captureFlow));
            frame.Initialize();
            MenuFactoryPatches.AddDrawable(factory, frame);

            // The button's child has to be the frame itself, not captureFlow: otherwise the frame
            // never gets ticked, so its popup isn't drawn and its auto-cancel timer never runs,
            // while the menu (which ignores up/down/cancel while its item is Running) sits
            // waiting with nothing on screen (found in hide_pause_window's in-game testing).
            return new DetailShortcutButton(frame);
        }

        /// <summary>
        /// Rebuilds its label whenever the bound buttons change - after a capture, or after Reset
        /// to Default. Settings always swaps in a new array on either, so one reference
        /// comparison per Draw is all it costs otherwise.
        /// </summary>
        private sealed class DetailShortcutButton : TextButton
        {
            private int[] m_shownKeyboard;
            private int[] m_shownPad;

            public DetailShortcutButton(IBTnode child)
                : base(string.Empty, child)
            {
                UpdateLabel();
            }

            public override void Draw(int x, int y, bool selected)
            {
                if (m_shownKeyboard != ModEntry.Settings.KeyboardDetailShortcut
                    || m_shownPad != ModEntry.Settings.PadDetailShortcut)
                {
                    UpdateLabel();
                }
                base.Draw(x, y, selected);
            }

            private void UpdateLabel()
            {
                m_shownKeyboard = ModEntry.Settings.KeyboardDetailShortcut;
                m_shownPad = ModEntry.Settings.PadDetailShortcut;
                base.Text = "Detail Shortcut (KB: " + DescribeKeyboard(m_shownKeyboard)
                    + " / Pad: " + DescribePad(m_shownPad, ModEntry.Settings.PadDetailShortcutIsXInput) + ")";
            }

            private static string DescribeKeyboard(int[] keys)
            {
                return keys.Length == 0 ? "-" : ((Keys)keys[0]).ToString();
            }

            /// <summary>
            /// XInput numbers are XNA Buttons flag values (A = 4096 etc.), so they're shown by
            /// name; DirectInput ones are plain button numbers.
            /// </summary>
            private static string DescribePad(int[] keys, bool isXInput)
            {
                if (keys.Length == 0)
                {
                    return "-";
                }
                if (isXInput)
                {
                    return ((Buttons)keys[0]).ToString();
                }
                return "Button " + keys[0];
            }
        }
    }
}
