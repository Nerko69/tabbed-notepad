using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Modeless Find dialog, like Notepad's, with an option to search every tab and a
    /// "Select All" button that highlights every match and shows how many there are.
    /// </summary>
    internal sealed class FindDialog : Form
    {
        private readonly TextBox _search;
        private readonly CheckBox _matchCase;
        private readonly CheckBox _allTabs;
        private readonly Label _result;
        private readonly Func<string, bool, bool, bool> _findNext;
        private readonly Func<string, bool, bool, string> _selectAll;

        public string SearchText { get => _search.Text; set => _search.Text = value; }
        public bool MatchCase => _matchCase.Checked;
        public bool AllTabs => _allTabs.Checked;

        /// <param name="findNext">Called with (text, matchCase, allTabs); returns whether a match was found.</param>
        /// <param name="selectAll">Called with (text, matchCase, allTabs); highlights all matches and returns a summary like "12 matches in 3 tabs".</param>
        public FindDialog(Func<string, bool, bool, bool> findNext, Func<string, bool, bool, string> selectAll)
        {
            _findNext = findNext;
            _selectAll = selectAll;

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
            var selectAllButton = new Button { Text = "Select All", AutoSize = true, Dock = DockStyle.Fill };
            _result = new Label { AutoSize = true, Margin = new Padding(3, 8, 3, 0), Font = new Font(Font, FontStyle.Bold) };
            var closeButton = new Button { Text = "Close", AutoSize = true, Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel };

            var left = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            left.Controls.Add(label, 0, 0);
            left.Controls.Add(_search, 1, 0);
            var options = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 8, 0, 0) };
            options.Controls.Add(_matchCase);
            options.Controls.Add(_allTabs);
            options.Controls.Add(_result);
            left.Controls.Add(options, 0, 1);
            left.SetColumnSpan(options, 2);

            var right = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Margin = new Padding(10, 0, 0, 0) };
            right.Controls.Add(findButton);
            right.Controls.Add(selectAllButton);
            right.Controls.Add(closeButton);

            layout.Controls.Add(left, 0, 0);
            layout.Controls.Add(right, 1, 0);
            Controls.Add(layout);

            AcceptButton = findButton;
            CancelButton = closeButton;

            findButton.Click += (s, e) => FindNext();
            selectAllButton.Click += (s, e) =>
            {
                if (_search.Text.Length > 0)
                    _result.Text = _selectAll(_search.Text, MatchCase, AllTabs);
            };
            closeButton.Click += (s, e) => Hide();
            _search.TextChanged += (s, e) =>
            {
                findButton.Enabled = selectAllButton.Enabled = _search.Text.Length > 0;
                _result.Text = "";
            };
            _matchCase.CheckedChanged += (s, e) => _result.Text = "";
            _allTabs.CheckedChanged += (s, e) => _result.Text = "";
            findButton.Enabled = selectAllButton.Enabled = false;

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

    /// <summary>
    /// Tools > Labels: the words you mark your notes with (e.g. AVADOMS, REGISTERED). Each gets a
    /// color and is highlighted wherever it appears; the Navigator lists where they are.
    /// </summary>
    internal sealed class LabelsDialog : Form
    {
        private readonly ListView _list;
        private readonly List<NoteLabel> _labels;
        private readonly ImageList _swatches = new ImageList();

        /// <summary>The label to insert at the cursor, if the user chose "Insert".</summary>
        public string InsertWord { get; private set; }

        public List<NoteLabel> Labels => _labels;

        public LabelsDialog(IEnumerable<NoteLabel> labels)
        {
            _labels = labels.Select(l => new NoteLabel { Word = l.Word, Color = l.Color }).ToList();

            Text = "Labels";
            Font = SystemFonts.MessageBoxFont;
            Size = new Size(Dpi.Scale(460), Dpi.Scale(420));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;

            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = Dpi.Scale(64),
                Padding = new Padding(Dpi.Scale(8), Dpi.Scale(8), Dpi.Scale(8), 0),
                Text = "Labels are words you use to mark your notes, like AVADOMS or REGISTERED. " +
                       "They are highlighted in their color wherever you type them (whole words, exact case), " +
                       "and the Navigator (F9) shows where they are.",
            };

            _swatches.ImageSize = new Size(Dpi.Scale(16), Dpi.Scale(16));
            _swatches.ColorDepth = ColorDepth.Depth32Bit;   // the default 8-bit depth shows pale colors as black
            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HeaderStyle = ColumnHeaderStyle.None,
                HideSelection = false,
                MultiSelect = false,
                SmallImageList = _swatches,
            };
            _list.Columns.Add("Label", Dpi.Scale(300));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(Dpi.Scale(6)) };
            var add = new Button { Text = "Add...", Width = Dpi.Scale(110) };
            var color = new Button { Text = "Change Color...", Width = Dpi.Scale(110) };
            var rename = new Button { Text = "Rename...", Width = Dpi.Scale(110) };
            var remove = new Button { Text = "Remove", Width = Dpi.Scale(110) };
            var insert = new Button { Text = "Insert in Note", Width = Dpi.Scale(110) };
            var ok = new Button { Text = "OK", Width = Dpi.Scale(110), DialogResult = DialogResult.OK, Margin = new Padding(3, Dpi.Scale(20), 3, 3) };
            var cancel = new Button { Text = "Cancel", Width = Dpi.Scale(110), DialogResult = DialogResult.Cancel };
            buttons.Controls.AddRange(new Control[] { add, color, rename, remove, insert, ok, cancel });

            Controls.Add(_list);
            Controls.Add(buttons);
            Controls.Add(intro);
            AcceptButton = ok;
            CancelButton = cancel;

            add.Click += (s, e) =>
            {
                string word = InputDialog.Ask(this, "Add Label", "Label word (one word, e.g. AVADOMS):", "");
                word = CleanWord(word);
                if (word == null) return;
                if (_labels.Any(l => l.Word == word)) return;
                var used = _labels.Select(l => l.Color);
                _labels.Add(new NoteLabel { Word = word, Color = TabColors.PickRandom(used) });
                Fill(word);
            };
            rename.Click += (s, e) =>
            {
                var label = Selected;
                if (label == null) return;
                string word = CleanWord(InputDialog.Ask(this, "Rename Label", "New word for this label:", label.Word));
                if (word == null || _labels.Any(l => l != label && l.Word == word)) return;
                label.Word = word;
                Fill(word);
            };
            color.Click += (s, e) =>
            {
                var label = Selected;
                if (label == null) return;
                using (var dialog = new ColorDialog { Color = label.Color, FullOpen = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        label.Color = dialog.Color;
                        Fill(label.Word);
                    }
            };
            remove.Click += (s, e) =>
            {
                var label = Selected;
                if (label == null) return;
                _labels.Remove(label);
                Fill(null);
            };
            insert.Click += (s, e) =>
            {
                if (Selected == null) return;
                InsertWord = Selected.Word;
                DialogResult = DialogResult.OK;
            };
            _list.DoubleClick += (s, e) => insert.PerformClick();
            Fill(null);
        }

        private NoteLabel Selected => _list.SelectedItems.Count > 0 ? (NoteLabel)_list.SelectedItems[0].Tag : null;

        /// <summary>Labels are single words: letters, digits, _ and -.</summary>
        private static string CleanWord(string word)
        {
            if (word == null) return null;
            word = new string(word.Trim().Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            return word.Length == 0 ? null : word;
        }

        private void Fill(string select)
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            _swatches.Images.Clear();
            foreach (var label in _labels.OrderBy(l => l.Word, StringComparer.OrdinalIgnoreCase))
            {
                _swatches.Images.Add(TabColors.Swatch(label.Color, _swatches.ImageSize.Width));
                var item = new ListViewItem(label.Word, _swatches.Images.Count - 1) { Tag = label };
                _list.Items.Add(item);
                if (label.Word == select) item.Selected = true;
            }
            _list.EndUpdate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _swatches.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>New Tab: the tab's name and, optionally, its category.</summary>
    internal sealed class NewTabDialog : Form
    {
        private readonly TextBox _name;
        private readonly ComboBox _category;
        private const string NoCategory = "(no category)";

        public string TabName => _name.Text.Trim();
        public string Category => _category.SelectedIndex <= 0 ? null : (string)_category.SelectedItem;

        public NewTabDialog(string suggestedName, string category)
        {
            Text = "New Tab";
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
            _name = new TextBox { Text = suggestedName, Width = Dpi.Scale(300), MaxLength = 100 };
            _category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.Scale(300) };
            _category.Items.Add(NoCategory);
            foreach (string c in TabCategories.Items) _category.Items.Add(c);
            int index = category == null ? 0 : _category.Items.IndexOf(category);
            _category.SelectedIndex = Math.Max(0, index);

            layout.Controls.Add(new Label { Text = "Name (for example a project name):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            layout.SetColumnSpan(layout.GetControlFromPosition(0, 0), 2);
            layout.Controls.Add(_name, 0, 1);
            layout.SetColumnSpan(_name, 2);
            layout.Controls.Add(new Label { Text = "Category:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 10, 3, 3) }, 0, 2);
            layout.SetColumnSpan(layout.GetControlFromPosition(0, 2), 2);
            layout.Controls.Add(_category, 0, 3);
            layout.SetColumnSpan(_category, 2);

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 2);
            Controls.Add(layout);

            AcceptButton = ok;
            CancelButton = cancel;
            Shown += (s, e) => { _name.SelectAll(); _name.Focus(); };
        }
    }

    /// <summary>Tools > Categories: add, rename, remove and order the tab categories.</summary>
    internal sealed class CategoriesDialog : Form
    {
        private readonly ListBox _list;

        public List<string> Categories => _list.Items.Cast<string>().ToList();

        /// <summary>Renames done in the dialog (old name to new name), so tabs can follow.</summary>
        public Dictionary<string, string> Renamed { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public CategoriesDialog(IEnumerable<string> categories, Func<string, int> tabsIn)
        {
            Text = "Tab Categories";
            Font = SystemFonts.MessageBoxFont;
            Size = new Size(Dpi.Scale(420), Dpi.Scale(400));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;

            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = Dpi.Scale(52),
                Padding = new Padding(Dpi.Scale(8), Dpi.Scale(8), Dpi.Scale(8), 0),
                Text = "Give each tab a category (right-click a tab > Category), then use the Category menu to show only the tabs of one category.",
            };
            _list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            foreach (string c in categories) _list.Items.Add(c);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(Dpi.Scale(6)) };
            Button B(string text) => new Button { Text = text, Width = Dpi.Scale(100) };
            var add = B("Add...");
            var rename = B("Rename...");
            var remove = B("Remove");
            var up = B("Move Up");
            var down = B("Move Down");
            var ok = B("OK");
            ok.DialogResult = DialogResult.OK;
            ok.Margin = new Padding(3, Dpi.Scale(20), 3, 3);
            var cancel = B("Cancel");
            cancel.DialogResult = DialogResult.Cancel;
            buttons.Controls.AddRange(new Control[] { add, rename, remove, up, down, ok, cancel });

            Controls.Add(_list);
            Controls.Add(buttons);
            Controls.Add(intro);
            AcceptButton = ok;
            CancelButton = cancel;

            add.Click += (s, e) =>
            {
                string name = Clean(InputDialog.Ask(this, "Add Category", "Category name:", ""));
                if (name == null || Contains(name)) return;
                _list.Items.Add(name);
                _list.SelectedItem = name;
            };
            rename.Click += (s, e) =>
            {
                if (!(_list.SelectedItem is string old)) return;
                string name = Clean(InputDialog.Ask(this, "Rename Category", "New name for this category:", old));
                if (name == null || name == old || (Contains(name) && !string.Equals(name, old, StringComparison.OrdinalIgnoreCase))) return;
                int i = _list.SelectedIndex;
                _list.Items[i] = name;
                // Remember the original name, also across several renames.
                string original = Renamed.FirstOrDefault(r => r.Value == old).Key ?? old;
                Renamed[original] = name;
            };
            remove.Click += (s, e) =>
            {
                if (!(_list.SelectedItem is string name)) return;
                int count = tabsIn(Renamed.FirstOrDefault(r => r.Value == name).Key ?? name);
                if (count > 0 && MessageBox.Show(this, count + (count == 1 ? " tab is" : " tabs are") + " in \"" + name + "\". They will have no category.\n\nRemove the category?",
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                _list.Items.Remove(name);
            };
            up.Click += (s, e) => MoveSelected(-1);
            down.Click += (s, e) => MoveSelected(+1);
        }

        private bool Contains(string name) => _list.Items.Cast<string>().Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

        private static string Clean(string name)
        {
            name = (name ?? "").Replace(";", "").Trim();
            return name.Length == 0 ? null : name;
        }

        private void MoveSelected(int delta)
        {
            int i = _list.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _list.Items.Count) return;
            object item = _list.Items[i];
            _list.Items.RemoveAt(i);
            _list.Items.Insert(j, item);
            _list.SelectedIndex = j;
        }
    }

    /// <summary>Help > About.</summary>
    internal sealed class AboutDialog : Form
    {
        public AboutDialog()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            string Attr<T>(Func<T, string> get) where T : Attribute =>
                assembly.GetCustomAttributes(typeof(T), false).OfType<T>().Select(get).FirstOrDefault() ?? "";

            Text = "About Tabbed Notepad";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Font = SystemFonts.MessageBoxFont;
            Padding = new Padding(Dpi.Scale(16));

            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
            var icon = new PictureBox { Image = Icon.ExtractAssociatedIcon(Application.ExecutablePath)?.ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 0, Dpi.Scale(14), 0) };
            var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
            text.Controls.Add(new Label { Text = Attr<System.Reflection.AssemblyProductAttribute>(a => a.Product), AutoSize = true, Font = new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold) });
            text.Controls.Add(new Label { Text = "Version " + Attr<System.Reflection.AssemblyFileVersionAttribute>(a => a.Version), AutoSize = true });
            text.Controls.Add(new Label { Text = Attr<System.Reflection.AssemblyTitleAttribute>(a => a.Title), AutoSize = true, Margin = new Padding(3, Dpi.Scale(10), 3, 3) });
            text.Controls.Add(new Label { Text = "Idea and product design: WebProgress.AI", AutoSize = true });
            text.Controls.Add(new Label { Text = Attr<System.Reflection.AssemblyCopyrightAttribute>(a => a.Copyright), AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, Dpi.Scale(10), 3, 3) });
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, Dpi.Scale(14), 3, 3) };

            layout.Controls.Add(icon, 0, 0);
            layout.Controls.Add(text, 1, 0);
            layout.Controls.Add(ok, 1, 1);
            Controls.Add(layout);
            AcceptButton = ok;
            CancelButton = ok;
        }
    }
}
