using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TabbedNotepad
{
    /// <summary>
    /// Shows the modern Windows folder picker (the same Explorer-style window as "Save As"),
    /// falling back to the classic folder tree if it is not available.
    /// </summary>
    internal static class FolderPicker
    {
        /// <returns>The chosen folder, or null if cancelled.</returns>
        public static string Pick(IWin32Window owner, string title, string okLabel, string startFolder)
        {
            try
            {
                return PickModern(owner, title, okLabel, startFolder);
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidCastException || ex is TypeLoadException ||
                                       ex is NotSupportedException || ex is EntryPointNotFoundException || ex is DllNotFoundException)
            {
                using (var dialog = new FolderBrowserDialog { Description = title, ShowNewFolderButton = true, SelectedPath = startFolder })
                    return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        private const uint FOS_PICKFOLDERS = 0x20;
        private const uint FOS_FORCEFILESYSTEM = 0x40;
        private const uint FOS_PATHMUSTEXIST = 0x800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

        private static string PickModern(IWin32Window owner, string title, string okLabel, string startFolder)
        {
            var dialog = (IFileDialog)new FileOpenDialogCom();
            try
            {
                dialog.GetOptions(out uint options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                dialog.SetTitle(title);
                dialog.SetOkButtonLabel(okLabel);

                if (!string.IsNullOrEmpty(startFolder) && System.IO.Directory.Exists(startFolder))
                {
                    Guid shellItemId = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(startFolder, IntPtr.Zero, ref shellItemId, out IShellItem folder) == 0)
                        dialog.SetFolder(folder);
                }

                int hr = dialog.Show(owner?.Handle ?? IntPtr.Zero);
                if (hr == ERROR_CANCELLED) return null;
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);

                dialog.GetResult(out IShellItem result);
                result.GetDisplayName(SIGDN_FILESYSPATH, out string path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dialog);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogCom { }

        // Only the methods up to GetResult are declared; their order must match the Windows definition.
        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr filterSpec);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint type, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        }
    }
}
