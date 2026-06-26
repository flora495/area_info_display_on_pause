using System.IO;
using System.Reflection;
using HarmonyLib;
using JumpKing.Mods;
using JumpKing.PauseMenu;

namespace AreaInfoDisplayOnPause
{
    [JumpKingMod("F.AreaInfoDisplayOnPause")]
    public static class ModEntry
    {
        private const string SettingsFileName = "F.AreaInfoDisplayOnPause.Settings.xml";

        private static readonly string SettingsPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), SettingsFileName);

        private static Settings s_settings;

        /// <summary>
        /// Lazily loaded so it's safe to access from the main menu's settings list, which can
        /// run before BeforeLevelLoad ever fires (e.g. before any save is loaded).
        /// </summary>
        public static Settings Settings => s_settings ?? (s_settings = Settings.Load(SettingsPath));

        [BeforeLevelLoad]
        public static void BeforeLevelLoad()
        {
            var harmony = new Harmony("F.AreaInfoDisplayOnPause.Harmony");
            LocationSettingsAccessor.Initialize();
            PlayTimeAccessor.Initialize();
            EndingScreensAccessor.Initialize();
            MenuFactoryPatches.Apply(harmony);
            LevelManagerPatches.Apply(harmony);
            SaveLubePatches.Apply(harmony);
            SaveLubePatches.LoadProgress();
        }

        [OnLevelStart]
        public static void OnLevelStart()
        {
            AreaTracker.OnLevelStart();
        }

        public static void SaveSettings()
        {
            Settings.Save(SettingsPath);
        }

        [MainMenuItemSetting]
        public static TotalDisplayModeOption MainTotalDisplayModeSetting(object factory, GuiFormat format)
        {
            return new TotalDisplayModeOption();
        }

        [MainMenuItemSetting]
        public static AttemptCounterToggle MainAttemptCounterSetting(object factory, GuiFormat format)
        {
            return new AttemptCounterToggle();
        }

        [MainMenuItemSetting]
        public static PersonalBestToggle MainPersonalBestSetting(object factory, GuiFormat format)
        {
            return new PersonalBestToggle();
        }

        [PauseMenuItemSetting]
        public static TotalDisplayModeOption PauseTotalDisplayModeSetting(object factory, GuiFormat format)
        {
            return new TotalDisplayModeOption();
        }

        [PauseMenuItemSetting]
        public static AttemptCounterToggle PauseAttemptCounterSetting(object factory, GuiFormat format)
        {
            return new AttemptCounterToggle();
        }

        [PauseMenuItemSetting]
        public static PersonalBestToggle PausePersonalBestSetting(object factory, GuiFormat format)
        {
            return new PersonalBestToggle();
        }

        [PauseMenuItemSetting]
        public static ProgressionDetailToggle PauseProgressionDetailSetting(object factory, GuiFormat format)
        {
            return new ProgressionDetailToggle();
        }
    }
}
