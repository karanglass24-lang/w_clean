param(
    [Parameter(Mandatory)][string]$Source,                 # PNG of the artwork (white background)
    [string]$OutDir = ""
)
# Makes assets\logo.png (transparent, square) and assets\WClean.ico (16..256) from the artwork.
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System; using System.Collections.Generic; using System.Drawing; using System.Drawing.Imaging; using System.IO;
public static class IconMaker {
  // Removes the near-white background that is connected to the image border.
  public static Bitmap CutBackground(Bitmap src) {
    int w = src.Width, h = src.Height;
    var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, w, h);
    var rect = new Rectangle(0, 0, w, h);
    var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var px = new byte[data.Stride * h];
    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
    int stride = data.Stride;
    var seen = new bool[w * h];
    var q = new Queue<int>();
    Func<int,int,bool> isBg = (x, y) => { int i = y * stride + x * 4; return px[i] > 232 && px[i+1] > 232 && px[i+2] > 232; };
    for (int x = 0; x < w; x++) { q.Enqueue(x); q.Enqueue((h-1)*w + x); }
    for (int y = 0; y < h; y++) { q.Enqueue(y*w); q.Enqueue(y*w + w-1); }
    while (q.Count > 0) {
      int p = q.Dequeue(); if (seen[p]) continue;
      int x = p % w, y = p / w;
      if (!isBg(x, y)) continue;
      seen[p] = true;
      px[y*stride + x*4 + 3] = 0;
      if (x > 0) q.Enqueue(p-1); if (x < w-1) q.Enqueue(p+1);
      if (y > 0) q.Enqueue(p-w); if (y < h-1) q.Enqueue(p+w);
    }
    // soften the 1px edge: opaque pixels touching removed background get half alpha
    var copy = (byte[])px.Clone();
    for (int y = 1; y < h-1; y++) for (int x = 1; x < w-1; x++) {
      int i = y*stride + x*4;
      if (copy[i+3] == 0) continue;
      if (copy[i-4+3]==0 || copy[i+4+3]==0 || copy[i-stride+3]==0 || copy[i+stride+3]==0) px[i+3] = 150;
    }
    System.Runtime.InteropServices.Marshal.Copy(px, 0, data.Scan0, px.Length);
    bmp.UnlockBits(data);
    return bmp;
  }
  public static Rectangle Bounds(Bitmap bmp) {
    int minx = bmp.Width, miny = bmp.Height, maxx = -1, maxy = -1;
    var data = bmp.LockBits(new Rectangle(0,0,bmp.Width,bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    var px = new byte[data.Stride * bmp.Height];
    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
    for (int y = 0; y < bmp.Height; y++) for (int x = 0; x < bmp.Width; x++)
      if (px[y*data.Stride + x*4 + 3] > 40) { if (x<minx) minx=x; if (x>maxx) maxx=x; if (y<miny) miny=y; if (y>maxy) maxy=y; }
    bmp.UnlockBits(data);
    return new Rectangle(minx, miny, maxx-minx+1, maxy-miny+1);
  }
  public static Bitmap SquareCrop(Bitmap bmp, Rectangle r, double pad) {
    int side = (int)(Math.Max(r.Width, r.Height) * (1 + pad));
    var o = new Bitmap(side, side, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(o)) {
      g.Clear(Color.Transparent);
      g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
      g.DrawImage(bmp, new Rectangle((side-r.Width)/2, (side-r.Height)/2, r.Width, r.Height), r, GraphicsUnit.Pixel);
    }
    return o;
  }
  public static Bitmap Resize(Bitmap bmp, int s) {
    var o = new Bitmap(s, s, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(o)) {
      g.Clear(Color.Transparent);
      g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
      g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
      g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
      g.DrawImage(bmp, 0, 0, s, s);
    }
    return o;
  }
  public static void WriteIco(Bitmap master, int[] sizes, string path) {
    var pngs = new List<byte[]>();
    foreach (var s in sizes) using (var b = Resize(master, s)) using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
    using (var fs = File.Create(path)) using (var bw = new BinaryWriter(fs)) {
      bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
      int offset = 6 + 16 * sizes.Length;
      for (int i = 0; i < sizes.Length; i++) {
        bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        bw.Write((byte)0); bw.Write((byte)0); bw.Write((short)1); bw.Write((short)32);
        bw.Write(pngs[i].Length); bw.Write(offset); offset += pngs[i].Length;
      }
      foreach (var p in pngs) bw.Write(p);
    }
  }
}
"@ -ReferencedAssemblies System.Drawing

if (-not $OutDir) { $OutDir = Join-Path (Split-Path $MyInvocation.MyCommand.Path -Parent) "..\assets" }
New-Item -ItemType Directory -Force $OutDir | Out-Null
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source))
$cut = [IconMaker]::CutBackground($src)
$sq = [IconMaker]::SquareCrop($cut, [IconMaker]::Bounds($cut), 0.04)
$logo = [IconMaker]::Resize($sq, 512)
$logo.Save((Join-Path $OutDir "logo.png"), [System.Drawing.Imaging.ImageFormat]::Png)
[IconMaker]::WriteIco($sq, @(16, 24, 32, 48, 64, 128, 256), (Join-Path $OutDir "WClean.ico"))
$src.Dispose()
"wrote $OutDir\logo.png and WClean.ico"
