using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Citicio.Services;

/// <summary>
/// Búsqueda de catálogo sin cambios a SQL Server: ignora acentos, permite
/// términos parciales y corrige hasta dos errores de escritura habituales.
/// Solo trabaja con los resultados publicados por la base de datos.
/// </summary>
public static class BusquedaFlexible
{
    private static readonly string[][] Grupos =
    [
        ["peluqueria", "peluqeria", "peluquero", "peluquera", "barberia", "barbero", "cabello", "corte", "peinado", "estilista", "salon"],
        ["manicura", "manicure", "unas", "nails", "pedicura", "pedicure"],
        ["masaje", "masajes", "spa", "relajacion"],
        ["maquillaje", "maquillar", "makeup", "cosmetica"],
        ["dentista", "dental", "odontologia", "odontologo"]
    ];

    public static string Normalizar(string? original)
    {
        var limpio = (original ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(limpio.Length);
        foreach (var ch in limpio)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    public static bool Coincide(string? consulta, params string?[] campos)
    {
        var q = Normalizar(consulta);
        if (q.Length == 0) return true;
        var texto = Normalizar(string.Join(' ', campos));
        if (texto.Length == 0) return false;
        if (texto.Contains(q, StringComparison.Ordinal)) return true;
        var tokens = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var terminos = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return terminos.All(t => TokenCoincide(t,tokens));
    }

    private static bool TokenCoincide(string termino, string[] tokens)
    {
        if (tokens.Any(t => t.Contains(termino, StringComparison.Ordinal) ||
                  (termino.Length >= 3 && termino.Contains(t, StringComparison.Ordinal) && t.Length>=3))) return true;
        foreach (var grupo in Grupos)
            if (grupo.Any(g => Similar(termino,g)) && tokens.Any(t => grupo.Any(g=>Similar(t,g)))) return true;
        return tokens.Any(t => Similar(termino,t));
    }

    private static bool Similar(string a,string b)
    {
        if (a==b) return true;
        if (a.Length<4 || b.Length<4 || Math.Abs(a.Length-b.Length)>2) return false;
        if (a.StartsWith(b,StringComparison.Ordinal) || b.StartsWith(a,StringComparison.Ordinal)) return true;
        var prev = Enumerable.Range(0,b.Length+1).ToArray();
        var curr = new int[b.Length+1];
        for (int i=1; i<=a.Length; i++)
        {
            curr[0]=i;
            for(int j=1; j<=b.Length; j++)
                curr[j]=Math.Min(Math.Min(curr[j-1]+1,prev[j]+1),prev[j-1]+(a[i-1]==b[j-1]?0:1));
            (curr,prev)=(prev,curr);
        }
        return prev[b.Length] <= (Math.Min(a.Length,b.Length)>=7?2:1);
    }
}
