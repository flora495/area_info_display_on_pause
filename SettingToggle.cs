using System;
using JumpKing.PauseMenu.BT.Actions;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Shared ON/OFF checkbox for one bool setting in ModEntry.Settings, used by both the main
    /// and the pause menu. Each concrete toggle only supplies its label and the setting it
    /// reads/writes.
    /// </summary>
    public abstract class SettingToggle : ITextToggle
    {
        private readonly string m_name;
        private readonly Func<bool> m_getValue;
        private readonly Action<bool> m_setValue;
        private readonly bool m_refreshDisplayFrame;

        protected SettingToggle(string name, Func<bool> getValue, Action<bool> setValue, bool refreshDisplayFrame)
            : base(getValue())
        {
            m_name = name;
            m_getValue = getValue;
            m_setValue = setValue;
            m_refreshDisplayFrame = refreshDisplayFrame;
        }

        public override void Draw(int x, int y, bool selected)
        {
            // Picks up Reset to Default, the only way the setting changes without this toggle.
            bool value = m_getValue();
            if (base.toggle != value)
            {
                OverrideToggle(value);
            }
            base.Draw(x, y, selected);
        }

        protected override string GetName()
        {
            return m_name;
        }

        protected override void OnToggle()
        {
            m_setValue(base.toggle);
            ModEntry.SaveSettings();
            if (m_refreshDisplayFrame)
            {
                MenuFactoryPatches.RefreshDisplayFrame();
            }
        }
    }
}
