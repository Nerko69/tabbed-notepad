using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TabbedNotepad
{
    /// <summary>One tab as stored on disk.</summary>
    internal sealed class NoteData
    {
        public string Id;
        public string Title;
        public string Text;
    }

    /// <summary>
    /// Saves notes in a folder (Documents\TabbedNotepad by default). Every tab is a plain
    /// .txt file named after the tab, so the notes stay readable with any editor, and
    /// "tabs.ini" keeps the tab order, tab names and window settings.
    /// </summary>
    internal sealed class NoteStore
    {
        private const string IndexFileName = "tabs.ini";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public string Folder { get; }
        public Dictionary<string, string> Settings { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public NoteStore(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(Folder);
        }

        public static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TabbedNotepad");

        /// <summary>True if the folder already has a tab index, i.e. it holds Tabbed Notepad notes.</summary>
        public bool HasIndex => File.Exists(Path.Combine(Folder, IndexFileName));

        public bool NoteFileExists(string id) => File.Exists(NotePath(id));

        /// <summary>
        /// Picks the file name (without .txt) for a tab: the tab name made safe for Windows,
        /// with " (2)", " (3)"... added if another tab already uses it.
        /// </summary>
        public static string FileNameFor(string title, IEnumerable<string> takenIds)
        {
            var taken = new HashSet<string>(takenIds, StringComparer.OrdinalIgnoreCase);
            string name = MakeFileNameSafe(title).Replace('=', '-');
            name = name.TrimStart('[', ';', '#', ' ');
            if (name.Length > 80) name = name.Substring(0, 80).TrimEnd(' ', '.');
            if (name.Length == 0) name = "Untitled";
            if (IsReservedName(name)) name += "_";

            string candidate = name;
            for (int n = 2; taken.Contains(candidate); n++)
                candidate = name + " (" + n + ")";
            return candidate;
        }

        /// <summary>Renames a tab's file on disk (if it exists) when the tab is renamed.</summary>
        public void RenameNote(string oldId, string newId)
        {
            if (oldId == newId) return;
            string from = NotePath(oldId);
            if (!File.Exists(from)) return;
            string to = NotePath(newId);
            if (string.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
            {
                // Only the letter case changes; Windows needs a detour through a temporary name.
                string temp = NotePath(newId + "." + Guid.NewGuid().ToString("N"));
                File.Move(from, temp);
                File.Move(temp, to);
            }
            else
            {
                File.Move(from, to);
            }
        }

        /// <summary>
        /// Loads the tab list and settings. If the folder has no tabs.ini (lost, or a folder of
        /// ordinary text files), every .txt file in it becomes a tab.
        /// </summary>
        public List<NoteData> Load()
        {
            var notes = new List<NoteData>();
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string indexPath = Path.Combine(Folder, IndexFileName);

            if (File.Exists(indexPath))
            {
                string section = "";
                foreach (string raw in File.ReadAllLines(indexPath, Utf8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    if (section == "settings")
                    {
                        Settings[key] = value;
                    }
                    else if (section == "tabs" && IsSafeId(key) && known.Add(key))
                    {
                        notes.Add(new NoteData { Id = key, Title = value, Text = ReadNote(key) });
                    }
                }
            }

            if (File.Exists(indexPath))
                return notes;

            foreach (string file in Directory.GetFiles(Folder, "*.txt"))
            {
                if (!string.Equals(Path.GetExtension(file), ".txt", StringComparison.OrdinalIgnoreCase)) continue;
                string id = Path.GetFileNameWithoutExtension(file);
                if (!IsSafeId(id) || !known.Add(id)) continue;
                notes.Add(new NoteData { Id = id, Title = id, Text = ReadNote(id) });
            }

            return notes;
        }

        public void SaveNote(NoteData note)
        {
            WriteAtomic(NotePath(note.Id), note.Text ?? "");
        }

        public void SaveIndex(IEnumerable<NoteData> notes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("; Tabbed Notepad - tab order, tab names and settings.");
            sb.AppendLine("; Each tab's text is stored in <id>.txt in this folder.");
            sb.AppendLine("[settings]");
            foreach (var pair in Settings)
                sb.Append(pair.Key).Append('=').AppendLine(CleanValue(pair.Value));
            sb.AppendLine();
            sb.AppendLine("[tabs]");
            foreach (var note in notes)
                sb.Append(note.Id).Append('=').AppendLine(CleanValue(note.Title));
            WriteAtomic(Path.Combine(Folder, IndexFileName), sb.ToString());
        }

        /// <summary>Keeps a closed tab's text in the "Closed tabs" subfolder instead of deleting it.</summary>
        public void ArchiveNote(NoteData note)
        {
            if (!string.IsNullOrEmpty(note.Text))
            {
                string archive = Path.Combine(Folder, "Closed tabs");
                Directory.CreateDirectory(archive);
                string name = MakeFileNameSafe(note.Title);
                if (name.Length == 0) name = "Untitled";
                string target = Path.Combine(archive, name + " (closed " + DateTime.Now.ToString("yyyy-MM-dd HH.mm.ss") + ").txt");
                File.WriteAllText(target, note.Text, Utf8);
            }

            string path = NotePath(note.Id);
            if (File.Exists(path))
                File.Delete(path);
        }

        public static string MakeFileNameSafe(string name)
        {
            var sb = new StringBuilder();
            var invalid = Path.GetInvalidFileNameChars();
            foreach (char c in name ?? "")
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString().Trim().TrimEnd('.');
        }

        private string NotePath(string id) => Path.Combine(Folder, id + ".txt");

        private string ReadNote(string id)
        {
            string path = NotePath(id);
            if (!File.Exists(path)) return "";
            byte[] bytes = File.ReadAllBytes(path);

            // Files with a byte order mark (UTF-8/UTF-16) are read as marked.
            if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF) ||
                                      (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)))
            {
                using (var reader = new StreamReader(new MemoryStream(bytes), Utf8, detectEncodingFromByteOrderMarks: true))
                    return reader.ReadToEnd();
            }

            // Otherwise UTF-8, unless the file isn't valid UTF-8: then it's an older Windows (ANSI)
            // text file, e.g. made with old Notepad. Reading it as UTF-8 would garble accented letters.
            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default.GetString(bytes);
            }
        }

        private static bool IsReservedName(string name)
        {
            string upper = name.ToUpperInvariant();
            int dot = upper.IndexOf('.');
            if (dot >= 0) upper = upper.Substring(0, dot);
            return Array.IndexOf(new[] { "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }, upper.TrimEnd()) >= 0;
        }

        private static bool IsSafeId(string id) =>
            id.Length > 0 && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && id.IndexOf('=') < 0 && !id.StartsWith("[");

        private static string CleanValue(string value) =>
            (value ?? "").Replace("\r", " ").Replace("\n", " ").Trim();

        /// <summary>Writes to a temporary file first so a crash or power loss never leaves a half-written note.</summary>
        private static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, Utf8);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }

    /// <summary>
    /// Remembers which folder holds the notes (after "Save All Tabs As" or "Open"), in
    /// %AppData%\TabbedNotepad\notes-folder.txt.
    /// </summary>
    internal static class AppConfig
    {
        private static string ConfigFile => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TabbedNotepad", "notes-folder.txt");

        /// <summary>The folder chosen last time, or null if the user never chose one.</summary>
        public static string LoadNotesFolder()
        {
            try
            {
                if (!File.Exists(ConfigFile)) return null;
                string folder = File.ReadAllText(ConfigFile).Trim();
                return folder.Length > 0 ? folder : null;
            }
            catch
            {
                return null;
            }
        }

        public static void SaveNotesFolder(string folder)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile));
            File.WriteAllText(ConfigFile, folder, new UTF8Encoding(false));
        }
    }
}
