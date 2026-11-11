using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace TabbedNotepad
{
    /// <summary>
    /// Turns copied HTML that contains a table (from a web page, Excel, Word, Google Sheets...)
    /// into plain text with the table drawn in lined-up columns:
    /// <code>
    /// ┌──────┬───────┐
    /// │ Name │ Price │
    /// ├──────┼───────┤
    /// │ Tea  │  3.50 │
    /// └──────┴───────┘
    /// </code>
    /// </summary>
    internal static class HtmlTables
    {
        private static readonly Regex Tag = new Regex(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)\b[^>]*>", RegexOptions.Compiled);
        private static readonly Regex Junk = new Regex(@"<!--.*?-->|<(style|script|head|title)\b.*?</\1\s*>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
        private static readonly Regex Colspan = new Regex(@"\bcolspan\s*=\s*[""']?(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Number = new Regex(@"^[-+]?[$€£]?\s?[\d.,]+\s?%?$", RegexOptions.Compiled);
        private const int MaxColumnWidth = 60;

        /// <summary>The clipboard's HTML as text, or null if it has no table (then paste the plain text).</summary>
        public static string ToText(string clipboardHtml)
        {
            if (string.IsNullOrEmpty(clipboardHtml)) return null;
            string html = Fragment(clipboardHtml);
            if (html.IndexOf("<table", StringComparison.OrdinalIgnoreCase) < 0) return null;
            html = Junk.Replace(html, "");

            var output = new StringBuilder();
            int pos = 0;
            while (FindTable(html, pos, out int start, out int end))
            {
                output.Append(InlineText(html.Substring(pos, start - pos)));
                string table = RenderTable(html.Substring(start, end - start));
                if (table.Length > 0)
                {
                    if (output.Length > 0 && output[output.Length - 1] != '\n') output.Append('\n');
                    output.Append(table);
                }
                pos = end;
            }
            output.Append(InlineText(html.Substring(pos)));
            string text = Regex.Replace(output.ToString(), @"\n{3,}", "\n\n").Trim('\n');
            return text.Length > 0 ? text + "\n" : null;
        }

        /// <summary>The part that was copied: the clipboard's HTML format wraps it in a header and markers.</summary>
        private static string Fragment(string html)
        {
            int start = html.IndexOf("<!--StartFragment", StringComparison.OrdinalIgnoreCase);
            int end = html.IndexOf("<!--EndFragment", StringComparison.OrdinalIgnoreCase);
            if (start >= 0 && end > start)
            {
                start = html.IndexOf("-->", start, StringComparison.Ordinal) + 3;
                return html.Substring(start, end - start);
            }
            int body = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (body >= 0) return html.Substring(body);
            int first = html.IndexOf('<');
            return first >= 0 ? html.Substring(first) : html;
        }

        /// <summary>Finds the next outermost &lt;table&gt;...&lt;/table&gt; (nested tables included).</summary>
        private static bool FindTable(string html, int from, out int start, out int end)
        {
            start = end = -1;
            int depth = 0;
            for (Match m = Tag.Match(html, from); m.Success; m = m.NextMatch())
            {
                if (!m.Groups[2].Value.Equals("table", StringComparison.OrdinalIgnoreCase)) continue;
                if (m.Groups[1].Value.Length == 0)
                {
                    if (depth++ == 0) start = m.Index;
                }
                else if (depth > 0 && --depth == 0)
                {
                    end = m.Index + m.Length;
                    return true;
                }
            }
            if (start < 0) return false;
            end = html.Length;   // table not closed: take the rest
            return true;
        }

        private sealed class Cell
        {
            public string Text;
            public int Span = 1;
        }

        private static string RenderTable(string tableHtml)
        {
            // Read rows and cells. Text of a table nested in a cell becomes part of that cell.
            var rows = new List<List<Cell>>();
            var headerRows = new HashSet<int>();
            List<Cell> row = null;
            int depth = 0, cellStart = -1, cellSpan = 1;
            bool headerCell = false;
            for (Match m = Tag.Match(tableHtml); m.Success; m = m.NextMatch())
            {
                string name = m.Groups[2].Value.ToLowerInvariant();
                bool closing = m.Groups[1].Value.Length > 0;
                if (name == "table")
                {
                    depth += closing ? -1 : 1;
                    continue;
                }
                if (depth != 1) continue;
                if (name == "tr")
                {
                    if (cellStart >= 0) { EndCell(row, tableHtml.Substring(cellStart, m.Index - cellStart), cellSpan); cellStart = -1; }
                    if (!closing) { row = new List<Cell>(); rows.Add(row); }
                }
                else if (name == "td" || name == "th")
                {
                    if (cellStart >= 0) { EndCell(row, tableHtml.Substring(cellStart, m.Index - cellStart), cellSpan); cellStart = -1; }
                    if (closing) continue;
                    if (row == null) { row = new List<Cell>(); rows.Add(row); }
                    cellStart = m.Index + m.Length;
                    var span = Colspan.Match(m.Value);
                    cellSpan = span.Success ? Math.Max(1, Math.Min(50, int.Parse(span.Groups[1].Value))) : 1;
                    headerCell = name == "th";
                    if (headerCell) headerRows.Add(rows.Count - 1);
                }
            }
            if (cellStart >= 0) EndCell(row, tableHtml.Substring(cellStart), cellSpan);
            rows.RemoveAll(r => r.Count == 0);
            if (rows.Count == 0) return "";

            int columns = rows.Max(r => r.Sum(c => c.Span));
            // Spread each row over the columns; a cell spanning columns fills the first of them.
            var grid = rows.Select(r =>
            {
                var cells = new string[columns];
                int i = 0;
                foreach (var c in r)
                {
                    cells[i] = c.Text;
                    for (int k = 1; k < c.Span && i + k < columns; k++) cells[i + k] = "";
                    i += c.Span;
                }
                for (; i < columns; i++) cells[i] = "";
                return cells;
            }).ToList();

            // Drop columns that are empty in every row (spacer cells).
            var keep = Enumerable.Range(0, columns).Where(c => grid.Any(r => r[c].Length > 0)).ToArray();
            if (keep.Length == 0) return "";
            grid = grid.Select(r => keep.Select(c => r[c]).ToArray()).ToList();
            columns = keep.Length;

            int[] widths = new int[columns];
            for (int c = 0; c < columns; c++)
                widths[c] = Math.Min(MaxColumnWidth, Math.Max(1, grid.Max(r => r[c].Length)));
            bool[] numeric = new bool[columns];
            for (int c = 0; c < columns; c++)
                numeric[c] = grid.Skip(1).Any(r => r[c].Length > 0) && grid.Skip(1).All(r => r[c].Length == 0 || Number.IsMatch(r[c]));

            var sb = new StringBuilder();
            void Border(char left, char middle, char right)
            {
                sb.Append(left);
                for (int c = 0; c < columns; c++)
                {
                    sb.Append('─', widths[c] + 2);
                    sb.Append(c == columns - 1 ? right : middle);
                }
                sb.Append('\n');
            }

            Border('┌', '┬', '┐');
            for (int r = 0; r < grid.Count; r++)
            {
                sb.Append('│');
                for (int c = 0; c < columns; c++)
                {
                    string text = grid[r][c];
                    if (text.Length > widths[c]) text = text.Substring(0, widths[c] - 1) + "…";
                    bool right = numeric[c] && !(r == 0 && headerRows.Contains(0));
                    sb.Append(' ').Append(right ? text.PadLeft(widths[c]) : text.PadRight(widths[c])).Append(" │");
                }
                sb.Append('\n');
                if (r < grid.Count - 1 && (headerRows.Contains(r) && !headerRows.Contains(r + 1) || r == 0 && headerRows.Count == 0 && grid.Count > 2 && LooksLikeHeader(grid)))
                    Border('├', '┼', '┤');
            }
            Border('└', '┴', '┘');
            return sb.ToString();
        }

        /// <summary>Without &lt;th&gt; cells, treat the first row as headings when the columns below it hold numbers.</summary>
        private static bool LooksLikeHeader(List<string[]> grid) =>
            Enumerable.Range(0, grid[0].Length).Any(c => grid[0][c].Length > 0 && !Number.IsMatch(grid[0][c]) &&
                grid.Skip(1).Any(r => r[c].Length > 0) && grid.Skip(1).All(r => r[c].Length == 0 || Number.IsMatch(r[c])));

        private static void EndCell(List<Cell> row, string html, int span)
        {
            if (row == null) return;
            string text = InlineText(html).Replace('\n', ' ');
            text = Regex.Replace(text, @"\s+", " ").Trim();
            row.Add(new Cell { Text = text, Span = span });
        }

        /// <summary>HTML outside tables as plain text: tags removed, line breaks kept for paragraphs, lists...</summary>
        private static string InlineText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            string text = Regex.Replace(html, @"\s+", " ");
            text = Regex.Replace(text, @"<li\b[^>]*>", "\n• ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<br\s*/?>|</?(p|div|h[1-6]|tr|ul|ol|blockquote|pre|section|article|header|footer)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"</t[dh]\s*>", "\t", RegexOptions.IgnoreCase);
            text = Tag.Replace(text, "");
            text = Regex.Replace(text, @"<[^>]*>", "");
            text = WebUtility.HtmlDecode(text).Replace(' ', ' ');
            var lines = text.Split('\n').Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim());
            return Regex.Replace(string.Join("\n", lines), @"\n{3,}", "\n\n");
        }
    }
}
