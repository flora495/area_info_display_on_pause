using JumpKing.PauseMenu.BT;
using Microsoft.Xna.Framework;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Same self-refreshing pattern as the engine's own IStatInfo (JumpKing.PauseMenu.BT.Stats):
    /// recompute the label text right before measuring/drawing it instead of caching it once.
    /// </summary>
    internal sealed class AreaInfoTextInfo : TextInfo
    {
        public AreaInfoTextInfo()
            : base(AreaTracker.GetDisplayText(), Color.White)
        {
        }

        public override Point GetSize()
        {
            base.Text = AreaTracker.GetDisplayText();
            return base.GetSize();
        }

        public override void Draw(int x, int y, bool selected)
        {
            base.Text = AreaTracker.GetDisplayText();
            base.Draw(x, y, selected);
        }
    }
}
