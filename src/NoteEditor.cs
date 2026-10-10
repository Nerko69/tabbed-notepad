using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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

    /// <summary>A label occurrence (e.g. AVADOMS) found in the text.</summary>
    internal struct LabelSpan
    {
        public int Start;
        public int Length;
        public NoteLabel Label;
    }

    /// <summary>
    /// The text area of a tab. A RichTextBox kept to plain text: it gives multi-level Undo/Redo
    /// and clickable links, while pasting, typing and saving only ever deal in plain text.
    /// On top of the text it paints bookmarked lines, date/separator lines, labels and search matches.
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

        /// <summary>
        /// A separator line such as "--------------2026-10-08" or "==== Monday ====": a run of at least
        /// five -, =, _, *, ~ or # characters, with or without text around it.
        /// </summary>
        private static readonly Regex SectionPattern = new Regex(@"([-=_*~#])\1{4,}", RegexOptions.Compiled);

        private string _highlightTerm;
        private bool _highlightMatchCase;

        // Caches, rebuilt when the text changes. Reading Text from the control is slow.
        private string _text;
        private int[] _lineStarts;
        private List<int> _highlights;
        private List<LinkSpan> _links;
        private List<int> _sectionLines;
        private List<LabelSpan> _labels;
        private int _labelsVersion = -1;

        // Bookmarks are kept as the start offsets of their lines and moved along as text is edited.
        private List<int> _bookmarks = new List<int>();
        private string _previousText = "";

        /// <summary>Raised when bookmarks or highlights change (the margins redraw).</summary>
        public event EventHandler MarksChanged;

        /// <summary>Raised when the user adds or removes a bookmark.</summary>
        public event EventHandler BookmarksChanged;

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
        }

        /// <summary>The text, with "\n" line breaks (positions match Select/SelectionStart).</summary>
        public string PlainText => _text ?? (_text = Text);

        protected override void OnTextChanged(EventArgs e)
        {
            _text = null;
            _lineStarts = null;
            _highlights = null;
            _links = null;
            _sectionLines = null;
            _labels = null;
            MoveBookmarksWithEdit();
            base.OnTextChanged(e);
        }

        /// <summary>
        /// Replaces the selection like typing would, so Undo can take it back. (Assigning
        /// SelectedText clears the undo history.)
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
            if (m.Msg == WM_PAINT && TextLength > 0)
                PaintMarks();
        }

        // ---------------------------------------------------------------- lines

        private int[] LineStarts
        {
            get
            {
                if (_lineStarts != null) return _lineStarts;
                string text = PlainText;
                var starts = new List<int> { 0 };
                for (int i = text.IndexOf('\n'); i >= 0; i = text.IndexOf('\n', i + 1))
                    starts.Add(i + 1);
                return _lineStarts = starts.ToArray();
            }
        }

        /// <summary>Number of lines as Notepad counts them (a wrapped line counts once).</summary>
        public int LineCount => LineStarts.Length;

        /// <summary>The (0-based) line a character is on.</summary>
        public int LineFromChar(int index)
        {
            int i = Array.BinarySearch(LineStarts, index);
            return i >= 0 ? i : ~i - 1;
        }

        public int LineStart(int line) => LineStarts[Math.Max(0, Math.Min(line, LineStarts.Length - 1))];

        public int LineEnd(int line) =>
            line + 1 < LineStarts.Length ? LineStarts[line + 1] - 1 : PlainText.Length;

        public string LineText(int line) => PlainText.Substring(LineStart(line), LineEnd(line) - LineStart(line));

        /// <summary>Screen position of a character; also works for the end of the text.</summary>
        public Point PositionOf(int index)
        {
            int length = TextLength;
            if (index < length || length == 0) return GetPositionFromCharIndex(Math.Max(0, index));
            Point last = GetPositionFromCharIndex(length - 1);
            if (PlainText[length - 1] == '\n')
                return new Point(GetPositionFromCharIndex(0).X, last.Y + LineHeight);
            return new Point(last.X + CharWidth(PlainText[length - 1]), last.Y);
        }

        public int LineHeight => Font.Height;

        private int CharWidth(char c) =>
            TextRenderer.MeasureText(c == '\n' || c == '\t' ? " " : c.ToString(), Font, Size.Empty, TextFormatFlags.NoPadding).Width;

        /// <summary>Moves the cursor to the start of a line and scrolls it into view (a few lines from the top).</summary>
        public void GoToLine(int line)
        {
            int start = LineStart(line);
            // Scroll so the line isn't glued to the bottom edge.
            int below = LineStart(Math.Min(line + 5, LineCount - 1));
            Select(below, 0);
            ScrollToCaret();
            Select(start, 0);
            ScrollToCaret();
            Focus();
        }

        /// <summary>The first and last character visible on screen.</summary>
        private void VisibleRange(out int first, out int last)
        {
            first = GetCharIndexFromPosition(Point.Empty);
            int lastLine = GetLineFromCharIndex(GetCharIndexFromPosition(new Point(ClientSize.Width, ClientSize.Height)));
            last = GetFirstCharIndexFromLine(lastLine + 1);
            if (last < 0) last = TextLength;
        }

        /// <summary>Position (0..1) of a character within the whole text, for the marker bar.</summary>
        public double RelativePosition(int index)
        {
            int total = GetLineFromCharIndex(TextLength) + 1;
            return total <= 1 ? 0 : GetLineFromCharIndex(Math.Min(index, TextLength)) / (double)total;
        }

        // ---------------------------------------------------------------- bookmarks

        /// <summary>Bookmarked lines (0-based), in order.</summary>
        public List<int> BookmarkLines => _bookmarks.Select(LineFromChar).Distinct().OrderBy(l => l).ToList();

        public bool IsBookmarked(int line) => _bookmarks.Contains(LineStart(line));

        public void ToggleBookmark(int line)
        {
            int start = LineStart(line);
            if (!_bookmarks.Remove(start)) _bookmarks.Add(start);
            _bookmarks.Sort();
            Invalidate();
            MarksChanged?.Invoke(this, EventArgs.Empty);
            BookmarksChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetBookmarkLines(IEnumerable<int> lines)
        {
            _bookmarks = lines.Where(l => l >= 0 && l < LineCount).Select(LineStart).Distinct().OrderBy(x => x).ToList();
            Invalidate();
            MarksChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Keeps bookmarks on their lines while typing: everything after the edited part moves by the
        /// number of characters added or removed.
        /// </summary>
        private void MoveBookmarksWithEdit()
        {
            string old = _previousText;
            string now = PlainText;
            _previousText = now;
            if (_bookmarks.Count == 0) return;

            int max = Math.Min(old.Length, now.Length);
            int prefix = 0;
            while (prefix < max && old[prefix] == now[prefix]) prefix++;
            int suffix = 0;
            while (suffix < max - prefix && old[old.Length - 1 - suffix] == now[now.Length - 1 - suffix]) suffix++;
            int delta = now.Length - old.Length;

            var moved = new List<int>();
            foreach (int offset in _bookmarks)
            {
                int o = offset <= prefix ? offset
                      : offset >= old.Length - suffix ? offset + delta
                      : prefix;                        // the line's start was inside the edited text
                o = Math.Max(0, Math.Min(o, now.Length));
                moved.Add(LineStart(LineFromChar(o)));
            }
            _bookmarks = moved.Distinct().OrderBy(x => x).ToList();
        }

        // ---------------------------------------------------------------- sections (date lines)

        public static bool IsSectionLine(string line) => SectionPattern.IsMatch(line);

        /// <summary>The text of a separator line without its dashes, e.g. "2026-10-08".</summary>
        public static string SectionTitle(string line)
        {
            string title = SectionPattern.Replace(line, " ").Trim();
            return title.Length > 0 ? title : "(separator)";
        }

        /// <summary>Lines that are separators / date lines.</summary>
        public List<int> SectionLines
        {
            get
            {
                if (_sectionLines != null) return _sectionLines;
                _sectionLines = new List<int>();
                for (int i = 0; i < LineCount; i++)
                    if (LineEnd(i) - LineStart(i) >= 5 && IsSectionLine(LineText(i)))
                        _sectionLines.Add(i);
                return _sectionLines;
            }
        }

        // ---------------------------------------------------------------- labels

        public List<LabelSpan> LabelSpans
        {
            get
            {
                if (_labels != null && _labelsVersion == NoteLabels.Version) return _labels;
                _labelsVersion = NoteLabels.Version;
                _labels = new List<LabelSpan>();
                var pattern = NoteLabels.Pattern;
                if (pattern == null) return _labels;
                foreach (Match m in pattern.Matches(PlainText))
                    _labels.Add(new LabelSpan { Start = m.Index, Length = m.Length, Label = NoteLabels.Find(m.Value) });
                return _labels;
            }
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

        public static IEnumerable<LinkSpan> FindLinks(string text)
        {
            foreach (Match m in UrlPattern.Matches(text))
            {
                // Leave out punctuation that usually ends a sentence rather than the link.
                string url = m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\'');
                if (url.EndsWith("(") || url.Length < 5) continue;
                yield return new LinkSpan { Start = m.Index, Length = url.Length, Url = url };
            }
        }

        public List<LinkSpan> Links => _links ?? (_links = FindLinks(PlainText).ToList());

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
                if (start.Y == end.Y)
                {
                    if (point.X < start.X || point.X > end.X || point.Y < start.Y || point.Y > start.Y + LineHeight) return null;
                }
                else if (point.Y < start.Y || point.Y > end.Y + LineHeight)
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
            return new Point(lastPos.X + CharWidth(PlainText[last]), lastPos.Y);
        }

        // ---------------------------------------------------------------- search highlighting

        /// <summary>Highlights every match of <paramref name="term"/>, or clears highlights when it is empty.</summary>
        public void SetHighlight(string term, bool matchCase)
        {
            if (string.IsNullOrEmpty(term)) term = null;
            if (term == _highlightTerm && matchCase == _highlightMatchCase) return;
            _highlightTerm = term;
            _highlightMatchCase = matchCase;
            _highlights = null;
            Invalidate();
            MarksChanged?.Invoke(this, EventArgs.Empty);
        }

        public string HighlightTerm => _highlightTerm;

        public IReadOnlyList<int> HighlightPositions =>
            _highlights ?? (_highlights = FindAll(PlainText, _highlightTerm, _highlightMatchCase));

        public static List<int> FindAll(string text, string term, bool matchCase)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(term)) return result;
            var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            for (int i = text.IndexOf(term, 0, comparison); i >= 0; i = text.IndexOf(term, i + term.Length, comparison))
                result.Add(i);
            return result;
        }

        // ---------------------------------------------------------------- painting

        private static readonly Color BookmarkColor = Color.FromArgb(60, 70, 140, 255);
        private static readonly Color SectionColor = Color.FromArgb(45, 120, 120, 120);
        private static readonly Color MatchFill = Color.FromArgb(70, 255, 225, 0);
        private static readonly Color MatchBorder = Color.FromArgb(170, 235, 170, 0);

        /// <summary>
        /// Paints light, see-through marks over the text: bookmarked lines, separator/date lines,
        /// labels and search matches. They're drawn after the text but transparent enough to read it.
        /// </summary>
        private void PaintMarks()
        {
            bool hasSearch = _highlightTerm != null && HighlightPositions.Count > 0;
            bool hasLabels = NoteLabels.Pattern != null;
            if (_bookmarks.Count == 0 && !hasSearch && !hasLabels && SectionLines.Count == 0) return;

            VisibleRange(out int first, out int last);
            int firstLine = LineFromChar(first), lastLine = LineFromChar(last);

            using (var g = Graphics.FromHwnd(Handle))
            {
                using (var brush = new SolidBrush(SectionColor))
                    foreach (int line in SectionLines)
                        if (line >= firstLine && line <= lastLine) FillLine(g, brush, line);

                using (var brush = new SolidBrush(BookmarkColor))
                    foreach (int line in BookmarkLines)
                        if (line >= firstLine && line <= lastLine) FillLine(g, brush, line);

                if (hasLabels)
                {
                    foreach (var span in LabelSpans)
                    {
                        if (span.Start + span.Length < first) continue;
                        if (span.Start > last) break;
                        using (var fill = new SolidBrush(Color.FromArgb(110, span.Label?.Color ?? Color.LightGreen)))
                        using (var border = new Pen(Color.FromArgb(200, TabColors.Darker(span.Label?.Color ?? Color.LightGreen, 0.7))))
                            FillRange(g, fill, border, span.Start, span.Length);
                    }
                }

                if (hasSearch)
                {
                    int length = _highlightTerm.Length;
                    int selStart = SelectionStart, selEnd = SelectionStart + SelectionLength;
                    using (var fill = new SolidBrush(MatchFill))
                    using (var border = new Pen(MatchBorder))
                    {
                        foreach (int start in HighlightPositions)
                        {
                            if (start + length < first) continue;
                            if (start > last) break;
                            // The current match is shown as the normal selection; don't tint it.
                            if (SelectionLength > 0 && start < selEnd && start + length > selStart) continue;
                            FillRange(g, fill, border, start, length);
                        }
                    }
                }
            }
        }

        /// <summary>Fills the full width of a line (all of its wrapped rows).</summary>
        private void FillLine(Graphics g, Brush brush, int line)
        {
            int top = PositionOf(LineStart(line)).Y;
            int bottom = PositionOf(LineEnd(line)).Y + LineHeight;
            g.FillRectangle(brush, 0, top, ClientSize.Width, Math.Max(LineHeight, bottom - top));
        }

        /// <summary>Fills a range of characters, one rectangle per screen row it covers.</summary>
        private void FillRange(Graphics g, Brush fill, Pen border, int start, int length)
        {
            int segStart = start;
            Point segPos = GetPositionFromCharIndex(segStart);
            for (int i = start + 1; i <= start + length; i++)
            {
                Point p = i < start + length ? GetPositionFromCharIndex(i) : Point.Empty;
                if (i == start + length || p.Y != segPos.Y)
                {
                    Point end = EndOfRange(segStart, i - segStart);
                    var rect = new Rectangle(segPos.X, segPos.Y, Math.Max(2, end.X - segPos.X), LineHeight);
                    g.FillRectangle(fill, rect);
                    if (border != null) g.DrawRectangle(border, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                    segStart = i;
                    segPos = p;
                }
            }
        }
    }

    /// <summary>A word you mark your notes with, such as AVADOMS or REGISTERED, and its color.</summary>
    internal sealed class NoteLabel
    {
        public string Word;
        public Color Color;
    }

    /// <summary>The labels defined by the user, shared by all tabs.</summary>
    internal static class NoteLabels
    {
        private static List<NoteLabel> _items = new List<NoteLabel>();

        public static IReadOnlyList<NoteLabel> Items => _items;

        /// <summary>Changes whenever the list changes, so editors know to look for labels again.</summary>
        public static int Version { get; private set; }

        /// <summary>Matches any label as a whole word (case-sensitive), or null if there are none.</summary>
        public static Regex Pattern { get; private set; }

        public static NoteLabel Find(string word) => _items.FirstOrDefault(l => l.Word == word);

        public static void Set(IEnumerable<NoteLabel> labels)
        {
            _items = labels.Where(l => !string.IsNullOrWhiteSpace(l.Word))
                .GroupBy(l => l.Word.Trim()).Select(g => new NoteLabel { Word = g.Key, Color = g.First().Color }).ToList();
            Pattern = _items.Count == 0 ? null : new Regex(
                @"(?<![\w])(?:" + string.Join("|", _items.OrderByDescending(l => l.Word.Length).Select(l => Regex.Escape(l.Word))) + @")(?![\w])");
            Version++;
        }

        /// <summary>Saved form: WORD:#RRGGBB;WORD2:#RRGGBB</summary>
        public static string ToSetting() =>
            string.Join(";", _items.Select(l => l.Word.Replace(";", "").Replace(":", "") + ":" + NoteStore.ColorToText(l.Color)));

        public static void FromSetting(string text)
        {
            var list = new List<NoteLabel>();
            foreach (string part in (text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = part.LastIndexOf(':');
                string word = colon > 0 ? part.Substring(0, colon).Trim() : part.Trim();
                Color color = colon > 0 && NoteStore.TryParseColor(part.Substring(colon + 1), out Color c) ? c : TabColors.Palette[3].Color;
                if (word.Length > 0) list.Add(new NoteLabel { Word = word, Color = color });
            }
            Set(list);
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
