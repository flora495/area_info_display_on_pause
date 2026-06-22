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
            string text = AreaTracker.GetDisplayText();
            base.Text = text;

            // The frame was sized to fit text + MeasureBuffer (see GetSize), but only the real
            // text is drawn here, and JumpKing's own GuiFormat.DrawMenuItems always draws flush
            // against the frame's left padding - never centered. That leaves a gap on the right
            // equal to the buffer's measured width. Shifting the draw position right by half of
            // that gap centers the text within the frame instead.
            float bufferWidth = base.Font.MeasureString(text + MeasureBuffer).X - base.Font.MeasureString(text).X;
            int offsetX = (int)(bufferWidth / 2f);
            base.Draw(x + offsetX, y, selected);
        }
    }
}
