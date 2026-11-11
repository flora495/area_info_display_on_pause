namespace AreaInfoDisplayOnPause
{
    public sealed class PersonalBestToggle : SettingToggle
    {
        public PersonalBestToggle()
            : base("Personal Best",
                () => ModEntry.Settings.PersonalBestEnabled,
                value => ModEntry.Settings.PersonalBestEnabled = value,
                refreshDisplayFrame: true)
        {
        }
    }
}
