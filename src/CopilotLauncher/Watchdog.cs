using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace CopilotLauncher;

/// <summary>
/// Proceso vigilante: lanza el launcher y lo vuelve a abrir si termina de forma inesperada.
/// Una salida desde el menú (código 0) o el cierre de sesión de Windows no lo reabren.
/// </summary>
public static class Watchdog
{
    private const string MutexName = "CopilotLauncher.Watchdog";

    public static void Run(string[] args)
    {
        using var mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew) return;

        var exe = Environment.ProcessPath!;
        var childArgs = string.Join(" ", args.Select(a => $"\"{a}\""));
        var recentCrashes = new Queue<DateTime>();

        while (true)
        {
            Process child;
            try
            {
                child = Process.Start(new ProcessStartInfo(exe, childArgs) { UseShellExecute = false })!;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return;
            }
            child.WaitForExit();
            int code = child.ExitCode;

            if (code == 0)
            {
                Log.Info("Vigilante: el launcher salió normalmente, no se reinicia.");
                return;
            }

            Log.Info($"Vigilante: el launcher terminó inesperadamente (código 0x{code:X8}). Reiniciando…");
            recentCrashes.Enqueue(DateTime.Now);
            while (recentCrashes.Count > 0 && DateTime.Now - recentCrashes.Peek() > TimeSpan.FromMinutes(5))
                recentCrashes.Dequeue();
            if (recentCrashes.Count > 5)
            {
                Log.Info("Vigilante: demasiados cierres seguidos, se deja de reintentar.");
                return;
            }
            Thread.Sleep(2000);
        }
    }
}
