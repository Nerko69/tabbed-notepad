using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TabbedNotepad
{
    // Opening and saving: text files, notes folders, copies.
    internal sealed partial class MainForm
    {
        private const long MaxOpenFileBytes = 50L * 1024 * 1024;

        private void SaveNow()
        {
            _indexDirty = true;
            if (SaveAll(showStatus: true))
                SetStatus("All tabs saved at " + DateTime.Now.ToString("t"));
        }

        // ---------------------------------------------------------------- Open Text File

        /// <summary>Opens one or more text files from anywhere, each in its own tab.</summary>
        private void OpenTextFiles()
        {
            string startFolder = CurrentTab?.IsExternal == true ? Path.GetDirectoryName(CurrentTab.ExternalPath) : null;
            using (var dialog = new OpenFileDialog
            {
                Title = "Open Text File",
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                Multiselect = true,
                InitialDirectory = startFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                foreach (string path in dialog.FileNames)
                    OpenTextFile(path);
            }
        }

        /// <summary>
        /// Opens a text file in a tab: in the current tab if that is empty, otherwise in a new one.
        /// The tab keeps saving into that file; the notes folder only remembers where it is.
        /// </summary>
        private void OpenTextFile(string path)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Already open? Just show it.
            var open = AllTabs.FirstOrDefault(t =>
                (t.IsExternal && string.Equals(t.ExternalPath, fullPath, StringComparison.OrdinalIgnoreCase)) ||
                (!t.IsExternal && string.Equals(_store.NotePath(t.Id), fullPath, StringComparison.OrdinalIgnoreCase)));
            if (open != null)
            {
                _tabs.SelectedTab = open;
                SetStatus(Path.GetFileName(fullPath) + " is already open");
                return;
            }

            string text;
            System.Text.Encoding encoding;
            try
            {
                var info = new FileInfo(fullPath);
                if (info.Length > MaxOpenFileBytes)
                {
                    MessageBox.Show(this, Path.GetFileName(fullPath) + " is too large to open here (over 50 MB).", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                text = NoteStore.ReadTextFile(fullPath, out encoding);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open " + Path.GetFileName(fullPath) + ":\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (text.IndexOf('\0') >= 0 &&
                MessageBox.Show(this, Path.GetFileName(fullPath) + " doesn't look like a text file. Saving changes to it could damage it.\n\nOpen it anyway?",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            // Reuse the current tab if it's an empty note; otherwise open a new tab.
            var tab = CurrentTab;
            bool reuse = tab != null && !tab.IsExternal && tab.Editor.TextLength == 0;
            if (reuse)
            {
                try
                {
                    _store.ArchiveNote(tab.ToData());   // removes the empty note's file
                }
                catch
                {
                    reuse = false;
                }
            }
            if (!reuse)
            {
                tab = new NoteTab(NoteStore.NewExternalId(), "", "")
                {
                    TabColor = TabColors.PickRandom(AllTabs.Select(t => t.TabColor), AllTabs.LastOrDefault()?.TabColor),
                };
                AddTab(tab);
            }
            else
            {
                tab.Id = NoteStore.NewExternalId();
            }

            tab.Text = Path.GetFileNameWithoutExtension(fullPath);
            tab.ExternalPath = fullPath;
            tab.FileEncoding = encoding;
            tab.FileTimestampUtc = File.GetLastWriteTimeUtc(fullPath);
            SetEditorText(tab, text);
            tab.Editor.Select(0, 0);
            tab.Dirty = false;
            UpdateTabToolTip(tab);
            _tabs.SelectedTab = tab;
            _tabs.Invalidate();
            UpdateTitle();
            UpdateFolderLabel();
            _indexDirty = true;
            SaveAll(showStatus: false);
            SetStatus("Opened " + fullPath);
        }

        /// <summary>For a tab opened from a text file: adds a copy of it as a normal tab saved in the notes folder.</summary>
        private void SaveCopyInNotesFolder(NoteTab source)
        {
            if (source == null || !source.IsExternal) return;
            string title = source.Text + " (copy)";
            var copy = new NoteTab(UniqueId(title, null), title, source.Editor.PlainText)
            {
                Dirty = true,
                TabColor = source.TabColor,
            };
            AddTab(copy, _tabs.TabPages.IndexOf(source) + 1);
            _indexDirty = true;
            if (SaveAll(showStatus: false))
                SetStatus("Copy saved in the notes folder as tab \"" + title + "\"");
        }

        // ---------------------------------------------------------------- Save All Tabs As / Open Folder

        /// <summary>
        /// Like "Save As" in Notepad, but for all tabs at once: saves every tab into a folder the
        /// user picks, and keeps saving there from then on. (Tabs opened from text files elsewhere
        /// keep saving into their own files.)
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
            var notes = AllTabs.Where(t => !t.IsExternal).ToList();
            var clashes = notes.Where(t => target.NoteFileExists(t.Id)).Select(t => t.Id + ".txt").ToList();
            if (target.HasIndex || clashes.Count > 0)
            {
                string message = target.HasIndex
                    ? "This folder already has notes saved by " + AppName + ". Saving here replaces its tab list with your current tabs."
                    : "Some files in this folder have the same names as your tabs.";
                if (clashes.Count > 0)
                    message += "\n\nThese files will be replaced:\n  " + string.Join("\n  ", clashes.Take(10)) + (clashes.Count > 10 ? "\n  ..." : "");
                message += "\n\nContinue? (To open the notes in that folder instead, choose No and use File > Open Folder.)";
                if (MessageBox.Show(this, message, AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }

            var oldStore = _store;
            foreach (var pair in oldStore.Settings) target.Settings[pair.Key] = pair.Value;
            _store = target;
            foreach (var tab in notes) tab.Dirty = true;
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
            foreach (var tab in AllTabs) UpdateTabToolTip(tab);
            UpdateFolderLabel();
            MessageBox.Show(this,
                "Saved " + notes.Count + (notes.Count == 1 ? " tab" : " tabs") + " in:\n" + folder +
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

            string folder = FolderPicker.Pick(this, "Open Folder - choose a folder with notes", "Open", _store.Folder);
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
                    MessageBox.Show(this, "There are no notes (.txt files) in this folder.\n\nTo save your current tabs there, use File > Save All Tabs As.\nTo open a single text file, use File > Open Text File.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show(this, "This folder has " + textFiles + " text " + (textFiles == 1 ? "file" : "files") + ". Open each one as a tab?\n\nChanges will be saved back to these files automatically.",
                        AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }

            ClearSearchHighlights();
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

        // ---------------------------------------------------------------- copies and folders

        private void ExportCurrentTab()
        {
            var tab = CurrentTab;
            if (tab == null) return;
            using (var dialog = new SaveFileDialog
            {
                Title = "Save a Copy of This Tab",
                Filter = "Text documents (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = NoteStore.MakeFileNameSafe(tab.Text) + ".txt",
            })
            {
                if (tab.IsExternal) dialog.InitialDirectory = Path.GetDirectoryName(tab.ExternalPath);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(dialog.FileName, NoteStore.ToWindowsNewlines(tab.Editor.PlainText), new System.Text.UTF8Encoding(false));
                    SetStatus("Copy of \"" + tab.Text + "\" saved to " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not save:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>Opens the notes folder in Explorer, or with <paramref name="currentFile"/> the folder of the current tab's text file.</summary>
        private void ShowNotesFolder(bool currentFile = false)
        {
            SaveAll(showStatus: false);
            try
            {
                var tab = CurrentTab;
                if (currentFile && tab != null && tab.IsExternal && File.Exists(tab.ExternalPath))
                    Process.Start("explorer.exe", "/select,\"" + tab.ExternalPath + "\"");
                else
                    Process.Start("explorer.exe", "\"" + _store.Folder + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppName);
            }
        }
    }
}
