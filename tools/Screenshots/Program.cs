using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TabbedNotepad.Screenshots
{
    /// <summary>
    /// Opens Tabbed Notepad with demo notes, checks the main features work and takes the
    /// screenshots used in docs/user-guide.md. Run on Windows (CI does): Screenshots.exe [outputFolder]
    /// Exit code = number of failed checks.
    /// </summary>
    internal static class Program
    {
        private static int _failures;
        private static string _outDir;

        [STAThread]
        private static int Main(string[] args)
        {
            _outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "screenshots");
            Directory.CreateDirectory(_outDir);
            // On the (throw-away) CI machine use the normal Documents\TabbedNotepad, so the screenshots
            // show the usual path; anywhere else use a temporary folder, never someone's real notes.
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            bool ci = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
            string root = ci && !Directory.Exists(Path.Combine(documents, "TabbedNotepad"))
                ? documents
                : Path.Combine(Path.GetTempPath(), "TabbedNotepadDemo-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string notes = Path.Combine(root, "TabbedNotepad");
            string elsewhere = Path.Combine(root, "Shopping");
            CreateDemoNotes(notes, elsewhere);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Fail("Unhandled exception: " + e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Console.WriteLine("UNHANDLED (background thread): " + e.ExceptionObject);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => Console.WriteLine("UNOBSERVED TASK: " + e.Exception);

            // Keep the mouse pointer away from the window so it doesn't hover over anything.
            Cursor.Position = new Point(SystemInformation.VirtualScreen.Right - 5, SystemInformation.VirtualScreen.Bottom - 5);
            var form = new MainForm(new NoteStore(notes));
            form.Show();
            Pump(1500);

            try
            {
                RunScenes(form, notes, elsewhere);
            }
            catch (Exception ex)
            {
                Fail("Scene crashed: " + ex);
                Shot(form, "zz-crash.png");
            }

            form.Close();
            Pump(300);
            // Exit the way the app does (no forced clean-up), so a crash on exit fails this check.
            Console.WriteLine("Shutdown: form closed, disposed=" + form.IsDisposed + ", open forms=" + Application.OpenForms.Count);
            Console.WriteLine(_failures == 0 ? "All checks passed." : _failures + " check(s) failed.");
            Console.WriteLine("Exit code: " + _failures);
            return _failures;
        }

        private static bool Skip(int scene) =>
            ("," + Environment.GetEnvironmentVariable("TN_SKIP") + ",").Contains("," + scene + ",");

        private static void RunScenes(MainForm form, string notes, string elsewhere)
        {
            if (Skip(0)) return;
            var tabs = Get<TabControl>(form, "_tabs");
            tabs.SelectedIndex = 0;
            var alpha = (NoteTab)tabs.SelectedTab;
            alpha.Editor.Select(0, 0);
            Pump(500);

            if (Environment.GetEnvironmentVariable("TN_STOP") == "1") return;
            // 1. Colored tabs in several rows.
            Check(tabs.TabCount >= 12, "demo tabs loaded (" + tabs.TabCount + ")");
            Check(tabs.Multiline, "tabs in multiple rows by default");
            Check(form.Text.StartsWith("Project Alpha"), "title bar shows the tab name: " + form.Text);
            Check(tabs.TabPages.Cast<NoteTab>().All(t => !t.TabColor.IsEmpty && t.TabColor != SystemColors.Control), "every tab has a color");
            Shot(form, "01-main.png");

            if (Environment.GetEnvironmentVariable("TN_STOP") == "2") return;
            // 2. Undo really works (it used to be wiped by pasting), including for paste and time/date.
            var editor = alpha.Editor;
            string before = editor.PlainText;
            editor.Select(editor.TextLength, 0);
            editor.ReplaceSelection("\nextra line");
            Clipboard.SetText("PASTED TEXT");
            editor.PastePlainText();
            Pump(200);
            Check(editor.PlainText.Contains("PASTED TEXT"), "plain-text paste");
            editor.Undo(); Pump(100);
            editor.Undo(); Pump(100);
            Check(editor.PlainText == before, "Undo takes back paste and typing");
            if (!IsMono)   // Mono's own RichTextBox Redo is broken; Windows uses the real control
            {
                editor.Redo(); Pump(100);
                Check(editor.PlainText.Contains("extra line"), "Redo");
                editor.Undo(); Pump(100);
                Check(editor.PlainText == before, "Undo after Redo");
            }

            if (Environment.GetEnvironmentVariable("TN_STOP") == "3") return;
            // 3. Pop-up search (Ctrl+F): "This tab" by default, a count circle on every tab with
            //    matches, markers next to the scroll bar, Enter jumps to the next match.
            var searchBox = Get<ToolStripTextBox>(form, "_searchBox");
            var searchCount = Get<ToolStripLabel>(form, "_searchCount");
            var scope = Get<ToolStripComboBox>(form, "_searchScope");
            Check(scope.SelectedIndex == 0, "search defaults to This tab");
            tabs.SelectedIndex = 0;
            alpha.Editor.Select(0, 0);
            Pump(200);
            Call(form, "ShowFindPopup");
            var popup = Get<FindPopup>(form, "_findPopup");
            popup.SearchTextBox.Text = "client";   // as if typed
            Pump(150);
            Check(searchBox.Text == "client", "pop-up search and search box stay in sync");
            Call(form, "UpdateSearchHighlights");
            Check(searchCount.Text.Contains("here") && searchCount.Text.Contains("tabs"), "search count shown: " + searchCount.Text);
            var badges = Get<Dictionary<NoteTab, int>>(form, "_tabMatchCounts");
            Check(badges.Count >= 4 && badges[alpha] == 2, "match counts on the tabs: " + string.Join(", ", badges.Select(b => b.Key.Text + "=" + b.Value)));
            Call(form, "Find", "client", false, false, true, false);
            Call(form, "Find", "client", false, false, true, false);
            Check(searchCount.Text.StartsWith("2 of 2"), "match number shown: " + searchCount.Text);
            Check(popup.CountText == searchCount.Text, "pop-up shows the count too");
            Pump(400);
            Shot(form, "02-search.png");
            Call(form, "CloseFindPopup");
            Check(searchBox.Text == "" && badges.Count == 0, "closing the pop-up clears the search");

            if (Environment.GetEnvironmentVariable("TN_STOP") == "4") return;
            // 4. Find dialog with Select All.
            tabs.SelectedIndex = 0;
            Pump(200);
            Call(form, "ShowFind");
            var find = Get<FindDialog>(form, "_findDialog");
            find.SearchText = "notes";
            find.Location = new Point(form.Right - find.Width - 40, form.Top + form.Height / 2 - 40);
            ClickButton(find, "Select All");
            Pump(500);
            var result = AllControls(find).OfType<Label>().FirstOrDefault(l => l.Text.Contains("match"));
            Check(result != null, "Select All shows a count: " + result?.Text);
            ShotUnion("03-find-select-all.png", form.Bounds, find.Bounds);
            find.Hide();
            Pump(200);

            if (Environment.GetEnvironmentVariable("TN_STOP") == "5") return;
            // 5. Links: hovering shows a copy icon at the end of the link.
            tabs.SelectedIndex = 0;
            alpha.Editor.Select(0, 0);
            Pump(300);
            var link = alpha.Editor.Links.FirstOrDefault();
            Check(link.Url != null && link.Url.StartsWith("https://"), "link detected: " + link.Url);
            if (link.Url != null)
            {
                Point p = alpha.Editor.GetPositionFromCharIndex(link.Start + 4);
                p.Offset(2, alpha.Editor.Font.Height / 2);
                Cursor.Position = alpha.Editor.PointToScreen(p);
                Call(alpha, "UpdateCopyButton", p);
                Pump(400);
                var copyButton = Get<CopyLinkButton>(alpha, "_copyButton");
                Check(copyButton.Visible && copyButton.Url == link.Url, "copy icon shown for the link");
                Shot(form, "04-link-copy.png");
                copyButton.Visible = false;
            }

            if (Environment.GetEnvironmentVariable("TN_STOP") == "6") return;
            // 6. Tab right-click menu with colors.
            var tabMenu = Get<ContextMenuStrip>(form, "_tabMenu");
            Rectangle tabRect = tabs.GetTabRect(tabs.SelectedIndex);
            tabMenu.Show(tabs, new Point(tabRect.Left + tabRect.Width / 2, tabRect.Bottom - 4));
            Pump(300);
            var colorItem = tabMenu.Items.OfType<ToolStripMenuItem>().First(i => i.Text.Contains("Color"));
            colorItem.ShowDropDown();
            Pump(500);
            ShotUnion("05-tab-color.png", form.Bounds, tabMenu.Bounds, colorItem.DropDown.Bounds);
            tabMenu.Close();
            Pump(200);

            if (Environment.GetEnvironmentVariable("TN_STOP") == "7") return;
            // 7. File menu: Open Text File, Open Folder, Save All Tabs As...
            var fileMenu = (ToolStripMenuItem)form.MainMenuStrip.Items[0];
            fileMenu.ShowDropDown();
            Pump(500);
            ShotUnion("06-file-menu.png", form.Bounds, fileMenu.DropDown.Bounds);
            fileMenu.HideDropDown();
            Pump(200);

            if (Environment.GetEnvironmentVariable("TN_STOP") == "8") return;
            // 8. A text file opened from another folder: saved back where it came from.
            string shopping = Path.Combine(elsewhere, "Shopping list.txt");
            Call(form, "OpenTextFile", shopping);
            Pump(500);
            var fileTab = (NoteTab)tabs.SelectedTab;
            Check(fileTab.IsExternal && fileTab.Text == "Shopping list", "text file opened in a tab");
            fileTab.Editor.Select(fileTab.Editor.TextLength, 0);
            fileTab.Editor.ReplaceSelection("\ncoffee");
            Call(form, "SaveAll", true, false);
            Check(File.ReadAllText(shopping).Contains("coffee"), "text file saved back in its own folder");
            Check(!File.Exists(Path.Combine(notes, "Shopping list.txt")), "no copy made in the notes folder");
            Pump(300);
            Shot(form, "07-text-file-tab.png");

            if (Environment.GetEnvironmentVariable("TN_STOP") == "9") return;
            // 9. Everything comes back after a restart.
            var reloaded = new NoteStore(notes).Load();
            Check(reloaded.Count == tabs.TabCount, "all tabs saved (" + reloaded.Count + " of " + tabs.TabCount + ")");
            Check(reloaded.All(n => n.Color.HasValue), "tab colors saved");
            Check(reloaded.Any(n => n.ExternalPath != null), "opened text file remembered");

            if (Environment.GetEnvironmentVariable("TN_STOP") == "10") return;
            // 10. Organizing: date lines, labels, bookmarks, navigator, word count, links.
            tabs.SelectedIndex = 1;
            Pump(300);
            var domains = (NoteTab)tabs.SelectedTab;
            var ed = domains.Editor;
            Check(domains.Text == "Domains", "Domains tab");
            Check(ed.SectionLines.Count == 2, "date lines found: " + ed.SectionLines.Count);
            Check(ed.LabelSpans.Count(l => l.Label?.Word == "AVADOMS") == 3 && ed.LabelSpans.Count(l => l.Label?.Word == "REGISTERED") == 1,
                "labels found: " + string.Join(", ", ed.LabelSpans.Select(l => l.Label?.Word)));
            Check(ed.BookmarkLines.SequenceEqual(new[] { 2, 6 }), "bookmarks loaded: " + string.Join(",", ed.BookmarkLines.Select(l => l + 1)));
            // Typing above a bookmark moves it down with its line.
            ed.Select(0, 0);
            ed.ReplaceSelection("new first line\n");
            Pump(200);
            Check(ed.BookmarkLines.SequenceEqual(new[] { 3, 7 }), "bookmarks move with their lines: " + string.Join(",", ed.BookmarkLines.Select(l => l + 1)));
            ed.Undo();
            Pump(200);
            ed.ToggleBookmark(0);
            Check(ed.IsBookmarked(0), "click-to-bookmark");
            ed.ToggleBookmark(0);
            // Insert date line (Ctrl+D).
            ed.Select(ed.TextLength, 0);
            Call(form, "InsertDateLine");
            Check(ed.SectionLines.Count == 3 && ed.LineText(ed.SectionLines[2]).EndsWith(DateTime.Now.ToString("yyyy-MM-dd")), "Insert Date Line");
            ed.Undo();
            Pump(200);
            Check(ed.SectionLines.Count == 2, "Undo of the date line");
            Call(form, "UpdateInfo");
            var stats = Get<ToolStripStatusLabel>(form, "_statsLabel");
            Check(stats.Text.Contains("words") && stats.Text.Contains("characters"), "word counter: " + stats.Text);
            Call(form, "SetNavigator", true);
            ed.Select(0, 0);
            Pump(600);
            Shot(form, "09-organize.png");
            Call(form, "SaveAll", true, false);
            var saved = new NoteStore(notes).Load().First(n => n.Id == "Domains");
            Check(saved.Bookmarks.SequenceEqual(new[] { 2, 6 }), "bookmarks saved: " + string.Join(",", saved.Bookmarks.Select(l => l + 1)));

            // Labels window.
            var labelsDialog = new LabelsDialog(NoteLabels.Items);
            labelsDialog.StartPosition = FormStartPosition.Manual;
            labelsDialog.Location = new Point(form.Left + 120, form.Top + 120);
            labelsDialog.Show(form);
            Pump(500);
            ShotUnion("10-labels.png", form.Bounds, labelsDialog.Bounds);
            labelsDialog.Close();
            Pump(200);

            // All Links window and web page.
            var linkStore = Get<LinkStore>(form, "_links");
            Check(linkStore.All.Count() >= 5, "links remembered: " + linkStore.All.Count());
            Check(File.Exists(Path.Combine(notes, LinkStore.FileName)), "links.tsv written");
            var current = (HashSet<string>)Call(form, "CurrentUrls");
            string page = linkStore.WriteWebPage(notes, current);
            Check(File.ReadAllText(page).Contains("namecheap.com"), "links web page generated");
            var linksDialog = new LinksDialog(linkStore, current, url => false, () => page);
            linksDialog.StartPosition = FormStartPosition.Manual;
            linksDialog.Location = new Point(form.Left + 60, form.Top + 90);
            linksDialog.Show(form);
            Pump(500);
            ShotUnion("11-links.png", form.Bounds, linksDialog.Bounds);
            linksDialog.Close();
            Call(form, "SetNavigator", false);
            Pump(200);

            if (Environment.GetEnvironmentVariable("TN_STOP") == "11") return;
            // 11. New tabs start with the date/time and their file path; Copy File Path; categories.
            var newTab = (NoteTab)Call(form, "CreateTab", "Course ideas", "Course");
            string[] header = newTab.Editor.PlainText.Split('\n');
            Check(header[0].StartsWith("Date/Time\t") && header[1] == "Path\t\t" + Path.Combine(notes, "Course ideas.txt"),
                "new tab header: " + header[0] + " | " + header[1]);
            Check(newTab.Category == "Course", "new tab category");
            Call(form, "RenameTabTo", newTab, "Course plan");
            Check(newTab.Editor.LineText(1) == "Path\t\t" + Path.Combine(notes, "Course plan.txt"), "header path follows a rename: " + newTab.Editor.LineText(1));
            Call(form, "CopyFilePath", newTab);
            Check(Clipboard.GetText() == Path.Combine(notes, "Course plan.txt"), "Copy File Path: " + Clipboard.GetText());

            int all = tabs.TabCount;
            Call(form, "SetCategoryFilter", "CC247");
            Check(tabs.TabCount == 2 && tabs.TabPages.Cast<NoteTab>().All(t => t.Category == "CC247"), "category filter shows CC247 tabs: " + tabs.TabCount);
            var categoryMenu = Get<ToolStripMenuItem>(form, "_categoryMenu");
            Check(categoryMenu.Text.Contains("CC247"), "category menu shows the filter: " + categoryMenu.Text);
            categoryMenu.ShowDropDown();
            Pump(500);
            ShotUnion("12-categories.png", form.Bounds, categoryMenu.DropDown.Bounds);
            categoryMenu.HideDropDown();
            Call(form, "SaveAll", true, false);
            var savedNotes = new NoteStore(notes).Load();
            Check(savedNotes.Count(n => n.Category == "CC247") == 2 && savedNotes.Count == all, "categories saved and hidden tabs kept");
            Call(form, "SetCategoryFilter", (string)null);
            Check(tabs.TabCount == all, "all tabs shown again");

            var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(MainForm).Assembly.Location);
            Check(version.ProductName == "Tabbed Notepad application by WebProgress.AI" && version.FileDescription == "Tabbed Notepad for better productivity",
                "file properties: " + version.FileDescription + " / " + version.ProductName + " / " + version.FileVersion);
            tabs.SelectedIndex = 0;
            Pump(300);

            if (Environment.GetEnvironmentVariable("TN_STOP") == "12") return;
            // 12. Quick tab switcher (Ctrl+P).
            if (!Skip(12)) {
            Check(QuickSwitcher.Score("prjal", "Project Alpha", null) > 0, "switcher finds letters in order");
            Check(QuickSwitcher.Score("cc2", "Client B", "CC247") > 0, "switcher matches categories");
            Check(QuickSwitcher.Score("xyz", "Client B", "CC247") == 0, "switcher skips non-matches");
            var recent = (List<NoteTab>)Call(form, "TabsByRecentUse");
            Check(recent.Count == tabs.TabCount && recent[0] == tabs.SelectedTab, "recently used tabs first");
            var switcher = new QuickSwitcher(recent);
            switcher.Query = "cli";
            Check(switcher.Results.Count > 0 && switcher.Results[0].Text == "Client B", "switcher: \"cli\" finds Client B first: " + string.Join(", ", switcher.Results.Select(t => t.Text)));
            switcher.Location = new Point(form.Left + (form.Width - switcher.Width) / 2, form.Top + 110);
            switcher.Show(form);
            Pump(500);
            ShotUnion("13-quick-switcher.png", form.Bounds, switcher.Bounds);
            switcher.DialogResult = DialogResult.Cancel;
            switcher.Close();
            Pump(200);
            }

            if (Environment.GetEnvironmentVariable("TN_STOP") == "13") return;
            // 13. Daily backup: made in the background when the app starts.
            if (!Skip(13)) {
            string backups = Path.Combine(notes, "Backups");
            string daily = Path.Combine(backups, Backup.DailyFileName(DateTime.Now));
            for (int i = 0; i < 50 && !File.Exists(daily); i++) Pump(100);
            Check(File.Exists(daily), "daily backup made at start: " + daily);
            if (File.Exists(daily))
            {
                using (var zip = System.IO.Compression.ZipFile.OpenRead(daily))
                {
                    var names = zip.Entries.Select(en => en.FullName).ToList();
                    Check(names.Contains("Project Alpha.txt") && names.Contains("tabs.ini") && !names.Any(n => n.StartsWith("Backups/")),
                        "backup holds the notes (" + names.Count + " files)");
                }
            }
            string manual = Backup.Create(notes, backups, new[] { shopping }, DateTime.Now, timestamped: true);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(manual))
                Check(zip.Entries.Any(en => en.FullName == "Other files/Shopping list.txt"), "backup includes text files opened from elsewhere");
            File.WriteAllText(Path.Combine(backups, "TabbedNotepad-2020-01-01.zip"), "old");
            File.WriteAllText(Path.Combine(backups, "TabbedNotepad-2020-01-01-0930.zip"), "old");
            int removed = Backup.Prune(backups, Backup.KeepDays, DateTime.Now);
            Check(removed == 2 && File.Exists(daily) && File.Exists(manual), "backups older than " + Backup.KeepDays + " days removed: " + removed);
            }

            if (Environment.GetEnvironmentVariable("TN_STOP") == "14") return;
            // 14. One row of tabs instead (View > Tabs in Multiple Rows off).
            Call(form, "SetMultiRow", false);
            tabs.SelectedIndex = 0;
            Pump(500);
            Shot(form, "08-single-row.png");
            Call(form, "SetMultiRow", true);
            Pump(200);
        }

        // ---------------------------------------------------------------- demo data

        private static void CreateDemoNotes(string notes, string elsewhere)
        {
            Directory.CreateDirectory(notes);
            Directory.CreateDirectory(elsewhere);
            void Note(string name, string text) => File.WriteAllText(Path.Combine(notes, name + ".txt"), text.Replace("\n", "\r\n"));

            Note("Project Alpha",
                "9:05 AM 10/8/2026\n" +
                "Standup: API is ready for testing, client demo moved to Friday.\n" +
                "Spec: https://github.com/Nerko69/tabbed-notepad\n" +
                "Design board: https://www.figma.com/file/alpha-dashboard\n\n" +
                "2:30 PM 10/8/2026\n" +
                "Call with the client about the login page. They want a \"remember me\" option.\n" +
                "TODO: update notes for the Friday demo\n");
            Note("Domains",
                "-----------------2026-10-07\n" +
                "Names for the new shop\n" +
                "coolshop.com AVADOMS\n" +
                "bestshop.net taken\n" +
                "coolshop.io AVADOMS https://www.namecheap.com/domains/\n" +
                "-----------------2026-10-08\n" +
                "coolshop.com REGISTERED at https://www.namecheap.com\n" +
                "shopcool.net AVADOMS - ask the client\n");
            Note("Client B",
                "Client B - support contract\n" +
                "Portal: https://portal.example.com/client-b\n" +
                "- send invoice for September\n- review client feedback notes\n");
            Note("Website", "Homepage redesign\n- new hero image\n- client logos section\n");
            Note("Marketing", "Newsletter ideas\n- product update\n- meeting notes summary\n");
            Note("Invoices", "September: sent\nOctober: draft\n");
            Note("Ideas", "App ideas\n- tabbed notes for every project\n");
            Note("Travel", "Flights: https://www.example.com/booking/123\nHotel: 3 nights\n");
            Note("Recipes", "Pancakes\n2 eggs, 200 g flour, 300 ml milk\n");
            Note("Reading list", "Books and articles to read\n");
            Note("Meeting notes", "Weekly sync notes\n- client update\n");
            Note("Budget 2026", "Q1: planning\nQ2: hiring\n");
            Note("Hiring", "Interview notes\n- front-end developer\n");
            Note("Taxes", "Documents to collect\n");
            Note("Home", "Fix the kitchen tap\n");

            var colors = new[] { "#A5D8FF", "#FFF09E", "#FFC9C9", "#B2F2BB", "#FFD8A8", "#D0BFFF", "#FFF09E", "#96F2D7", "#FCC2D7", "#BAC8FF", "#D8F5A2", "#99E9F2", "#DEE2E6", "#FFD8A8", "#B2F2BB" };
            var names = new[] { "Project Alpha", "Domains", "Client B", "Website", "Marketing", "Invoices", "Ideas", "Travel", "Recipes", "Reading list", "Meeting notes", "Budget 2026", "Hiring", "Taxes", "Home" };
            var ini = new List<string> { "[settings]", "window=20,20,1000,640,0", "labels=AVADOMS:#B2F2BB;REGISTERED:#FFC9C9", "[tabs]" };
            ini.AddRange(names.Select(n => n + "=" + n));
            ini.Add("[colors]");
            ini.AddRange(names.Select((n, i) => n + "=" + colors[i]));
            ini.Add("[categories]");
            ini.Add("Project Alpha=CC247");
            ini.Add("Client B=CC247");
            ini.Add("Domains=WP Plugin");
            ini.Add("Website=WP Plugin");
            ini.Add("Hiring=OrgSys");
            ini.Add("[bookmarks]");
            ini.Add("Domains=3,7");
            File.WriteAllLines(Path.Combine(notes, "tabs.ini"), ini);

            File.WriteAllText(Path.Combine(elsewhere, "Shopping list.txt"), "Shopping list\r\nmilk\r\nbread\r\n");
        }

        // ---------------------------------------------------------------- helpers

        private static bool IsMono => Type.GetType("Mono.Runtime") != null;

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
            if (!ok) _failures++;
        }

        private static void Fail(string what)
        {
            Console.WriteLine("FAIL  " + what);
            _failures++;
        }

        private static void Pump(int milliseconds)
        {
            var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < end)
            {
                Application.DoEvents();
                Thread.Sleep(15);
            }
        }

        private static T Get<T>(object target, string field) =>
            (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private static object Call(object target, string method, params object[] args)
        {
            var m = target.GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(x => x.Name == method && x.GetParameters().Length == args.Length);
            object result = m.Invoke(target, args);
            Pump(150);
            return result;
        }

        private static IEnumerable<Control> AllControls(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                yield return c;
                foreach (var child in AllControls(c)) yield return child;
            }
        }

        private static void ClickButton(Control parent, string text) =>
            AllControls(parent).OfType<Button>().First(b => b.Text == text).PerformClick();

        private static void Shot(Form form, string name) => ShotUnion(name, form.Bounds);

        private static void ShotUnion(string name, params Rectangle[] areas)
        {
            Pump(200);
            Rectangle r = areas.Aggregate(Rectangle.Union);
            r = VisibleFrame(r);
            r.Intersect(SystemInformation.VirtualScreen);
            using (var bmp = new Bitmap(r.Width, r.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.Location, Point.Empty, r.Size);
                bmp.Save(Path.Combine(_outDir, name), ImageFormat.Png);
            }
            Console.WriteLine("Saved " + name);
        }

        // Windows 10/11 windows have invisible resize borders (about 7 px) that would show the desktop.
        private static Rectangle VisibleFrame(Rectangle r)
        {
            int border = SystemInformation.FrameBorderSize.Width + SystemInformation.BorderSize.Width;
            return Environment.OSVersion.Version.Major >= 6
                ? new Rectangle(r.X + border, r.Y, r.Width - 2 * border, r.Height - border)
                : r;
        }
    }
}
