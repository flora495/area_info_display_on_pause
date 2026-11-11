namespace AreaInfoDisplayOnPause
{
    public sealed class AttemptCounterToggle : SettingToggle
    {
        public AttemptCounterToggle()
            : base("Attempt Counter",
                () => ModEntry.Settings.AttemptCounterEnabled,
                value => ModEntry.Settings.AttemptCounterEnabled = value,
                refreshDisplayFrame: false)
        {
        }
    }
}
