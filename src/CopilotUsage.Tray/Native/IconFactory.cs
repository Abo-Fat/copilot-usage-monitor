using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using CopilotUsage.Core;
using Microsoft.Win32;

namespace CopilotUsage.Tray.Native;

public readonly record struct IconPalette(Color Foreground, Color Background)
{
    public static IconPalette Read()
    {
        if (SystemInformation.HighContrast)
            return new(SystemColors.WindowText, SystemColors.Window);
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0
            ? new(Color.FromArgb(45, 45, 45), Color.FromArgb(245, 245, 245))
            : new(Color.FromArgb(245, 245, 245), Color.FromArgb(32, 32, 32));
    }
}

public static class IconFactory
{
    private static readonly int[] SourceSizes = [16, 20, 24, 32, 40, 48, 64];

    public static int TrayPixelSize()
    {
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        var dpi = taskbar == IntPtr.Zero ? GetDpiForSystem() : GetDpiForWindow(taskbar);
        if (dpi == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        var size = GetSystemMetricsForDpi(49, dpi); // SM_CXSMICON follows the taskbar's monitor, not the details window.
        if (size <= 0) throw new InvalidOperationException("Windows did not return a valid tray icon size.");
        return size;
    }

    public static Icon Create(BatteryIndicator indicator, int size, IconPalette palette)
    {
        using var bitmap = Render(indicator, size, palette);
        var handle = bitmap.GetHicon();
        if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        Icon? result = null;
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            result = (Icon)borrowed.Clone();
            return result;
        }
        finally
        {
            if (!DestroyIcon(handle))
            {
                var error = Marshal.GetLastWin32Error();
                result?.Dispose();
                throw new Win32Exception(error);
            }
        }
    }

    internal static Bitmap Render(BatteryIndicator indicator, int size, IconPalette palette)
    {
        if (indicator.Bars is < 0 or > 4 || !Enum.IsDefined(indicator.Badge))
            throw new ArgumentOutOfRangeException(nameof(indicator));
        if (size is < 16 or > 256) throw new ArgumentOutOfRangeException(nameof(size));
        var sourceSize = SourceSizes.FirstOrDefault(s => s >= size, SourceSizes[^1]);
        var name = $"CopilotUsage.Battery.battery-{indicator.Bars ?? 0}-{sourceSize}.png";
        using var stream = typeof(IconFactory).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Missing embedded tray icon: {name}");
        using var source = new Bitmap(stream);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var foreground = palette.Foreground;
            using var attributes = new ImageAttributes();
            attributes.SetColorMatrix(new ColorMatrix
            {
                Matrix00 = foreground.R / 255f,
                Matrix11 = foreground.G / 255f,
                Matrix22 = foreground.B / 255f
            });
            if (size == sourceSize)
                graphics.DrawImage(source, new Rectangle(0, 0, size, size), 0, 0, size, size, GraphicsUnit.Pixel, attributes);
            else
                graphics.DrawImage(source, new Rectangle(0, 0, size, size), 0, 0, sourceSize, sourceSize, GraphicsUnit.Pixel, attributes);
            DrawBadge(graphics, indicator.Badge, size, palette);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void DrawBadge(Graphics graphics, BatteryBadge badge, int size, IconPalette palette)
    {
        if (badge == BatteryBadge.None) return;
        var diameter = MathF.Round(size * .5f);
        var bounds = new RectangleF(size - diameter, 0, diameter, diameter);
        var fillColor = badge switch
        {
            BatteryBadge.Warning => Color.FromArgb(255, 174, 0),
            BatteryBadge.Syncing => Color.FromArgb(38, 132, 255),
            _ => palette.Foreground
        };
        using var halo = new SolidBrush(palette.Background);
        graphics.FillEllipse(halo, bounds);
        var inset = Math.Max(.5f, size / 32f);
        bounds.Inflate(-inset, -inset);
        using var fill = new SolidBrush(fillColor);
        graphics.FillEllipse(fill, bounds);
        if (badge == BatteryBadge.Syncing) return;
        using var text = new SolidBrush(badge == BatteryBadge.Warning ? Color.Black : palette.Background);
        using var font = new Font("Segoe UI", diameter * .82f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.DrawString(badge == BatteryBadge.Warning ? "!" : "?", font, text,
            new RectangleF(size - diameter, -diameter * .04f, diameter, diameter), format);
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern IntPtr FindWindowW(string className, string? windowName);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
