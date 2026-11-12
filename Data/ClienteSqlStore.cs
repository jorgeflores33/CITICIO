using System.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Data;

internal sealed class ClienteSqlStore(IConfiguration config) {
    private string ConnectionString => config.GetConnectionString("AgenciaCitas") is { Length: > 0 } connection
        ? connection : throw new InvalidOperationException("Falta ConnectionStrings:AgenciaCitas.");
    public async Task<SqlConnection> Open(CancellationToken ct) {
        var cn = new SqlConnection(ConnectionString);
        try { await cn.OpenAsync(ct); return cn; }
        catch { await cn.DisposeAsync(); throw; }
    }
    public Task<List<Dictionary<string, object?>>> Sp(string name, CancellationToken ct, params SqlParameter[] parameters)
        => Sp(name, ct, null, null, parameters);
    public async Task<List<Dictionary<string, object?>>> Sp(string name, CancellationToken ct,
        SqlConnection? cn, SqlTransaction? tx, params SqlParameter[] parameters) {
        var ownsConnection = cn is null;
        cn ??= await Open(ct);
        try {
            await using var cmd = new SqlCommand(name, cn, tx) { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
            cmd.Parameters.AddRange(parameters);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var result = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(ct)) {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                result.Add(row);
            }
            return result;
        } finally { if (ownsConnection) await cn.DisposeAsync(); }
    }
}
