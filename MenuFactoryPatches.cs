using System;
using HarmonyLib;
using JumpKing.PauseMenu.BT;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// MenuFactory is internal, so its Create* methods can't be targeted with [HarmonyPatch]
    /// attributes directly; they're patched manually via reflection instead (same approach as
    /// ConfirmCountControl's MenuFactoryPatches).
    /// </summary>
    internal static class MenuFactoryPatches
    {
        public static void Apply(Harmony harmony)
        {
            Type menuFactoryType = AccessTools.TypeByName("JumpKing.PauseMenu.MenuFactory");
            harmony.Patch(AccessTools.Method(menuFactoryType, "CreatePauseInfo"),
                postfix: new HarmonyMethod(typeof(MenuFactoryPatches), nameof(CreatePauseInfoPostfix)));
        }

        private static void CreatePauseInfoPostfix(ref DisplayFrame __result)
        {
            if (!ModEntry.Settings.IsEnabled)
            {
                return;
            }
            __result.AddChild(new AreaInfoTextInfo());
            __result.Initialize();
        }
    }
}
