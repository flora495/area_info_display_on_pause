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
        private static DisplayFrame s_displayFrame;

        public static void Apply(Harmony harmony)
        {
            Type menuFactoryType = AccessTools.TypeByName("JumpKing.PauseMenu.MenuFactory");
            s_guiFormatField = AccessTools.Field(menuFactoryType, "GUI_FORMAT");
            s_addDrawableMethod = AccessTools.Method(menuFactoryType, "AddDrawable");
            harmony.Patch(AccessTools.Method(menuFactoryType, "CreatePauseInfo"),
                prefix: new HarmonyMethod(typeof(MenuFactoryPatches), nameof(CreatePauseInfoPrefix)));

            // PauseManager builds its pause-info DisplayFrame once per level (when its behaviour
            // tree is first constructed) and DisplayFrame.Initialize() only measures the text and
            // caches the resulting frame bounds at that single moment. Our text keeps changing
            // length (area name, page digits, attempt count digits) every time pause is reopened,
            // so the cached bounds drift out of sync with what's actually drawn. Re-running
            // Initialize() every time pause is entered keeps the frame matching the text.
            Type pauseManagerType = AccessTools.TypeByName("JumpKing.PauseMenu.PauseManager");
            harmony.Patch(AccessTools.Method(pauseManagerType, "SetPause"),
                postfix: new HarmonyMethod(typeof(MenuFactoryPatches), nameof(SetPausePostfix)));
        }

        /// <summary>
        /// Every pause starts on the normal display, with whatever's held at that moment (e.g.
        /// the pause button itself) not yet counting as a Detail Shortcut press. Done on open
        /// rather than on close so it sits with the re-measure and doesn't depend on which way
        /// pause was closed.
        /// </summary>
        private static void SetPausePostfix(bool p_pause)
        {
            if (p_pause)
            {
                AreaTracker.ShowProgressionDetail = false;
                DetailShortcut.SuppressCurrentPress();
                s_displayFrame?.Initialize();
            }
        }

        /// <summary>
        /// MenuFactory.AddDrawable, for popups the engine doesn't register for drawing on its own
        /// (it only does that for a TextButton's direct MenuSelector child). Resolved from the
        /// factory itself when needed, since the title screen's mod settings can be built before
        /// Apply ever runs.
        /// </summary>
        public static void AddDrawable(object factory, object drawable)
        {
            if (s_addDrawableMethod == null)
            {
                s_addDrawableMethod = AccessTools.Method(factory.GetType(), "AddDrawable");
            }
            s_addDrawableMethod.Invoke(factory, new object[] { drawable });
        }

        /// <summary>
        /// Re-measures the pause-info frame's bounds. SetPausePostfix already covers the
        /// pause-open case; this is for changes that happen mid-pause-session, like toggling
        /// Personal Best or Reset to Default, which change the displayed text's line
        /// count/length without a pause-open event to hang a refresh off of.
        /// </summary>
        public static void RefreshDisplayFrame()
        {
            s_displayFrame?.Initialize();
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
            GuiFormat format = (GuiFormat)s_guiFormatField.GetValue(null);
            format.all_margin /= 2;
            // Match the original CreatePauseInfo's own padding (halved, +1) instead of the full
            // 16px - keeping the full amount made the gap around the text noticeably larger than
            // vanilla's own pause-info box.
            format.all_padding = format.all_padding / 2 + 1;
            format.element_margin = 0;
            format.anchor = new Vector2(0.5f, 1f);

            DisplayFrame displayFrame = new PauseInfoFrame(format);
            displayFrame.AddChild(new AreaInfoTextInfo());
            displayFrame.Initialize();
            s_displayFrame = displayFrame;
            AddDrawable(__instance, displayFrame);
            __result = displayFrame;
            return false;
        }
    }
}
