using System.Runtime.InteropServices;

namespace WClean;

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    internal const uint SHERB_NOCONFIRMATION = 0x1;
    internal const uint SHERB_NOPROGRESSUI = 0x2;
    internal const uint SHERB_NOSOUND = 0x4;
}
