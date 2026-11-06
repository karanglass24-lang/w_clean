param(
    [string]$Out,
    [string[]]$AppArgs = @(),
    [int]$Wait = 6
)
# Launches the Debug build, captures its window to a PNG, closes it. For UI checks only.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public class WShot {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint f);
  public struct RECT { public int L, T, R, B; }
}
"@
$exe = Join-Path $PSScriptRoot "..\src\WClean.App\bin\Debug\net9.0-windows\WClean.exe"
$p = Start-Process $exe -ArgumentList $AppArgs -PassThru
Start-Sleep -Seconds $Wait
$p.Refresh()
$r = New-Object WShot+RECT
[WShot]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.R - $r.L; $h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc(); [WShot]::PrintWindow($p.MainWindowHandle, $dc, 2) | Out-Null; $g.ReleaseHdc($dc)
$g.Dispose(); $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
Stop-Process $p -Force
"$w x $h -> $Out"
