using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace TabbedNotepad
{
    /// <summary>
    /// Zip backups of the notes folder: TabbedNotepad-2026-10-10.zip once a day (plus
    /// TabbedNotepad-2026-10-10-1530.zip for "Back Up Now"), keeping the last few weeks.
    /// </summary>
    internal static class Backup
    {
        public const int KeepDays = 30;
        private static readonly Regex BackupName = new Regex(@"^TabbedNotepad-(\d{4}-\d{2}-\d{2})(-\d{4})?\.zip$", RegexOptions.IgnoreCase);

        public static string DefaultFolder(string notesFolder) => Path.Combine(notesFolder, "Backups");

        public static string DailyFileName(DateTime day) =>
            "TabbedNotepad-" + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".zip";

        public static bool HasDailyBackup(string backupFolder, DateTime day) =>
            File.Exists(Path.Combine(backupFolder, DailyFileName(day)));

        /// <summary>
        /// Zips everything in the notes folder (except the backups themselves) plus text files opened
        /// from elsewhere (under "Other files"). Returns the path of the zip.
        /// </summary>
        public static string Create(string notesFolder, string backupFolder, IEnumerable<string> otherFiles, DateTime now, bool timestamped)
        {
            Directory.CreateDirectory(backupFolder);
            string name = timestamped
                ? "TabbedNotepad-" + now.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture) + ".zip"
                : DailyFileName(now);
            string target = Path.Combine(backupFolder, name);
            string tmp = target + ".tmp";
            string backupFull = Path.GetFullPath(backupFolder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            string notesFull = Path.GetFullPath(notesFolder).TrimEnd('\\', '/');

            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (string file in Directory.GetFiles(notesFull, "*", SearchOption.AllDirectories))
                {
                    string full = Path.GetFullPath(file);
                    if (full.StartsWith(backupFull, StringComparison.OrdinalIgnoreCase)) continue;   // don't back up the backups
                    if (full.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                    AddFile(zip, full, full.Substring(notesFull.Length + 1).Replace('\\', '/'));
                }

                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in otherFiles.Where(File.Exists))
                {
                    string entry = "Other files/" + Path.GetFileName(file);
                    for (int n = 2; !used.Add(entry); n++)
                        entry = "Other files/" + Path.GetFileNameWithoutExtension(file) + " (" + n + ")" + Path.GetExtension(file);
                    AddFile(zip, file, entry);
                }
            }

            if (File.Exists(target)) File.Delete(target);
            File.Move(tmp, target);
            return target;
        }

        private static void AddFile(ZipArchive zip, string path, string entryName)
        {
            try
            {
                // Share everything: the app may be saving this file at the same moment.
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                    entry.LastWriteTime = File.GetLastWriteTime(path);
                    using (var output = entry.Open()) input.CopyTo(output);
                }
            }
            catch (IOException)
            {
                // A file locked by another program is skipped rather than failing the whole backup.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>Deletes backups older than <paramref name="keepDays"/> days. Returns how many were deleted.</summary>
        public static int Prune(string backupFolder, int keepDays, DateTime now)
        {
            if (!Directory.Exists(backupFolder)) return 0;
            DateTime oldest = now.Date.AddDays(-keepDays);
            int deleted = 0;
            foreach (string file in Directory.GetFiles(backupFolder, "TabbedNotepad-*.zip"))
            {
                var m = BackupName.Match(Path.GetFileName(file));
                if (!m.Success) continue;
                if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) continue;
                if (day >= oldest) continue;
                try
                {
                    File.Delete(file);
                    deleted++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return deleted;
        }

        /// <summary>The newest backup in a folder, or null.</summary>
        public static FileInfo Latest(string backupFolder)
        {
            if (!Directory.Exists(backupFolder)) return null;
            return new DirectoryInfo(backupFolder).GetFiles("TabbedNotepad-*.zip")
                .Where(f => BackupName.IsMatch(f.Name))
                .OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
        }
    }
}
