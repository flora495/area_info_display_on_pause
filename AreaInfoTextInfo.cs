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
        // IStatInfo (the engine's own equivalent) measures with extra trailing characters
        // ("12345") rather than the exact text, since Font.MeasureString slightly
        // under-measures the actual drawn width. Without this, the box ends up a few pixels
        // too narrow and the text overflows past the right edge of its own frame.
        private const string MeasureBuffer = "12345";

        public AreaInfoTextInfo()
            : base(AreaTracker.GetDisplayText(), Color.White)
        {
        }

        public override Point GetSize()
        {
            base.Text = AreaTracker.GetDisplayText() + MeasureBuffer;
            return base.GetSize();
        }

        public override void Draw(int x, int y, bool selected)
        {
            base.Text = AreaTracker.GetDisplayText();
            base.Draw(x, y, selected);
        }
    }
}
