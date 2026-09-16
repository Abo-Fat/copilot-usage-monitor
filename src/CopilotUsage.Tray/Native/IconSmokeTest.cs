using System.Drawing.Imaging;
using CopilotUsage.Core;

namespace CopilotUsage.Tray.Native;

internal static class IconSmokeTest
{
    public static int Run(string? previewPath)
    {
        try
        {
            int[] sizes = [16, 20, 24, 32, 40, 48, 64];
            IconPalette[] palettes =
            [
                new(Color.FromArgb(45, 45, 45), Color.FromArgb(245, 245, 245)),
                new(Color.FromArgb(245, 245, 245), Color.FromArgb(32, 32, 32)),
                new(Color.Yellow, Color.Black)
            ];
            foreach (var palette in palettes)
            foreach (var size in sizes)
            {
                long previousAlpha = 0;
                for (var bars = 0; bars <= 4; bars++)
                {
                    var indicator = new BatteryIndicator(bars, BatteryBadge.None);
                    using var bitmap = IconFactory.Render(indicator, size, palette);
                    Check(bitmap.Size == new Size(size, size), "Wrong raster size.");
                    Check(bitmap.GetPixel(0, 0).A == 0, "Transparent background was lost.");
                    long alpha = 0;
                    for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++) alpha += bitmap.GetPixel(x, y).A;
                    Check(alpha > previousAlpha, $"Battery {bars} bars at {size}px is missing a bar.");
                    previousAlpha = alpha;
                    var pixel = bitmap.GetPixel(size / 4, (int)MathF.Round(size * 14f / 48));
                    Check(pixel.A > 0 && Math.Abs(pixel.R - palette.Foreground.R) <= 3 &&
                        Math.Abs(pixel.G - palette.Foreground.G) <= 3 &&
                        Math.Abs(pixel.B - palette.Foreground.B) <= 3,
                        $"Theme tint was lost at {size}px: {pixel}, expected {palette.Foreground}.");
                    foreach (var badge in Enum.GetValues<BatteryBadge>())
                    {
                        using var icon = IconFactory.Create(indicator with { Badge = badge }, size, palette);
                        Check(icon.Size == new Size(size, size), "HICON dimensions are incorrect.");
                        using var raster = icon.ToBitmap();
                        Check(raster.GetPixel(0, 0).A == 0, "HICON lost transparency.");
                        if (badge != BatteryBadge.None)
                        {
                            var corner = raster.GetPixel(size * 3 / 4, size / 4);
                            Check(corner.A > 0, "Status badge is missing.");
                        }
                    }
                }
            }
            using (var unknown = IconFactory.Create(new(null, BatteryBadge.Unknown), 16, palettes[0]))
                Check(unknown.Size.Width == 16, "Unknown quota icon failed.");
            if (previewPath is not null) SavePreview(Path.GetFullPath(previewPath), palettes);
            Console.WriteLine("Icon regression: 420 themed HICON variants and 105 battery rasters passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Icon regression failed: {ex.Message}");
            return 1;
        }
    }

    private static void SavePreview(string path, IconPalette[] palettes)
    {
        using var image = new Bitmap(760, 330);
        using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.White);
        using var font = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        for (var theme = 0; theme < 2; theme++)
        {
            var palette = palettes[theme];
            using var background = new SolidBrush(palette.Background);
            using var text = new SolidBrush(palette.Foreground);
            graphics.FillRectangle(background, 0, theme * 165, 760, 165);
            graphics.DrawString(theme == 0 ? "Light taskbar" : "Dark taskbar", font, text, 12, theme * 165 + 8);
            BatteryIndicator[] variants =
            [
                new(4, BatteryBadge.None), new(3, BatteryBadge.None), new(2, BatteryBadge.None),
                new(1, BatteryBadge.None), new(0, BatteryBadge.None), new(null, BatteryBadge.Unknown),
                new(3, BatteryBadge.Syncing), new(2, BatteryBadge.Warning)
            ];
            string[] labels = ["4 bars", "3 bars", "2 bars", "1 bar", "Empty", "Unknown", "Syncing", "Stale/error"];
            for (var column = 0; column < variants.Length; column++)
            {
                var x = 15 + column * 92;
                using var small = IconFactory.Render(variants[column], 16, palette);
                using var scaled = IconFactory.Render(variants[column], 32, palette);
                using var large = IconFactory.Render(variants[column], 48, palette);
                graphics.DrawImageUnscaled(small, x, theme * 165 + 33);
                graphics.DrawImageUnscaled(scaled, x + 28, theme * 165 + 29);
                graphics.DrawImageUnscaled(large, x + 4, theme * 165 + 66);
                graphics.DrawString(labels[column], font, text, x, theme * 165 + 125);
            }
        }
        image.Save(path, ImageFormat.Png);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
