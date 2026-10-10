using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TabbedNotepad
{
    // Tab categories (and the filter that shows one category's tabs), file paths of tabs,
    // and the date/path header at the top of new tabs.
    internal sealed partial class MainForm
    {
        /// <summary>Filter value for "tabs without a category".</summary>
        private const string NoCategoryFilter = "\u0001none";

        /// <summary>Start of the "Path" line in a new tab's header.</summary>
        private const string PathHeaderPrefix = "Path\t\t";

        private string _categoryFilter;   // null = all tabs
        private ToolStripMenuItem _categoryMenu;

        private bool PassesFilter(NoteTab tab) =>
            _categoryFilter == null ||
            (_categoryFilter == NoCategoryFilter ? tab.Category == null : string.Equals(tab.Category, _categoryFilter, StringComparison.OrdinalIgnoreCase));

        private static string FilterName(string filter) =>
            filter == null ? "All" : filter == NoCategoryFilter ? "No category" : filter;

        private void UpdateCategoryMenuText()
        {
            _categoryMenu.Text = "Category: " + FilterName(_categoryFilter) + " ▾";
            _categoryMenu.Font = new System.Drawing.Font(_categoryMenu.Font, _categoryFilter == null ? System.Drawing.FontStyle.Regular : System.Drawing.FontStyle.Bold);
        }

        /// <summary>
        /// Shows only the tabs of one category (null: all tabs). The tabs that are hidden stay open and
        /// keep saving; only the tab strip changes.
        /// </summary>
        private void ApplyCategoryFilter(string filter, bool keepEmpty)
        {
            var current = CurrentTab;
            bool wasLoading = _loading;
            _loading = true;
            _tabs.SuspendLayout();
            try
            {
                _categoryFilter = filter;
                var shown = _allTabs.Where(PassesFilter).ToList();
                if (shown.Count == 0 && !keepEmpty && filter != null)
                {
                    _categoryFilter = null;
                    shown = _allTabs.ToList();
                }
                // Rebuild the tab strip in the saved order.
                foreach (var tab in VisibleTabs.ToList())
                    if (!shown.Contains(tab)) _tabs.TabPages.Remove(tab);
                for (int i = 0; i < shown.Count; i++)
                {
                    int at = _tabs.TabPages.IndexOf(shown[i]);
                    if (at == i) continue;
                    if (at >= 0) _tabs.TabPages.Remove(shown[i]);
                    _tabs.TabPages.Insert(i, shown[i]);
                }
                if (current != null && shown.Contains(current)) _tabs.SelectedTab = current;
                else if (_tabs.TabCount > 0) _tabs.SelectedIndex = 0;
            }
            finally
            {
                _tabs.ResumeLayout();
                _loading = wasLoading;
            }
            UpdateCategoryMenuText();
            _indexDirty = true;
            if (!_loading)
            {
                OnSelectedTabChanged();
                if (_highlightSource != HighlightSource.None) UpdateSearchHighlights();
            }
        }

        /// <summary>Category menu: show the tabs of one category.</summary>
        private void SetCategoryFilter(string filter)
        {
            if (filter != null && !_allTabs.Any(t => FilterMatches(t, filter)))
            {
                var answer = MessageBox.Show(this, "There are no tabs in \"" + FilterName(filter) + "\" yet.\n\nCreate a new tab in it?",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
                using (var dialog = new NewTabDialog(filter == NoCategoryFilter ? "New tab" : filter + " notes", filter == NoCategoryFilter ? null : filter))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK || dialog.TabName.Length == 0) return;
                    CreateTab(dialog.TabName, dialog.Category);   // switches to the new tab's category
                }
                return;
            }
            ApplyCategoryFilter(filter, keepEmpty: false);
            ScheduleSave();
            SetStatus(filter == null ? "Showing all tabs" : "Showing " + _tabs.TabCount + " of " + _allTabs.Count + " tabs (" + FilterName(filter) + ")");
        }

        private static bool FilterMatches(NoteTab tab, string filter) =>
            filter == NoCategoryFilter ? tab.Category == null : string.Equals(tab.Category, filter, StringComparison.OrdinalIgnoreCase);

        private void FillCategoryFilterMenu()
        {
            var items = _categoryMenu.DropDownItems;
            items.Clear();
            ToolStripMenuItem Option(string text, string filter, int count)
            {
                var item = new ToolStripMenuItem(text + "  (" + count + ")", null, (s, e) => SetCategoryFilter(filter))
                {
                    Checked = _categoryFilter == filter,
                };
                return item;
            }
            items.Add(Option("All tabs", null, _allTabs.Count));
            items.Add(new ToolStripSeparator());
            foreach (string category in TabCategories.Items)
                items.Add(Option(category, category, _allTabs.Count(t => FilterMatches(t, category))));
            items.Add(Option("No category", NoCategoryFilter, _allTabs.Count(t => t.Category == null)));
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Manage Categories...", null, (s, e) => ManageCategories()));
        }

        /// <summary>Right-click a tab > Category: put the tab in a category.</summary>
        private void FillTabCategoryMenu(ToolStripMenuItem menu)
        {
            var tab = CurrentTab;
            menu.DropDownItems.Clear();
            menu.DropDownItems.Add(new ToolStripMenuItem("(no category)", null, (s, e) => SetTabCategory(tab, null)) { Checked = tab?.Category == null });
            menu.DropDownItems.Add(new ToolStripSeparator());
            foreach (string category in TabCategories.Items)
            {
                string c = category;
                menu.DropDownItems.Add(new ToolStripMenuItem(c, null, (s, e) => SetTabCategory(tab, c))
                {
                    Checked = string.Equals(tab?.Category, c, StringComparison.OrdinalIgnoreCase),
                });
            }
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(new ToolStripMenuItem("Manage Categories...", null, (s, e) => ManageCategories()));
        }

        private void SetTabCategory(NoteTab tab, string category)
        {
            if (tab == null) return;
            tab.Category = category;
            UpdateTabToolTip(tab);
            _indexDirty = true;
            ScheduleSave();
            if (!PassesFilter(tab))
            {
                // It no longer belongs in the category being shown.
                ApplyCategoryFilter(_categoryFilter, keepEmpty: false);
                SetStatus("\"" + tab.Text + "\" moved to " + (category ?? "no category") + " (hidden by the Category filter)");
            }
            else
            {
                SetStatus("\"" + tab.Text + "\" is in " + (category ?? "no category"));
            }
        }

        private void ManageCategories()
        {
            using (var dialog = new CategoriesDialog(TabCategories.Items,
                name => _allTabs.Count(t => string.Equals(t.Category, name, StringComparison.OrdinalIgnoreCase))))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (var tab in _allTabs)
                {
                    if (tab.Category == null) continue;
                    var renamed = dialog.Renamed.FirstOrDefault(r => string.Equals(r.Key, tab.Category, StringComparison.OrdinalIgnoreCase));
                    if (renamed.Key != null) tab.Category = renamed.Value;
                    if (!dialog.Categories.Contains(tab.Category, StringComparer.OrdinalIgnoreCase)) tab.Category = null;
                    UpdateTabToolTip(tab);
                }
                if (_categoryFilter != null && _categoryFilter != NoCategoryFilter)
                {
                    var renamedFilter = dialog.Renamed.FirstOrDefault(r => string.Equals(r.Key, _categoryFilter, StringComparison.OrdinalIgnoreCase));
                    if (renamedFilter.Key != null) _categoryFilter = renamedFilter.Value;
                    if (!dialog.Categories.Contains(_categoryFilter, StringComparer.OrdinalIgnoreCase)) _categoryFilter = null;
                }
                TabCategories.Set(dialog.Categories);
                ApplyCategoryFilter(_categoryFilter, keepEmpty: false);
                _indexDirty = true;
                ScheduleSave();
            }
        }

        /// <summary>Shows a tab, switching back to all tabs if the category filter hides it.</summary>
        private void ShowTab(NoteTab tab)
        {
            if (!_tabs.TabPages.Contains(tab)) ApplyCategoryFilter(null, keepEmpty: false);
            _tabs.SelectedTab = tab;
        }

        // ---------------------------------------------------------------- file paths

        private string FilePathOf(NoteTab tab) => tab.IsExternal ? tab.ExternalPath : _store.NotePath(tab.Id);

        private void CopyFilePath(NoteTab tab)
        {
            if (tab == null) return;
            SaveAll(showStatus: false, quiet: true);   // make sure the file exists
            string path = FilePathOf(tab);
            try
            {
                Clipboard.SetText(path);
                SetStatus("Copied: " + path);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                SetStatus("The clipboard is busy; try again");
            }
        }

        /// <summary>Opens Explorer with the tab's file selected.</summary>
        private void ShowInFolder(NoteTab tab)
        {
            if (tab == null) return;
            SaveAll(showStatus: false, quiet: true);
            string path = FilePathOf(tab);
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(path) + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppName);
            }
        }

        /// <summary>
        /// When a tab's file moves (rename, Save All Tabs As), updates the "Path" line at the top of the
        /// tab, if it still shows the old path.
        /// </summary>
        private void UpdatePathHeader(NoteTab tab, string oldPath, string newPath)
        {
            var editor = tab.Editor;
            string oldLine = PathHeaderPrefix + oldPath;
            for (int line = 0; line < Math.Min(5, editor.LineCount); line++)
            {
                if (!string.Equals(editor.LineText(line).TrimEnd(), oldLine, StringComparison.OrdinalIgnoreCase)) continue;
                int caret = editor.SelectionStart;
                int start = editor.LineStart(line);
                int delta = newPath.Length - oldPath.Length;
                editor.Select(start, editor.LineEnd(line) - start);
                editor.ReplaceSelection(PathHeaderPrefix + newPath);
                editor.Select(caret > start ? caret + delta : caret, 0);
                return;
            }
        }
    }
}
