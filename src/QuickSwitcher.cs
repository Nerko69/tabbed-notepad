using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>
    /// Ctrl+P: type part of a tab's name (or category) and press Enter to go to it. With nothing
    /// typed it lists the most recently used tabs first. Includes tabs hidden by the Category filter.
    /// </summary>
    internal sealed class QuickSwitcher : Form
    {
        private readonly TextBox _query;
        private readonly ListBox _list;
        private readonly Label _hint;
        private readonly List<NoteTab> _tabs;   // most recently used first

        public NoteTab SelectedTab { get; private set; }

        /// <param name="tabsByRecentUse">All tabs, most recently used first.</param>
        public QuickSwitcher(IEnumerable<NoteTab> tabsByRecentUse)
        {
            _tabs = tabsByRecentUse.ToList();

            Text = "Go to Tab";
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            KeyPreview = true;
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(Dpi.Scale(460), Dpi.Scale(330));
            Padding = new Padding(Dpi.Scale(8));

            _query = new TextBox { Dock = DockStyle.Top, Font = new Font(Font.FontFamily, Font.Size * 1.2f) };
            _hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = Dpi.Scale(22),
                ForeColor = SystemColors.GrayText,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Type part of a tab's name or category · ↑↓ to choose · Enter to open · Esc to close",
            };
            _list = new ListBox
            {
                Dock = DockStyle.Fill,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Math.Max(Dpi.Scale(26), Font.Height + Dpi.Scale(8)),
                IntegralHeight = false,
                BorderStyle = BorderStyle.FixedSingle,
            };
            var gap = new Panel { Dock = DockStyle.Top, Height = Dpi.Scale(6) };

            Controls.Add(_list);
            Controls.Add(gap);
            Controls.Add(_query);
            Controls.Add(_hint);

            _query.TextChanged += (s, e) => Fill();
            _list.DrawItem += DrawEntry;
            _list.DoubleClick += (s, e) => Choose();
            KeyDown += OnKeyDown;
            Deactivate += (s, e) => { if (Visible && DialogResult == DialogResult.None) Close(); };
            Fill();
        }

        /// <summary>Lets callers (and tests) type a query.</summary>
        public string Query
        {
            get => _query.Text;
            set => _query.Text = value;
        }

        /// <summary>Tabs shown for the current query, best match first.</summary>
        public IReadOnlyList<NoteTab> Results => _list.Items.Cast<NoteTab>().ToList();

        private void Fill()
        {
            string q = _query.Text.Trim();
            IEnumerable<NoteTab> results = q.Length == 0
                ? _tabs
                : _tabs.Select((t, i) => (Tab: t, Score: Score(q, t.Text, t.Category), Recent: i))
                       .Where(x => x.Score > 0)
                       .OrderByDescending(x => x.Score).ThenBy(x => x.Recent)
                       .Select(x => x.Tab);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var tab in results) _list.Items.Add(tab);
            _list.EndUpdate();
            if (_list.Items.Count > 0) _list.SelectedIndex = q.Length == 0 && _list.Items.Count > 1 ? 1 : 0;   // empty query: previous tab, like Alt+Tab
        }

        /// <summary>
        /// How well a tab matches what was typed (0 = not at all): whole name, start of the name,
        /// start of a word, anywhere in the name, the category, or the letters in order ("prjal" finds "Project Alpha").
        /// </summary>
        public static int Score(string query, string title, string category)
        {
            string q = query.Trim().ToLowerInvariant();
            string t = (title ?? "").ToLowerInvariant();
            string c = (category ?? "").ToLowerInvariant();
            if (q.Length == 0) return 1;
            if (t == q) return 100;
            if (t.StartsWith(q)) return 80;
            if (t.Split(new[] { ' ', '-', '_', '.', '(', ')' }, StringSplitOptions.RemoveEmptyEntries).Any(w => w.StartsWith(q))) return 60;
            if (t.Contains(q)) return 40;
            if (c.Length > 0 && (c == q || c.StartsWith(q))) return 35;
            if (c.Contains(q)) return 25;
            int i = 0;
            foreach (char ch in t)
                if (i < q.Length && ch == q[i]) i++;
            return i == q.Length ? 10 : 0;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    e.SuppressKeyPress = true;
                    DialogResult = DialogResult.Cancel;
                    Close();
                    break;
                case Keys.Enter:
                    e.SuppressKeyPress = true;
                    Choose();
                    break;
                case Keys.Down:
                case Keys.Up:
                    if (_list.Items.Count == 0) break;
                    e.SuppressKeyPress = true;
                    int next = _list.SelectedIndex + (e.KeyCode == Keys.Down ? 1 : -1);
                    _list.SelectedIndex = (next + _list.Items.Count) % _list.Items.Count;
                    break;
            }
        }

        private void Choose()
        {
            if (!(_list.SelectedItem is NoteTab tab)) return;
            SelectedTab = tab;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void DrawEntry(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            var tab = (NoteTab)_list.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            var g = e.Graphics;
            Rectangle r = e.Bounds;
            using (var back = new SolidBrush(selected ? Color.FromArgb(220, 232, 252) : SystemColors.Window))
                g.FillRectangle(back, r);

            // The tab's color as a small square, its name, and its category on the right.
            int box = r.Height - Dpi.Scale(12);
            var swatch = new Rectangle(r.X + Dpi.Scale(6), r.Y + (r.Height - box) / 2, box, box);
            using (var fill = new SolidBrush(tab.TabColor))
            using (var pen = new Pen(TabColors.Darker(tab.TabColor)))
            {
                g.FillRectangle(fill, swatch);
                g.DrawRectangle(pen, swatch);
            }
            var textRect = new Rectangle(swatch.Right + Dpi.Scale(8), r.Y, r.Width - swatch.Right - Dpi.Scale(120), r.Height);
            using (var font = tab.IsExternal ? new Font(_list.Font, FontStyle.Italic) : null)
                TextRenderer.DrawText(g, tab.Text, font ?? _list.Font, textRect, SystemColors.WindowText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (tab.Category != null)
                TextRenderer.DrawText(g, tab.Category, _list.Font, new Rectangle(r.Right - Dpi.Scale(116), r.Y, Dpi.Scale(110), r.Height),
                    SystemColors.GrayText, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
