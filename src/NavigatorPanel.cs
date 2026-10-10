using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>
    /// Side panel (F9) listing what's in the current tab: date/separator lines, bookmarks, labels and
    /// links. Click an entry to jump to its line; double-click a label to find it in all tabs.
    /// </summary>
    internal sealed class NavigatorPanel : Panel
    {
        private readonly TreeView _tree;
        private readonly Label _title;
        private readonly HashSet<string> _collapsed = new HashSet<string>();

        /// <summary>The user clicked an entry: go to this line (0-based) in the current tab.</summary>
        public event EventHandler<int> LineSelected;

        /// <summary>The user double-clicked a label: search for it in all tabs.</summary>
        public event EventHandler<string> LabelSearchRequested;

        public event EventHandler CloseClicked;

        public NavigatorPanel()
        {
            Dock = DockStyle.Right;
            Width = Dpi.Scale(260);
            BackColor = SystemColors.Window;
            Font = SystemFonts.MessageBoxFont;

            var header = new Panel { Dock = DockStyle.Top, Height = Dpi.Scale(28), BackColor = Color.FromArgb(240, 240, 240) };
            _title = new Label { Text = "Navigator", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Dpi.Scale(6), 0, 0, 0), Font = new Font(Font, FontStyle.Bold) };
            var close = new Button { Text = "✕", Dock = DockStyle.Right, Width = Dpi.Scale(28), FlatStyle = FlatStyle.Flat, TabStop = false, Font = new Font("Segoe UI Symbol", 8f) };
            close.FlatAppearance.BorderSize = 0;
            new ToolTip().SetToolTip(close, "Hide the navigator (F9)");
            close.Click += (s, e) => CloseClicked?.Invoke(this, EventArgs.Empty);
            header.Controls.Add(_title);
            header.Controls.Add(close);

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                HideSelection = false,
                ShowLines = false,
                FullRowSelect = true,
                ShowNodeToolTips = true,
                Indent = Dpi.Scale(16),
                ItemHeight = Math.Max(Dpi.Scale(20), Font.Height + Dpi.Scale(4)),
            };
            _tree.NodeMouseClick += (s, e) =>
            {
                if (e.Node.Tag is int line) LineSelected?.Invoke(this, line);
            };
            _tree.NodeMouseDoubleClick += (s, e) =>
            {
                if (e.Node.Tag is NoteLabel label) LabelSearchRequested?.Invoke(this, label.Word);
            };
            _tree.AfterCollapse += (s, e) => { if (e.Node.Level == 0) _collapsed.Add(e.Node.Name); };
            _tree.AfterExpand += (s, e) => { if (e.Node.Level == 0) _collapsed.Remove(e.Node.Name); };

            var hint = new Label
            {
                Dock = DockStyle.Bottom,
                Height = Dpi.Scale(80),
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(Dpi.Scale(6), Dpi.Scale(4), Dpi.Scale(4), 0),
                Text = "Click an entry to jump to it. Double-click a label to find it in all tabs. Click a line number to bookmark a line.",
            };

            Controls.Add(_tree);
            Controls.Add(hint);
            Controls.Add(header);

            // A thin line between the panel and the text.
            Paint += (s, e) => { using (var p = new Pen(Color.FromArgb(220, 220, 220))) e.Graphics.DrawLine(p, 0, 0, 0, Height); };
            Padding = new Padding(1, 0, 0, 0);
        }

        /// <summary>Rebuilds the list for a tab.</summary>
        public void ShowTab(NoteTab tab)
        {
            _title.Text = tab == null ? "Navigator" : tab.Text;
            string selectedPath = _tree.SelectedNode?.FullPath;
            int scroll = _tree.Nodes.Count > 0 && _tree.TopNode != null ? _tree.TopNode.Index : 0;

            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            if (tab != null)
            {
                var editor = tab.Editor;

                var sections = Group("sections", "Dates & sections", editor.SectionLines.Count);
                foreach (int line in editor.SectionLines)
                    sections.Nodes.Add(Entry(NoteEditor.SectionTitle(editor.LineText(line)), line, editor.LineText(line)));

                var bookmarks = Group("bookmarks", "Bookmarks", editor.BookmarkLines.Count);
                foreach (int line in editor.BookmarkLines)
                    bookmarks.Nodes.Add(Entry("Ln " + (line + 1) + ": " + Shorten(editor.LineText(line)), line, editor.LineText(line)));

                var labelSpans = editor.LabelSpans;
                var labels = Group("labels", "Labels", labelSpans.Count);
                foreach (var label in NoteLabels.Items)
                {
                    var spans = labelSpans.Where(sp => sp.Label == label).ToList();
                    var node = new TreeNode(label.Word + "  (" + spans.Count + ")")
                    {
                        Tag = label,
                        ForeColor = spans.Count == 0 ? SystemColors.GrayText : SystemColors.WindowText,
                        BackColor = TabColors.Lighter(label.Color, 0.2),
                        ToolTipText = "Double-click to find " + label.Word + " in all tabs",
                    };
                    foreach (var line in spans.Select(sp => editor.LineFromChar(sp.Start)).Distinct())
                        node.Nodes.Add(Entry("Ln " + (line + 1) + ": " + Shorten(editor.LineText(line)), line, editor.LineText(line)));
                    labels.Nodes.Add(node);
                }
                if (NoteLabels.Items.Count == 0)
                    labels.Nodes.Add(new TreeNode("No labels yet: Tools > Labels...") { ForeColor = SystemColors.GrayText });

                var links = Group("links", "Links", editor.Links.Count);
                foreach (var link in editor.Links)
                    links.Nodes.Add(Entry(link.Url, editor.LineFromChar(link.Start), link.Url));

                foreach (var group in new[] { sections, bookmarks, labels, links })
                {
                    _tree.Nodes.Add(group);
                    if (!_collapsed.Contains(group.Name)) group.Expand();
                }
            }

            if (selectedPath != null)
            {
                var again = FindByPath(_tree.Nodes, selectedPath);
                if (again != null) _tree.SelectedNode = again;
            }
            if (scroll > 0 && scroll < _tree.Nodes.Count) _tree.TopNode = _tree.Nodes[scroll];
            _tree.EndUpdate();
        }

        private static TreeNode Group(string name, string text, int count) =>
            new TreeNode(text + "  (" + count + ")") { Name = name, NodeFont = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold) };

        private static TreeNode Entry(string text, int line, string toolTip) =>
            new TreeNode(text) { Tag = line, ToolTipText = "Line " + (line + 1) + ": " + toolTip };

        private static string Shorten(string text)
        {
            text = text.Trim();
            return text.Length > 60 ? text.Substring(0, 57) + "..." : text;
        }

        private static TreeNode FindByPath(TreeNodeCollection nodes, string path)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.FullPath == path) return n;
                var child = FindByPath(n.Nodes, path);
                if (child != null) return child;
            }
            return null;
        }
    }
}
