using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>One web link ever written in the notes.</summary>
    internal sealed class LinkRecord
    {
        public string Url;
        public DateTime FirstSeen;
        public DateTime LastSeen;
        public SortedSet<string> Tabs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        public string Context;   // the line it was on, last time it was seen
    }

    /// <summary>
    /// Remembers every web link written in the notes, even after it's deleted from a note, in
    /// links.tsv in the notes folder (a tab-separated text file that opens in Excel).
    /// </summary>
    internal sealed class LinkStore
    {
        public const string FileName = "links.tsv";
        private readonly string _path;
        private readonly Dictionary<string, LinkRecord> _links = new Dictionary<string, LinkRecord>(StringComparer.Ordinal);
        private bool _dirty;

        public LinkStore(string folder)
        {
            _path = Path.Combine(folder, FileName);
            Load();
        }

        public IEnumerable<LinkRecord> All => _links.Values;

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                foreach (string line in File.ReadAllLines(_path, Encoding.UTF8).Skip(1))
                {
                    var f = line.Split('\t');
                    if (f.Length < 5 || f[0].Length == 0) continue;
                    var r = new LinkRecord
                    {
                        Url = f[0],
                        FirstSeen = ParseDate(f[1]),
                        LastSeen = ParseDate(f[2]),
                        Context = f[4],
                    };
                    foreach (string tab in f[3].Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries)) r.Tabs.Add(tab);
                    _links[r.Url] = r;
                }
            }
            catch (IOException)
            {
                // Unreadable right now: start empty, the file is rewritten on the next save.
            }
        }

        private static DateTime ParseDate(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : DateTime.Now;

        /// <summary>Records the links found in a tab's text.</summary>
        public void Update(string tabTitle, string text, DateTime now)
        {
            foreach (var link in NoteEditor.FindLinks(text))
            {
                string context = LineAround(text, link.Start);
                if (!_links.TryGetValue(link.Url, out var r))
                {
                    r = new LinkRecord { Url = link.Url, FirstSeen = now, LastSeen = now, Context = context };
                    _links[link.Url] = r;
                    _dirty = true;
                }
                // Update "last seen" at most once a day, so saving doesn't rewrite the file all the time.
                if (r.LastSeen.Date != now.Date) { r.LastSeen = now; _dirty = true; }
                if (r.Tabs.Add(tabTitle)) _dirty = true;
                if (r.Context != context) { r.Context = context; _dirty = true; }
            }
        }

        private static string LineAround(string text, int index)
        {
            int start = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
            int end = text.IndexOf('\n', index);
            if (end < 0) end = text.Length;
            string line = text.Substring(start, end - start).Replace('\t', ' ').Replace("\r", "").Trim();
            return line.Length > 200 ? line.Substring(0, 197) + "..." : line;
        }

        public void SaveIfChanged()
        {
            if (!_dirty) return;
            var sb = new StringBuilder();
            sb.AppendLine("Link\tFirst seen\tLast seen\tTabs\tLine");
            foreach (var r in _links.Values.OrderByDescending(r => r.LastSeen))
                sb.Append(r.Url).Append('\t')
                  .Append(r.FirstSeen.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(r.LastSeen.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(string.Join(" | ", r.Tabs)).Append('\t')
                  .AppendLine(r.Context);
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));   // BOM so Excel reads it as UTF-8
            if (File.Exists(_path)) File.Replace(tmp, _path, null); else File.Move(tmp, _path);
            _dirty = false;
        }

        /// <summary>Writes links.html: all links grouped by tab, newest first, and returns its path.</summary>
        public string WriteWebPage(string folder, ICollection<string> currentUrls)
        {
            string path = Path.Combine(folder, "links.html");
            string H(string s) => WebUtility.HtmlEncode(s ?? "");
            string Href(string url) => url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "http://" + url : url;

            var sb = new StringBuilder();
            sb.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>Links - Tabbed Notepad</title>");
            sb.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222}h1{font-size:22px}" +
                          "h2{font-size:17px;margin-top:28px;border-bottom:1px solid #ddd;padding-bottom:4px}" +
                          "table{border-collapse:collapse;width:100%}td,th{text-align:left;padding:5px 8px;border-bottom:1px solid #eee;vertical-align:top;font-size:14px}" +
                          "th{color:#666;font-weight:600}.gone{color:#999}.line{color:#666}.date{white-space:nowrap;color:#666}" +
                          "a{color:#0b5cad;word-break:break-all}</style></head><body>");
            sb.AppendLine("<h1>All links in your notes</h1>");
            sb.Append("<p>").Append(_links.Count).Append(" links, generated ").Append(H(DateTime.Now.ToString("f")))
              .AppendLine(". Grey links are no longer in any note.</p>");

            var byTab = _links.Values.SelectMany(r => r.Tabs.DefaultIfEmpty("(unknown tab)").Select(t => (Tab: t, Link: r)))
                .GroupBy(x => x.Tab, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var group in byTab)
            {
                sb.Append("<h2>").Append(H(group.Key)).AppendLine("</h2><table><tr><th>Link</th><th>Line</th><th>First seen</th><th>Last seen</th></tr>");
                foreach (var r in group.Select(x => x.Link).OrderByDescending(r => r.LastSeen))
                {
                    bool gone = !currentUrls.Contains(r.Url);
                    sb.Append("<tr").Append(gone ? " class=\"gone\"" : "").Append("><td><a href=\"").Append(H(Href(r.Url))).Append("\">")
                      .Append(H(r.Url)).Append("</a></td><td class=\"line\">").Append(H(r.Context)).Append("</td><td class=\"date\">")
                      .Append(H(r.FirstSeen.ToString("yyyy-MM-dd"))).Append("</td><td class=\"date\">")
                      .Append(H(r.LastSeen.ToString("yyyy-MM-dd"))).AppendLine("</td></tr>");
                }
                sb.AppendLine("</table>");
            }
            sb.AppendLine("</body></html>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
            return path;
        }
    }

    /// <summary>Tools > All Links: every link ever written in the notes, with search, open, copy and "go to".</summary>
    internal sealed class LinksDialog : Form
    {
        private readonly LinkStore _store;
        private readonly HashSet<string> _current;
        private readonly Func<string, bool> _goTo;
        private readonly Func<string> _makeWebPage;
        private readonly ListView _list;
        private readonly TextBox _filter;
        private readonly Label _count;
        private int _sortColumn = 3;
        private bool _sortDescending = true;

        /// <param name="current">Links that are in the notes right now.</param>
        /// <param name="goTo">Shows a link in its tab; returns false if it isn't in any note any more.</param>
        /// <param name="makeWebPage">Writes the links web page and returns its path.</param>
        public LinksDialog(LinkStore store, HashSet<string> current, Func<string, bool> goTo, Func<string> makeWebPage)
        {
            _store = store;
            _current = current;
            _goTo = goTo;
            _makeWebPage = makeWebPage;

            Text = "All Links";
            Font = SystemFonts.MessageBoxFont;
            Size = new Size(Dpi.Scale(900), Dpi.Scale(520));
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(Dpi.Scale(6)), WrapContents = false };
            top.Controls.Add(new Label { Text = "Filter:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, Dpi.Scale(6), 0, 0) });
            _filter = new TextBox { Width = Dpi.Scale(260) };
            top.Controls.Add(_filter);
            _count = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(Dpi.Scale(10), Dpi.Scale(6), 0, 0) };
            top.Controls.Add(_count);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
            };
            _list.Columns.Add("Link", Dpi.Scale(330));
            _list.Columns.Add("Tab", Dpi.Scale(120));
            _list.Columns.Add("First seen", Dpi.Scale(95));
            _list.Columns.Add("Last seen", Dpi.Scale(95));
            _list.Columns.Add("Line", Dpi.Scale(240));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Dpi.Scale(6)) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            var page = new Button { Text = "Open as Web Page", AutoSize = true };
            var show = new Button { Text = "Show in Note", AutoSize = true };
            var copy = new Button { Text = "Copy Link", AutoSize = true };
            var open = new Button { Text = "Open Link", AutoSize = true };
            buttons.Controls.AddRange(new Control[] { close, page, show, copy, open });

            Controls.Add(_list);
            Controls.Add(top);
            Controls.Add(buttons);
            CancelButton = close;

            _filter.TextChanged += (s, e) => Fill();
            _list.DoubleClick += (s, e) => NoteEditor.OpenLink(SelectedUrl);
            _list.ColumnClick += (s, e) =>
            {
                if (_sortColumn == e.Column) _sortDescending = !_sortDescending;
                else { _sortColumn = e.Column; _sortDescending = e.Column >= 2; }
                Fill();
            };
            open.Click += (s, e) => NoteEditor.OpenLink(SelectedUrl);
            copy.Click += (s, e) =>
            {
                if (SelectedUrl == null) return;
                try { Clipboard.SetText(SelectedUrl); } catch (System.Runtime.InteropServices.ExternalException) { }
            };
            show.Click += (s, e) =>
            {
                if (SelectedUrl == null) return;
                if (_goTo(SelectedUrl)) Close();
                else MessageBox.Show(this, "This link isn't in any of your notes any more.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            page.Click += (s, e) =>
            {
                try
                {
                    Process.Start(_makeWebPage());
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Could not create the web page:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            Fill();
        }

        private string SelectedUrl => _list.SelectedItems.Count > 0 ? (string)_list.SelectedItems[0].Tag : null;

        private void Fill()
        {
            string filter = _filter.Text.Trim();
            IEnumerable<LinkRecord> links = _store.All;
            if (filter.Length > 0)
                links = links.Where(r => r.Url.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         (r.Context ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         r.Tabs.Any(t => t.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
            Func<LinkRecord, IComparable> key;
            switch (_sortColumn)
            {
                case 0: key = r => r.Url; break;
                case 1: key = r => string.Join(", ", r.Tabs); break;
                case 2: key = r => r.FirstSeen; break;
                case 4: key = r => r.Context ?? ""; break;
                default: key = r => r.LastSeen; break;
            }
            var sorted = (_sortDescending ? links.OrderByDescending(key) : links.OrderBy(key)).ToList();

            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var r in sorted)
            {
                var item = new ListViewItem(new[]
                {
                    r.Url, string.Join(", ", r.Tabs), r.FirstSeen.ToString("yyyy-MM-dd"), r.LastSeen.ToString("yyyy-MM-dd"), r.Context,
                }) { Tag = r.Url };
                if (!_current.Contains(r.Url))
                {
                    item.ForeColor = SystemColors.GrayText;
                    item.ToolTipText = "No longer in any note";
                }
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            _count.Text = sorted.Count + " of " + _store.All.Count() + " links (grey: no longer in any note). Double-click to open.";
        }
    }
}
