using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TabbedNotepad
{
    // Searching: the search box on the menu bar, the Find dialog, and highlighting matches.
    internal sealed partial class MainForm
    {
        private enum HighlightSource { None, SearchBox, FindDialog }

        private ToolStripTextBox _searchBox;
        private ToolStripComboBox _searchScope;
        private ToolStripLabel _searchCount;
        private Timer _searchTimer;
        private FindDialog _findDialog;

        // What is highlighted right now, and the last search (for F3 / Shift+F3).
        private HighlightSource _highlightSource;
        private string _highlightTerm;
        private bool _highlightMatchCase;
        private bool _highlightAllTabs;
        private string _lastTerm;
        private bool _lastMatchCase;
        private bool _lastAllTabs;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        private const int EM_SETCUEBANNER = 0x1501;

        private bool SearchAllTabs => _searchScope.SelectedIndex == 1;
        private bool SearchBoxFocused => _searchBox != null && _searchBox.Focused;

        private void AddSearchBar(MenuStrip menu)
        {
            _searchScope = new ToolStripComboBox
            {
                Alignment = ToolStripItemAlignment.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                AutoSize = false,
                Width = Dpi.Scale(80),
                FlatStyle = FlatStyle.System,
                ToolTipText = "Search only this tab, or all open tabs",
            };
            _searchScope.Items.AddRange(new object[] { "This tab", "All tabs" });
            _searchScope.SelectedIndex = 1;
            _searchScope.SelectedIndexChanged += (s, e) => { UpdateSearchHighlights(); _searchBox.Focus(); };

            _searchBox = new ToolStripTextBox
            {
                Alignment = ToolStripItemAlignment.Right,
                AutoSize = false,
                Width = Dpi.Scale(170),
                BorderStyle = BorderStyle.FixedSingle,
                ToolTipText = "Search (Ctrl+F). Enter: next match, Shift+Enter: previous, Esc: clear",
            };
            _searchBox.TextChanged += (s, e) => { _searchTimer.Stop(); _searchTimer.Start(); };
            _searchBox.KeyDown += SearchBox_KeyDown;
            // Grey hint text inside the empty box.
            _searchBox.TextBox.HandleCreated += (s, e) =>
            {
                try { SendMessage(_searchBox.TextBox.Handle, EM_SETCUEBANNER, (IntPtr)1, "Search tabs..."); }
                catch (Exception ex) when (ex is EntryPointNotFoundException || ex is DllNotFoundException) { /* not on Windows */ }
            };

            _searchCount = new ToolStripLabel { Alignment = ToolStripItemAlignment.Right, ForeColor = SystemColors.GrayText };

            _searchTimer = new Timer { Interval = 250 };
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); UpdateSearchHighlights(); };

            // Right-aligned items fill in from the right edge: scope, then box, then the count.
            menu.Items.Add(_searchScope);
            menu.Items.Add(_searchBox);
            menu.Items.Add(_searchCount);
        }

        private void FocusSearchBox()
        {
            var editor = CurrentEditor;
            if (editor != null && editor.SelectionLength > 0 && editor.SelectionLength < 100 && editor.SelectedText.IndexOf('\n') < 0)
                _searchBox.Text = editor.SelectedText;
            _searchBox.Focus();
            _searchBox.SelectAll();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _searchTimer.Stop();
                UpdateSearchHighlights();
                if (_searchBox.Text.Length > 0)
                    Find(_searchBox.Text, false, SearchAllTabs, forward: !e.Shift, showNotFound: false);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                _searchBox.Text = "";
                _searchTimer.Stop();
                UpdateSearchHighlights();
                CurrentEditor?.Focus();
            }
        }

        /// <summary>Highlights the search box's text in this tab or all tabs and shows how many matches there are.</summary>
        private void UpdateSearchHighlights()
        {
            string term = _searchBox.Text;
            if (term.Length == 0)
            {
                if (_highlightSource == HighlightSource.SearchBox) ClearSearchHighlights();
                _searchCount.Text = "";
                _searchBox.BackColor = SystemColors.Window;
                return;
            }

            var (total, tabs) = HighlightAll(term, false, SearchAllTabs, HighlightSource.SearchBox);
            _lastTerm = term; _lastMatchCase = false; _lastAllTabs = SearchAllTabs;
            _searchCount.Text = DescribeCount(total, tabs, SearchAllTabs);
            _searchBox.BackColor = total == 0 ? Color.FromArgb(255, 225, 225) : SystemColors.Window;
        }

        private static string DescribeCount(int total, int tabs, bool allTabs)
        {
            if (total == 0) return "No matches";
            string text = total + (total == 1 ? " match" : " matches");
            if (allTabs) text += " in " + tabs + (tabs == 1 ? " tab" : " tabs");
            return text;
        }

        /// <summary>Highlights every match in the current tab (or all tabs). Returns the number of matches and of tabs with matches.</summary>
        private (int Total, int Tabs) HighlightAll(string term, bool matchCase, bool allTabs, HighlightSource source)
        {
            _highlightSource = source;
            _highlightTerm = term;
            _highlightMatchCase = matchCase;
            _highlightAllTabs = allTabs;

            int total = 0, tabs = 0;
            foreach (var tab in AllTabs)
            {
                bool include = allTabs || tab == CurrentTab;
                tab.Editor.SetHighlight(include ? term : null, matchCase);
                if (!include) continue;
                int count = tab.Editor.HighlightPositions.Count;
                total += count;
                if (count > 0) tabs++;
            }
            return (total, tabs);
        }

        private void ClearSearchHighlights()
        {
            _highlightSource = HighlightSource.None;
            _highlightTerm = null;
            foreach (var tab in AllTabs) tab.Editor.SetHighlight(null, false);
        }

        /// <summary>When searching only "this tab", the highlights follow you to the newly selected tab.</summary>
        private void OnSearchTabChanged()
        {
            if (_highlightSource == HighlightSource.None || _highlightAllTabs) return;
            var (total, tabs) = HighlightAll(_highlightTerm, _highlightMatchCase, false, _highlightSource);
            if (_highlightSource == HighlightSource.SearchBox)
                _searchCount.Text = DescribeCount(total, tabs, false);
        }

        /// <summary>Keeps the match count up to date while typing in a tab.</summary>
        private void OnEditorTextChanged(NoteTab tab)
        {
            if (_highlightSource == HighlightSource.SearchBox && !_searchTimer.Enabled)
            {
                _searchTimer.Stop();
                _searchTimer.Start();
            }
        }

        // ---------------------------------------------------------------- Find dialog

        private void ShowFind()
        {
            if (_findDialog == null)
            {
                _findDialog = new FindDialog(
                    (text, matchCase, allTabs) => Find(text, matchCase, allTabs, forward: true, showNotFound: true),
                    (text, matchCase, allTabs) =>
                    {
                        var (total, tabs) = HighlightAll(text, matchCase, allTabs, HighlightSource.FindDialog);
                        _lastTerm = text; _lastMatchCase = matchCase; _lastAllTabs = allTabs;
                        string summary = DescribeCount(total, tabs, allTabs);
                        SetStatus(summary + " for \"" + text + "\"");
                        return summary;
                    });
                _findDialog.VisibleChanged += (s, e) =>
                {
                    if (!_findDialog.Visible && _highlightSource == HighlightSource.FindDialog)
                        ClearSearchHighlights();
                };
            }

            var editor = CurrentEditor;
            if (editor != null && editor.SelectionLength > 0 && editor.SelectionLength < 100 && editor.SelectedText.IndexOf('\n') < 0)
                _findDialog.SearchText = editor.SelectedText;

            if (!_findDialog.Visible)
                _findDialog.Show(this);
            _findDialog.Activate();
        }

        private void FindAgain(bool forward)
        {
            if (string.IsNullOrEmpty(_lastTerm))
            {
                FocusSearchBox();
                return;
            }
            Find(_lastTerm, _lastMatchCase, _lastAllTabs, forward, showNotFound: true);
        }

        /// <summary>
        /// Selects the next (or previous) match after the cursor: in the current tab, then the
        /// following tabs if <paramref name="allTabs"/>, wrapping around at the end.
        /// </summary>
        private bool Find(string text, bool matchCase, bool allTabs, bool forward, bool showNotFound)
        {
            _lastTerm = text; _lastMatchCase = matchCase; _lastAllTabs = allTabs;
            var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int count = _tabs.TabCount;
            int start = _tabs.SelectedIndex;
            if (start < 0 || string.IsNullOrEmpty(text)) return false;

            var editor = CurrentEditor;
            var order = new List<(NoteTab Tab, int From)>();
            if (forward)
            {
                order.Add((CurrentTab, editor.SelectionStart + editor.SelectionLength));
                if (allTabs)
                    for (int i = 1; i <= count; i++) order.Add(((NoteTab)_tabs.TabPages[(start + i) % count], 0));
                else
                    order.Add((CurrentTab, 0));
            }
            else
            {
                order.Add((CurrentTab, editor.SelectionStart - 1));
                if (allTabs)
                    for (int i = 1; i <= count; i++) order.Add(((NoteTab)_tabs.TabPages[(start - i + count) % count], int.MaxValue));
                else
                    order.Add((CurrentTab, int.MaxValue));
            }

            foreach (var (tab, from) in order)
            {
                string content = tab.Editor.PlainText;
                int found;
                if (forward)
                {
                    found = from <= content.Length ? content.IndexOf(text, from, comparison) : -1;
                }
                else
                {
                    int last = Math.Min(from, content.Length - 1);
                    found = last >= 0 ? content.LastIndexOf(text, last, comparison) : -1;
                }
                if (found < 0) continue;

                if (_tabs.SelectedTab != tab) _tabs.SelectedTab = tab;
                tab.Editor.Select(found, text.Length);
                tab.Editor.ScrollToCaret();
                UpdatePosition();
                ShowMatchNumber(tab, found, text, matchCase, allTabs);
                return true;
            }

            if (showNotFound)
            {
                MessageBox.Show(_findDialog != null && _findDialog.Visible ? (IWin32Window)_findDialog : this,
                    "Cannot find \"" + text + "\"", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                SetStatus("Cannot find \"" + text + "\"");
            }
            return false;
        }

        /// <summary>Shows "3 of 12" next to the search box after jumping to a match.</summary>
        private void ShowMatchNumber(NoteTab tab, int position, string text, bool matchCase, bool allTabs)
        {
            int before = 0, total = 0;
            foreach (var t in allTabs ? AllTabs : new[] { tab })
            {
                var positions = NoteEditor.FindAll(t.Editor.PlainText, text, matchCase);
                if (t == tab) before = total + positions.Count(p => p < position);
                total += positions.Count;
            }
            string summary = (before + 1) + " of " + total;
            if (string.Equals(_searchBox.Text, text, StringComparison.Ordinal) && !matchCase)
                _searchCount.Text = summary;
            SetStatus("Match " + summary + (allTabs ? " (all tabs)" : ""));
        }
    }
}
