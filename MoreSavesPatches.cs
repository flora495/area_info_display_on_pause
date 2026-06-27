using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Optional integration with the separate "More Saves" mod (Zebra.MoreSaves, Workshop ID
    /// 3239040787), which lets a player create any number of named manual save slots for the same
    /// level (and also keeps its own auto-save slot per level) - something this mod's own progress
    /// file (keyed only by level, since the base game on its own only ever has one save per level)
    /// can't distinguish between on its own. Entirely via Harmony postfixes on More Saves' own
    /// public SaveManager methods; More Saves itself is never modified, and its own save files are
    /// never touched - this only adds one extra file of its own alongside them, per slot.
    ///
    /// Reflection-only (no project reference to MoreSaves.dll) so this mod works identically
    /// whether or not More Saves is installed: AccessTools.TypeByName returns null if it isn't,
    /// and Apply just returns without patching anything.
    /// </summary>
    internal static class MoreSavesPatches
    {
        private const string ProgressFileName = "F.AreaInfoDisplayOnPause.AreaProgress.xml";

        private static PropertyInfo s_manualDirectoryProperty;
        private static PropertyInfo s_autoDirectoryProperty;
        private static PropertyInfo s_saveNameProperty;

        public static void Apply(Harmony harmony)
        {
            Type saveManagerType = AccessTools.TypeByName("MoreSaves.Saves.SaveManager");
            if (saveManagerType == null)
            {
                return;
            }

            s_manualDirectoryProperty = AccessTools.Property(saveManagerType, "ManualDirectory");
            s_autoDirectoryProperty = AccessTools.Property(saveManagerType, "AutoDirectory");
            s_saveNameProperty = AccessTools.Property(saveManagerType, "SaveName");

            harmony.Patch(AccessTools.Method(saveManagerType, "SaveAllManual"),
                postfix: new HarmonyMethod(typeof(MoreSavesPatches), nameof(SaveAllManualPostfix)));
            harmony.Patch(AccessTools.Method(saveManagerType, "SaveAllAuto"),
                postfix: new HarmonyMethod(typeof(MoreSavesPatches), nameof(SaveAllAutoPostfix)));
            harmony.Patch(AccessTools.Method(saveManagerType, "LoadSave"),
                postfix: new HarmonyMethod(typeof(MoreSavesPatches), nameof(LoadSavePostfix)));
        }

        /// <summary>
        /// Snapshots the current progress data (every level's, same as the main file) into the
        /// manual save slot's own folder, right next to More Saves' own Saves/SavesPerma
        /// subfolders. Unconditional (SaveSnapshot, not Save) since this slot needs its own
        /// up-to-date copy every time it's saved, regardless of whether the main file happens to
        /// be dirty at this exact moment.
        /// </summary>
        private static void SaveAllManualPostfix(object __instance, string folderName)
        {
            string manualDirectory = (string)s_manualDirectoryProperty.GetValue(__instance);
            string slotPath = Path.Combine(manualDirectory, folderName, ProgressFileName);
            AreaProgressStore.SaveSnapshot(slotPath);
        }

        /// <summary>
        /// Same idea as SaveAllManualPostfix, but for More Saves' own auto-save slot (one per
        /// level, computed from AutoDirectory + SaveName rather than a folder name argument).
        /// Mirrors the real SaveAllAuto's own "is a save currently active" guard (SaveName empty
        /// between StopSaving/StartSaving calls) so this doesn't snapshot into a bogus path.
        /// </summary>
        private static void SaveAllAutoPostfix(object __instance)
        {
            string saveName = (string)s_saveNameProperty.GetValue(__instance);
            if (string.IsNullOrEmpty(saveName))
            {
                return;
            }
            string autoDirectory = (string)s_autoDirectoryProperty.GetValue(__instance);
            string slotPath = Path.Combine(autoDirectory, saveName, ProgressFileName);
            AreaProgressStore.SaveSnapshot(slotPath);
        }

        /// <summary>
        /// Restores progress data from a save slot's snapshot (manual or auto) when that slot is
        /// loaded, so the live data matches what it was when that slot was last saved instead of
        /// whatever the main file (shared across every save) currently holds. Does nothing if this
        /// particular slot has no snapshot of its own (e.g. it was saved before this mod was
        /// installed, or before this feature existed) - the live data is simply left as-is, same
        /// as today.
        /// </summary>
        private static void LoadSavePostfix(object __instance, string directory)
        {
            string manualDirectory = (string)s_manualDirectoryProperty.GetValue(__instance);
            string autoDirectory = (string)s_autoDirectoryProperty.GetValue(__instance);
            bool isKnownSlot = directory.StartsWith(manualDirectory, StringComparison.OrdinalIgnoreCase)
                || directory.StartsWith(autoDirectory, StringComparison.OrdinalIgnoreCase);
            if (!isKnownSlot)
            {
                return;
            }
            string slotPath = Path.Combine(directory, ProgressFileName);
            if (File.Exists(slotPath))
            {
                AreaProgressStore.LoadSnapshot(slotPath);
            }
        }
    }
}
