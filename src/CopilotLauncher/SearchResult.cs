using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace CopilotLauncher;

public enum ResultKind
{
    App,
    Folder,
    File
}

public sealed class SearchResult : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private bool _iconRequested;

    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required ResultKind Kind { get; init; }

    /// <summary>Ruta de archivo/carpeta o "shell:AppsFolder\AUMID".</summary>
    public required string Target { get; init; }

    public double Score { get; set; }

    public string KindLabel => Kind switch
    {
        ResultKind.App => "Aplicación",
        ResultKind.Folder => "Carpeta",
        _ => "Archivo"
    };

    public string GroupLabel => Kind == ResultKind.App ? "Aplicaciones" : "Archivos";

    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                IconFactory.Request(Target, img =>
                    dispatcher?.BeginInvoke(() => { _icon = img; OnPropertyChanged(); }));
            }
            return _icon;
        }
    }

    public void Open()
    {
        if (Kind == ResultKind.App && Target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            Process.Start(new ProcessStartInfo("explorer.exe", Target) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo(Target) { UseShellExecute = true });
    }

    public void OpenContainingFolder()
    {
        if (Kind == ResultKind.App && Target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            // Si la app es un ejecutable conocido, abrimos su carpeta; si no, la carpeta de Aplicaciones.
            var exe = Subtitle;
            if (File.Exists(exe))
                Process.Start("explorer.exe", $"/select,\"{exe}\"");
            else
                Process.Start("explorer.exe", "shell:AppsFolder");
            return;
        }
        if (File.Exists(Target) || Directory.Exists(Target))
            Process.Start("explorer.exe", $"/select,\"{Target}\"");
    }

    public void RunAsAdmin()
    {
        var psi = Kind == ResultKind.App && Target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("explorer.exe", Target)
            : new ProcessStartInfo(Target);
        psi.UseShellExecute = true;
        psi.Verb = "runas";
        Process.Start(psi);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
