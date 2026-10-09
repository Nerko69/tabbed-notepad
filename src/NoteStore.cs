using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace TabbedNotepad
{
    /// <summary>One tab as stored on disk.</summary>
    internal sealed class NoteData
    {
        public string Id;
        public string Title;
        public string Text;
        public Color? Color;

        /// <summary>For a text file opened from elsewhere: its full path. Null for tabs kept in the notes folder.</summary>
        public string ExternalPath;
        public Encoding FileEncoding;
        public DateTime FileTimestampUtc;
    }

    /// <summary>
    /// Saves notes in a folder (Documents\TabbedNotepad by default). Every tab is a plain
    /// .txt file named after the tab, so the notes stay readable with any editor, and
    /// "tabs.ini" keeps the tab order, tab names, tab colors and window settings.
    /// Text files opened from elsewhere stay where they are; tabs.ini only remembers their path.
    /// </summary>
    internal sealed class NoteStore
    {
        private const string IndexFileName = "tabs.ini";
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public string Folder { get; }
        public Dictionary<string, string> Settings { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Files opened from elsewhere that could not be found by the last <see cref="Load"/>.</summary>
        public List<string> MissingFiles { get; } = new List<string>();

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

        public string NotePath(string id) => Path.Combine(Folder, id + ".txt");

        public static string NewExternalId() => "file-" + Guid.NewGuid().ToString("N").Substring(0, 12);

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
            var byId = new Dictionary<string, NoteData>(StringComparer.OrdinalIgnoreCase);
            var externalPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
            MissingFiles.Clear();
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

                    switch (section)
                    {
                        case "settings":
                            Settings[key] = value;
                            break;
                        case "tabs":
                            if (IsSafeId(key) && !byId.ContainsKey(key))
                            {
                                var note = new NoteData { Id = key, Title = value };
                                byId[key] = note;
                                notes.Add(note);
                            }
                            break;
                        case "files":
                            externalPaths[key] = value;
                            break;
                        case "colors":
                            if (TryParseColor(value, out Color color)) colors[key] = color;
                            break;
                    }
                }

                foreach (var note in notes.ToList())
                {
                    if (colors.TryGetValue(note.Id, out Color color)) note.Color = color;
                    if (externalPaths.TryGetValue(note.Id, out string path))
                    {
                        note.ExternalPath = path;
                        if (!File.Exists(path))
                        {
                            MissingFiles.Add(path);
                            notes.Remove(note);
                            continue;
                        }
                        try
                        {
                            note.Text = ReadTextFile(path, out note.FileEncoding);
                            note.FileTimestampUtc = File.GetLastWriteTimeUtc(path);
                        }
                        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                        {
                            // Not one of our notes: skip it rather than fail to open everything.
                            MissingFiles.Add(path + " (" + ex.Message + ")");
                            notes.Remove(note);
                        }
                    }
                    else
                    {
                        string notePath = NotePath(note.Id);
                        note.Text = File.Exists(notePath) ? ReadTextFile(notePath, out _) : "";
                    }
                }
                return notes;
            }

            foreach (string file in Directory.GetFiles(Folder, "*.txt"))
            {
                if (!string.Equals(Path.GetExtension(file), ".txt", StringComparison.OrdinalIgnoreCase)) continue;
                string id = Path.GetFileNameWithoutExtension(file);
                if (!IsSafeId(id) || byId.ContainsKey(id)) continue;
                var note = new NoteData { Id = id, Title = id, Text = ReadTextFile(file, out _) };
                byId[id] = note;
                notes.Add(note);
            }
            return notes;
        }

        public void SaveNote(NoteData note)
        {
            WriteAtomic(NotePath(note.Id), ToWindowsNewlines(note.Text), Utf8);
        }

        public void SaveIndex(IEnumerable<NoteData> notes)
        {
            var list = notes.ToList();
            var sb = new StringBuilder();
            sb.AppendLine("; Tabbed Notepad - tab order, tab names, tab colors and settings.");
            sb.AppendLine("; Each tab's text is stored in <name>.txt in this folder, except tabs listed under [files],");
            sb.AppendLine("; which are text files opened from elsewhere and saved where they are.");
            sb.AppendLine("[settings]");
            foreach (var pair in Settings)
                sb.Append(pair.Key).Append('=').AppendLine(CleanValue(pair.Value));
            sb.AppendLine();
            sb.AppendLine("[tabs]");
            foreach (var note in list)
                sb.Append(note.Id).Append('=').AppendLine(CleanValue(note.Title));
            sb.AppendLine();
            sb.AppendLine("[files]");
            foreach (var note in list.Where(n => n.ExternalPath != null))
                sb.Append(note.Id).Append('=').AppendLine(CleanValue(note.ExternalPath));
            sb.AppendLine();
            sb.AppendLine("[colors]");
            foreach (var note in list.Where(n => n.Color.HasValue))
                sb.Append(note.Id).Append('=').AppendLine(ColorToText(note.Color.Value));
            WriteAtomic(Path.Combine(Folder, IndexFileName), sb.ToString(), Utf8);
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
                File.WriteAllText(target, ToWindowsNewlines(note.Text), Utf8);
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

        // ---------------------------------------------------------------- text files

        /// <summary>
        /// Reads a text file, detecting its encoding: a byte order mark (UTF-8/UTF-16), else UTF-8,
        /// else the Windows ANSI code page (older Notepad files). Reading those as UTF-8 would
        /// garble accented letters.
        /// </summary>
        public static string ReadTextFile(string path, out Encoding encoding)
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                encoding = new UTF8Encoding(true);
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                encoding = Encoding.Unicode;
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                encoding = Encoding.BigEndianUnicode;
            else
            {
                try
                {
                    string text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
                    encoding = Utf8;
                    return text;
                }
                catch (DecoderFallbackException)
                {
                    encoding = Encoding.Default;
                    return Encoding.Default.GetString(bytes);
                }
            }

            using (var reader = new StreamReader(new MemoryStream(bytes), encoding, detectEncodingFromByteOrderMarks: true))
                return reader.ReadToEnd();
        }

        /// <summary>
        /// Saves a text file opened from elsewhere in the encoding it had. If the text now holds
        /// characters that encoding can't store (e.g. emoji in an ANSI file), saves as UTF-8 instead.
        /// Returns the encoding used.
        /// </summary>
        public static Encoding WriteTextFile(string path, string text, Encoding encoding)
        {
            text = ToWindowsNewlines(text);
            encoding = encoding ?? Utf8;
            if (!CanEncode(encoding, text))
                encoding = new UTF8Encoding(true);
            WriteAtomic(path, text, encoding);
            return encoding;
        }

        private static bool CanEncode(Encoding encoding, string text)
        {
            if (encoding is UTF8Encoding || encoding is UnicodeEncoding) return true;
            try
            {
                var strict = Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                strict.GetBytes(text);
                return true;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }

        /// <summary>Notepad-style line breaks (\r\n), whatever the editor produced.</summary>
        public static string ToWindowsNewlines(string text) =>
            (text ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

        // ---------------------------------------------------------------- helpers

        public static string ColorToText(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        public static bool TryParseColor(string text, out Color color)
        {
            color = Color.Empty;
            text = (text ?? "").Trim().TrimStart('#');
            if (text.Length != 6) return false;
            if (!int.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out int rgb)) return false;
            color = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return true;
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

        /// <summary>Writes to a temporary file first so a crash or power loss never leaves a half-written file.</summary>
        private static void WriteAtomic(string path, string content, Encoding encoding)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, encoding);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }

    /// <summary>
    /// Remembers which folder holds the notes (after "Save All Tabs As" or "Open Folder"), in
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
