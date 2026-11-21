using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>
    /// A small picture shown while the mouse is over a link to an image: a pasted picture
    /// (file:///...png), a picture on this PC, or one on the internet (https://...jpg).
    /// </summary>
    internal sealed class ImagePreview : Form
    {
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };
        private static readonly Dictionary<string, Image> Cache = new Dictionary<string, Image>(StringComparer.Ordinal);
        private static readonly HashSet<string> Loading = new HashSet<string>(StringComparer.Ordinal);
        private const int MaxCached = 30;

        private Image _image;
        private string _url;
        private string _message;

        public ImagePreview()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            DoubleBuffered = true;
            Font = SystemFonts.MessageBoxFont;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080;   // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
                cp.ClassStyle |= 0x00020000;              // CS_DROPSHADOW
                return cp;
            }
        }

        /// <summary>True for links that point at a picture file.</summary>
        public static bool IsImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            try
            {
                string path = url.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? new Uri("http://" + url).AbsolutePath : new Uri(url).AbsolutePath;
                return Extensions.Contains(Path.GetExtension(Uri.UnescapeDataString(path)).ToLowerInvariant());
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        public string Url => _url;

        /// <summary>Shows the picture of <paramref name="url"/> with its top-left corner near <paramref name="screenPoint"/>.</summary>
        public void ShowFor(string url, Point screenPoint, IWin32Window owner)
        {
            if (_url == url && Visible) return;
            _url = url;
            _message = null;
            _image = null;
            if (Cache.TryGetValue(url, out var cached)) _image = cached;
            else
            {
                _message = "Loading picture…";
                StartLoading(url);
            }
            Place(screenPoint);
            if (!Visible) Show(owner);
            Invalidate();
        }

        private Point _anchor;

        private void Place(Point screenPoint)
        {
            _anchor = screenPoint;
            Size size = PreviewSize();
            var screen = Screen.FromPoint(screenPoint).WorkingArea;
            int x = Math.Min(screenPoint.X, screen.Right - size.Width - 4);
            int y = screenPoint.Y + size.Height > screen.Bottom ? screenPoint.Y - size.Height - Dpi.Scale(28) : screenPoint.Y;
            Bounds = new Rectangle(Math.Max(screen.Left, x), Math.Max(screen.Top, y), size.Width, size.Height);
        }

        private Size PreviewSize()
        {
            int pad = Dpi.Scale(6);
            if (_image == null) return new Size(Dpi.Scale(220), Dpi.Scale(44));
            int max = Dpi.Scale(420);
            double scale = Math.Min(1.0, Math.Min((double)max / _image.Width, (double)max / _image.Height));
            return new Size((int)(_image.Width * scale) + 2 * pad, (int)(_image.Height * scale) + 2 * pad);
        }

        private void StartLoading(string url)
        {
            if (!Loading.Add(url)) return;
            Task.Run(() => Load(url)).ContinueWith(t =>
            {
                Loading.Remove(url);
                if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                {
                    if (Cache.Count >= MaxCached) Cache.Clear();   // pictures still shown stay alive until replaced
                    Cache[url] = t.Result;
                }
                if (IsDisposed || _url != url) return;
                if (Cache.TryGetValue(url, out var image)) _image = image;
                else _message = "The picture could not be loaded";
                Place(_anchor);
                Invalidate();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static Image Load(string url)
        {
            byte[] bytes;
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                bytes = File.ReadAllBytes(new Uri(url).LocalPath);
            }
            else
            {
                if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) url = "http://" + url;
                using (var web = new WebClient())
                {
                    web.Headers[HttpRequestHeader.UserAgent] = "TabbedNotepad";
                    bytes = web.DownloadData(url);
                }
            }
            // Copy into memory so the file isn't kept locked.
            using (var stream = new MemoryStream(bytes))
            using (var image = Image.FromStream(stream))
                return new Bitmap(image);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            using (var border = new Pen(Color.FromArgb(170, 170, 170)))
                g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            int pad = Dpi.Scale(6);
            if (_image != null)
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(_image, new Rectangle(pad, pad, Width - 2 * pad, Height - 2 * pad));
            }
            else
            {
                TextRenderer.DrawText(g, _message ?? "", Font, ClientRectangle, SystemColors.GrayText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
