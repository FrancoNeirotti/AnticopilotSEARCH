using System;
using Microsoft.Win32;

namespace CopilotLauncher;

/// <summary>Inicio automático con Windows mediante HKCU\...\Run.</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CopilotLauncher";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --silent");
            else
                key.DeleteValue(ValueName, false);
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
    }
}
