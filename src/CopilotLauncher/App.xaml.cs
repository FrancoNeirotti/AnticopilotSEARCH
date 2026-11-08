using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;

namespace CopilotLauncher;

public partial class App : System.Windows.Application
{
    private const string MutexName = "CopilotLauncher.SingleInstance";
    private const string ShowEventName = "CopilotLauncher.ShowEvent";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private Forms.NotifyIcon? _tray;
    private KeyboardHook? _hook;
    private HotKey? _fallbackHotKey;
    private SearchWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        int st = Array.FindIndex(e.Args, a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase));
        if (st >= 0)
        {
            RunSelfTest(st + 1 < e.Args.Length ? e.Args[st + 1] : "notepad");
            return;
        }

        if (e.Args.Any(a => a.Equals("--watchdog", StringComparison.OrdinalIgnoreCase)))
        {
            Watchdog.Run(e.Args.Where(a => !a.Equals("--watchdog", StringComparison.OrdinalIgnoreCase)).ToArray());
            Shutdown();
            return;
        }

        InstallCrashHandlers();
        // Render por software: evita que un reinicio del driver de video tire abajo la app.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            // Ya hay una instancia: pedirle que se muestre y salir.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => _window?.ShowLauncher());
        }) { IsBackground = true };
        listener.Start();

        Log.Info("Iniciando CopilotLauncher");

        _window = new SearchWindow();
        _window.Prepare();

        _hook = new KeyboardHook();
        _hook.CopilotKeyPressed += () => Dispatcher.BeginInvoke(() => _window.ToggleLauncher());
        bool hookOk = _hook.Install();
        Log.Info($"Hook de teclado (tecla Copilot): {(hookOk ? "OK" : "FALLÓ")}");

        _fallbackHotKey = new HotKey(_window.Handle, 1, HotKey.MOD_ALT, 0x20 /* Space */);
        _fallbackHotKey.Pressed += () => _window.ToggleLauncher();
        bool hotkeyOk = _fallbackHotKey.Register();
        Log.Info($"Atajo de respaldo Alt+Espacio: {(hotkeyOk ? "OK" : "FALLÓ")}");

        CreateTray(hookOk, hotkeyOk);

        bool silent = e.Args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
        if (!silent)
        {
            _tray!.ShowBalloonTip(3000, "Copilot Launcher",
                "Presioná la tecla Copilot (o Alt+Espacio) para buscar apps y archivos.",
                Forms.ToolTipIcon.Info);
        }

        if (e.Args.Any(a => a.Equals("--show", StringComparison.OrdinalIgnoreCase)))
            _window.ShowLauncher();
    }

    /// <summary>Prueba de diagnóstico: busca el texto y escribe los resultados en el registro.</summary>
    private async void RunSelfTest(string query)
    {
        try
        {
            var apps = new AppIndex();
            await apps.LoadAsync();
            var files = new FileSearch();
            var fileResults = await files.SearchAsync(query, 10, CancellationToken.None);
            var lines = new System.Collections.Generic.List<string>
            {
                $"SELFTEST consulta='{query}' apps={apps.Count} windowsSearch={files.UsingWindowsSearch}"
            };
            lines.AddRange(apps.Search(query, 5).Select(r => $"  [App] {r.Title} -> {r.Target}"));
            lines.AddRange(fileResults.Select(r => $"  [{r.KindLabel}] {r.Title} -> {r.Target}"));
            Log.Info(string.Join(Environment.NewLine, lines));
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        Shutdown();
    }

    private void CreateTray(bool hookOk, bool hotkeyOk)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir buscador", null, (_, _) => _window?.ShowLauncher());
        menu.Items.Add("Actualizar lista de aplicaciones", null, (_, _) => _window?.ReloadApps());

        var startup = new Forms.ToolStripMenuItem("Iniciar con Windows") { Checked = StartupManager.IsEnabled() };
        startup.Click += (_, _) =>
        {
            StartupManager.SetEnabled(!startup.Checked);
            startup.Checked = StartupManager.IsEnabled();
        };
        menu.Items.Add(startup);

        menu.Items.Add(new Forms.ToolStripSeparator());
        var status = new Forms.ToolStripMenuItem(
            $"Tecla Copilot: {(hookOk ? "activa" : "no disponible")} · Alt+Espacio: {(hotkeyOk ? "activo" : "en uso")}")
        { Enabled = false };
        menu.Items.Add(status);
        menu.Items.Add("Abrir registro", null, (_, _) => Log.OpenLog());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon
        {
            Icon = IconFactory.CreateTrayIcon(),
            Text = "Copilot Launcher",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) _window?.ShowLauncher();
        };
    }

    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Info("Excepción no controlada en la interfaz (se continúa):");
            Log.Error(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Info($"Excepción fatal (terminando={args.IsTerminating}):");
            if (args.ExceptionObject is Exception ex) Log.Error(ex);
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception);
            args.SetObserved();
        };
        SessionEnding += (_, args) => Log.Info($"Cierre de sesión de Windows: {args.ReasonSessionEnding}");
        Microsoft.Win32.SystemEvents.SessionSwitch += (_, args) => Log.Info($"Cambio de sesión: {args.Reason}");
    }

    private void ExitApp()
    {
        Log.Info("Salida solicitada desde el menú");
        _hook?.Dispose();
        _fallbackHotKey?.Dispose();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_window != null) Log.Info($"Proceso finalizando (código {e.ApplicationExitCode})");
        _hook?.Dispose();
        _fallbackHotKey?.Dispose();
        if (_tray != null) _tray.Visible = false;
        base.OnExit(e);
    }
}
