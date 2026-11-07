using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace CopilotLauncher;

public partial class SearchWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>Windows restringe SetForegroundWindow; nos enganchamos al hilo de la ventana activa para poder tomar el foco.</summary>
    private void ForceForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == Handle) return;
        uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero);
        uint myThread = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != myThread && AttachThreadInput(myThread, fgThread, true);
        try
        {
            // Pulsación de una tecla virtual sin asignar: habilita el cambio de foco
            // (Alt activaría el modo menú de la ventana y "comería" el Esc siguiente).
            keybd_event(0xE8, 0, 0, UIntPtr.Zero);
            keybd_event(0xE8, 0, 2, UIntPtr.Zero);
            BringWindowToTop(Handle);
            SetForegroundWindow(Handle);
        }
        finally
        {
            if (attached) AttachThreadInput(myThread, fgThread, false);
        }
    }

    private readonly AppIndex _apps = new();
    private readonly FileSearch _files = new();
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _cts;
    private List<SearchResult> _appResults = new();
    private DateTime _lastToggle = DateTime.MinValue;

    public IntPtr Handle { get; private set; }

    public SearchWindow()
    {
        InitializeComponent();
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await SearchFilesAsync();
        };
        Deactivated += (_, _) => HideLauncher();
    }

    public void Prepare()
    {
        Handle = new WindowInteropHelper(this).EnsureHandle();
        // Pre-render fuera de pantalla para que la primera apertura sea instantánea.
        Left = -20000; Top = -20000; ShowActivated = false;
        Show();
        Hide();
        ShowActivated = true;
        _ = _apps.LoadAsync();
    }

    public void ReloadApps() => _ = _apps.LoadAsync();

    public void ToggleLauncher()
    {
        // Evita parpadeo por repetición del teclado.
        if ((DateTime.Now - _lastToggle).TotalMilliseconds < 200) return;
        _lastToggle = DateTime.Now;
        if (IsVisible) HideLauncher();
        else ShowLauncher();
    }

    public void ShowLauncher()
    {
        PositionOnActiveScreen();
        Show();
        ForceForeground();
        Activate();
        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        QueryBox.SelectAll();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            Activate();
            Keyboard.Focus(QueryBox);
        });
    }

    public void HideLauncher()
    {
        if (!IsVisible) return;
        _cts?.Cancel();
        Hide();
        QueryBox.Text = string.Empty;
    }

    private void PositionOnActiveScreen()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var source = PresentationSource.FromVisual(this);
        double scaleX = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
        double scaleY = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;
        var area = screen.WorkingArea;
        Left = area.Left * scaleX + (area.Width * scaleX - Width) / 2;
        Top = area.Top * scaleY + area.Height * scaleY * 0.22;
    }

    private void QueryBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var text = QueryBox.Text;
        Placeholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _cts?.Cancel();
        _debounce.Stop();

        if (string.IsNullOrWhiteSpace(text))
        {
            _appResults = new();
            ShowResults(new List<SearchResult>());
            return;
        }

        _appResults = _apps.Search(text, 8);
        ShowResults(_appResults);
        _debounce.Start();
    }

    private async Task SearchFilesAsync()
    {
        var text = QueryBox.Text;
        if (text.Trim().Length < 2) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        try
        {
            var files = await _files.SearchAsync(text, 15, ct);
            if (ct.IsCancellationRequested || text != QueryBox.Text) return;
            ShowResults(_appResults.Concat(files.OrderByDescending(f => f.Score)).ToList(), keepSelection: true);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error(ex); }
    }

    private void ShowResults(List<SearchResult> results, bool keepSelection = false)
    {
        int prev = ResultsList.SelectedIndex;
        ResultsList.ItemsSource = results;
        ResultsPanel.Visibility = QueryBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (results.Count > 0)
            ResultsList.SelectedIndex = keepSelection && prev >= 0 && prev < results.Count ? prev : 0;
        StatusText.Text = results.Count == 0 && QueryBox.Text.Length > 0
            ? "Sin resultados"
            : "Enter: abrir · Ctrl+Enter: abrir ubicación · Ctrl+Shift+Enter: como administrador · Esc: cerrar";
    }

    private void QueryBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        int count = ResultsList.Items.Count;
        switch (e.Key)
        {
            case Key.Escape:
                HideLauncher();
                e.Handled = true;
                break;
            case Key.Down when count > 0:
                ResultsList.SelectedIndex = (ResultsList.SelectedIndex + 1) % count;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when count > 0:
                ResultsList.SelectedIndex = (ResultsList.SelectedIndex - 1 + count) % count;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter:
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                Execute(ctrl && shift ? Action.Admin : ctrl ? Action.Folder : Action.Open);
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Execute(Action.Open);

    private enum Action { Open, Folder, Admin }

    private void Execute(Action action)
    {
        if (ResultsList.SelectedItem is not SearchResult result) return;
        try
        {
            switch (action)
            {
                case Action.Open: result.Open(); break;
                case Action.Folder: result.OpenContainingFolder(); break;
                case Action.Admin: result.RunAsAdmin(); break;
            }
            HideLauncher();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // El usuario canceló el UAC.
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            StatusText.Text = "No se pudo abrir: " + ex.Message;
        }
    }
}
