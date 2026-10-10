using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TabbedNotepad
{
    // Searching: the search box on the menu bar, the pop-up search box, the Find dialog,
    // highlighting matches and the match-count badges on the tabs.
    internal sealed partial class MainForm
    {
        private enum HighlightSource { None, SearchBox, FindDialog }

        private ToolStripTextBox _searchBox;
        private ToolStripButton _searchClear;
        private ToolStripComboBox _searchScope;
        private ToolStripLabel _searchCount;
        private FindPopup _findPopup;
        private Timer _searchTimer;
        private FindDialog _findDialog;
        private bool _syncingSearch;

        // What is highlighted right now, and the last search (for F3 / Shift+F3).
        private HighlightSource _highlightSource;
        private string _highlightTerm;
        private bool _highlightMatchCase;
        private bool _highlightAllTabs;
        private string _lastTerm;
        private bool _lastMatchCase;
        private bool _lastAllTabs;

        /// <summary>Matches per tab for the current search, shown as a number in a circle on each tab.</summary>
        private readonly Dictionary<NoteTab, int> _tabMatchCounts = new Dictionary<NoteTab, int>();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        private const int EM_SETCUEBANNER = 0x1501;

        private bool SearchAllTabs => _searchScope.SelectedIndex == 1;
        private bool SearchBoxFocused => (_searchBox != null && _searchBox.Focused) || (_findPopup != null && _findPopup.ContainsFocus);

        private static void SetCueBanner(TextBox box, string text)
        {
            box.HandleCreated += (s, e) =>
            {
                try { SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text); }
                catch (Exception ex) when (ex is EntryPointNotFoundException || ex is DllNotFoundException) { /* not on Windows */ }
            };
        }

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
            _searchScope.SelectedIndex = 0;
            _searchScope.SelectedIndexChanged += (s, e) =>
            {
                if (!_syncingSearch) _findPopup?.SetScope(_searchScope.SelectedIndex);
                UpdateSearchHighlights();
            };

            _searchClear = new ToolStripButton("Clear search", CloseImage())
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ImageScaling = ToolStripItemImageScaling.None,
                ToolTipText = "Clear the search (Esc)",
                Visible = false,
            };
            _searchClear.Click += (s, e) => ClearSearchBox();

            _searchBox = new ToolStripTextBox
            {
                Alignment = ToolStripItemAlignment.Right,
                AutoSize = false,
                Width = Dpi.Scale(170),
                BorderStyle = BorderStyle.FixedSingle,
                ToolTipText = "Search (Enter: next match, Shift+Enter: previous, Esc: clear)",
            };
            _searchBox.TextChanged += (s, e) => OnSearchTextChanged(_searchBox.Text, fromPopup: false);
            _searchBox.KeyDown += (s, e) => SearchKeyDown(e);
            SetCueBanner(_searchBox.TextBox, "Search...");

            _searchCount = new ToolStripLabel { Alignment = ToolStripItemAlignment.Right, ForeColor = SystemColors.GrayText };

            _searchTimer = new Timer { Interval = 250 };
            _searchTimer.Tick += (s, e) => { _searchTimer.Stop(); UpdateSearchHighlights(); };

            // Right-aligned items fill in from the right edge: scope, clear button, box, count.
            menu.Items.Add(_searchScope);
            menu.Items.Add(_searchClear);
            menu.Items.Add(_searchBox);
            menu.Items.Add(_searchCount);

            // The pop-up search box (Ctrl+F) works on the same search as the box on the menu bar.
            _findPopup = new FindPopup();
            SetCueBanner(_findPopup.SearchTextBox, "Search...");
            _findPopup.SearchTextChanged += (s, text) => OnSearchTextChanged(text, fromPopup: true);
            _findPopup.SearchKeyDown += (s, e) => SearchKeyDown(e);
            _findPopup.NextClicked += (s, e) => FindFromSearchBox(forward: true);
            _findPopup.PreviousClicked += (s, e) => FindFromSearchBox(forward: false);
            _findPopup.ScopeChanged += (s, index) =>
            {
                _syncingSearch = true;
                _searchScope.SelectedIndex = index;
                _syncingSearch = false;
            };
            _findPopup.CloseClicked += (s, e) => CloseFindPopup();
        }

        /// <summary>A small "x" for the clear-search button.</summary>
        private static Bitmap CloseImage()
        {
            int size = Dpi.Scale(16);
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            using (var pen = new Pen(Color.FromArgb(90, 90, 90), Math.Max(1.5f, Dpi.Scale(2) * 0.8f)))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float a = size * 0.3f, b = size * 0.7f;
                g.DrawLine(pen, a, a, b, b);
                g.DrawLine(pen, b, a, a, b);
            }
            return bmp;
        }

        private void OnSearchTextChanged(string text, bool fromPopup)
        {
            if (_syncingSearch) return;
            _syncingSearch = true;
            if (fromPopup) _searchBox.Text = text;
            else _findPopup.SearchText = text;
            _syncingSearch = false;
            _searchClear.Visible = text.Length > 0;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        /// <summary>Ctrl+F: the pop-up search box, or the box on the menu bar (View menu option).</summary>
        private void FocusSearchBox()
        {
            var editor = CurrentEditor;
            if (editor != null && editor.SelectionLength > 0 && editor.SelectionLength < 100 && editor.SelectedText.IndexOf('\n') < 0)
                _searchBox.Text = editor.SelectedText;

            if (_popupSearchItem.Checked)
            {
                ShowFindPopup();
            }
            else
            {
                _searchBox.Focus();
                _searchBox.SelectAll();
            }
        }

        private void ShowFindPopup()
        {
            if (_findPopup.Parent == null)
            {
                Controls.Add(_findPopup);
                _tabs.Layout += (s, e) => PositionFindPopup();
            }
            _findPopup.SetScope(_searchScope.SelectedIndex);
            _findPopup.CountText = _searchCount.Text;
            PositionFindPopup();
            _findPopup.Visible = true;
            _findPopup.BringToFront();
            _findPopup.FocusText();
        }

        /// <summary>Top right of the text, just under the tabs (like the search box in a web browser).</summary>
        private void PositionFindPopup()
        {
            if (_findPopup.Parent == null) return;
            Rectangle area = _tabs.DisplayRectangle;
            Point topRight = PointToClient(_tabs.PointToScreen(new Point(area.Right, area.Top)));
            _findPopup.Location = new Point(topRight.X - _findPopup.Width - Dpi.Scale(24), topRight.Y + Dpi.Scale(8));
        }

        private void CloseFindPopup()
        {
            ClearSearchBox();
            _findPopup.Visible = false;
        }

        private void ClearSearchBox()
        {
            _searchBox.Text = "";
            _searchTimer.Stop();
            UpdateSearchHighlights();
            CurrentEditor?.Focus();
        }

        private void SearchKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                FindFromSearchBox(forward: !e.Shift);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                if (_findPopup.Visible) CloseFindPopup();
                else ClearSearchBox();
            }
        }

        private void FindFromSearchBox(bool forward)
        {
            _searchTimer.Stop();
            UpdateSearchHighlights();
            if (_searchBox.Text.Length > 0)
                Find(_searchBox.Text, false, SearchAllTabs, forward, showNotFound: false);
        }

        private void SetSearchCount(string text)
        {
            _searchCount.Text = text;
            _findPopup.CountText = text;
        }

        /// <summary>Highlights the search text in this tab or all tabs and shows how many matches there are.</summary>
        private void UpdateSearchHighlights()
        {
            string term = _searchBox.Text;
            _searchClear.Visible = term.Length > 0;
            if (term.Length == 0)
            {
                if (_highlightSource == HighlightSource.SearchBox) ClearSearchHighlights();
                SetSearchCount("");
                _searchBox.BackColor = SystemColors.Window;
                _findPopup.NoMatches = false;
                return;
            }

            var (here, total, tabs) = HighlightAll(term, false, SearchAllTabs, HighlightSource.SearchBox);
            _lastTerm = term; _lastMatchCase = false; _lastAllTabs = SearchAllTabs;
            SetSearchCount(DescribeCount(here, total, tabs, SearchAllTabs));
            _searchBox.BackColor = total == 0 ? Color.FromArgb(255, 225, 225) : SystemColors.Window;
            _findPopup.NoMatches = total == 0;
        }

        private static string DescribeCount(int here, int total, int tabs, bool allTabs)
        {
            if (total == 0) return "No matches";
            string inTabs = total + (total == 1 ? " match" : " matches") + " in " + tabs + (tabs == 1 ? " tab" : " tabs");
            if (allTabs) return inTabs;
            return here + " here \u00B7 " + inTabs;
        }

        /// <summary>
        /// Highlights every match in the current tab (or all tabs) and counts the matches in every tab for
        /// the badges. Returns the matches in the current tab, in all tabs, and how many tabs have matches.
        /// </summary>
        private (int Here, int Total, int Tabs) HighlightAll(string term, bool matchCase, bool allTabs, HighlightSource source)
        {
            _highlightSource = source;
            _highlightTerm = term;
            _highlightMatchCase = matchCase;
            _highlightAllTabs = allTabs;

            int here = 0, total = 0, tabs = 0;
            _tabMatchCounts.Clear();
            foreach (var tab in AllTabs) if (!_tabs.TabPages.Contains(tab)) tab.Editor.SetHighlight(null, false);
            foreach (var tab in VisibleTabs)
            {
                bool include = allTabs || tab == CurrentTab;
                tab.Editor.SetHighlight(include ? term : null, matchCase);
                int count = include ? tab.Editor.HighlightPositions.Count : NoteEditor.FindAll(tab.Editor.PlainText, term, matchCase).Count;
                if (tab == CurrentTab) here = count;
                total += count;
                if (count > 0)
                {
                    tabs++;
                    _tabMatchCounts[tab] = count;
                }
            }
            UpdateTabBadges();
            return (here, total, tabs);
        }

        private void ClearSearchHighlights()
        {
            _highlightSource = HighlightSource.None;
            _highlightTerm = null;
            foreach (var tab in AllTabs) tab.Editor.SetHighlight(null, false);
            _tabMatchCounts.Clear();
            UpdateTabBadges();
        }

        /// <summary>Makes room on the tabs for the match-count circles while a search is active.</summary>
        private void UpdateTabBadges()
        {
            int basePadding = Dpi.Scale(12);
            int wanted = _tabMatchCounts.Count > 0 ? basePadding + BadgeSpace / 2 : basePadding;
            if (_tabs.Padding.X != wanted)
                _tabs.Padding = new Point(wanted, _tabs.Padding.Y);
            _tabs.Invalidate();
        }

        private int BadgeSpace => Dpi.Scale(26);

        /// <summary>When searching only "this tab", the highlights follow you to the newly selected tab.</summary>
        private void OnSearchTabChanged()
        {
            if (_highlightSource == HighlightSource.None || _highlightAllTabs) return;
            var (here, total, tabs) = HighlightAll(_highlightTerm, _highlightMatchCase, false, _highlightSource);
            if (_highlightSource == HighlightSource.SearchBox)
                SetSearchCount(DescribeCount(here, total, tabs, false));
        }

        /// <summary>Keeps the match counts up to date while typing in a tab.</summary>
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
                        var (here, total, tabs) = HighlightAll(text, matchCase, allTabs, HighlightSource.FindDialog);
                        _lastTerm = text; _lastMatchCase = matchCase; _lastAllTabs = allTabs;
                        string summary = DescribeCount(here, total, tabs, allTabs);
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
            foreach (var t in allTabs ? VisibleTabs : new[] { tab })
            {
                var positions = NoteEditor.FindAll(t.Editor.PlainText, text, matchCase);
                if (t == tab) before = total + positions.Count(p => p < position);
                total += positions.Count;
            }
            string summary = (before + 1) + " of " + total;
            if (string.Equals(_searchBox.Text, text, StringComparison.Ordinal) && !matchCase)
                SetSearchCount(summary + (allTabs ? "" : " here") + (_tabMatchCounts.Count > 1 ? " \u00B7 in " + _tabMatchCounts.Count + " tabs" : ""));
            SetStatus("Match " + summary + (allTabs ? " (all tabs)" : ""));
        }
    }
}
