using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TabbedNotepad
{
    // The note's background color (easier on the eyes than white, e.g. at night) and the
    // button-style Date/Time and Timestamp items on the menu bar.
    internal sealed partial class MainForm
    {
        private static readonly (string Name, Color Back, Color Fore)[] BackgroundPresets =
        {
            ("White", Color.White, Color.Black),
            ("Warm Paper", Color.FromArgb(0xFA, 0xF6, 0xEC), Color.FromArgb(0x22, 0x22, 0x22)),
            ("Sepia", Color.FromArgb(0xF4, 0xEC, 0xD8), Color.FromArgb(0x4A, 0x3A, 0x2C)),
            ("Soft Gray", Color.FromArgb(0xEC, 0xEE, 0xF0), Color.FromArgb(0x1E, 0x1E, 0x1E)),
            ("Mint", Color.FromArgb(0xE9, 0xF5, 0xEC), Color.FromArgb(0x1C, 0x2B, 0x20)),
            ("Light Blue", Color.FromArgb(0xEA, 0xF1, 0xF8), Color.FromArgb(0x1B, 0x26, 0x33)),
            ("Night (dark gray)", Color.FromArgb(0x1E, 0x1E, 0x1E), Color.FromArgb(0xD8, 0xD8, 0xD8)),
            ("Night Blue", Color.FromArgb(0x1B, 0x24, 0x30), Color.FromArgb(0xD8, 0xDE, 0xE9)),
        };

        private Color _noteBack = Color.White;
        private Color _noteFore = Color.Black;
        private ToolStripMenuItem _backgroundMenu;

        private ToolStripMenuItem BuildBackgroundMenu()
        {
            _backgroundMenu = new ToolStripMenuItem("&Background Color");
            foreach (var preset in BackgroundPresets)
            {
                var p = preset;
                var item = new ToolStripMenuItem(p.Name, Swatch(p.Back, p.Fore), (s, e) => SetNoteColors(p.Back, p.Fore));
                item.Tag = p.Back;
                _backgroundMenu.DropDownItems.Add(item);
            }
            _backgroundMenu.DropDownItems.Add(new ToolStripSeparator());
            _backgroundMenu.DropDownItems.Add(new ToolStripMenuItem("Custom Background Color...", null, (s, e) => PickNoteColor(background: true)));
            _backgroundMenu.DropDownItems.Add(new ToolStripMenuItem("Custom Text Color...", null, (s, e) => PickNoteColor(background: false)));
            _backgroundMenu.DropDownOpening += (s, e) =>
            {
                foreach (ToolStripItem item in _backgroundMenu.DropDownItems)
                    if (item is ToolStripMenuItem m && m.Tag is Color c) m.Checked = c.ToArgb() == _noteBack.ToArgb();
            };
            return _backgroundMenu;
        }

        private static Bitmap Swatch(Color back, Color fore)
        {
            int size = Dpi.Scale(16);
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            using (var fill = new SolidBrush(back))
            using (var border = new Pen(Color.FromArgb(150, 150, 150)))
            using (var font = new Font("Segoe UI", size * 0.45f, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.FillRectangle(fill, 0, 0, size - 1, size - 1);
                g.DrawRectangle(border, 0, 0, size - 1, size - 1);
                TextRenderer.DrawText(g, "A", font, new Rectangle(0, 0, size, size), fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            return bmp;
        }

        private void PickNoteColor(bool background)
        {
            using (var dialog = new ColorDialog { Color = background ? _noteBack : _noteFore, FullOpen = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                if (background)
                {
                    // Keep the text readable: switch to light text on a dark background and back.
                    Color fore = _noteFore;
                    bool darkBack = Brightness(dialog.Color) < 0.5;
                    if (darkBack && Brightness(fore) < 0.5) fore = Color.FromArgb(0xD8, 0xD8, 0xD8);
                    if (!darkBack && Brightness(fore) >= 0.5) fore = Color.Black;
                    SetNoteColors(dialog.Color, fore);
                }
                else
                {
                    SetNoteColors(_noteBack, dialog.Color);
                }
            }
        }

        private static double Brightness(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        private void SetNoteColors(Color back, Color fore)
        {
            _noteBack = back;
            _noteFore = fore;
            foreach (var tab in AllTabs) tab.SetColors(back, fore);
            _indexDirty = true;
            ScheduleSave();
        }

        private static Color ParseColor(string text, Color fallback)
        {
            try
            {
                return string.IsNullOrEmpty(text) ? fallback : ColorTranslator.FromHtml(text);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        // ---------------------------------------------------------------- buttons on the menu bar

        /// <summary>A menu bar item drawn as a button (with a frame), so it stands out from the menus.</summary>
        private sealed class MenuButton : ToolStripMenuItem
        {
            public MenuButton(string text, Image image) : base(text, image)
            {
                ImageScaling = ToolStripItemImageScaling.None;
                Margin = new Padding(Dpi.Scale(4), Dpi.Scale(1), 0, Dpi.Scale(1));
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = RoundedBox(r, Dpi.Scale(4)))
                using (var fill = new SolidBrush(Selected || Pressed ? Color.FromArgb(214, 230, 250) : Color.FromArgb(248, 250, 253)))
                using (var border = new Pen(Selected || Pressed ? Color.FromArgb(80, 130, 210) : Color.FromArgb(160, 170, 185)))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }
                g.SmoothingMode = SmoothingMode.Default;
                int x = Dpi.Scale(6);
                if (Image != null)
                {
                    g.DrawImage(Image, x, (Height - Image.Height) / 2, Image.Width, Image.Height);
                    x += Image.Width + Dpi.Scale(4);
                }
                TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Width - x - Dpi.Scale(4), Height), SystemColors.ControlText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }

            public override Size GetPreferredSize(Size constrainingSize)
            {
                Size text = TextRenderer.MeasureText(Text, Font);
                int width = Dpi.Scale(6) + (Image != null ? Image.Width + Dpi.Scale(4) : 0) + text.Width + Dpi.Scale(6);
                return new Size(width, Math.Max(base.GetPreferredSize(constrainingSize).Height, text.Height + Dpi.Scale(6)));
            }
        }
    }
}
