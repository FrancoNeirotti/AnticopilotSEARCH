using System;
using System.Globalization;
using System.Text;

namespace CopilotLauncher;

/// <summary>Puntuación simple tipo Spotlight: exacto &gt; prefijo &gt; inicio de palabra &gt; contiene &gt; difuso.</summary>
public static class Matcher
{
    public static string Normalize(string s)
    {
        var decomposed = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }

    /// <returns>0 si no coincide; mayor es mejor.</returns>
    public static double Score(string normalizedQuery, string candidate)
    {
        if (normalizedQuery.Length == 0) return 0;
        var text = Normalize(candidate);

        if (text == normalizedQuery) return 1000;
        if (text.StartsWith(normalizedQuery, StringComparison.Ordinal)) return 800 - Math.Min(text.Length, 100);

        int idx = text.IndexOf(normalizedQuery, StringComparison.Ordinal);
        if (idx > 0)
        {
            char prev = text[idx - 1];
            bool wordStart = !char.IsLetterOrDigit(prev);
            return (wordStart ? 600 : 400) - idx - Math.Min(text.Length, 100) * 0.5;
        }

        // Todas las palabras de la consulta presentes.
        var words = normalizedQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
        {
            bool all = true;
            foreach (var w in words)
                if (!text.Contains(w, StringComparison.Ordinal)) { all = false; break; }
            if (all) return 350 - Math.Min(text.Length, 100) * 0.5;
        }

        // Iniciales: "vsc" -> "Visual Studio Code".
        var initials = new StringBuilder();
        bool atStart = true;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (atStart) initials.Append(c);
                atStart = false;
            }
            else atStart = true;
        }
        if (normalizedQuery.Length >= 2 && initials.ToString().StartsWith(normalizedQuery, StringComparison.Ordinal))
            return 500;

        // Subsecuencia difusa.
        int qi = 0, gaps = 0, last = -1;
        for (int i = 0; i < text.Length && qi < normalizedQuery.Length; i++)
        {
            if (text[i] == normalizedQuery[qi])
            {
                if (last >= 0) gaps += i - last - 1;
                last = i;
                qi++;
            }
        }
        if (qi == normalizedQuery.Length && normalizedQuery.Length >= 2)
            return Math.Max(1, 200 - gaps * 5 - text.Length * 0.5);

        return 0;
    }
}
