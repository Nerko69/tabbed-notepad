using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TabbedNotepad
{
    // Tab groups by category (like tab groups in Chrome), the Date/Time button, pasted pictures
    // and renaming a tab's file.
    internal sealed partial class MainForm
    {
        // ---------------------------------------------------------------- tab groups

        private ToolStripMenuItem _groupTabsItem;
        private ToolStripMenuItem _categoryOnTabsItem;

        private bool GroupTabs => _groupTabsItem.Checked;
        private bool ShowCategoryOnTabs => _categoryOnTabsItem.Checked;

        /// <summary>Strong colors for the category tags on tabs (white text stays readable on all of them).</summary>
        private static readonly Color[] GroupColors =
        {
            Color.FromArgb(0x1A, 0x73, 0xE8),   // blue
            Color.FromArgb(0xD9, 0x30, 0x25),   // red
            Color.FromArgb(0x1E, 0x8E, 0x3E),   // green
            Color.FromArgb(0x93, 0x34, 0xE6),   // purple
            Color.FromArgb(0xB0, 0x60, 0x00),   // amber
            Color.FromArgb(0x00, 0x7B, 0x83),   // cyan
            Color.FromArgb(0xD0, 0x18, 0x84),   // pink
            Color.FromArgb(0x5F, 0x63, 0x68),   // gray
            Color.FromArgb(0xE8, 0x71, 0x0A),   // orange
            Color.FromArgb(0x3F, 0x51, 0xB5),   // indigo
        };

        public static Color GroupColor(string category)
        {
            if (category == null) return Color.Empty;
            int index = TabCategories.Items.ToList().FindIndex(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(category));
            return GroupColors[index % GroupColors.Length];
        }

        /// <summary>The first letters of a category shown on its tabs: "CC247" → "CC2", "OrgSys" → "Org".</summary>
        public static string CategoryTag(string category) =>
            category == null ? "" : new string(category.Where(char.IsLetterOrDigit).Take(3).ToArray());

        private int CategoryTagSpace => Dpi.Scale(34);

        private void SetGroupTabs(bool group)
        {
            _groupTabsItem.Checked = group;
            if (group) RegroupTabs();
            _indexDirty = true;
            ScheduleSave();
        }

        private void SetCategoryOnTabs(bool show)
        {
            _categoryOnTabsItem.Checked = show;
            UpdateTabBadges();
            _indexDirty = true;
            ScheduleSave();
        }

        /// <summary>
        /// The tabs in blocks: all tabs of one category together (a group), each tab without a
        /// category on its own. A group stays where its first tab is.
        /// </summary>
        private static List<List<NoteTab>> Blocks(IEnumerable<NoteTab> tabs)
        {
            var blocks = new List<List<NoteTab>>();
            var groups = new Dictionary<string, List<NoteTab>>(StringComparer.OrdinalIgnoreCase);
            foreach (var tab in tabs)
            {
                if (tab.Category == null)
                {
                    blocks.Add(new List<NoteTab> { tab });
                }
                else if (groups.TryGetValue(tab.Category, out var group))
                {
                    group.Add(tab);
                }
                else
                {
                    group = new List<NoteTab> { tab };
                    groups[tab.Category] = group;
                    blocks.Add(group);
                }
            }
            return blocks;
        }

        /// <summary>Puts the tabs of each category next to each other (when grouping is on).</summary>
        private void RegroupTabs()
        {
            if (!GroupTabs) return;
            var order = Blocks(_allTabs).SelectMany(b => b).ToList();
            if (order.SequenceEqual(_allTabs)) return;
            _allTabs.Clear();
            _allTabs.AddRange(order);
            ApplyCategoryFilter(_categoryFilter, keepEmpty: true);
            _indexDirty = true;
            ScheduleSave();
        }

        /// <summary>
        /// Moves a tab one place left or right. With grouping on, a tab moves within its group; at
        /// the edge of the group the whole group moves past its neighbour (and a tab without a
        /// category jumps over a whole group), like tab groups in a web browser.
        /// </summary>
        private void MoveCurrentTab(int delta)
        {
            var tab = CurrentTab;
            if (tab == null) return;
            int index = _tabs.TabPages.IndexOf(tab);
            int target = index + delta;
            if (target < 0 || target >= _tabs.TabCount) return;
            var neighbour = (NoteTab)_tabs.TabPages[target];
            bool sameGroup = tab.Category != null && string.Equals(tab.Category, neighbour.Category, StringComparison.OrdinalIgnoreCase);
            if (!GroupTabs || sameGroup || _categoryFilter != null)
            {
                MoveTab(tab, target);
            }
            else
            {
                // Swap this tab's block with the neighbouring block (all tabs are shown).
                var blocks = Blocks(_allTabs);
                int mine = blocks.FindIndex(b => b.Contains(tab));
                int other = blocks.FindIndex(b => b.Contains(neighbour));
                var block = blocks[mine];
                blocks[mine] = blocks[other];
                blocks[other] = block;
                _allTabs.Clear();
                _allTabs.AddRange(blocks.SelectMany(b => b));
                ApplyCategoryFilter(null, keepEmpty: true);
                _tabs.SelectedTab = tab;
                _indexDirty = true;
                ScheduleSave();
            }
            tab.Editor.Focus();
        }

        /// <summary>The category tag drawn at the left of a tab, and the group's color along its bottom.</summary>
        private Rectangle DrawCategoryTag(Graphics g, NoteTab tab, Rectangle r)
        {
            if (!ShowCategoryOnTabs || tab.Category == null) return r;
            Color color = GroupColor(tab.Category);
            using (var bar = new SolidBrush(color))
                g.FillRectangle(bar, r.X + 1, r.Bottom - Dpi.Scale(3), r.Width - 2, Dpi.Scale(3));

            string text = CategoryTag(tab.Category);
            using (var font = new Font(_tabs.Font.FontFamily, _tabs.Font.Size * 0.75f, FontStyle.Bold))
            {
                Size size = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding);
                int height = Math.Min(r.Height - Dpi.Scale(8), size.Height + Dpi.Scale(2));
                var chip = new Rectangle(r.X + Dpi.Scale(6), r.Y + (r.Height - height) / 2 - Dpi.Scale(1), size.Width + Dpi.Scale(6), height);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var fill = new SolidBrush(color))
                using (var path = RoundedRect(chip, Dpi.Scale(3)))
                    g.FillPath(fill, path);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
                TextRenderer.DrawText(g, text, font, chip, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                int used = chip.Right - r.X;
                return new Rectangle(r.X + used, r.Y, r.Width - used, r.Height);
            }
        }

        // ---------------------------------------------------------------- Date/Time button

        /// <summary>The Date/Time button (Shift+F5): inserts e.g. "10/16/2026-10:45 PM".</summary>
        private void InsertDateTimeStamp()
        {
            var editor = CurrentEditor;
            if (editor == null) return;
            editor.ReplaceSelection(DateTimeStamp(DateTime.Now));
            editor.Focus();
        }

        public static string DateTimeStamp(DateTime when) =>
            when.ToString("MM/dd/yyyy-h:mm tt", CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- pasted pictures

        private const string ImagesFolderName = "Images";

        /// <summary>Saves a pasted picture as a .png in the notes folder's Images subfolder and returns a link to it.</summary>
        private string SavePastedImage(Image image)
        {
            try
            {
                string folder = Path.Combine(_store.Folder, ImagesFolderName);
                Directory.CreateDirectory(folder);
                string name = "Image-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
                string path = Path.Combine(folder, name + ".png");
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, name + "-" + n + ".png");
                image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                SetStatus("Picture saved: " + path + " (click the link to open it)");
                return "Image: " + new Uri(path).AbsoluteUri + "\n";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The picture could not be saved:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        // ---------------------------------------------------------------- renaming files

        private ToolStripMenuItem _keepOldFileItem;

        /// <summary>
        /// Renames the file of a tab opened from elsewhere to match the tab's new name (same folder,
        /// same extension). Returns false (and says why) if it can't.
        /// </summary>
        private bool RenameExternalFile(NoteTab tab, string name)
        {
            string oldPath = tab.ExternalPath;
            string fileName = string.Concat(name.Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.');
            if (fileName.Length == 0) return false;
            string extension = Path.GetExtension(oldPath);
            if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) fileName += extension;
            string newPath = Path.Combine(Path.GetDirectoryName(oldPath), fileName);
            if (string.Equals(newPath, oldPath, StringComparison.Ordinal)) return true;
            try
            {
                SaveAll(showStatus: false, quiet: true);
                bool caseOnly = string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase);
                if (!caseOnly && File.Exists(newPath))
                {
                    MessageBox.Show(this, "The tab was renamed, but its file was not: there is already a file called\n" + newPath, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                if (KeepOldFileOnRename && !caseOnly)
                {
                    File.Copy(oldPath, newPath);
                }
                else if (caseOnly)
                {
                    string temp = newPath + "." + Guid.NewGuid().ToString("N");
                    File.Move(oldPath, temp);
                    File.Move(temp, newPath);
                }
                else
                {
                    File.Move(oldPath, newPath);
                }
                tab.ExternalPath = newPath;
                tab.FileTimestampUtc = File.GetLastWriteTimeUtc(newPath);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The tab was renamed, but its file could not be renamed:\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private bool KeepOldFileOnRename => _keepOldFileItem.Checked;
    }
}
