using System.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Data;
public sealed class DbRow {
    public Dictionary<string,object?> Cells {get;} = new(StringComparer.OrdinalIgnoreCase);
    public string S(string key) => Cells.TryGetValue(key,out var v) && v is not null ? Convert.ToString(v) ?? "" : "";
    public int I(string key) => Cells.TryGetValue(key,out var v) && v is not null ? Convert.ToInt32(v) : 0;
    public decimal M(string key) => Cells.TryGetValue(key,out var v) && v is not null ? Convert.ToDecimal(v) : 0m;
    public DateTime D(string key) => Cells.TryGetValue(key,out var v) && v is not null ? Convert.ToDateTime(v) : DateTime.MinValue;
    public bool B(string key) => Cells.TryGetValue(key,out var v) && v is not null && Convert.ToBoolean(v);
}
public sealed class SqlStore(IConfiguration cfg) {
    private string ConnectionString => cfg.GetConnectionString("AgenciaCitas")
        ?? throw new InvalidOperationException("Configura ConnectionStrings:AgenciaCitas");
    public async Task<SqlConnection> OpenAsync(CancellationToken ct=default) {
        var c = new SqlConnection(ConnectionString);
        try { await c.OpenAsync(ct); return c; }
        catch { await c.DisposeAsync(); throw; }
    }
    public static Dictionary<string,object?> P(params (string,object?)[] pairs) => pairs.ToDictionary(x=>x.Item1,x=>x.Item2);
    private static SqlCommand Make(SqlConnection conn, SqlTransaction? tx, string sql, Dictionary<string,object?>? parameters) {
        var cmd = new SqlCommand(sql,conn,tx) { CommandType=CommandType.StoredProcedure,CommandTimeout=30 };
        if(parameters is not null) foreach(var (key,value) in parameters) {
            var name = key.StartsWith('@') ? key : "@"+key;
            var p = cmd.Parameters.AddWithValue(name,value ?? DBNull.Value);
            if(value is string) p.SqlDbType=SqlDbType.NVarChar;
            if(value is byte[]) {p.SqlDbType=SqlDbType.VarBinary;p.Size=-1;}
        }
        return cmd;
    }
    public static async Task<List<DbRow>> ReadAsync(SqlConnection conn,SqlTransaction? tx,string sql,Dictionary<string,object?>? pars=null,CancellationToken ct=default) {
        await using var cmd=Make(conn,tx,sql,pars);
        await using var rdr=await cmd.ExecuteReaderAsync(ct);
        var list=new List<DbRow>();
        while(await rdr.ReadAsync(ct)) {
            var row=new DbRow();
            for(var i=0;i<rdr.FieldCount;i++) row.Cells[rdr.GetName(i)]=await rdr.IsDBNullAsync(i,ct)?null:rdr.GetValue(i);
            list.Add(row);
        }
        return list;
    }
    // Consultas de lectura parametrizadas; nunca interpolar valores del usuario.
    public static async Task<List<DbRow>> ReadTextAsync(SqlConnection conn,SqlTransaction? tx,
        string sql,Dictionary<string,object?>? pars=null,CancellationToken ct=default) {
        await using var cmd=Make(conn,tx,sql,pars);
        cmd.CommandType=CommandType.Text;
        await using var rdr=await cmd.ExecuteReaderAsync(ct);
        var result=new List<DbRow>();
        while(await rdr.ReadAsync(ct)) {
            var row=new DbRow();
            for(var i=0;i<rdr.FieldCount;i++)row.Cells[rdr.GetName(i)]=await rdr.IsDBNullAsync(i,ct)?null:rdr.GetValue(i);
            result.Add(row);
        }
        return result;
    }
    public async Task<List<DbRow>> CallAsync(string proc,Dictionary<string,object?>? pars=null,CancellationToken ct=default) {
        await using var conn=await OpenAsync(ct);
        return await ReadAsync(conn,null,proc,pars,ct);
    }
}
