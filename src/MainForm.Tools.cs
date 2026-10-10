using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TabbedNotepad
{
    // Organizing notes: line numbers and bookmarks, date lines, labels, the navigator panel,
    // the word counter and the list of links.
    internal sealed partial class MainForm
    {
        private static readonly Regex WordPattern = new Regex(@"\S+", RegexOptions.Compiled);

        // ---------------------------------------------------------------- view options

        private void SetLineNumbers(bool show)
        {
            _lineNumbersItem.Checked = show;
            foreach (var tab in AllTabs) tab.ShowLineNumbers = show;
            _indexDirty = true;
            ScheduleSave();
        }

        private void SetNavigator(bool show)
        {
            _navigatorItem.Checked = show;
            _navigator.Visible = show;
            _navigatorSplitter.Visible = show;
            if (show) _navigator.ShowTab(CurrentTab);
            _indexDirty = true;
            ScheduleSave();
        }

        // ---------------------------------------------------------------- word count and navigator

        private void ScheduleInfo()
        {
            _infoTimer.Stop();
            _infoTimer.Start();
        }

        /// <summary>Updates the word/character counter and the navigator for the current tab.</summary>
        private void UpdateInfo()
        {
            _infoTimer.Stop();
            var editor = CurrentEditor;
            if (editor == null) { _statsLabel.Text = ""; return; }

            string text = editor.PlainText;
            string Plural(int n, string word) => n.ToString("N0") + " " + word + (n == 1 ? "" : "s");
            string Describe(string t) =>
                Plural(WordPattern.Matches(t).Count, "word") + " · " + Plural(t.Length - t.Count(c => c == '\n'), "character");
            _statsLabel.Text = editor.SelectionLength > 0
                ? "Selected: " + Describe(editor.SelectedText.Replace("\r\n", "\n")) + " (of " + WordPattern.Matches(text).Count.ToString("N0") + " words)"
                : Describe(text);

            if (_navigator.Visible) _navigator.ShowTab(CurrentTab);
        }

        // ---------------------------------------------------------------- date lines and bookmarks

        /// <summary>
        /// Inserts a separator line with today's date, e.g. "------------------------------2026-10-10",
        /// on its own line. Such lines are shaded and listed in the navigator.
        /// </summary>
        private void InsertDateLine()
        {
            var editor = CurrentEditor;
            if (editor == null) return;
            string line = new string('-', 30) + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            int lineIndex = editor.LineFromChar(editor.SelectionStart);
            bool lineEmpty = editor.LineEnd(lineIndex) == editor.LineStart(lineIndex);
            if (lineEmpty)
            {
                editor.Select(editor.LineStart(lineIndex), 0);
                editor.ReplaceSelection(line + "\n");
            }
            else
            {
                editor.Select(editor.LineEnd(lineIndex), 0);
                editor.ReplaceSelection("\n" + line + "\n");
            }
        }

        private void ToggleBookmarkAtCursor()
        {
            var editor = CurrentEditor;
            if (editor == null) return;
            int line = editor.LineFromChar(editor.SelectionStart);
            editor.ToggleBookmark(line);
            SetStatus(editor.IsBookmarked(line) ? "Bookmarked line " + (line + 1) : "Bookmark removed");
        }

        /// <summary>F8 / Shift+F8: next or previous bookmark in this tab (wrapping around).</summary>
        private void GoToBookmark(int direction)
        {
            var editor = CurrentEditor;
            if (editor == null) return;
            var lines = editor.BookmarkLines;
            if (lines.Count == 0)
            {
                SetStatus("No bookmarks in this tab. Click a line number (or press Ctrl+F2) to add one.");
                return;
            }
            int current = editor.LineFromChar(editor.SelectionStart);
            var after = lines.Where(l => l > current).ToList();
            var before = lines.Where(l => l < current).ToList();
            int target = direction > 0
                ? (after.Count > 0 ? after[0] : lines[0])
                : (before.Count > 0 ? before[before.Count - 1] : lines[lines.Count - 1]);
            editor.GoToLine(target);
            SetStatus("Bookmark " + (lines.IndexOf(target) + 1) + " of " + lines.Count);
        }

        // ---------------------------------------------------------------- labels

        private void ShowLabels()
        {
            using (var dialog = new LabelsDialog(NoteLabels.Items))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ApplyLabels(dialog.Labels);
                if (dialog.InsertWord != null) CurrentEditor?.ReplaceSelection(dialog.InsertWord);
            }
        }

        private void AddSelectionAsLabel()
        {
            var editor = CurrentEditor;
            string word = editor?.SelectedText.Trim();
            if (string.IsNullOrEmpty(word) || word.Any(char.IsWhiteSpace))
            {
                MessageBox.Show(this, "Select (double-click) a single word first, for example AVADOMS.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (NoteLabels.Find(word) != null)
            {
                SetStatus("\"" + word + "\" is already a label");
                return;
            }
            var labels = NoteLabels.Items.ToList();
            labels.Add(new NoteLabel { Word = word, Color = TabColors.PickRandom(labels.Select(l => l.Color)) });
            ApplyLabels(labels);
            SetStatus("\"" + word + "\" is now a label");
        }

        private void ApplyLabels(IEnumerable<NoteLabel> labels)
        {
            NoteLabels.Set(labels);
            foreach (var tab in AllTabs) tab.Editor.Invalidate();
            _indexDirty = true;
            ScheduleSave();
            UpdateInfo();
        }

        /// <summary>Searches all tabs for a word (used by the navigator's labels).</summary>
        private void SearchAllTabsFor(string word)
        {
            _searchScope.SelectedIndex = 1;
            _searchBox.Text = word;
            _searchTimer.Stop();
            UpdateSearchHighlights();
            SetStatus("Tabs containing " + word + " show the number of matches; press Enter in the search box to go through them.");
        }

        // ---------------------------------------------------------------- links

        /// <summary>Opens the list of links for the current notes folder and adds the links already in the notes.</summary>
        private void OpenLinkStore()
        {
            _links = new LinkStore(_store.Folder);
            var now = DateTime.Now;
            foreach (var tab in AllTabs) _links.Update(tab.Text, tab.Editor.PlainText, now);
            try { _links.SaveIfChanged(); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException) { }
        }

        private HashSet<string> CurrentUrls() =>
            new HashSet<string>(AllTabs.SelectMany(t => t.Editor.Links.Select(l => l.Url)), StringComparer.Ordinal);

        private void ShowLinks()
        {
            SaveAll(showStatus: false, quiet: true);
            using (var dialog = new LinksDialog(_links, CurrentUrls(), GoToLink,
                () => _links.WriteWebPage(_store.Folder, CurrentUrls())))
            {
                dialog.ShowDialog(this);
            }
        }

        /// <summary>Shows a link in the first tab that contains it.</summary>
        private bool GoToLink(string url)
        {
            foreach (var tab in AllTabs)
            {
                var link = tab.Editor.Links.FirstOrDefault(l => l.Url == url);
                if (link.Url == null) continue;
                _tabs.SelectedTab = tab;
                tab.Editor.GoToLine(tab.Editor.LineFromChar(link.Start));
                tab.Editor.Select(link.Start, link.Length);
                return true;
            }
            return false;
        }
    }
}
