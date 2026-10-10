using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>
    /// Line numbers on the left of a note. Clicking a line number bookmarks that line (click again to
    /// remove the bookmark). Bookmarked lines get a blue dot here and a blue band in the text.
    /// </summary>
    internal sealed class LineNumberGutter : Control
    {
        private readonly NoteEditor _editor;
        private static readonly Color BookmarkDot = Color.FromArgb(40, 110, 230);

        public LineNumberGutter(NoteEditor editor)
        {
            _editor = editor;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Left;
            Cursor = Cursors.Hand;
            TabStop = false;
            BackColor = Color.FromArgb(246, 246, 246);
            ForeColor = Color.FromArgb(140, 140, 140);

            _editor.VScroll += (s, e) => Invalidate();
            _editor.TextChanged += (s, e) => { UpdateWidth(); Invalidate(); };
            _editor.FontChanged += (s, e) => { UpdateWidth(); Invalidate(); };
            _editor.Resize += (s, e) => Invalidate();
            _editor.MarksChanged += (s, e) => Invalidate();
            _editor.SelectionChanged += (s, e) => Invalidate();
            UpdateWidth();
        }

        private int DotSize => Math.Max(6, _editor.LineHeight / 2);

        private void UpdateWidth()
        {
            int digits = Math.Max(3, _editor.LineCount.ToString().Length);
            int width = TextRenderer.MeasureText(new string('9', digits), _editor.Font).Width + DotSize + Dpi.Scale(10);
            if (Width != width) Width = width;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            using (var edge = new Pen(Color.FromArgb(225, 225, 225)))
                g.DrawLine(edge, Width - 1, 0, Width - 1, Height);
            if (!_editor.IsHandleCreated) return;

            // The editor's text starts a bit lower than ours (color strip above it).
            int offset = PointToClient(_editor.PointToScreen(Point.Empty)).Y;
            int caretLine = _editor.LineFromChar(_editor.SelectionStart);
            string text = _editor.PlainText;
            int lineHeight = _editor.LineHeight;

            // Walk the screen rows that are visible; number the ones where a line starts
            // (a long line wrapped over several rows gets one number).
            int row = _editor.GetLineFromCharIndex(_editor.GetCharIndexFromPosition(new Point(1, 1)));
            using (var current = new SolidBrush(Color.FromArgb(60, 60, 60)))
            using (var dot = new SolidBrush(BookmarkDot))
            {
                for (; ; row++)
                {
                    int index = _editor.GetFirstCharIndexFromLine(row);
                    if (index < 0) break;
                    int y = _editor.PositionOf(index).Y + offset;
                    if (y > Height) break;
                    if (index > 0 && index <= text.Length && text[index - 1] != '\n') continue;   // wrapped continuation

                    int line = _editor.LineFromChar(index);
                    var rect = new Rectangle(DotSize + Dpi.Scale(4), y, Width - DotSize - Dpi.Scale(8), lineHeight);
                    TextRenderer.DrawText(g, (line + 1).ToString(), _editor.Font, rect,
                        line == caretLine ? current.Color : ForeColor,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                    if (_editor.IsBookmarked(line))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.FillEllipse(dot, Dpi.Scale(3), y + (lineHeight - DotSize) / 2, DotSize, DotSize);
                    }
                    if (index >= text.Length) break;
                }
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || _editor.TextLength == 0) return;
            int offset = PointToClient(_editor.PointToScreen(Point.Empty)).Y;
            int y = e.Y - offset;
            // Ignore clicks below the last line.
            if (y > _editor.PositionOf(_editor.TextLength).Y + _editor.LineHeight) return;
            int index = _editor.GetCharIndexFromPosition(new Point(1, Math.Max(0, y)));
            _editor.ToggleBookmark(_editor.LineFromChar(index));
        }
    }

    /// <summary>
    /// A thin strip at the right of a note showing where in the whole note the search matches
    /// (orange) and bookmarks (blue) are. Click a mark to jump there.
    /// </summary>
    internal sealed class MarkerBar : Control
    {
        private readonly NoteEditor _editor;
        private static readonly Color MatchColor = Color.FromArgb(240, 150, 0);
        private static readonly Color BookmarkColor = Color.FromArgb(40, 110, 230);

        public MarkerBar(NoteEditor editor)
        {
            _editor = editor;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Right;
            Width = Dpi.Scale(12);
            TabStop = false;
            BackColor = Color.FromArgb(246, 246, 246);
            _editor.TextChanged += (s, e) => Invalidate();
            _editor.MarksChanged += (s, e) => Invalidate();
            _editor.Resize += (s, e) => Invalidate();
        }

        private int MarkY(double position) => (int)(position * (Height - Dpi.Scale(4))) + Dpi.Scale(1);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (!_editor.IsHandleCreated) return;
            int h = Math.Max(2, Dpi.Scale(3));

            using (var match = new SolidBrush(MatchColor))
                foreach (int pos in _editor.HighlightPositions)
                    g.FillRectangle(match, Dpi.Scale(2), MarkY(_editor.RelativePosition(pos)), Width - Dpi.Scale(4), h);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var bookmark = new SolidBrush(BookmarkColor))
                foreach (int line in _editor.BookmarkLines)
                {
                    int size = Width - Dpi.Scale(4);
                    g.FillEllipse(bookmark, Dpi.Scale(2), MarkY(_editor.RelativePosition(_editor.LineStart(line))) - size / 2 + h / 2, size, size);
                }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int tolerance = Dpi.Scale(5);

            // Nearest match or bookmark to the click.
            int best = -1, bestDistance = int.MaxValue, bestLength = 0;
            foreach (int pos in _editor.HighlightPositions)
            {
                int d = Math.Abs(MarkY(_editor.RelativePosition(pos)) - e.Y);
                if (d < bestDistance) { best = pos; bestDistance = d; bestLength = _editor.HighlightTerm?.Length ?? 0; }
            }
            foreach (int line in _editor.BookmarkLines)
            {
                int pos = _editor.LineStart(line);
                int d = Math.Abs(MarkY(_editor.RelativePosition(pos)) - e.Y);
                if (d < bestDistance) { best = pos; bestDistance = d; bestLength = 0; }
            }
            if (best < 0 || bestDistance > tolerance) return;
            _editor.GoToLine(_editor.LineFromChar(best));
            _editor.Select(best, bestLength);
        }
    }
}
