using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TabbedNotepad
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        private const int SW_RESTORE = 9;

        [STAThread]
        private static void Main()
        {
            // Only one window may edit the notes at a time, otherwise two copies would overwrite each other.
            using (var mutex = new Mutex(true, @"Local\TabbedNotepad.SingleInstance", out bool isFirst))
            {
                if (!isFirst)
                {
                    BringExistingWindowToFront();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // Show unexpected errors instead of closing the app; notes are auto-saved anyway.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                    MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "Tabbed Notepad", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Run(new MainForm(new NoteStore(NoteStore.DefaultFolder)));
                GC.KeepAlive(mutex);
            }
        }

        private static void BringExistingWindowToFront()
        {
            var current = Process.GetCurrentProcess();
            foreach (var process in Process.GetProcessesByName(current.ProcessName))
            {
                if (process.Id == current.Id || process.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(process.MainWindowHandle)) ShowWindow(process.MainWindowHandle, SW_RESTORE);
                SetForegroundWindow(process.MainWindowHandle);
                return;
            }
        }
    }
}
