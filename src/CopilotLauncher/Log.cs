using System;
using System.Diagnostics;
using System.IO;

namespace CopilotLauncher;

public static class Log
{
    private static readonly object Gate = new();

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopilotLauncher");

    public static string FilePath => Path.Combine(Directory, "launcher.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(Exception ex) => Write("ERROR", ex.ToString());

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000) info.Delete();
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void OpenLog()
    {
        try { Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true }); } catch { }
    }
}
