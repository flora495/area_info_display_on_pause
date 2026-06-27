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
            base.Text = AddBufferToWidestLine(AreaTracker.GetDisplayText());
            return base.GetSize();
        }

        public override void Draw(int x, int y, bool selected)
        {
            string text = AreaTracker.GetDisplayText();
            base.Text = text;

            // The frame was sized to fit the widest line + MeasureBuffer (see GetSize), but only
            // the real text is drawn here, and JumpKing's own GuiFormat.DrawMenuItems always draws
            // flush against the frame's left padding - never centered. That leaves a gap on the
            // right equal to the buffer's measured width. Shifting the draw position right by half
            // of that gap centers the text within the frame instead.
            string widestLine = GetWidestLine(text);
            float bufferWidth = base.Font.MeasureString(widestLine + MeasureBuffer).X - base.Font.MeasureString(widestLine).X;
            int offsetX = (int)(bufferWidth / 2f);
            base.Draw(x + offsetX, y, selected);
        }

        /// <summary>
        /// Appends MeasureBuffer to whichever line of text is actually the widest, instead of
        /// always the last one. The display text can be multiple lines (e.g. "PB: ..." above the
        /// current area, or the full Progression Detail list) and SpriteFont.MeasureString takes
        /// the widest line's width for multi-line strings - appending the buffer to the end of the
        /// whole string only padded the last line, so whenever an earlier line (like a long PB
        /// area name) was actually the widest, its systematic under-measurement went uncorrected
        /// and the frame ended up too narrow for it.
        /// </summary>
        private string AddBufferToWidestLine(string text)
        {
            string[] lines = text.Split('\n');
            int widestIndex = GetWidestLineIndex(lines);
            lines[widestIndex] += MeasureBuffer;
            return string.Join("\n", lines);
        }

        private string GetWidestLine(string text)
        {
            string[] lines = text.Split('\n');
            return lines[GetWidestLineIndex(lines)];
        }

        private int GetWidestLineIndex(string[] lines)
        {
            int widestIndex = 0;
            float widestWidth = base.Font.MeasureString(lines[0]).X;
            for (int i = 1; i < lines.Length; i++)
            {
                float width = base.Font.MeasureString(lines[i]).X;
                if (width > widestWidth)
                {
                    widestWidth = width;
                    widestIndex = i;
                }
            }
            return widestIndex;
        }
    }
}
