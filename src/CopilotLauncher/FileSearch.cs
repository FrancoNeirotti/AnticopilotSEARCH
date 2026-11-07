using System;
using System.Collections.Generic;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotLauncher;

/// <summary>Búsqueda de archivos: Windows Search (índice del sistema) con respaldo de escaneo de carpetas del usuario.</summary>
public sealed class FileSearch
{
    private const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows';";

    private bool _indexAvailable = true;
    private List<string>? _fallbackIndex;
    private DateTime _fallbackBuilt;
    private readonly object _fallbackGate = new();

    public bool UsingWindowsSearch => _indexAvailable;

    public Task<List<SearchResult>> SearchAsync(string query, int max, CancellationToken ct) =>
        Task.Run(() => Search(query, max, ct), ct);

    private List<SearchResult> Search(string query, int max, CancellationToken ct)
    {
        query = query.Trim();
        if (query.Length < 2) return new();

        if (_indexAvailable)
        {
            try
            {
                return SearchIndex(query, max, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Info("Windows Search no disponible, se usa escaneo de carpetas: " + ex.Message);
                _indexAvailable = false;
            }
        }
        return SearchFallback(query, max, ct);
    }

    private List<SearchResult> SearchIndex(string query, int max, CancellationToken ct)
    {
        var term = Sanitize(query);
        var words = term.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var conditions = string.Join(" AND ", words.Select(w => $"System.ItemNameDisplay LIKE '%{w}%'"));
        var sql = $"SELECT TOP {max * 3} System.ItemUrl, System.ItemNameDisplay, System.ItemType " +
                  $"FROM SystemIndex WHERE SCOPE='file:' AND {conditions} ORDER BY System.DateModified DESC";

        var q = Matcher.Normalize(query);
        var results = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var conn = new OleDbConnection(ConnectionString);
        conn.Open();
        using var cmd = new OleDbCommand(sql, conn);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var path = UrlToPath(reader[0] as string);
            var name = reader[1] as string;
            var type = reader[2] as string;
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(name)) continue;
            if (IsNoisePath(path)) continue;
            if (!seen.Add(path)) continue;
            bool isFolder = string.Equals(type, "Directory", StringComparison.OrdinalIgnoreCase);
            results.Add(Create(path, name, isFolder, q));
        }
        return results.OrderByDescending(r => r.Score).Take(max).ToList();
    }

    private static string Sanitize(string s)
    {
        // Evita inyección/comodines en la consulta AQS/SQL de Windows Search.
        var chars = s.Where(c => c != '\'' && c != '%' && c != '_' && c != '[' && c != ']' && c != '"').ToArray();
        return new string(chars);
    }

    private static string? UrlToPath(string? url)
    {
        // System.ItemUrl = "file:C:/Users/..." (ruta real, a diferencia de ItemPathDisplay que viene localizada).
        if (string.IsNullOrEmpty(url)) return null;
        if (!url.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return null;
        return url.Substring(5).Replace('/', '\\');
    }

    private static bool IsNoisePath(string path) =>
        path.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\Application Data\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\Microsoft\Windows\Start Menu\", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\node_modules\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\.git\", StringComparison.OrdinalIgnoreCase) ||
        path.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase);

    private static SearchResult Create(string path, string name, bool isFolder, string normalizedQuery)
    {
        double score = Matcher.Score(normalizedQuery, name);
        if (score <= 0) score = 50;
        if (isFolder) score += 20;
        return new SearchResult
        {
            Title = name,
            Subtitle = Path.GetDirectoryName(path) ?? path,
            Kind = isFolder ? ResultKind.Folder : ResultKind.File,
            Target = path,
            Score = score
        };
    }

    private List<SearchResult> SearchFallback(string query, int max, CancellationToken ct)
    {
        var index = GetFallbackIndex(ct);
        var q = Matcher.Normalize(query);
        return index
            .Select(p => (p, s: Matcher.Score(q, Path.GetFileName(p))))
            .Where(x => x.s > 0)
            .OrderByDescending(x => x.s)
            .Take(max)
            .Select(x => Create(x.p, Path.GetFileName(x.p), Directory.Exists(x.p), q))
            .ToList();
    }

    private List<string> GetFallbackIndex(CancellationToken ct)
    {
        lock (_fallbackGate)
        {
            if (_fallbackIndex != null && DateTime.Now - _fallbackBuilt < TimeSpan.FromMinutes(10))
                return _fallbackIndex;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new[] { "Desktop", "Documents", "Downloads", "Pictures", "Music", "Videos", "OneDrive" }
                .Select(d => Path.Combine(home, d))
                .Concat(Directory.Exists(home) ? Directory.GetDirectories(home, "OneDrive*") : Array.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(Directory.Exists);

            var list = new List<string>();
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };
            foreach (var root in roots)
            {
                try
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", options))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (IsNoisePath(entry)) continue;
                        list.Add(entry);
                        if (list.Count > 200_000) break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }
            }
            _fallbackIndex = list;
            _fallbackBuilt = DateTime.Now;
            Log.Info($"Índice de respaldo construido: {list.Count} entradas");
            return list;
        }
    }
}
