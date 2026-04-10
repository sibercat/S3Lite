using System.Runtime.InteropServices;

namespace S3Lite.Helpers;

/// <summary>
/// Reliable folder picker using IFileOpenDialog COM interface directly.
/// Avoids FolderBrowserDialog which deadlocks on Windows 10/11.
/// </summary>
public static class FolderPicker
{
    public static string? Pick(IntPtr ownerHandle, string? initialPath = null)
    {
        IFileOpenDialog? dialog = null;
        try
        {
            dialog = (IFileOpenDialog)new FileOpenDialogRCW();
            dialog.SetOptions(FOS.FOS_PICKFOLDERS | FOS.FOS_FORCEFILESYSTEM | FOS.FOS_NOVALIDATE);

            if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            {
                SHCreateItemFromParsingName(initialPath, IntPtr.Zero,
                    ref IID_IShellItem, out IShellItem? folder);
                if (folder != null)
                {
                    dialog.SetFolder(folder);
                    Marshal.ReleaseComObject(folder);
                }
            }

            int hr = dialog.Show(ownerHandle);
            if (hr != 0) return null; // user cancelled (HRESULT_FROM_WIN32 ERROR_CANCELLED)

            dialog.GetResult(out IShellItem? result);
            if (result == null) return null;

            result.GetDisplayName(SIGDN.SIGDN_FILESYSPATH, out string? path);
            Marshal.ReleaseComObject(result);
            return path;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (dialog != null) Marshal.ReleaseComObject(dialog);
        }
    }

    // ── COM interfaces ────────────────────────────────────────────────────────

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHCreateItemFromParsingName(
        string pszPath, IntPtr pbc, ref Guid riid, out IShellItem? ppv);

    private static Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    private class FileOpenDialogRCW { }

    [ComImport, Guid("D57C7288-D4AD-4768-BE02-9D969532D960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOpenDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
        void SetFileTypeIndex(uint iFileType);
        void GetFileTypeIndex(out uint piFileType);
        void Advise(IntPtr pfde, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOptions(FOS fos);
        void GetOptions(out FOS pfos);
        void SetDefaultFolder(IShellItem psi);
        void SetFolder(IShellItem psi);
        void GetFolder(out IShellItem? ppsi);
        void GetCurrentSelection(out IShellItem? ppsi);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        void GetResult(out IShellItem? ppsi);
        void AddPlace(IShellItem psi, int alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
        void Close(int hr);
        void SetClientGuid(ref Guid guid);
        void ClearClientData();
        void SetFilter(IntPtr pFilter);
        void GetResults(out IntPtr ppenum);
        void GetSelectedItems(out IntPtr ppsai);
    }

    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem? ppsi);
        void GetDisplayName(SIGDN sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string? pszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [Flags]
    private enum FOS : uint
    {
        FOS_OVERWRITEPROMPT   = 0x00000002,
        FOS_STRICTFILETYPES   = 0x00000004,
        FOS_NOCHANGEDIR       = 0x00000008,
        FOS_PICKFOLDERS       = 0x00000020,
        FOS_FORCEFILESYSTEM   = 0x00000040,
        FOS_NOVALIDATE        = 0x00000100,
        FOS_ALLOWMULTISELECT  = 0x00000200,
        FOS_PATHMUSTEXIST     = 0x00000800,
        FOS_FILEMUSTEXIST     = 0x00001000,
    }

    private enum SIGDN : uint
    {
        SIGDN_FILESYSPATH = 0x80058000,
    }
}
