using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>A link found in the text.</summary>
    internal struct LinkSpan
    {
        public int Start;
        public int Length;
        public string Url;
        public int End => Start + Length;
    }

    /// <summary>
    /// The text area of a tab. A RichTextBox kept to plain text: it gives multi-level Undo/Redo
    /// and clickable links, while pasting, typing and saving only ever deal in plain text.
    /// It can also highlight every match of a search term.
    /// </summary>
    internal sealed class NoteEditor : RichTextBox
    {
        private const int WM_PAINT = 0x000F;
        private const int WM_PASTE = 0x0302;
        private const int EM_REPLACESEL = 0x00C2;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private static readonly Regex UrlPattern = new Regex(
            @"\b(?:https?://|www\.)[^\s<>""'`]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private string _highlightTerm;
        private bool _highlightMatchCase;
        private List<int> _highlights;   // start positions of matches, cached until the text changes
        private List<LinkSpan> _links;   // cached until the text changes
        private string _text;            // reading Text from the control is slow, so it's cached

        public NoteEditor()
        {
            Multiline = true;
            AcceptsTab = true;
            DetectUrls = true;
            HideSelection = false;     // keep search results visible while the search box has focus
            BorderStyle = BorderStyle.None;
            Dock = DockStyle.Fill;
            WordWrap = true;
            ScrollBars = RichTextBoxScrollBars.Vertical;
            MaxLength = int.MaxValue;
            EnableAutoDragDrop = false;
            LinkClicked += (s, e) => OpenLink(e.LinkText);
            TextChanged += (s, e) => { _highlights = null; _links = null; _text = null; };
        }

        /// <summary>The text, with "\n" line breaks (positions match Select/SelectionStart).</summary>
        public string PlainText => _text ?? (_text = Text);

        /// <summary>
        /// Replaces the selection like typing would, so Undo can take it back. (Assigning
        /// SelectedText clears the undo history, which is why Undo seemed not to work before.)
        /// </summary>
        public void ReplaceSelection(string text)
        {
            if (IsHandleCreated)
            {
                try
                {
                    SendMessage(Handle, EM_REPLACESEL, (IntPtr)1, text);
                    return;
                }
                catch (Exception ex) when (ex is EntryPointNotFoundException || ex is DllNotFoundException)
                {
                    // Not running on Windows (only happens in testing).
                }
            }
            SelectedText = text;
        }

        /// <summary>Pastes the clipboard as plain text, never as formatted text or pictures.</summary>
        public void PastePlainText()
        {
            string text;
            try
            {
                if (!Clipboard.ContainsText()) return;
                text = Clipboard.GetText();
            }
            catch (ExternalException)
            {
                return; // clipboard is busy in another program
            }
            ReplaceSelection(text.Replace("\r\n", "\n").Replace('\r', '\n'));
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Menu shortcuts (Ctrl+T, Ctrl+S, ...) first.
            if (base.ProcessCmdKey(ref msg, keyData)) return true;

            if (keyData == (Keys.Shift | Keys.Insert) || keyData == (Keys.Control | Keys.V))
            {
                PastePlainText();
                return true;
            }

            // Swallow the rich-text shortcuts built into the control (Ctrl+E centres text,
            // Ctrl+1/2/5 change line spacing, Ctrl+= subscript, ...). This is a plain-text editor.
            Keys mods = keyData & Keys.Modifiers;
            Keys key = keyData & Keys.KeyCode;
            if ((mods & Keys.Control) != 0 && (mods & Keys.Alt) == 0 && !IsAllowedControlKey(key))
                return true;
            return false;
        }

        private static bool IsAllowedControlKey(Keys key)
        {
            switch (key)
            {
                case Keys.C: case Keys.X: case Keys.Z: case Keys.Y: case Keys.A:
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                case Keys.Back: case Keys.Delete: case Keys.Insert: case Keys.Tab:
                case Keys.ControlKey: case Keys.ShiftKey: case Keys.Menu:
                    return true;
                default:
                    return false;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PASTE)
            {
                PastePlainText();
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT && !string.IsNullOrEmpty(_highlightTerm))
                PaintHighlights();
        }

        // ---------------------------------------------------------------- links

        public static void OpenLink(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            url = url.Trim();
            if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) url = "http://" + url;
            // Only web links: a note shouldn't be able to start programs.
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
            try
            {
                System.Diagnostics.Process.Start(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open the link:\n" + url + "\n\n" + ex.Message, "Tabbed Notepad", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public List<LinkSpan> Links
        {
            get
            {
                if (_links != null) return _links;
                _links = new List<LinkSpan>();
                foreach (Match m in UrlPattern.Matches(PlainText))
                {
                    // Leave out punctuation that usually ends a sentence rather than the link.
                    string url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
                    if (url.EndsWith("(") || url.Length < 5) continue;
                    _links.Add(new LinkSpan { Start = m.Index, Length = url.Length, Url = url });
                }
                return _links;
            }
        }

        /// <summary>The link under a point (in this control's coordinates), if any.</summary>
        public LinkSpan? LinkAtPoint(Point point)
        {
            if (TextLength == 0) return null;
            int index = GetCharIndexFromPosition(point);
            foreach (var link in Links)
            {
                if (index < link.Start || index >= link.End) continue;
                // GetCharIndexFromPosition returns the nearest character even past the end of a
                // line, so also check that the point really is over the link's text.
                Point start = GetPositionFromCharIndex(link.Start);
                Point end = EndOfRange(link.Start, link.Length);
                int lineHeight = Font.Height;
                if (start.Y == end.Y)
                {
                    if (point.X < start.X || point.X > end.X || point.Y < start.Y || point.Y > start.Y + lineHeight) return null;
                }
                else if (point.Y < start.Y || point.Y > end.Y + lineHeight)
                {
                    return null;
                }
                return link;
            }
            return null;
        }

        /// <summary>The screen position just after the last character of a range (same line as that character).</summary>
        public Point EndOfRange(int start, int length)
        {
            int last = start + length - 1;
            Point lastPos = GetPositionFromCharIndex(last);
            Point after = GetPositionFromCharIndex(start + length);
            if (start + length < TextLength && after.Y == lastPos.Y && after.X > lastPos.X)
                return after;
            // The range ends a line (or the text): add the width of its last character.
            int width = TextRenderer.MeasureText(PlainText.Substring(last, 1), Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            return new Point(lastPos.X + width, lastPos.Y);
        }

        // ---------------------------------------------------------------- highlighting

        /// <summary>Highlights every match of <paramref name="term"/>, or clears highlights when it is empty.</summary>
        public void SetHighlight(string term, bool matchCase)
        {
            if (term == _highlightTerm && matchCase == _highlightMatchCase) return;
            _highlightTerm = string.IsNullOrEmpty(term) ? null : term;
            _highlightMatchCase = matchCase;
            _highlights = null;
            Invalidate();
        }

        public IReadOnlyList<int> HighlightPositions
        {
            get
            {
                if (_highlights != null) return _highlights;
                _highlights = FindAll(PlainText, _highlightTerm, _highlightMatchCase);
                return _highlights;
            }
        }

        public static List<int> FindAll(string text, string term, bool matchCase)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(term)) return result;
            var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            for (int i = text.IndexOf(term, 0, comparison); i >= 0; i = text.IndexOf(term, i + term.Length, comparison))
                result.Add(i);
            return result;
        }

        private void PaintHighlights()
        {
            var positions = HighlightPositions;
            if (positions.Count == 0) return;

            int firstVisible = GetCharIndexFromPosition(Point.Empty);
            int lastLine = GetLineFromCharIndex(GetCharIndexFromPosition(new Point(ClientSize.Width, ClientSize.Height)));
            int lastVisible = GetFirstCharIndexFromLine(lastLine + 1);
            if (lastVisible < 0) lastVisible = TextLength;
            int length = _highlightTerm.Length;
            int lineHeight = Font.Height;

            using (var g = Graphics.FromHwnd(Handle))
            using (var fill = new SolidBrush(Color.FromArgb(110, 255, 200, 0)))
            using (var border = new Pen(Color.FromArgb(200, 230, 150, 0)))
            {
                foreach (int start in positions)
                {
                    if (start + length < firstVisible) continue;
                    if (start > lastVisible) break;
                    // Draw one rectangle per line the match covers (it may wrap).
                    int segStart = start;
                    Point segPos = GetPositionFromCharIndex(segStart);
                    for (int i = start + 1; i <= start + length; i++)
                    {
                        Point p = i < start + length ? GetPositionFromCharIndex(i) : Point.Empty;
                        if (i == start + length || p.Y != segPos.Y)
                        {
                            Point end = EndOfRange(segStart, i - segStart);
                            var rect = new Rectangle(segPos.X, segPos.Y, Math.Max(2, end.X - segPos.X), lineHeight);
                            g.FillRectangle(fill, rect);
                            g.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                            segStart = i;
                            segPos = p;
                        }
                    }
                }
            }
        }
    }

    /// <summary>The small "copy" icon shown at the end of a link when the mouse is over it.</summary>
    internal sealed class CopyLinkButton : Control
    {
        private bool _hover;

        public CopyLinkButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Size = new Size(Dpi.Scale(20), Dpi.Scale(18));
            Cursor = Cursors.Hand;
            Visible = false;
            TabStop = false;
        }

        public string Url { get; set; }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_hover ? Color.FromArgb(225, 236, 252) : SystemColors.Window);
            using (var frame = new Pen(_hover ? Color.FromArgb(70, 120, 200) : Color.FromArgb(170, 170, 170)))
                g.DrawRectangle(frame, 0, 0, Width - 1, Height - 1);

            // Two overlapping "pages", the usual copy symbol.
            int s = Math.Min(Width, Height);
            int box = (int)(s * 0.42);
            int x = (Width - box) / 2 - s / 10, y = (Height - box) / 2 - s / 10;
            int off = Math.Max(2, s / 6);
            using (var pen = new Pen(Color.FromArgb(60, 60, 60), Math.Max(1, Dpi.Scale(1))))
            using (var paper = new SolidBrush(_hover ? Color.FromArgb(225, 236, 252) : SystemColors.Window))
            {
                g.DrawRectangle(pen, x, y, box, box);
                g.FillRectangle(paper, x + off, y + off, box, box);
                g.DrawRectangle(pen, x + off, y + off, box, box);
            }
        }
    }
}
