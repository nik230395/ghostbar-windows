using System.Drawing.Imaging;

namespace Ghostbar.Services;

static class ScreenCapture
{
    public static string? CaptureBase64(int maxWidth = 1440)
    {
        var bounds = SystemInformation.VirtualScreen;
        using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

        var scale = full.Width > maxWidth ? (double)maxWidth / full.Width : 1.0;
        var w = Math.Max(1, (int)(full.Width * scale));
        var h = Math.Max(1, (int)(full.Height * scale));
        using var scaled = new Bitmap(full, w, h);
        using var ms = new MemoryStream();
        scaled.Save(ms, ImageFormat.Png);
        return Convert.ToBase64String(ms.ToArray());
    }
}
