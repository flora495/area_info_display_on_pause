using System;
using System.Reflection;
using HarmonyLib;
using JumpKing.MiscSystems.LocationText;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// JumpKing.MiscSystems.LocationText.LocationTextManager is internal, so its static SETTINGS
    /// property can't be called directly; it's read via reflection instead. The struct types it
    /// returns (LocationSettings/Location) are public, so they can still be used directly here.
    /// </summary>
    internal static class LocationSettingsAccessor
    {
        private static MethodInfo s_settingsGetter;

        public static void Initialize()
        {
            Type locationTextManagerType = AccessTools.TypeByName("JumpKing.MiscSystems.LocationText.LocationTextManager");
            s_settingsGetter = AccessTools.PropertyGetter(locationTextManagerType, "SETTINGS");
        }

        /// <summary>
        /// Returns a fresh copy of the current level's locations, since the array returned by
        /// the engine is the same instance the engine itself uses elsewhere and must not be
        /// mutated (e.g. by sorting it in place).
        /// </summary>
        public static Location[] GetCurrentLocations()
        {
            var settings = (LocationSettings)s_settingsGetter.Invoke(null, null);
            if (settings.locations == null)
            {
                return new Location[0];
            }
            return (Location[])settings.locations.Clone();
        }
    }
}
