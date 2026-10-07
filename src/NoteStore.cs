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
    /// .txt file so the notes stay readable with any editor, and "tabs.ini" keeps the tab
    /// order, tab names and window settings.
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

        public static string NewId() => "note-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);

        /// <summary>Loads the tab list and settings. Text files without an index entry are added as extra tabs.</summary>
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

            // Pick up .txt files that are not in the index (e.g. index lost or files copied in by hand).
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
            // Detects a byte order mark if present, otherwise reads as UTF-8.
            return File.Exists(path) ? File.ReadAllText(path, Utf8) : "";
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
}
