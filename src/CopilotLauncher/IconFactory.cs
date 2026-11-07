using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CopilotLauncher;

public static class IconFactory
{
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const int SIIGBF_ICONONLY = 0x04;
    private const int SIIGBF_BIGGERSIZEOK = 0x01;

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<(string key, Action<ImageSource?> done)> Queue = new();

    static IconFactory()
    {
        var worker = new Thread(() =>
        {
            foreach (var (key, done) in Queue.GetConsumingEnumerable())
            {
                var img = Cache.GetOrAdd(key, Load);
                try { done(img); } catch { }
            }
        }) { IsBackground = true, Name = "IconLoader" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    /// <summary>Carga asíncrona del ícono del shell para una ruta o "shell:AppsFolder\AUMID".</summary>
    public static void Request(string parsingName, Action<ImageSource?> done)
    {
        if (Cache.TryGetValue(parsingName, out var cached))
        {
            done(cached);
            return;
        }
        Queue.Add((parsingName, done));
    }

    private static ImageSource? Load(string parsingName)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            var size = new SIZE { cx = 64, cy = 64 };
            if (factory.GetImage(size, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hbm) != 0 || hbm == IntPtr.Zero)
                return null;
            var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            Marshal.ReleaseComObject(factory);
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
        }
    }

    /// <summary>Ícono de bandeja dibujado en tiempo de ejecución (lupa).</summary>
    public static Icon CreateTrayIcon()
    {
        using var bmp = new System.Drawing.Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(System.Drawing.Color.Transparent);
            using var bg = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 32, 32),
                System.Drawing.Color.FromArgb(255, 72, 120, 255), System.Drawing.Color.FromArgb(255, 160, 80, 255), 45f);
            g.FillEllipse(bg, 0, 0, 31, 31);
            using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawEllipse(pen, 7, 7, 13, 13);
            g.DrawLine(pen, 18, 18, 24, 24);
        }
        IntPtr h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }
}
