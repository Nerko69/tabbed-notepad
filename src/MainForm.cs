using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TabbedNotepad
{
    internal sealed partial class MainForm : Form
    {
        private const string AppName = "Tabbed Notepad";
        private const string UserGuideUrl = "https://github.com/Nerko69/tabbed-notepad/blob/main/docs/user-guide.md";

        private NoteStore _store;
        private readonly TabControl _tabs;
        private readonly ContextMenuStrip _tabMenu;
        private readonly ContextMenuStrip _editorMenu;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly ToolStripStatusLabel _folderLabel;
        private readonly ToolStripStatusLabel _positionLabel;
        private readonly ToolStripMenuItem _wordWrapItem;
        private readonly ToolStripMenuItem _multiRowItem;
        private readonly ToolStripMenuItem _saveCopyToNotesItem;
        private readonly Timer _saveTimer;

        private Font _editorFont = new Font("Consolas", 11f);
        private bool _indexDirty;
        private bool _loading;
        private bool _resolvingConflict;
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
            Size = new Size(Dpi.Scale(1000), Dpi.Scale(680));
            StartPosition = FormStartPosition.WindowsDefaultLocation;

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Padding = new Point(Dpi.Scale(12), Dpi.Scale(5)),
                DrawMode = TabDrawMode.OwnerDrawFixed,
                Appearance = TabAppearance.FlatButtons,   // rows of tabs stay in place when switching
                ShowToolTips = true,
                Multiline = true,
            };
            _tabs.DrawItem += Tabs_DrawItem;
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
            _tabMenu.Items.Add(BuildColorMenu("Tab &Color"));
            _tabMenu.Items.Add("&New Tab", null, (s, e) => NewTab());
            _tabMenu.Items.Add(new ToolStripSeparator());
            _tabMenu.Items.Add("Move &Left", null, (s, e) => MoveCurrentTab(-1));
            _tabMenu.Items.Add("Move Righ&t", null, (s, e) => MoveCurrentTab(+1));
            _tabMenu.Items.Add(new ToolStripSeparator());
            var tabMenuSaveCopy = new ToolStripMenuItem("Save a Copy in Notes &Folder", null, (s, e) => SaveCopyInNotesFolder(CurrentTab));
            _tabMenu.Items.Add(tabMenuSaveCopy);
            _tabMenu.Items.Add("&Close Tab", null, (s, e) => CloseTab(CurrentTab));
            _tabMenu.Opening += (s, e) => tabMenuSaveCopy.Visible = CurrentTab?.IsExternal == true;

            _editorMenu = BuildEditorMenu();

            // Menus
            var menu = new MenuStrip();

            var file = new ToolStripMenuItem("&File");
            file.DropDownItems.Add(Item("&New Tab", Keys.Control | Keys.T, (s, e) => NewTab()));
            file.DropDownItems.Add(Item("&Rename Tab...", Keys.F2, (s, e) => RenameTab(CurrentTab)));
            file.DropDownItems.Add(Item("&Close Tab", Keys.Control | Keys.W, (s, e) => CloseTab(CurrentTab)));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("&Open Text File...", Keys.Control | Keys.O, (s, e) => OpenTextFiles()));
            file.DropDownItems.Add(Item("Open &Folder...", Keys.Control | Keys.Shift | Keys.O, (s, e) => OpenFolder()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("&Save", Keys.Control | Keys.S, (s, e) => SaveNow()));
            file.DropDownItems.Add(Item("Save &All Tabs As...", Keys.Control | Keys.Shift | Keys.S, (s, e) => SaveAllTabsAs()));
            file.DropDownItems.Add(Item("Save a Cop&y of This Tab As...", Keys.None, (s, e) => ExportCurrentTab()));
            _saveCopyToNotesItem = Item("Save a Copy in Notes F&older", Keys.None, (s, e) => SaveCopyInNotesFolder(CurrentTab));
            file.DropDownItems.Add(_saveCopyToNotesItem);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("Show Notes Fol&der", Keys.None, (s, e) => ShowNotesFolder()));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("E&xit", Keys.None, (s, e) => Close()));
            file.DropDownOpening += (s, e) => _saveCopyToNotesItem.Enabled = CurrentTab?.IsExternal == true;

            var edit = new ToolStripMenuItem("&Edit");
            // Edit commands act on the search box while you type in it, otherwise on the current tab.
            var undo = Item("&Undo", Keys.Control | Keys.Z, (s, e) => EditCommand(ed => ed.Undo(), box => box.Undo()));
            var redo = Item("&Redo", Keys.Control | Keys.Y, (s, e) => EditCommand(ed => ed.Redo(), box => { }));
            edit.DropDownItems.Add(undo);
            edit.DropDownItems.Add(redo);
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Cu&t", Keys.Control | Keys.X, (s, e) => EditCommand(ed => ed.Cut(), box => box.Cut())));
            edit.DropDownItems.Add(Item("&Copy", Keys.Control | Keys.C, (s, e) => EditCommand(ed => ed.Copy(), box => box.Copy())));
            edit.DropDownItems.Add(Item("&Paste", Keys.Control | Keys.V, (s, e) => EditCommand(ed => ed.PastePlainText(), box => box.Paste())));
            edit.DropDownItems.Add(Item("De&lete", Keys.None, (s, e) => EditCommand(ed => ed.ReplaceSelection(""), box => box.SelectedText = "")));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("&Search Tabs", Keys.Control | Keys.F, (s, e) => FocusSearchBox()));
            edit.DropDownItems.Add(Item("&Find...", Keys.Control | Keys.Shift | Keys.F, (s, e) => ShowFind()));
            edit.DropDownItems.Add(Item("Find &Next", Keys.F3, (s, e) => FindAgain(forward: true)));
            edit.DropDownItems.Add(Item("Find Pre&vious", Keys.Shift | Keys.F3, (s, e) => FindAgain(forward: false)));
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item("Select &All", Keys.Control | Keys.A, (s, e) => EditCommand(ed => ed.SelectAll(), box => box.SelectAll())));
            edit.DropDownItems.Add(Item("Time/&Date", Keys.F5, (s, e) => InsertTimeDate()));
            edit.DropDownOpening += (s, e) =>
            {
                undo.Enabled = SearchBoxFocused || CurrentEditor?.CanUndo == true;
                redo.Enabled = !SearchBoxFocused && CurrentEditor?.CanRedo == true;
            };

            var format = new ToolStripMenuItem("F&ormat");
            _wordWrapItem = Item("&Word Wrap", Keys.None, (s, e) => SetWordWrap(!_wordWrapItem.Checked));
            format.DropDownItems.Add(_wordWrapItem);
            format.DropDownItems.Add(Item("&Font...", Keys.None, (s, e) => ChooseFont()));
            format.DropDownItems.Add(new ToolStripSeparator());
            _multiRowItem = Item("Tabs in &Multiple Rows", Keys.None, (s, e) => SetMultiRow(!_multiRowItem.Checked));
            format.DropDownItems.Add(_multiRowItem);
            format.DropDownItems.Add(BuildColorMenu("Tab &Color"));

            var help = new ToolStripMenuItem("&Help");
            help.DropDownItems.Add(Item("&User Guide", Keys.F1, (s, e) => NoteEditor.OpenLink(UserGuideUrl)));
            help.DropDownItems.Add(Item("&Keyboard Shortcuts", Keys.None, (s, e) => ShowHelp()));

            menu.Items.AddRange(new ToolStripItem[] { file, edit, format, help });
            // Right side of the menu bar: search, move-tab arrows and "+ New Tab".
            // Right-aligned items are placed from the right edge inwards, so they're added in reverse.
            var plus = new ToolStripMenuItem("+ New Tab") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "New tab (Ctrl+T)" };
            plus.Click += (s, e) => NewTab();
            var moveRight = new ToolStripMenuItem("►") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Move tab right (Ctrl+Shift+Page Down)" };
            moveRight.Click += (s, e) => MoveCurrentTab(+1);
            var moveLeft = new ToolStripMenuItem("◄") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Move tab left (Ctrl+Shift+Page Up)" };
            moveLeft.Click += (s, e) => MoveCurrentTab(-1);
            menu.Items.Add(plus);
            menu.Items.Add(moveRight);
            menu.Items.Add(moveLeft);
            AddSearchBar(menu);
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
            _folderLabel.Click += (s, e) => ShowNotesFolder(currentFile: true);
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

        private void EditCommand(Action<NoteEditor> onEditor, Action<TextBox> onSearchBox)
        {
            if (SearchBoxFocused)
                onSearchBox(_searchBox.TextBox);
            else if (CurrentEditor != null)
                onEditor(CurrentEditor);
        }

        private NoteTab CurrentTab => _tabs.SelectedTab as NoteTab;
        private NoteEditor CurrentEditor => CurrentTab?.Editor;
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
            _multiRowItem.Checked = !settings.TryGetValue("multirow", out string multiRow) || multiRow != "0";
            _tabs.Multiline = _multiRowItem.Checked;
            if (applyWindowSettings)
                RestoreWindowBounds(settings.TryGetValue("window", out string bounds) ? bounds : null);

            foreach (var note in notes)
            {
                var tab = new NoteTab(note.Id, note.Title, note.Text)
                {
                    ExternalPath = note.ExternalPath,
                    FileEncoding = note.FileEncoding,
                    FileTimestampUtc = note.FileTimestampUtc,
                };
                if (note.Color.HasValue)
                {
                    tab.TabColor = note.Color.Value;
                }
                else
                {
                    tab.TabColor = TabColors.PickRandom(AllTabs.Select(t => t.TabColor), AllTabs.LastOrDefault()?.TabColor);
                    _indexDirty = true;
                }
                AddTab(tab);
            }

            if (_tabs.TabCount == 0)
            {
                AddTab(new NoteTab(UniqueId("My notes", null), "My notes", "") { TabColor = TabColors.PickRandom(new Color[0]) });
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

            if (_store.MissingFiles.Count > 0)
            {
                _indexDirty = true;
                BeginInvoke((Action)(() => MessageBox.Show(this,
                    "These files were open in tabs but can't be found any more (moved, renamed or deleted?):\n\n  " +
                    string.Join("\n  ", _store.MissingFiles), AppName, MessageBoxButtons.OK, MessageBoxIcon.Information)));
            }
        }

        /// <summary>
        /// Gives every tab's file the tab's name (e.g. "Project A.txt"), so the notes folder is easy
        /// to understand. Also upgrades notes saved by version 1.0, whose files had generated names.
        /// </summary>
        private void RenameFilesToTabNames()
        {
            foreach (var tab in AllTabs.Where(t => !t.IsExternal))
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
            var tab = CurrentTab;
            string path = tab != null && tab.IsExternal ? tab.ExternalPath : _store.Folder;
            string shown = path;
            // Shorten very long paths from the middle; the tooltip always has the full path.
            if (shown.Length > 60)
                shown = shown.Substring(0, 20) + "..." + shown.Substring(shown.Length - 35);
            if (tab != null && tab.IsExternal)
            {
                _folderLabel.Text = "File: " + shown;
                _folderLabel.ToolTipText = path + "\nThis tab is saved in this file. Click to show it in its folder.";
            }
            else
            {
                _folderLabel.Text = "Notes folder: " + shown;
                _folderLabel.ToolTipText = path + "\nClick to open this folder";
            }
        }

        private void AddTab(NoteTab tab, int index = -1)
        {
            tab.Editor.Font = _editorFont;
            tab.Editor.WordWrap = _wordWrapItem.Checked;
            tab.Editor.ScrollBars = _wordWrapItem.Checked ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
            tab.Editor.ClearUndo();
            tab.Editor.ContextMenuStrip = _editorMenu;
            tab.Editor.TextChanged += (s, e) =>
            {
                if (_loading) return;
                tab.Dirty = true;
                ScheduleSave();
                UpdatePosition();
                OnEditorTextChanged(tab);
            };
            tab.Editor.SelectionChanged += (s, e) => { if (tab == CurrentTab) UpdatePosition(); };
            tab.LinkCopied += (s, url) => SetStatus("Link copied: " + url);
            UpdateTabToolTip(tab);

            if (index < 0 || index >= _tabs.TabCount)
                _tabs.TabPages.Add(tab);
            else
                _tabs.TabPages.Insert(index, tab);
        }

        private void UpdateTabToolTip(NoteTab tab)
        {
            tab.ToolTipText = tab.IsExternal
                ? tab.Text + "\nFile: " + tab.ExternalPath
                : tab.Text + "\nSaved as: " + _store.NotePath(tab.Id);
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
                foreach (var tab in AllTabs.Where(t => t.Dirty).ToList())
                {
                    if (tab.IsExternal)
                    {
                        SaveExternal(tab);
                    }
                    else
                    {
                        _store.SaveNote(tab.ToData());
                        tab.Dirty = false;
                    }
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
                    settings["multirow"] = _multiRowItem.Checked ? "1" : "0";
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

        /// <summary>Saves a tab opened from a text file back into that file, in the file's own encoding.</summary>
        private void SaveExternal(NoteTab tab)
        {
            if (_resolvingConflict) return;   // a question about this file is already on screen
            string path = tab.ExternalPath;
            if (File.Exists(path) && File.GetLastWriteTimeUtc(path) != tab.FileTimestampUtc)
            {
                // Someone else changed the file since we opened or last saved it.
                _resolvingConflict = true;
                DialogResult answer;
                try
                {
                    answer = MessageBox.Show(this,
                        Path.GetFileName(path) + " was changed by another program while it was open here.\n\n" +
                        "Yes: save your version from this tab (replacing the other changes)\n" +
                        "No: load the other version into this tab (your recent changes here are dropped)",
                        AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
                }
                finally
                {
                    _resolvingConflict = false;
                }
                if (answer == DialogResult.No)
                {
                    ReloadExternal(tab);
                    return;
                }
            }

            tab.FileEncoding = NoteStore.WriteTextFile(path, tab.Editor.PlainText, tab.FileEncoding);
            tab.FileTimestampUtc = File.GetLastWriteTimeUtc(path);
            tab.Dirty = false;
        }

        private void ReloadExternal(NoteTab tab)
        {
            string text = NoteStore.ReadTextFile(tab.ExternalPath, out var encoding);
            int caret = tab.Editor.SelectionStart;
            SetEditorText(tab, text);
            tab.Editor.Select(Math.Min(caret, tab.Editor.TextLength), 0);
            tab.FileEncoding = encoding;
            tab.FileTimestampUtc = File.GetLastWriteTimeUtc(tab.ExternalPath);
            tab.Dirty = false;
        }

        /// <summary>Replaces a tab's whole text without marking it as changed (and without undo history).</summary>
        private void SetEditorText(NoteTab tab, string text)
        {
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                tab.Editor.Text = text;
                tab.Editor.ClearUndo();
            }
            finally
            {
                _loading = wasLoading;
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // Pick up changes made to opened text files by other programs while we were in the background.
            foreach (var tab in AllTabs.Where(t => t.IsExternal && !t.Dirty))
            {
                try
                {
                    if (File.Exists(tab.ExternalPath) && File.GetLastWriteTimeUtc(tab.ExternalPath) != tab.FileTimestampUtc)
                        ReloadExternal(tab);
                }
                catch
                {
                    // File busy; try again next time.
                }
            }
        }

        private void SetStatus(string text) => _statusLabel.Text = text;

        // ---------------------------------------------------------------- tab actions

        private void NewTab()
        {
            string name = InputDialog.Ask(this, "New Tab", "Name for the new tab (for example a project name):", "Project " + (_tabs.TabCount + 1));
            if (name == null) return;

            var tab = new NoteTab(UniqueId(name, null), name, "")
            {
                Dirty = true,
                TabColor = TabColors.PickRandom(AllTabs.Select(t => t.TabColor), AllTabs.LastOrDefault()?.TabColor),
            };
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

            if (!tab.IsExternal)
            {
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
            }
            tab.Text = name;
            UpdateTabToolTip(tab);
            _indexDirty = true;
            UpdateTitle();
            SaveAll(showStatus: false);
        }

        private void CloseTab(NoteTab tab)
        {
            if (tab == null) return;

            if (tab.IsExternal)
            {
                // The text lives in its own file: save it there and just close the tab.
                if (tab.Dirty && !SaveAll(showStatus: false))
                    return;
            }
            else
            {
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
            }

            // Select the neighbouring tab first so focus never sits on a page being removed.
            if (_tabs.TabCount == 1)
                AddTab(new NoteTab(UniqueId("My notes", tab), "My notes", "") { TabColor = TabColors.PickRandom(new[] { tab.TabColor }) });
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
            tab.Editor.Focus();
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
            foreach (var tab in AllTabs) tab.HideCopyButton();
            UpdateTitle();
            UpdatePosition();
            UpdateFolderLabel();
            OnSearchTabChanged();
            // Don't steal focus from the Find dialog or the search box when a search jumps to another tab.
            if (ActiveForm == this && !SearchBoxFocused) CurrentEditor?.Focus();
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

        // ---------------------------------------------------------------- tab colors, rows and drawing

        private ToolStripMenuItem BuildColorMenu(string text)
        {
            var menu = new ToolStripMenuItem(text);
            int size = Dpi.Scale(14);
            foreach (var (name, color) in TabColors.Palette)
            {
                var c = color;
                var item = new ToolStripMenuItem(name, TabColors.Swatch(c, size), (s, e) => SetTabColor(CurrentTab, c));
                menu.DropDownItems.Add(item);
            }
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add("&Random Color", null, (s, e) =>
            {
                var tab = CurrentTab;
                if (tab == null) return;
                SetTabColor(tab, TabColors.PickRandom(AllTabs.Where(t => t != tab).Select(t => t.TabColor), tab.TabColor));
            });
            menu.DropDownItems.Add("&Custom Color...", null, (s, e) => ChooseCustomColor(CurrentTab));
            menu.DropDownOpening += (s, e) =>
            {
                int current = CurrentTab?.TabColor.ToArgb() ?? 0;
                foreach (var item in menu.DropDownItems.OfType<ToolStripMenuItem>())
                    item.Checked = item.Image != null && TabColors.Palette.Any(p => p.Name == item.Text && p.Color.ToArgb() == current);
            };
            return menu;
        }

        private void SetTabColor(NoteTab tab, Color color)
        {
            if (tab == null) return;
            tab.TabColor = color;
            _tabs.Invalidate();
            _indexDirty = true;
            ScheduleSave();
        }

        private void ChooseCustomColor(NoteTab tab)
        {
            if (tab == null) return;
            using (var dialog = new ColorDialog { Color = tab.TabColor, FullOpen = true, AnyColor = true })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SetTabColor(tab, dialog.Color);
            }
        }

        private void SetMultiRow(bool multiRow)
        {
            _multiRowItem.Checked = multiRow;
            _tabs.Multiline = multiRow;
            _indexDirty = true;
            ScheduleSave();
        }

        private void Tabs_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _tabs.TabCount) return;
            var tab = (NoteTab)_tabs.TabPages[e.Index];
            bool selected = e.Index == _tabs.SelectedIndex;
            var g = e.Graphics;
            Rectangle r = e.Bounds;

            Color color = tab.TabColor.IsEmpty ? SystemColors.Control : tab.TabColor;
            using (var fill = new SolidBrush(selected ? color : TabColors.Lighter(color)))
                g.FillRectangle(fill, r);
            if (selected)
            {
                using (var pen = new Pen(TabColors.Darker(color), Dpi.Scale(2)) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset })
                    g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }
            else
            {
                using (var pen = new Pen(TabColors.Darker(color, 0.8)))
                    g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
            }

            // Tabs opened from a text file elsewhere are shown in italics.
            using (var font = tab.IsExternal ? new Font(_tabs.Font, FontStyle.Italic) : null)
            {
                TextRenderer.DrawText(g, tab.Text, font ?? _tabs.Font, r, Color.Black,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
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
            if (Math.Abs(e.X - _dragStart.X) < SystemInformation.DragSize.Width &&
                Math.Abs(e.Y - _dragStart.Y) < SystemInformation.DragSize.Height) return;

            var over = TabAt(e.Location);
            if (over == null || over == _dragTab) return;

            // Only swap once the pointer is far enough into the other tab that, after the swap,
            // it is over the dragged tab again. Otherwise tabs of different widths flip back and forth.
            int from = _tabs.TabPages.IndexOf(_dragTab);
            int to = _tabs.TabPages.IndexOf(over);
            Rectangle target = _tabs.GetTabRect(to);
            Rectangle source = _tabs.GetTabRect(from);
            bool otherRow = target.Y != source.Y;
            bool farEnough = otherRow || (to > from ? e.X >= target.Right - source.Width : e.X <= target.Left + source.Width);
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

        private ContextMenuStrip BuildEditorMenu()
        {
            var menu = new ContextMenuStrip();
            var openLink = new ToolStripMenuItem("&Open Link");
            var copyLink = new ToolStripMenuItem("Copy &Link");
            var linkSeparator = new ToolStripSeparator();
            var undo = new ToolStripMenuItem("&Undo", null, (s, e) => CurrentEditor?.Undo());
            var redo = new ToolStripMenuItem("&Redo", null, (s, e) => CurrentEditor?.Redo());
            var cut = new ToolStripMenuItem("Cu&t", null, (s, e) => CurrentEditor?.Cut());
            var copy = new ToolStripMenuItem("&Copy", null, (s, e) => CurrentEditor?.Copy());
            var paste = new ToolStripMenuItem("&Paste", null, (s, e) => CurrentEditor?.PastePlainText());
            var delete = new ToolStripMenuItem("&Delete", null, (s, e) => CurrentEditor?.ReplaceSelection(""));
            var selectAll = new ToolStripMenuItem("Select &All", null, (s, e) => CurrentEditor?.SelectAll());
            menu.Items.AddRange(new ToolStripItem[]
            {
                openLink, copyLink, linkSeparator,
                undo, redo, new ToolStripSeparator(), cut, copy, paste, delete, new ToolStripSeparator(), selectAll,
            });

            string linkUrl = null;
            openLink.Click += (s, e) => NoteEditor.OpenLink(linkUrl);
            copyLink.Click += (s, e) =>
            {
                try { Clipboard.SetText(linkUrl); SetStatus("Link copied: " + linkUrl); }
                catch (ExternalException) { /* clipboard busy */ }
            };
            menu.Opening += (s, e) =>
            {
                var editor = CurrentEditor;
                if (editor == null) { e.Cancel = true; return; }
                var link = editor.LinkAtPoint(editor.PointToClient(Cursor.Position));
                linkUrl = link?.Url;
                openLink.Visible = copyLink.Visible = linkSeparator.Visible = link != null;
                undo.Enabled = editor.CanUndo;
                redo.Enabled = editor.CanRedo;
                bool hasSelection = editor.SelectionLength > 0;
                cut.Enabled = copy.Enabled = delete.Enabled = hasSelection;
            };
            return menu;
        }

        private void InsertTimeDate()
        {
            var editor = CurrentEditor;
            if (editor == null) return;
            DateTime now = DateTime.Now;
            editor.ReplaceSelection(now.ToShortTimeString() + " " + now.ToShortDateString());
        }

        private void SetWordWrap(bool wrap)
        {
            _wordWrapItem.Checked = wrap;
            foreach (var tab in AllTabs)
            {
                tab.Editor.WordWrap = wrap;
                tab.Editor.ScrollBars = wrap ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.Both;
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

        // ---------------------------------------------------------------- misc

        private void ShowHelp()
        {
            MessageBox.Show(this,
                "Tabs\n" +
                "  Ctrl+T\tNew tab (or click \"+ New Tab\")\n" +
                "  F2\tRename tab (or double-click the tab)\n" +
                "  Ctrl+W\tClose tab (or middle-click the tab)\n" +
                "  Ctrl+Tab\tNext tab  (Ctrl+Shift+Tab: previous)\n" +
                "  Ctrl+1..9\tJump to tab 1..9\n" +
                "  Ctrl+Shift+PgUp/PgDn\tMove tab left/right (or the ◄ ► buttons)\n" +
                "  Right-click a tab for its color and more\n\n" +
                "Editing\n" +
                "  Ctrl+Z / Ctrl+Y\tUndo / Redo\n" +
                "  F5\tInsert time and date\n" +
                "  Click a link to open it; hover it for a copy button\n\n" +
                "Searching\n" +
                "  Ctrl+F\tSearch box (this tab or all tabs)\n" +
                "  Ctrl+Shift+F\tFind dialog (with Select All)\n" +
                "  F3 / Shift+F3\tNext / previous match\n\n" +
                "Files\n" +
                "  Ctrl+O\tOpen a text file in a tab\n" +
                "  Ctrl+Shift+O\tOpen a notes folder\n" +
                "  Ctrl+S\tSave (saving is automatic anyway)\n" +
                "  Ctrl+Shift+S\tSave all tabs as... (pick a folder)\n\n" +
                "Your notes are saved automatically, one .txt file per tab, in:\n" + _store.Folder,
                "Keyboard Shortcuts - " + AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys mods = keyData & Keys.Modifiers;

            // Ctrl+Shift+PageUp / PageDown move the current tab.
            if (mods == (Keys.Control | Keys.Shift) && (key == Keys.PageUp || key == Keys.PageDown))
            {
                MoveCurrentTab(key == Keys.PageUp ? -1 : 1);
                return true;
            }

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
