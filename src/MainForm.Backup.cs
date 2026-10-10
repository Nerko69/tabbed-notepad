using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TabbedNotepad
{
    // The quick tab switcher (Ctrl+P) and the daily backup.
    internal sealed partial class MainForm
    {
        // ---------------------------------------------------------------- quick tab switcher

        /// <summary>Tabs in the order they were last used (current tab first).</summary>
        private readonly List<NoteTab> _recentTabs = new List<NoteTab>();

        private void RememberRecentTab(NoteTab tab)
        {
            if (tab == null) return;
            _recentTabs.Remove(tab);
            _recentTabs.Insert(0, tab);
        }

        private List<NoteTab> TabsByRecentUse() =>
            _recentTabs.Where(_allTabs.Contains).Concat(_allTabs.Where(t => !_recentTabs.Contains(t))).ToList();

        /// <summary>Ctrl+P: type part of a tab's name and press Enter to go there.</summary>
        private void ShowQuickSwitcher()
        {
            using (var switcher = new QuickSwitcher(TabsByRecentUse()))
            {
                // Just below the tabs, centred, like the quick-open box of a code editor.
                Point top = PointToScreen(new Point(ClientSize.Width / 2, 0));
                Rectangle area = _tabs.RectangleToScreen(_tabs.DisplayRectangle);
                switcher.Location = new Point(top.X - switcher.Width / 2, area.Top + Dpi.Scale(10));
                if (switcher.ShowDialog(this) != DialogResult.OK || switcher.SelectedTab == null) return;
                ShowTab(switcher.SelectedTab);
                switcher.SelectedTab.Editor.Focus();
            }
        }

        // ---------------------------------------------------------------- daily backup

        private ToolStripMenuItem _dailyBackupItem;
        private ToolStripMenuItem _lastBackupItem;
        private string _backupFolderSetting;   // null = Backups folder inside the notes folder
        private bool _backupRunning;
        private DateTime _lastBackupCheck = DateTime.MinValue;

        private string BackupFolder => string.IsNullOrEmpty(_backupFolderSetting) ? Backup.DefaultFolder(_store.Folder) : _backupFolderSetting;

        private ToolStripMenuItem BuildBackupMenu()
        {
            var menu = new ToolStripMenuItem("&Backups");
            _dailyBackupItem = new ToolStripMenuItem("&Daily Backup", null, (s, e) =>
            {
                _dailyBackupItem.Checked = !_dailyBackupItem.Checked;
                _indexDirty = true;
                ScheduleSave();
                if (_dailyBackupItem.Checked) StartDailyBackupIfDue();
            }) { Checked = true };
            _lastBackupItem = new ToolStripMenuItem("") { Enabled = false };
            menu.DropDownItems.Add(Item("Back Up &Now", Keys.None, (s, e) => BackUpNow()));
            menu.DropDownItems.Add(Item("&Open Backups Folder", Keys.None, (s, e) => OpenBackupsFolder()));
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(_dailyBackupItem);
            menu.DropDownItems.Add(Item("&Choose Backup Folder...", Keys.None, (s, e) => ChooseBackupFolder()));
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(_lastBackupItem);
            menu.DropDownOpening += (s, e) =>
            {
                var latest = Backup.Latest(BackupFolder);
                _lastBackupItem.Text = latest == null
                    ? "No backups yet"
                    : "Last backup: " + latest.LastWriteTime.ToString("g") + " (" + latest.Name + ")";
                _dailyBackupItem.ToolTipText = "Once a day, zip all notes into " + BackupFolder + " and keep the last " + Backup.KeepDays + " days";
            };
            return menu;
        }

        private IEnumerable<string> OtherFilePaths() => _allTabs.Where(t => t.IsExternal).Select(t => t.ExternalPath).ToList();

        /// <summary>
        /// Makes today's backup if there isn't one yet, in the background so typing is never held up.
        /// Called after loading and from the save timer (so a window left open overnight gets one too).
        /// </summary>
        private void StartDailyBackupIfDue()
        {
            if (_dailyBackupItem == null || !_dailyBackupItem.Checked || _backupRunning || _readOnlyAfterLoadError) return;
            DateTime now = DateTime.Now;
            if (_lastBackupCheck.Date == now.Date) return;
            string notes = _store.Folder, folder = BackupFolder;
            var others = OtherFilePaths();
            _backupRunning = true;
            _lastBackupCheck = now;
            Task.Run(() =>
            {
                string message = null;
                try
                {
                    if (!Backup.HasDailyBackup(folder, now))
                    {
                        string zip = Backup.Create(notes, folder, others, now, timestamped: false);
                        int removed = Backup.Prune(folder, Backup.KeepDays, now);
                        message = "Daily backup saved: " + Path.GetFileName(zip) + (removed > 0 ? " (" + removed + " old removed)" : "");
                    }
                }
                catch (Exception ex)
                {
                    message = "Daily backup failed: " + ex.Message;
                    _lastBackupCheck = DateTime.MinValue;   // try again later
                }
                finally
                {
                    _backupRunning = false;
                }
                if (message != null && IsHandleCreated)
                    BeginInvoke((Action)(() => SetStatus(message)));
            });
        }

        private void BackUpNow()
        {
            SaveAll(showStatus: false, quiet: true);
            Cursor = Cursors.WaitCursor;
            try
            {
                string zip = Backup.Create(_store.Folder, BackupFolder, OtherFilePaths(), DateTime.Now, timestamped: true);
                Backup.Prune(BackupFolder, Backup.KeepDays, DateTime.Now);
                SetStatus("Backup saved: " + zip);
                if (MessageBox.Show(this, "All your notes were backed up to:\n" + zip + "\n\nOpen the backups folder?", AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    OpenBackupsFolder();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The backup could not be made:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OpenBackupsFolder()
        {
            try
            {
                Directory.CreateDirectory(BackupFolder);
                Process.Start("explorer.exe", "\"" + BackupFolder + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppName);
            }
        }

        private void ChooseBackupFolder()
        {
            string folder = FolderPicker.Pick(this, "Choose a folder for backups (for example in OneDrive or on a USB stick)", "Use this folder", BackupFolder);
            if (folder == null) return;
            bool isDefault = string.Equals(Path.GetFullPath(folder).TrimEnd('\\', '/'), Path.GetFullPath(Backup.DefaultFolder(_store.Folder)).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            _backupFolderSetting = isDefault ? null : folder;
            _indexDirty = true;
            SaveAll(showStatus: false);
            _lastBackupCheck = DateTime.MinValue;
            StartDailyBackupIfDue();
            SetStatus("Backups will be saved in " + BackupFolder);
        }
    }
}
