using System;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Microsoft.Win32;

namespace CopilotLauncher;

/// <summary>Lee el color de énfasis de Windows (Configuración › Personalización › Colores).</summary>
public static class AccentColors
{
    private static readonly Color Fallback = Color.FromRgb(0x2F, 0x81, 0xF7);

    /// <summary>
    /// Devuelve el tono para superficies oscuras: Windows usa las variantes claras
    /// de la paleta (AccentLight1) sobre fondos oscuros para mantener el contraste.
    /// </summary>
    public static Color Get()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentPalette") is byte[] p && p.Length >= 32)
                return Color.FromRgb(p[8], p[9], p[10]);

            using var dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (dwm?.GetValue("AccentColor") is int abgr)
                return Color.FromRgb((byte)abgr, (byte)(abgr >> 8), (byte)(abgr >> 16));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        return Fallback;
    }
}
