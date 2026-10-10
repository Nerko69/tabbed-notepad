using System;
using System.Drawing;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>
    /// The pop-up search box (Ctrl+F) shown at the top right of the note, like the find bar of a web
    /// browser: search text, match count, previous/next, this tab or all tabs, and close.
    /// </summary>
    internal sealed class FindPopup : Panel
    {
        private readonly TextBox _text;
        private readonly Label _count;
        private readonly ComboBox _scope;
        private readonly ToolTip _tips = new ToolTip();
        private bool _setting;
        private bool _noMatches;

        public event EventHandler<string> SearchTextChanged;
        public event EventHandler<KeyEventArgs> SearchKeyDown;
        public event EventHandler NextClicked;
        public event EventHandler PreviousClicked;
        public event EventHandler<int> ScopeChanged;
        public event EventHandler CloseClicked;

        public FindPopup()
        {
            Visible = false;
            BackColor = Color.FromArgb(250, 250, 250);
            Padding = new Padding(Dpi.Scale(6));
            Font = SystemFonts.MessageBoxFont;
            Anchor = AnchorStyles.Top | AnchorStyles.Right;

            _text = new TextBox { Width = Dpi.Scale(190), BorderStyle = BorderStyle.FixedSingle };
            _count = new Label { AutoSize = false, Width = Dpi.Scale(150), TextAlign = ContentAlignment.MiddleLeft, ForeColor = SystemColors.GrayText };
            var previous = SmallButton("▲", "Previous match (Shift+Enter)");
            var next = SmallButton("▼", "Next match (Enter)");
            _scope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(80), FlatStyle = FlatStyle.System };
            _scope.Items.AddRange(new object[] { "This tab", "All tabs" });
            _scope.SelectedIndex = 0;
            var close = SmallButton("✕", "Close (Esc)");

            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Dock = DockStyle.Fill,
                BackColor = BackColor,
            };
            foreach (Control c in new Control[] { _text, _count, previous, next, _scope, close })
            {
                c.Margin = new Padding(Dpi.Scale(2), 0, Dpi.Scale(2), 0);
                c.Anchor = AnchorStyles.Left;
                row.Controls.Add(c);
            }
            Controls.Add(row);
            Size = new Size(row.PreferredSize.Width + Padding.Horizontal + 2, Math.Max(_text.PreferredHeight, _scope.PreferredHeight) + Padding.Vertical + 2);
            _count.Height = _text.Height;

            _text.TextChanged += (s, e) => { if (!_setting) SearchTextChanged?.Invoke(this, _text.Text); };
            _text.KeyDown += (s, e) => SearchKeyDown?.Invoke(this, e);
            previous.Click += (s, e) => PreviousClicked?.Invoke(this, EventArgs.Empty);
            next.Click += (s, e) => NextClicked?.Invoke(this, EventArgs.Empty);
            close.Click += (s, e) => CloseClicked?.Invoke(this, EventArgs.Empty);
            _scope.SelectedIndexChanged += (s, e) => { if (!_setting) ScopeChanged?.Invoke(this, _scope.SelectedIndex); _text.Focus(); };
        }

        private Button SmallButton(string text, string tip)
        {
            var b = new Button
            {
                Text = text,
                Width = Dpi.Scale(28),
                Height = Dpi.Scale(24),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Font = new Font("Segoe UI Symbol", 8f),
            };
            b.FlatAppearance.BorderSize = 0;
            _tips.SetToolTip(b, tip);
            return b;
        }

        public TextBox SearchTextBox => _text;

        public string SearchText
        {
            get => _text.Text;
            set
            {
                if (_text.Text == value) return;
                _setting = true;
                _text.Text = value;
                _setting = false;
            }
        }

        public string CountText
        {
            get => _count.Text;
            set => _count.Text = value;
        }

        public bool NoMatches
        {
            set
            {
                if (_noMatches == value) return;
                _noMatches = value;
                _text.BackColor = value ? Color.FromArgb(255, 225, 225) : SystemColors.Window;
            }
        }

        public void SetScope(int index)
        {
            if (_scope.SelectedIndex == index) return;
            _setting = true;
            _scope.SelectedIndex = index;
            _setting = false;
        }

        public void FocusText()
        {
            _text.Focus();
            _text.SelectAll();
        }

        protected override void Dispose(bool disposing)
        {
            // A ToolTip left for the garbage collector destroys its window from the finalizer thread,
            // which can crash the app as it exits.
            if (disposing) _tips.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Color.FromArgb(180, 180, 180)))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
