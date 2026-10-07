using System;
using System.Drawing;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>Small "type a name" dialog used to name and rename tabs.</summary>
    internal sealed class InputDialog : Form
    {
        private readonly TextBox _input;

        public string Value => _input.Text.Trim();

        public InputDialog(string caption, string prompt, string initialValue)
        {
            Text = caption;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(10);

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            var label = new Label { Text = prompt, AutoSize = true, Margin = new Padding(3, 3, 3, 6) };
            _input = new TextBox { Text = initialValue, Width = Dpi.Scale(320), MaxLength = 100 };

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);

            layout.Controls.Add(label);
            layout.Controls.Add(_input);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (s, e) => { _input.SelectAll(); _input.Focus(); };
        }

        /// <summary>Shows the dialog and returns the entered text, or null if cancelled or left empty.</summary>
        public static string Ask(IWin32Window owner, string caption, string prompt, string initialValue)
        {
            using (var dialog = new InputDialog(caption, prompt, initialValue))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.Value.Length == 0)
                    return null;
                return dialog.Value;
            }
        }
    }

    /// <summary>Modeless Find dialog, like Notepad's, with an option to search every tab.</summary>
    internal sealed class FindDialog : Form
    {
        private readonly TextBox _search;
        private readonly CheckBox _matchCase;
        private readonly CheckBox _allTabs;
        private readonly Func<string, bool, bool, bool> _findNext;

        public string SearchText { get => _search.Text; set => _search.Text = value; }
        public bool MatchCase => _matchCase.Checked;
        public bool AllTabs => _allTabs.Checked;

        /// <param name="findNext">Called with (text, matchCase, allTabs); returns whether a match was found.</param>
        public FindDialog(Func<string, bool, bool, bool> findNext)
        {
            _findNext = findNext;

            Text = "Find";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(10);

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var label = new Label { Text = "Find what:", AutoSize = true, Anchor = AnchorStyles.Left };
            _search = new TextBox { Width = Dpi.Scale(260) };
            _matchCase = new CheckBox { Text = "Match case", AutoSize = true };
            _allTabs = new CheckBox { Text = "Search all tabs", AutoSize = true, Checked = true };
            var findButton = new Button { Text = "Find Next", AutoSize = true, Dock = DockStyle.Fill };
            var closeButton = new Button { Text = "Close", AutoSize = true, Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };

            var left = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            left.Controls.Add(label, 0, 0);
            left.Controls.Add(_search, 1, 0);
            var options = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 8, 0, 0) };
            options.Controls.Add(_matchCase);
            options.Controls.Add(_allTabs);
            left.Controls.Add(options, 0, 1);
            left.SetColumnSpan(options, 2);

            var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(10, 0, 0, 0) };
            right.Controls.Add(findButton);
            right.Controls.Add(closeButton);

            layout.Controls.Add(left, 0, 0);
            layout.Controls.Add(right, 1, 0);
            Controls.Add(layout);

            AcceptButton = findButton;
            CancelButton = closeButton;

            findButton.Click += (s, e) => FindNext();
            closeButton.Click += (s, e) => Hide();
            _search.TextChanged += (s, e) => findButton.Enabled = _search.Text.Length > 0;
            findButton.Enabled = false;

            // Keep the dialog around so it remembers the search text; just hide it.
            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
            Activated += (s, e) => { _search.SelectAll(); _search.Focus(); };
        }

        public void FindNext()
        {
            if (_search.Text.Length > 0)
                _findNext(_search.Text, MatchCase, AllTabs);
        }
    }
}
