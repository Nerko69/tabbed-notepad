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
            string root = Path.Combine(Path.GetTempPath(), "TabbedNotepadDemo-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            string notes = Path.Combine(root, "TabbedNotepad");
            string elsewhere = Path.Combine(root, "Documents");
            CreateDemoNotes(notes, elsewhere);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Fail("Unhandled exception: " + e.Exception);

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
            Console.WriteLine(_failures == 0 ? "All checks passed." : _failures + " check(s) failed.");
            return _failures;
        }

        private static void RunScenes(MainForm form, string notes, string elsewhere)
        {
            var tabs = Get<TabControl>(form, "_tabs");
            tabs.SelectedIndex = 0;
            var alpha = (NoteTab)tabs.SelectedTab;
            alpha.Editor.Select(0, 0);
            Pump(500);

            // 1. Colored tabs in several rows.
            Check(tabs.TabCount >= 12, "demo tabs loaded (" + tabs.TabCount + ")");
            Check(tabs.Multiline, "tabs in multiple rows by default");
            Check(tabs.TabPages.Cast<NoteTab>().All(t => !t.TabColor.IsEmpty && t.TabColor != SystemColors.Control), "every tab has a color");
            Shot(form, "01-main.png");

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

            // 3. Search bar: highlight all matches in all tabs, Enter jumps to the next one.
            var searchBox = Get<ToolStripTextBox>(form, "_searchBox");
            var searchCount = Get<ToolStripLabel>(form, "_searchCount");
            tabs.SelectedIndex = 0;
            alpha.Editor.Select(0, 0);
            Pump(200);
            searchBox.Text = "client";
            Call(form, "UpdateSearchHighlights");
            Check(searchCount.Text.Contains("matches in"), "search count shown: " + searchCount.Text);
            Call(form, "Find", "client", false, true, true, false);
            Call(form, "Find", "client", false, true, true, false);
            Check(searchCount.Text.StartsWith("2 of "), "match number shown: " + searchCount.Text);
            searchBox.Focus();
            Pump(400);
            Shot(form, "02-search.png");
            searchBox.Text = "";
            Call(form, "UpdateSearchHighlights");

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

            // 7. File menu: Open Text File, Open Folder, Save All Tabs As...
            var fileMenu = (ToolStripMenuItem)form.MainMenuStrip.Items[0];
            fileMenu.ShowDropDown();
            Pump(500);
            ShotUnion("06-file-menu.png", form.Bounds, fileMenu.DropDown.Bounds);
            fileMenu.HideDropDown();
            Pump(200);

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

            // 9. Everything comes back after a restart.
            var reloaded = new NoteStore(notes).Load();
            Check(reloaded.Count == tabs.TabCount, "all tabs saved (" + reloaded.Count + " of " + tabs.TabCount + ")");
            Check(reloaded.All(n => n.Color.HasValue), "tab colors saved");
            Check(reloaded.Any(n => n.ExternalPath != null), "opened text file remembered");

            // 10. One row of tabs instead (Format > Tabs in Multiple Rows off).
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

            var colors = new[] { "#A5D8FF", "#FFC9C9", "#B2F2BB", "#FFD8A8", "#D0BFFF", "#FFF09E", "#96F2D7", "#FCC2D7", "#BAC8FF", "#D8F5A2", "#99E9F2", "#DEE2E6", "#FFD8A8", "#B2F2BB" };
            var names = new[] { "Project Alpha", "Client B", "Website", "Marketing", "Invoices", "Ideas", "Travel", "Recipes", "Reading list", "Meeting notes", "Budget 2026", "Hiring", "Taxes", "Home" };
            var ini = new List<string> { "[settings]", "window=20,20,1000,640,0", "[tabs]" };
            ini.AddRange(names.Select(n => n + "=" + n));
            ini.Add("[colors]");
            ini.AddRange(names.Select((n, i) => n + "=" + colors[i]));
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
