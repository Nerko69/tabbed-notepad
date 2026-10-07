using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>A tab page holding one note's editor.</summary>
    internal sealed class NoteTab : TabPage
    {
        /// <summary>The tab's file name in the notes folder, without ".txt".</summary>
        public string Id { get; set; }
        public TextBox Editor { get; }
        public bool Dirty { get; set; }

        public NoteTab(string id, string title, string text)
        {
            Id = id;
            Text = title;
            UseVisualStyleBackColor = false;
            Editor = new TextBox
            {
                Multiline = true,
                AcceptsTab = true,
                AcceptsReturn = true,
                MaxLength = 0,           // no 32K limit, daily logs grow
                HideSelection = false,   // keep Find results visible while the Find dialog has focus
                ScrollBars = ScrollBars.Both,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                Dock = DockStyle.Fill,
                Text = text ?? "",
            };
            // A WinForms TextBox selects all of its text the first time it gets focus unless a
            // selection was set, so one keystroke after switching tabs would replace the whole note.
            Editor.Select(0, 0);
            Controls.Add(Editor);
        }

        public NoteData ToData() => new NoteData { Id = Id, Title = Text, Text = Editor.Text };
    }

    internal sealed class MainForm : Form
    {
        private const string AppName = "Tabbed Notepad";

        private NoteStore _store;
        private readonly TabControl _tabs;
        private readonly ContextMenuStrip _tabMenu;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly ToolStripStatusLabel _folderLabel;
        private readonly ToolStripStatusLabel _positionLabel;
        private readonly ToolStripMenuItem _wordWrapItem;
        private readonly Timer _saveTimer;
        private FindDialog _findDialog;

        private Font _editorFont = new Font("Consolas", 11f);
        private bool _indexDirty;
        private bool _loading;
        private string _lastSaveError;

        // Tab drag-to-reorder state.
        private NoteTab _dragTab;
        private Point _dragStart;

        public MainForm(NoteStore store)
        {
            _store = store;

            Text = AppName;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = SystemFonts.MessageBoxFont;
            Size = new Size(Dpi.Scale(900), Dpi.Scale(650));
            StartPosition = FormStartPosition.WindowsDefaultLocation;

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(Dpi.Scale(12), Dpi.Scale(4)),
            };
            _tabs.SelectedIndexChanged += (s, e) => OnSelectedTabChanged();
            _tabs.MouseDown += Tabs_MouseDown;
            _tabs.MouseMove += Tabs_MouseMove;
            _tabs.MouseUp += Tabs_MouseUp;
            _tabs.MouseDoubleClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left && TabAt(e.Location) is NoteTab tab) RenameTab(tab);
            };

            _tabMenu = new ContextMenuStrip();
            _tabMenu.Items.Add("&Rename...", null, (s, e) => RenameTab(CurrentTab));
            _tabMenu.Items.Add("&New Tab", null, (s, e) => NewTab());
            _tabMenu.Items.Add(new ToolStripSeparator());
            _tabMenu.Items.Add("Move &Left", null, (s, e) => MoveCurrentTab(-1));
            _tabMenu.Items.Add("Move Righ&t", null, (s, e) => MoveCurrentTab(+1));
            _tabMenu.Items.Add(new ToolStripSeparator());
            _tabMenu.Items.Add("&Close Tab", null, (s, e) => CloseTab(CurrentTab));

            // Menus
            var menu = new MenuStrip();

            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(Item("&New Tab", Keys.Control | Keys.T, (s, e) => NewTab()));
            file.DropDownItems.Add(Item("&Rename Tab...", Keys.F2, (s, e) => RenameTab(CurrentTab)));
            file.DropDownItems.Add(Item("&Close Tab", Keys.Control | Keys.W, (s, e) => CloseTab(CurrentTab)));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("&Open...", Keys.Control | Keys.O, (s, e) => OpenFolder()));
            file.DropDownItems.Add(Item("&Save", Keys.Control | Keys.S, (s, e) => SaveNow()));
            file.DropDownItems.Add(Item("Save &All Tabs As...", Keys.Control | Keys.Shift | Keys.S, (s, e) => SaveAllTabsAs()));
            file.DropDownItems.Add(Item("Save This Tab As &Text File...", Keys.None, (s, e) => ExportCurrentTab()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("Show Notes &Folder", Keys.None, (s, e) => ShowNotesFolder()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("E&xit", Keys.None, (s, e) => Close()));

            var edit = new ToolStripMenuItem("&Edit");
            edit.DropDownItems.Add(Item("&Undo", Keys.Control | Keys.Z, (s, e) => CurrentEditor?.Undo()));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Cu&t", Keys.Control | Keys.X, (s, e) => CurrentEditor?.Cut()));
            edit.DropDownItems.Add(Item("&Copy", Keys.Control | Keys.C, (s, e) => CurrentEditor?.Copy()));
            edit.DropDownItems.Add(Item("&Paste", Keys.Control | Keys.V, (s, e) => PastePlainText()));
            edit.DropDownItems.Add(Item("De&lete", Keys.None, (s, e) => { if (CurrentEditor != null) CurrentEditor.SelectedText = ""; }));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("&Find...", Keys.Control | Keys.F, (s, e) => ShowFind()));
            edit.DropDownItems.Add(Item("Find &Next", Keys.F3, (s, e) => FindAgain()));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Select &All", Keys.Control | Keys.A, (s, e) => CurrentEditor?.SelectAll()));
            edit.DropDownItems.Add(Item("Time/&Date", Keys.F5, (s, e) => InsertTimeDate()));

            var format = new ToolStripMenuItem("F&ormat");
            _wordWrapItem = Item("&Word Wrap", Keys.None, (s, e) => SetWordWrap(!_wordWrapItem.Checked));
            format.DropDownItems.Add(_wordWrapItem);
            format.DropDownItems.Add(Item("&Font...", Keys.None, (s, e) => ChooseFont()));

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(Item("&Keyboard Shortcuts", Keys.None, (s, e) => ShowHelp()));

            // A "+" button on the menu bar to add a tab with one click.
            var plus = new ToolStripMenuItem("+ New Tab") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "New tab (Ctrl+T)" };
            plus.Click += (s, e) => NewTab();

            menu.Items.AddRange(new ToolStripItem[] { file, edit, format, help, plus });
            MainMenuStrip = menu;

            var status = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel { AutoSize = true };
            // Always show where the notes are saved; clicking it opens the folder.
            _folderLabel = new ToolStripStatusLabel
            {
                IsLink = true,
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                BorderSides = ToolStripStatusLabelBorderSides.Left,
            };
            _folderLabel.Click += (s, e) => ShowNotesFolder();
            _positionLabel = new ToolStripStatusLabel { AutoSize = true, BorderSides = ToolStripStatusLabelBorderSides.Left };
            status.Items.Add(_statusLabel);
            status.Items.Add(_folderLabel);
            status.Items.Add(_positionLabel);

            Controls.Add(_tabs);
            Controls.Add(menu);
            Controls.Add(status);

            // Save a moment after typing stops, so notes are never lost.
            _saveTimer = new Timer { Interval = 1500 };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); SaveAll(showStatus: false); };

            LoadNotes(applyWindowSettings: true);
        }

        private static ToolStripMenuItem Item(string text, Keys keys, EventHandler onClick)
        {
            var item = new ToolStripMenuItem(text, null, onClick);
            if (keys != Keys.None) item.ShortcutKeys = keys;
            return item;
        }

        private NoteTab CurrentTab => _tabs.SelectedTab as NoteTab;
        private TextBox CurrentEditor => CurrentTab?.Editor;
        private IEnumerable<NoteTab> AllTabs => _tabs.TabPages.Cast<NoteTab>();

        // ---------------------------------------------------------------- loading & saving

        /// <summary>Loads all tabs from the current notes folder, replacing any open tabs.</summary>
        private void LoadNotes(bool applyWindowSettings)
        {
            _loading = true;
            _tabs.SuspendLayout();
            foreach (var old in AllTabs.ToList())
            {
                _tabs.TabPages.Remove(old);
                old.Dispose();
            }
            List<NoteData> notes;
            try
            {
                notes = _store.Load();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not read your notes from\n" + _store.Folder + "\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                notes = new List<NoteData>();
            }

            var settings = _store.Settings;
            if (settings.TryGetValue("font", out string fontText))
            {
                try
                {
                    if (new FontConverter().ConvertFromInvariantString(fontText) is Font f) _editorFont = f;
                }
                catch { /* keep default font */ }
            }
            _wordWrapItem.Checked = !settings.TryGetValue("wordwrap", out string wrap) || wrap != "0";
            if (applyWindowSettings)
                RestoreWindowBounds(settings.TryGetValue("window", out string bounds) ? bounds : null);

            foreach (var note in notes)
                AddTab(new NoteTab(note.Id, note.Title, note.Text));

            if (_tabs.TabCount == 0)
            {
                AddTab(new NoteTab(UniqueId("My notes", null), "My notes", ""));
                _indexDirty = true;
            }

            if (settings.TryGetValue("selected", out string sel) && int.TryParse(sel, out int index) && index >= 0 && index < _tabs.TabCount)
                _tabs.SelectedIndex = index;

            _tabs.ResumeLayout();
            _loading = false;
            RenameFilesToTabNames();
            UpdateFolderLabel();
            OnSelectedTabChanged();
            SetStatus("Notes are saved automatically");
        }

        /// <summary>
        /// Gives every tab's file the tab's name (e.g. "Project A.txt"), so the notes folder is easy
        /// to understand. Also upgrades notes saved by version 1.0, whose files had generated names.
        /// </summary>
        private void RenameFilesToTabNames()
        {
            foreach (var tab in AllTabs)
            {
                string wanted = UniqueId(tab.Text, tab);
                if (wanted == tab.Id) continue;
                try
                {
                    _store.RenameNote(tab.Id, wanted);
                    tab.Id = wanted;
                    _indexDirty = true;
                }
                catch
                {
                    // Keep the old file name; the note itself is fine.
                }
            }
            if (_indexDirty) SaveAll(showStatus: false, quiet: true);
        }

        /// <summary>
        /// A file name for a tab called <paramref name="title"/> that no other tab, and no other
        /// file in the notes folder, is using. <paramref name="self"/> is the tab being named (or null).
        /// </summary>
        private string UniqueId(string title, NoteTab self)
        {
            var taken = AllTabs.Where(t => t != self).Select(t => t.Id).ToList();
            while (true)
            {
                string id = NoteStore.FileNameFor(title, taken);
                bool ownFile = self != null && string.Equals(id, self.Id, StringComparison.OrdinalIgnoreCase);
                if (ownFile || !_store.NoteFileExists(id)) return id;
                taken.Add(id);
            }
        }

        private void UpdateFolderLabel()
        {
            // Shorten very long paths from the middle; the tooltip always has the full path.
            string folder = _store.Folder;
            if (folder.Length > 60)
                folder = folder.Substring(0, 20) + "..." + folder.Substring(folder.Length - 35);
            _folderLabel.Text = "Notes folder: " + folder;
            _folderLabel.ToolTipText = _store.Folder + "\nClick to open this folder";
        }

        private void AddTab(NoteTab tab, int index = -1)
        {
            tab.Editor.Font = _editorFont;
            tab.Editor.WordWrap = _wordWrapItem.Checked;
            tab.Editor.ScrollBars = _wordWrapItem.Checked ? ScrollBars.Vertical : ScrollBars.Both;
            tab.Editor.TextChanged += (s, e) =>
            {
                if (_loading) return;
                tab.Dirty = true;
                ScheduleSave();
                UpdatePosition();
            };
            tab.Editor.KeyUp += (s, e) => UpdatePosition();
            tab.Editor.MouseUp += (s, e) => UpdatePosition();

            if (index < 0 || index >= _tabs.TabCount)
                _tabs.TabPages.Add(tab);
            else
                _tabs.TabPages.Insert(index, tab);
        }

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Writes changed tabs and the index to disk. Returns false if saving failed.</summary>
        private bool SaveAll(bool showStatus, bool quiet = false)
        {
            _saveTimer.Stop();
            try
            {
                foreach (var tab in AllTabs.Where(t => t.Dirty))
                {
                    _store.SaveNote(tab.ToData());
                    tab.Dirty = false;
                }

                var settings = _store.Settings;
                string selected = _tabs.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                string bounds = WindowBoundsText();
                if (!settings.TryGetValue("selected", out string oldSel) || oldSel != selected ||
                    !settings.TryGetValue("window", out string oldBounds) || oldBounds != bounds)
                {
                    settings["selected"] = selected;
                    settings["window"] = bounds;
                    _indexDirty = true;
                }
                if (_indexDirty)
                {
                    settings["font"] = new FontConverter().ConvertToInvariantString(_editorFont);
                    settings["wordwrap"] = _wordWrapItem.Checked ? "1" : "0";
                    _store.SaveIndex(AllTabs.Select(t => t.ToData()));
                    _indexDirty = false;
                }

                _lastSaveError = null;
                SetStatus((showStatus ? "Saved" : "All changes saved") + " at " + DateTime.Now.ToString("t"));
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Could not save: " + ex.Message);
                // Retry later; only pop up a message once per distinct error.
                ScheduleSave();
                if (!quiet && (_lastSaveError != ex.Message || showStatus))
                {
                    _lastSaveError = ex.Message;
                    MessageBox.Show(this, "Your notes could not be saved:\n\n" + ex.Message + "\n\nThe app will keep trying.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return false;
            }
        }

        private void SetStatus(string text) => _statusLabel.Text = text;

        // ---------------------------------------------------------------- tab actions

        private void NewTab()
        {
            string name = InputDialog.Ask(this, "New Tab", "Name for the new tab (for example a project name):", "Project " + (_tabs.TabCount + 1));
            if (name == null) return;

            var tab = new NoteTab(UniqueId(name, null), name, "") { Dirty = true };
            AddTab(tab);
            _tabs.SelectedTab = tab;
            _indexDirty = true;
            SaveAll(showStatus: false);
            tab.Editor.Focus();
        }

        private void RenameTab(NoteTab tab)
        {
            if (tab == null) return;
            string name = InputDialog.Ask(this, "Rename Tab", "New name for this tab:", tab.Text);
            if (name == null || name == tab.Text) return;

            // Save first so the file being renamed has the latest text, then rename it to match the tab.
            SaveAll(showStatus: false, quiet: true);
            string newId = UniqueId(name, tab);
            try
            {
                _store.RenameNote(tab.Id, newId);
                tab.Id = newId;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The tab was renamed, but its file could not be renamed:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            tab.Text = name;
            _indexDirty = true;
            UpdateTitle();
            SaveAll(showStatus: false);
        }

        private void CloseTab(NoteTab tab)
        {
            if (tab == null) return;
            if (tab.Editor.TextLength > 0)
            {
                var answer = MessageBox.Show(this,
                    "Close the tab \"" + tab.Text + "\"?\n\nIts text will be moved to the \"Closed tabs\" folder inside your notes folder, so you can still recover it.",
                    AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.OK) return;
            }

            try
            {
                _store.ArchiveNote(tab.ToData());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not close the tab:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Select the neighbouring tab first so focus never sits on a page being removed.
            if (_tabs.TabCount == 1)
                AddTab(new NoteTab(UniqueId("My notes", tab), "My notes", ""));
            int index = _tabs.TabPages.IndexOf(tab);
            _tabs.SelectedIndex = index + 1 < _tabs.TabCount ? index + 1 : index - 1;
            _tabs.TabPages.Remove(tab);
            tab.Dispose();
            _indexDirty = true;
            SaveAll(showStatus: false);
        }

        private void MoveCurrentTab(int delta)
        {
            var tab = CurrentTab;
            if (tab == null) return;
            MoveTab(tab, _tabs.TabPages.IndexOf(tab) + delta);
        }

        private void MoveTab(NoteTab tab, int newIndex)
        {
            int oldIndex = _tabs.TabPages.IndexOf(tab);
            if (newIndex < 0 || newIndex >= _tabs.TabCount || newIndex == oldIndex) return;

            _tabs.SuspendLayout();
            _tabs.TabPages.Remove(tab);
            _tabs.TabPages.Insert(newIndex, tab);
            _tabs.SelectedTab = tab;
            _tabs.ResumeLayout();
            _indexDirty = true;
            ScheduleSave();
        }

        private void OnSelectedTabChanged()
        {
            if (_loading) return;
            UpdateTitle();
            UpdatePosition();
            // Don't steal focus from the Find dialog when a search jumps to another tab.
            if (ActiveForm == this) CurrentEditor?.Focus();
            ScheduleSave();
        }

        private void UpdateTitle() => Text = CurrentTab == null ? AppName : CurrentTab.Text + " - " + AppName;

        private void UpdatePosition()
        {
            var editor = CurrentEditor;
            if (editor == null) { _positionLabel.Text = ""; return; }
            int pos = editor.SelectionStart;
            int line = editor.GetLineFromCharIndex(pos);
            int col = pos - editor.GetFirstCharIndexFromLine(line);
            _positionLabel.Text = "Ln " + (line + 1) + ", Col " + (col + 1);
        }

        private NoteTab TabAt(Point location)
        {
            for (int i = 0; i < _tabs.TabCount; i++)
                if (_tabs.GetTabRect(i).Contains(location))
                    return (NoteTab)_tabs.TabPages[i];
            return null;
        }

        private void Tabs_MouseDown(object sender, MouseEventArgs e)
        {
            _dragTab = e.Button == MouseButtons.Left ? TabAt(e.Location) : null;
            _dragStart = e.Location;
        }

        private void Tabs_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragTab == null || e.Button != MouseButtons.Left) return;
            if (Math.Abs(e.X - _dragStart.X) < SystemInformation.DragSize.Width) return;

            var over = TabAt(e.Location);
            if (over == null || over == _dragTab) return;

            // Only swap once the pointer is far enough into the other tab that, after the swap,
            // it is over the dragged tab again. Otherwise tabs of different widths flip back and forth.
            int from = _tabs.TabPages.IndexOf(_dragTab);
            int to = _tabs.TabPages.IndexOf(over);
            Rectangle target = _tabs.GetTabRect(to);
            int width = _tabs.GetTabRect(from).Width;
            bool farEnough = to > from ? e.X >= target.Right - width : e.X <= target.Left + width;
            if (farEnough)
                MoveTab(_dragTab, to);
        }

        private void Tabs_MouseUp(object sender, MouseEventArgs e)
        {
            _dragTab = null;
            var tab = TabAt(e.Location);
            if (tab == null) return;

            if (e.Button == MouseButtons.Middle)
            {
                CloseTab(tab);
            }
            else if (e.Button == MouseButtons.Right)
            {
                _tabs.SelectedTab = tab;
                _tabMenu.Show(_tabs, e.Location);
            }
        }

        // ---------------------------------------------------------------- editing

        private void PastePlainText()
        {
            var editor = CurrentEditor;
            if (editor == null) return;
            string text;
            try
            {
                if (!Clipboard.ContainsText()) return;
                text = Clipboard.GetText();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return; // clipboard is busy in another program
            }
            // Text copied from some programs uses bare \n line breaks, which a TextBox would not show.
            editor.SelectedText = text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        private void InsertTimeDate()
        {
            var editor = CurrentEditor;
            if (editor == null) return;
            DateTime now = DateTime.Now;
            editor.SelectedText = now.ToShortTimeString() + " " + now.ToShortDateString();
        }

        private void SetWordWrap(bool wrap)
        {
            _wordWrapItem.Checked = wrap;
            foreach (var tab in AllTabs)
            {
                tab.Editor.WordWrap = wrap;
                tab.Editor.ScrollBars = wrap ? ScrollBars.Vertical : ScrollBars.Both;
            }
            _indexDirty = true;
            ScheduleSave();
        }

        private void ChooseFont()
        {
            using (var dialog = new FontDialog { Font = _editorFont, ShowEffects = false })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                _editorFont = dialog.Font;
                foreach (var tab in AllTabs) tab.Editor.Font = _editorFont;
                _indexDirty = true;
                ScheduleSave();
            }
        }

        // ---------------------------------------------------------------- find

        private void ShowFind()
        {
            if (_findDialog == null)
                _findDialog = new FindDialog(Find);

            var editor = CurrentEditor;
            if (editor != null && editor.SelectionLength > 0 && editor.SelectedText.IndexOf('\n') < 0)
                _findDialog.SearchText = editor.SelectedText;

            if (!_findDialog.Visible)
                _findDialog.Show(this);
            _findDialog.Activate();
        }

        private void FindAgain()
        {
            if (_findDialog == null || _findDialog.SearchText.Length == 0)
                ShowFind();
            else
                _findDialog.FindNext();
        }

        private bool Find(string text, bool matchCase, bool allTabs)
        {
            var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int count = _tabs.TabCount;
            int start = _tabs.SelectedIndex;
            if (start < 0) return false;

            // Search the rest of the current tab, then the following tabs (or wrap around in this tab).
            var editor = CurrentEditor;
            int from = editor.SelectionStart + editor.SelectionLength;
            if (TrySelect(CurrentTab, text, from, comparison)) return true;

            if (allTabs)
            {
                for (int i = 1; i <= count; i++)
                    if (TrySelect((NoteTab)_tabs.TabPages[(start + i) % count], text, 0, comparison)) return true;
            }
            else if (TrySelect(CurrentTab, text, 0, comparison))
            {
                return true;
            }

            MessageBox.Show(_findDialog != null && _findDialog.Visible ? (IWin32Window)_findDialog : this,
                "Cannot find \"" + text + "\"", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        private bool TrySelect(NoteTab tab, string text, int from, StringComparison comparison)
        {
            string content = tab.Editor.Text;
            if (from > content.Length) return false;
            int found = content.IndexOf(text, from, comparison);
            if (found < 0) return false;

            if (_tabs.SelectedTab != tab) _tabs.SelectedTab = tab;
            tab.Editor.Select(found, text.Length);
            tab.Editor.ScrollToCaret();
            UpdatePosition();
            return true;
        }

        // ---------------------------------------------------------------- misc

        private void SaveNow()
        {
            _indexDirty = true;
            if (SaveAll(showStatus: true))
                SetStatus("All tabs saved at " + DateTime.Now.ToString("t"));
        }

        /// <summary>
        /// Like "Save As" in Notepad, but for all tabs at once: saves every tab into a folder the
        /// user picks, and keeps saving there from then on.
        /// </summary>
        private void SaveAllTabsAs()
        {
            SaveAll(showStatus: false, quiet: true);
            string folder = FolderPicker.Pick(this, "Save All Tabs As - choose a folder for your notes", "Save here", _store.Folder);
            if (folder == null) return;
            if (SameFolder(folder, _store.Folder))
            {
                SaveNow();
                return;
            }

            NoteStore target;
            try
            {
                target = new NoteStore(folder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not use this folder:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Never replace anything already in that folder without asking.
            var clashes = AllTabs.Where(t => target.NoteFileExists(t.Id)).Select(t => t.Id + ".txt").ToList();
            if (target.HasIndex || clashes.Count > 0)
            {
                string message = target.HasIndex
                    ? "This folder already has notes saved by " + AppName + ". Saving here replaces its tab list with your current tabs."
                    : "Some files in this folder have the same names as your tabs.";
                if (clashes.Count > 0)
                    message += "\n\nThese files will be replaced:\n  " + string.Join("\n  ", clashes.Take(10)) + (clashes.Count > 10 ? "\n  ..." : "");
                message += "\n\nContinue? (To open the notes in that folder instead, choose No and use File > Open.)";
                if (MessageBox.Show(this, message, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }

            var oldStore = _store;
            foreach (var pair in oldStore.Settings) target.Settings[pair.Key] = pair.Value;
            _store = target;
            foreach (var tab in AllTabs) tab.Dirty = true;
            _indexDirty = true;
            if (!SaveAll(showStatus: false, quiet: true))
            {
                _store = oldStore;
                _indexDirty = true;
                MessageBox.Show(this, "Your tabs could not be saved in\n" + folder + "\n\n" + _statusLabel.Text + "\n\nThey are still saved in\n" + oldStore.Folder,
                    AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                ScheduleSave();
                return;
            }

            RememberFolder(folder);
            UpdateFolderLabel();
            MessageBox.Show(this,
                "Saved " + _tabs.TabCount + (_tabs.TabCount == 1 ? " tab" : " tabs") + " in:\n" + folder +
                "\n\nFrom now on your notes are saved there automatically, one .txt file per tab." +
                "\n\nThe earlier copy in " + oldStore.Folder + " was left as it was.",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>Switches to the notes in another folder (or a folder of .txt files, each opened as a tab).</summary>
        private void OpenFolder()
        {
            if (!SaveAll(showStatus: false, quiet: true))
            {
                MessageBox.Show(this, "Your current notes could not be saved, so nothing else was opened:\n\n" + _statusLabel.Text, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string folder = FolderPicker.Pick(this, "Open - choose a folder with notes", "Open", _store.Folder);
            if (folder == null || SameFolder(folder, _store.Folder)) return;

            NoteStore target;
            int textFiles;
            try
            {
                target = new NoteStore(folder);
                textFiles = Directory.GetFiles(folder, "*.txt").Count(f => string.Equals(Path.GetExtension(f), ".txt", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open this folder:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (!target.HasIndex)
            {
                if (textFiles == 0)
                {
                    MessageBox.Show(this, "There are no notes (.txt files) in this folder.\n\nTo save your current tabs there, use File > Save All Tabs As.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show(this, "This folder has " + textFiles + " text " + (textFiles == 1 ? "file" : "files") + ". Open each one as a tab?\n\nChanges will be saved back to these files automatically.",
                        AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            _store = target;
            LoadNotes(applyWindowSettings: false);
            RememberFolder(folder);
            SetStatus("Opened " + _tabs.TabCount + (_tabs.TabCount == 1 ? " tab" : " tabs"));
        }

        private void RememberFolder(string folder)
        {
            try
            {
                AppConfig.SaveNotesFolder(folder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not remember this folder for next time:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static bool SameFolder(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private void ExportCurrentTab()
        {
            var tab = CurrentTab;
            if (tab == null) return;
            using (var dialog = new SaveFileDialog
            {
                Title = "Save This Tab As Text File",
                Filter = "Text documents (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = tab.Id + ".txt",
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, tab.Editor.Text, new System.Text.UTF8Encoding(false));
                    SetStatus("Copy of \"" + tab.Text + "\" saved to " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ShowNotesFolder()
        {
            SaveAll(showStatus: false);
            try { Process.Start("explorer.exe", "\"" + _store.Folder + "\""); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, AppName); }
        }

        private void ShowHelp()
        {
            MessageBox.Show(this,
                "Tabs\n" +
                "  Ctrl+T\tNew tab (or click \"+ New Tab\")\n" +
                "  F2\tRename tab (or double-click the tab)\n" +
                "  Ctrl+W\tClose tab (or middle-click the tab)\n" +
                "  Ctrl+Tab\tNext tab  (Ctrl+Shift+Tab: previous)\n" +
                "  Ctrl+1..9\tJump to tab 1..9\n" +
                "  Drag a tab to reorder; right-click for more\n\n" +
                "Editing\n" +
                "  F5\tInsert time and date\n" +
                "  Ctrl+F\tFind (can search all tabs)\n" +
                "  F3\tFind next\n" +
                "Saving\n" +
                "  Ctrl+S\tSave (saving is automatic anyway)\n" +
                "  Ctrl+Shift+S\tSave all tabs as... (pick a folder)\n" +
                "  Ctrl+O\tOpen notes from another folder\n\n" +
                "Your notes are saved automatically, one .txt file per tab, in:\n" + _store.Folder,
                "Keyboard Shortcuts - " + AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;

            // Ctrl+Tab / Ctrl+Shift+Tab and Ctrl+PageDown / Ctrl+PageUp switch tabs.
            if (_tabs.TabCount > 1 && (mods & Keys.Control) != 0 && (mods & Keys.Alt) == 0)
            {
                int step = 0;
                if (key == Keys.Tab) step = (mods & Keys.Shift) != 0 ? -1 : 1;
                else if (key == Keys.Next && mods == Keys.Control) step = 1;
                else if (key == Keys.Prior && mods == Keys.Control) step = -1;
                if (step != 0)
                {
                    _tabs.SelectedIndex = (_tabs.SelectedIndex + step + _tabs.TabCount) % _tabs.TabCount;
                    return true;
                }
            }

            // Ctrl+1..Ctrl+9 jump to a tab (Ctrl+9 = last tab, like browsers).
            if (mods == Keys.Control)
            {
                if (key >= Keys.D1 && key <= Keys.D9 && _tabs.TabCount > 0)
                {
                    int n = key - Keys.D1;
                    _tabs.SelectedIndex = key == Keys.D9 ? _tabs.TabCount - 1 : Math.Min(n, _tabs.TabCount - 1);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private string WindowBoundsText()
        {
            Rectangle r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            return string.Join(",", r.X, r.Y, r.Width, r.Height, WindowState == FormWindowState.Maximized ? 1 : 0);
        }

        private void RestoreWindowBounds(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var parts = text.Split(',');
            if (parts.Length < 5) return;
            var values = new int[5];
            for (int i = 0; i < 5; i++)
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i])) return;

            var r = new Rectangle(values[0], values[1], values[2], values[3]);
            if (r.Width < 200 || r.Height < 150) return;
            // Only restore if the window would be visible on a connected screen.
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r))) return;

            StartPosition = FormStartPosition.Manual;
            Bounds = r;
            if (values[4] == 1) WindowState = FormWindowState.Maximized;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (AllTabs.Any(t => t.Dirty)) SaveAll(showStatus: false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CurrentEditor?.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _indexDirty = true;
            if (!SaveAll(showStatus: false, quiet: true) && e.CloseReason == CloseReason.UserClosing)
            {
                var answer = MessageBox.Show(this,
                    "Your latest changes could not be saved:\n\n" + _statusLabel.Text + "\n\nClose anyway and lose them?",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            _saveTimer.Stop();
            base.OnFormClosing(e);
        }
    }
}
