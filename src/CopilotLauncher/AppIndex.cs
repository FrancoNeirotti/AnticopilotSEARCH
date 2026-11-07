using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotLauncher;

/// <summary>Índice de aplicaciones instaladas: carpeta virtual shell:AppsFolder (incluye apps de la Store) + accesos directos del menú Inicio.</summary>
public sealed class AppIndex
{
    private sealed record AppEntry(string Name, string Target, string Subtitle);

    private volatile List<AppEntry> _apps = new();

    public int Count => _apps.Count;

    public Task LoadAsync()
    {
        var tcs = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            try
            {
                var list = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in LoadAppsFolder()) list.TryAdd(e.Name, e);
                foreach (var e in LoadStartMenu()) list.TryAdd(e.Name, e);
                _apps = list.Values.ToList();
                Log.Info($"Aplicaciones indexadas: {_apps.Count}");
            }
            catch (Exception ex)
            {
                Log.Error(ex);
            }
            tcs.SetResult();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static IEnumerable<AppEntry> LoadAppsFolder()
    {
        var result = new List<AppEntry>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return result;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            foreach (dynamic item in folder.Items())
            {
                string name = item.Name;
                string path = item.Path; // AUMID o ruta/GUID conocido
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                if (IsNoise(name)) continue;
                string subtitle = path.Contains('!') ? "Aplicación de Microsoft Store" : ResolveKnownFolders(path);
                if (!System.IO.Path.IsPathRooted(subtitle)) subtitle = "Aplicación";
                result.Add(new AppEntry(name, "shell:AppsFolder\\" + path, subtitle));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        return result;
    }

    private static IEnumerable<AppEntry> LoadStartMenu()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                    .Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            catch { continue; }

            foreach (var f in files)
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (IsNoise(name)) continue;
                yield return new AppEntry(name, f, f);
            }
        }
    }

    private static bool IsNoise(string name)
    {
        var n = name.ToLowerInvariant();
        return n.StartsWith("uninstall") || n.StartsWith("desinstalar") || n == "desktop.ini";
    }

    private static string ResolveKnownFolders(string path)
    {
        // Rutas del tipo {6D809377-...}\app.exe -> ruta real.
        var map = new Dictionary<string, Environment.SpecialFolder>(StringComparer.OrdinalIgnoreCase)
        {
            ["{6D809377-6AF0-444B-8957-A3773F02200E}"] = Environment.SpecialFolder.ProgramFiles,
            ["{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}"] = Environment.SpecialFolder.ProgramFilesX86,
            ["{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}"] = Environment.SpecialFolder.System,
            ["{F38BF404-1D43-42F2-9305-67DE0B28FC23}"] = Environment.SpecialFolder.Windows,
        };
        foreach (var kv in map)
            if (path.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
                return Environment.GetFolderPath(kv.Value) + path.Substring(kv.Key.Length);
        return path;
    }

    public List<SearchResult> Search(string query, int max)
    {
        var q = Matcher.Normalize(query.Trim());
        if (q.Length == 0) return new();
        return _apps
            .Select(a => (a, score: Matcher.Score(q, a.Name)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(max)
            .Select(x => new SearchResult
            {
                Title = x.a.Name,
                Subtitle = x.a.Subtitle,
                Kind = ResultKind.App,
                Target = x.a.Target,
                Score = x.score + 300 // las apps tienen prioridad sobre archivos
            })
            .ToList();
    }
}
