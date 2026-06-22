using System;
using System.Reflection;
using BehaviorTree;
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
        private static readonly FieldInfo s_childrenField = AccessTools.Field(typeof(IBTcomposite), "m_children");

        public static void Apply(Harmony harmony)
        {
            Type menuFactoryType = AccessTools.TypeByName("JumpKing.PauseMenu.MenuFactory");
            harmony.Patch(AccessTools.Method(menuFactoryType, "CreatePauseInfo"),
                postfix: new HarmonyMethod(typeof(MenuFactoryPatches), nameof(CreatePauseInfoPostfix)));
        }

        /// <summary>
        /// Replaces the "Objective: ..." line entirely with the area info line, rather than
        /// adding a second line below it. IBTcomposite (DisplayFrame's base) exposes Children
        /// as a read-only array with no public way to remove a child, so the backing field is
        /// swapped via reflection instead.
        /// </summary>
        private static void CreatePauseInfoPostfix(ref DisplayFrame __result)
        {
            if (!ModEntry.Settings.IsEnabled)
            {
                return;
            }
            s_childrenField.SetValue(__result, new IBTnode[] { new AreaInfoTextInfo() });
            __result.Initialize();
        }
    }
}
