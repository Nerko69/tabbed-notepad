using System;
using System.Drawing;

namespace TabbedNotepad
{
    internal static class Dpi
    {
        private static readonly float Factor = GetFactor();

        /// <summary>Scales a size designed for 100% (96 DPI) to the screen's DPI setting.</summary>
        public static int Scale(int pixels) => (int)Math.Round(pixels * Factor);

        private static float GetFactor()
        {
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    return g.DpiX / 96f;
            }
            catch
            {
                return 1f;
            }
        }
    }
}
