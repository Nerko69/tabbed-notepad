using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>A tab page holding one note: a strip in the tab's color, the editor and the link "copy" icon.</summary>
    internal sealed class NoteTab : TabPage
    {
        private readonly Panel _colorStrip;
        private readonly CopyLinkButton _copyButton;
        private readonly Timer _hideCopyTimer;
        private readonly ToolTip _toolTip;
        private Color _tabColor;

        /// <summary>The tab's file name in the notes folder, without ".txt" (for files opened from elsewhere: an internal id).</summary>
        public string Id { get; set; }
        public NoteEditor Editor { get; }
        public bool Dirty { get; set; }

        /// <summary>Full path of a text file opened from elsewhere; null for tabs kept in the notes folder.</summary>
        public string ExternalPath { get; set; }
        public Encoding FileEncoding { get; set; }
        public DateTime FileTimestampUtc { get; set; }
        public bool IsExternal => ExternalPath != null;

        /// <summary>Raised after a link was copied with the copy icon.</summary>
        public event EventHandler<string> LinkCopied;

        public NoteTab(string id, string title, string text)
        {
            Id = id;
            Text = title;
            UseVisualStyleBackColor = false;
            BackColor = SystemColors.Window;
            Padding = new Padding(Dpi.Scale(4), 0, 0, 0);

            Editor = new NoteEditor();
            Editor.Text = text ?? "";
            Editor.Select(0, 0);

            _colorStrip = new Panel { Dock = DockStyle.Top, Height = Dpi.Scale(4) };
            var gap = new Panel { Dock = DockStyle.Top, Height = Dpi.Scale(3), BackColor = SystemColors.Window };

            _copyButton = new CopyLinkButton();
            _toolTip = new ToolTip();
            _toolTip.SetToolTip(_copyButton, "Copy link");

            // Docked controls are laid out in reverse order of adding.
            Controls.Add(Editor);
            Controls.Add(gap);
            Controls.Add(_colorStrip);
            Controls.Add(_copyButton);
            _copyButton.BringToFront();

            _hideCopyTimer = new Timer { Interval = 600 };
            _hideCopyTimer.Tick += (s, e) => HideCopyButton();

            Editor.MouseMove += (s, e) => UpdateCopyButton(e.Location);
            Editor.MouseLeave += (s, e) => { if (_copyButton.Visible) _hideCopyTimer.Start(); };
            Editor.VScroll += (s, e) => HideCopyButton();
            Editor.HScroll += (s, e) => HideCopyButton();
            Editor.TextChanged += (s, e) => HideCopyButton();
            Editor.Resize += (s, e) => HideCopyButton();
            _copyButton.MouseEnter += (s, e) => _hideCopyTimer.Stop();
            _copyButton.MouseLeave += (s, e) => _hideCopyTimer.Start();
            _copyButton.Click += (s, e) => CopyLink();

            TabColor = SystemColors.Control;
        }

        public Color TabColor
        {
            get => _tabColor;
            set
            {
                _tabColor = value;
                _colorStrip.BackColor = value;
                Parent?.Invalidate();
            }
        }

        public NoteData ToData() => new NoteData
        {
            Id = Id,
            Title = Text,
            Text = Editor.PlainText,
            Color = TabColor,
            ExternalPath = ExternalPath,
            FileEncoding = FileEncoding,
            FileTimestampUtc = FileTimestampUtc,
        };

        private void UpdateCopyButton(Point mouse)
        {
            var link = Editor.LinkAtPoint(mouse);
            if (link == null)
            {
                if (_copyButton.Visible) _hideCopyTimer.Start();
                return;
            }

            _hideCopyTimer.Stop();
            var span = link.Value;
            if (_copyButton.Visible && _copyButton.Url == span.Url) return;

            // Place the icon right after the link's last character, vertically centred on its line.
            Point end = Editor.EndOfRange(span.Start, span.Length);
            Point inPage = PointToClient(Editor.PointToScreen(end));
            int y = inPage.Y + (Editor.Font.Height - _copyButton.Height) / 2;
            int x = Math.Min(inPage.X + Dpi.Scale(2), ClientSize.Width - _copyButton.Width - 1);
            _copyButton.Location = new Point(Math.Max(0, x), Math.Max(0, y));
            _copyButton.Url = span.Url;
            _copyButton.Visible = true;
            _copyButton.BringToFront();
        }

        public void HideCopyButton()
        {
            _hideCopyTimer.Stop();
            _copyButton.Visible = false;
        }

        private void CopyLink()
        {
            string url = _copyButton.Url;
            if (string.IsNullOrEmpty(url)) return;
            try
            {
                Clipboard.SetText(url);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return; // clipboard busy
            }
            _toolTip.Show("Copied!", _copyButton, 0, -_copyButton.Height - Dpi.Scale(4), 1200);
            LinkCopied?.Invoke(this, url);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hideCopyTimer.Dispose();
                _toolTip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>The colors tabs can have: light enough that black text stays easy to read.</summary>
    internal static class TabColors
    {
        public static readonly (string Name, Color Color)[] Palette =
        {
            ("Red", Color.FromArgb(0xFF, 0xC9, 0xC9)),
            ("Orange", Color.FromArgb(0xFF, 0xD8, 0xA8)),
            ("Yellow", Color.FromArgb(0xFF, 0xF0, 0x9E)),
            ("Lime", Color.FromArgb(0xD8, 0xF5, 0xA2)),
            ("Green", Color.FromArgb(0xB2, 0xF2, 0xBB)),
            ("Teal", Color.FromArgb(0x96, 0xF2, 0xD7)),
            ("Cyan", Color.FromArgb(0x99, 0xE9, 0xF2)),
            ("Blue", Color.FromArgb(0xA5, 0xD8, 0xFF)),
            ("Indigo", Color.FromArgb(0xBA, 0xC8, 0xFF)),
            ("Violet", Color.FromArgb(0xD0, 0xBF, 0xFF)),
            ("Pink", Color.FromArgb(0xFC, 0xC2, 0xD7)),
            ("Gray", Color.FromArgb(0xDE, 0xE2, 0xE6)),
        };

        private static readonly Random Random = new Random();

        /// <summary>
        /// A random palette color, preferring ones that no other tab uses yet and never the
        /// same as the tabs right next to it.
        /// </summary>
        public static Color PickRandom(IEnumerable<Color> usedColors, params Color?[] neighbours)
        {
            var used = usedColors.Select(c => c.ToArgb()).ToList();
            var near = neighbours.Where(c => c.HasValue).Select(c => c.Value.ToArgb()).ToList();
            var candidates = Palette.Select(p => p.Color).Where(c => !near.Contains(c.ToArgb())).ToList();
            int leastUse = candidates.Min(c => used.Count(u => u == c.ToArgb()));
            var best = candidates.Where(c => used.Count(u => u == c.ToArgb()) == leastUse).ToList();
            return best[Random.Next(best.Count)];
        }

        /// <summary>A darker shade of a tab color, for the selected tab's outline.</summary>
        public static Color Darker(Color c, double factor = 0.55) =>
            Color.FromArgb((int)(c.R * factor), (int)(c.G * factor), (int)(c.B * factor));

        /// <summary>A lighter version, for tabs that are not selected.</summary>
        public static Color Lighter(Color c, double amount = 0.35) =>
            Color.FromArgb(c.R + (int)((255 - c.R) * amount), c.G + (int)((255 - c.G) * amount), c.B + (int)((255 - c.B) * amount));

        public static Bitmap Swatch(Color color, int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            using (var fill = new SolidBrush(color))
            using (var pen = new Pen(Darker(color)))
            {
                g.FillRectangle(fill, 0, 0, size - 1, size - 1);
                g.DrawRectangle(pen, 0, 0, size - 1, size - 1);
            }
            return bmp;
        }
    }
}
