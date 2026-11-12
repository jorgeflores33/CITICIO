using System.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Data;

public sealed class CiticioDb(IConfiguration configuration)
{
    private string ConnectionString => configuration.GetConnectionString("AgenciaCitas")
        ?? configuration.GetConnectionString("CiticioConnection")
        ?? configuration.GetConnectionString("InventarioConnection")
        ?? configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Configura ConnectionStrings:AgenciaCitas (o CiticioConnection).");

    public static SqlParameter Param(string name, object? value) => new(name, value ?? DBNull.Value);

    public async Task<List<Dictionary<string, object?>>> QueryAsync(
        string command, CommandType type = CommandType.Text, params SqlParameter[] args)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqlCommand(command, conn) { CommandType = type, CommandTimeout = 25 };
        cmd.Parameters.AddRange(args);
        await using var reader = await cmd.ExecuteReaderAsync();
        var result = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i) ? null : reader.GetValue(i);
            result.Add(row);
        }
        return result;
    }

    public async Task<Dictionary<string, object?>?> FirstAsync(
        string command, CommandType type = CommandType.Text, params SqlParameter[] args)
        => (await QueryAsync(command, type, args)).FirstOrDefault();

    // Cambio atómico: comprueba los permisos con bloqueo y llama al SP existente
    // en la MISMA transacción. Protege incluso si se edita la asignación en paralelo.
    public async Task<bool> UpdatePriceIfAuthorizedAsync(int usuarioId, int servicioId, decimal precio)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            const string comprobar = @"SELECT sv.id_empresa FROM dbo.servicios sv WITH (UPDLOCK, HOLDLOCK)
              JOIN dbo.empleados em ON em.id_empresa=sv.id_empresa
              JOIN dbo.usuarios u ON u.id_empleado=em.id_empleado
              JOIN dbo.roles rol ON rol.id_rol=u.id_rol
              JOIN dbo.estados estado ON estado.id_estado=em.id_estado
              WHERE u.id_usuario=@usuario AND sv.id_servicio=@servicio
                AND estado.nombre_estado='empleado_activo'
                AND (rol.nombre_rol='admin_empresa'
                  OR (rol.nombre_rol='empleado' AND EXISTS
                    (SELECT 1 FROM dbo.empleados_servicios es WITH (HOLDLOCK)
                     WHERE es.id_empleado=em.id_empleado AND es.id_servicio=sv.id_servicio)))";
            await using var check = new SqlCommand(comprobar, conn, tx);
            check.Parameters.AddRange([Param("@usuario", usuarioId), Param("@servicio", servicioId)]);
            var empresa = await check.ExecuteScalarAsync();
            if (empresa is null) { await tx.RollbackAsync(); return false; }
            await using var cmd = new SqlCommand("dbo.sp_servicios", conn, tx)
                { CommandType = CommandType.StoredProcedure, CommandTimeout = 30 };
            cmd.Parameters.AddRange([
                Param("@accion", "actualizar"), Param("@id_servicio", servicioId),
                Param("@id_empresa", Convert.ToInt32(empresa)), Param("@precio", precio)]);
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            try { await tx.RollbackAsync(); } catch { }
            throw;
        }
    }

    // Verificacion de pertenencia y cambio de estado dentro de la misma transaccion:
    // nunca se puede mandar el id de empresa o sucursal desde el navegador.
    public async Task<bool> ChangeAppointmentStatusAsync(int citaId, int empresaId, int? sucursalId,
        string role, string estado)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            const string q = @"SELECT c.id_cita FROM dbo.citas c WITH (UPDLOCK,HOLDLOCK)
             JOIN dbo.sucursales s ON s.id_sucursal=c.id_sucursal
             JOIN dbo.estados es ON es.id_estado=c.id_estado
             WHERE c.id_cita=@cita AND s.id_empresa=@empresa
               AND (@rol='admin_empresa' OR c.id_sucursal=@sucursal)
               AND ((es.nombre_estado='cita_pendiente' AND @estado IN ('cita_confirmada','cita_cancelada'))
                  OR (es.nombre_estado='cita_confirmada' AND @estado IN ('cita_completada','cita_cancelada')))";
            await using var check = new SqlCommand(q, conn, tx);
            check.Parameters.AddRange([
                Param("@cita", citaId), Param("@empresa", empresaId), Param("@sucursal", sucursalId),
                Param("@rol", role), Param("@estado", estado)]);
            var owned = await check.ExecuteScalarAsync();
            if (owned is null)
            {
                await tx.RollbackAsync();
                return false;
            }
            await using var idCmd = new SqlCommand("SELECT id_estado FROM dbo.estados WHERE nombre_estado=@estado", conn, tx);
            idCmd.Parameters.Add(Param("@estado", estado));
            var idEstado = await idCmd.ExecuteScalarAsync();
            if (idEstado is null) { await tx.RollbackAsync(); return false; }
            await using var cmd = new SqlCommand("dbo.sp_citas", conn, tx) { CommandType = CommandType.StoredProcedure };
            cmd.Parameters.AddRange([Param("@accion", "cambiar_estado"), Param("@id_cita", citaId),
                Param("@id_estado", (int)idEstado)]);
            await cmd.ExecuteNonQueryAsync();
            await tx.CommitAsync();
            return true;
        }
        catch { try { await tx.RollbackAsync(); } catch { } throw; }
    }
}
