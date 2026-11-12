using System.Globalization;

namespace Citicio.Data;

internal static class Val {
    public static string Str(Dictionary<string, object?> row, string key)
        => row.TryGetValue(key, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" : "";
    public static int Int(Dictionary<string, object?> row, string key)
        => Convert.ToInt32(row[key], CultureInfo.InvariantCulture);
    public static decimal Dec(Dictionary<string, object?> row, string key)
        => Convert.ToDecimal(row[key], CultureInfo.InvariantCulture);
    public static DateTime Date(Dictionary<string, object?> row, string key)
        => Convert.ToDateTime(row[key], CultureInfo.InvariantCulture);
}
