using System;
using System.Reflection;
using HarmonyLib;
using JumpKing.PauseMenu;
using JumpKing.PauseMenu.BT;
using Microsoft.Xna.Framework;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// MenuFactory is internal, so its Create* methods can't be targeted with [HarmonyPatch]
    /// attributes directly; they're patched manually via reflection instead (same approach as
    /// ConfirmCountControl's MenuFactoryPatches).
    /// </summary>
    internal static class MenuFactoryPatches
    {
        private static FieldInfo s_guiFormatField;
        private static MethodInfo s_addDrawableMethod;

        public static void Apply(Harmony harmony)
        {
            Type menuFactoryType = AccessTools.TypeByName("JumpKing.PauseMenu.MenuFactory");
            s_guiFormatField = AccessTools.Field(menuFactoryType, "GUI_FORMAT");
            s_addDrawableMethod = AccessTools.Method(menuFactoryType, "AddDrawable");
            harmony.Patch(AccessTools.Method(menuFactoryType, "CreatePauseInfo"),
                prefix: new HarmonyMethod(typeof(MenuFactoryPatches), nameof(CreatePauseInfoPrefix)));
        }

        /// <summary>
        /// Skips the original method and rebuilds the same "Objective" display frame using the
        /// exact same GuiFormat setup, but with our own text as the only child, so the original
        /// line is replaced rather than just covered up. Mutating the already-built frame's
        /// children array after the fact (the previous approach) left the frame border and
        /// centering looking wrong, since CalculateBounds/the GuiFrame border depend on the
        /// item(s) present when Initialize() first ran; going through the same construction
        /// path the engine itself uses avoids that entirely.
        /// </summary>
        private static bool CreatePauseInfoPrefix(object __instance, ref DisplayFrame __result)
        {
            if (!ModEntry.Settings.IsEnabled)
            {
                return true;
            }

            GuiFormat format = (GuiFormat)s_guiFormatField.GetValue(null);
            format.all_margin /= 2;
            // The original CreatePauseInfo halves padding too (it's tuned for a long sentence,
            // "Objective: Get to the babe at the top!"); for our shorter text that left almost
            // no breathing room between the text and the border. Keep the full, un-halved
            // padding instead (a 3px bump out of 9 turned out to be imperceptible in testing).
            format.element_margin = 0;
            format.anchor = new Vector2(0.5f, 1f);

            DisplayFrame displayFrame = new DisplayFrame(format, BehaviorTree.BTresult.Running);
            displayFrame.AddChild(new AreaInfoTextInfo());
            displayFrame.Initialize();
            s_addDrawableMethod.Invoke(__instance, new object[] { displayFrame });
            __result = displayFrame;
            return false;
        }
    }
}
