namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// AreaTracker notices the change itself the next time it's used (see
    /// EnsureBabeLocationsCurrent), the same way it would after Reset to Default.
    /// </summary>
    public sealed class BabeAsAreaToggle : SettingToggle
    {
        public BabeAsAreaToggle()
            : base("Babe as Area",
                () => ModEntry.Settings.BabeAsAreaEnabled,
                value => ModEntry.Settings.BabeAsAreaEnabled = value,
                refreshDisplayFrame: true)
        {
        }
    }
}
