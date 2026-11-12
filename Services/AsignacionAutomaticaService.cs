using System.Text.Json;
using Citicio.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;

/// <summary>
/// Al asignar servicios a un trabajador habilita, en la misma transacción,
/// cada servicio en su sucursal activa. Conserva servicios ya ofrecidos
/// por otros empleados; no borra las relaciones de sucursal al desmarcar.
/// No agrega tablas, columnas ni procedimientos almacenados.
/// </summary>
public sealed class AsignacionAutomaticaService(SqlStore db)
{
    public async Task GuardarAsync(int actorId, int empresaId, int empleadoId,
        IEnumerable<int> seleccionados, CancellationToken ct = default, int sucursalPermitida = 0)
    {
        var ids = seleccionados.Distinct().ToArray();
        if (actorId <= 0 || empresaId <= 0 || empleadoId <= 0 || ids.Length > 150 || ids.Any(i => i <= 0) || sucursalPermitida < 0)
            throw new InvalidOperationException("Empleado o servicios no válidos.");

        await using var cn = await db.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
        try
        {
            var trabajador = (await SqlStore.ReadTextAsync(cn, tx, @"
                SELECT TOP (1) e.id_sucursal_principal AS sucursal
                FROM dbo.empleados e
                JOIN dbo.sucursales su ON su.id_sucursal=e.id_sucursal_principal AND su.id_empresa=e.id_empresa
                JOIN dbo.estados est ON est.id_estado=su.id_estado AND est.nombre_estado='sucursal_activa'
                WHERE e.id_empleado=@empleado AND e.id_empresa=@empresa
                  AND (@sucursal_permitida=0 OR e.id_sucursal_principal=@sucursal_permitida)",
                SqlStore.P(("empleado", empleadoId), ("empresa", empresaId), ("sucursal_permitida", sucursalPermitida)), ct)).FirstOrDefault();
            if (trabajador is null || trabajador.I("sucursal") <= 0)
                throw new InvalidOperationException("El empleado debe tener una sucursal activa de su empresa.");
            var sucursal = trabajador.I("sucursal");

            var vigentes = await SqlStore.ReadTextAsync(cn, tx, @"
                SELECT s.id_servicio FROM dbo.servicios s
                LEFT JOIN dbo.estados es ON es.id_estado=s.id_estado
                WHERE s.id_empresa=@empresa AND ISNULL(es.nombre_estado,'')<>'servicio_inactivo'",
                SqlStore.P(("empresa", empresaId)), ct);
            var permitidos = vigentes.Select(x => x.I("id_servicio")).ToHashSet();
            if (ids.Any(x => !permitidos.Contains(x)))
                throw new InvalidOperationException("Uno o más servicios están inactivos o pertenecen a otra empresa.");

            // Guardamos la duración actual de los servicios existentes de la sucursal.
            var actuales = await SqlStore.ReadTextAsync(cn, tx, @"
                SELECT ss.id_servicio, ss.duracion_minutos
                FROM dbo.sucursales_servicios ss
                JOIN dbo.servicios sv ON sv.id_servicio=ss.id_servicio AND sv.id_empresa=@empresa
                LEFT JOIN dbo.estados st ON st.id_estado=sv.id_estado
                WHERE ss.id_sucursal=@sucursal AND ISNULL(st.nombre_estado,'')<>'servicio_inactivo'",
                SqlStore.P(("empresa", empresaId), ("sucursal", sucursal)), ct);
            var duraciones = actuales.ToDictionary(x => x.I("id_servicio"), x => x.I("duracion_minutos"));
            foreach (var id in ids)
                if (!duraciones.ContainsKey(id)) duraciones[id] = 30; // duración inicial configurable después
            var enSucursal = duraciones.OrderBy(x => x.Key)
                .Select(x => new { idServicio = x.Key, duracionMinutos = x.Value is >= 1 and <= 720 ? x.Value : 30 });

            await SqlStore.ReadAsync(cn, tx, "dbo.sp_citicio_servicios_sucursal_guardar", SqlStore.P(
                ("id_usuario_actor", actorId), ("id_sucursal", sucursal),
                ("servicios_json", JsonSerializer.Serialize(enSucursal))), ct);
            await SqlStore.ReadAsync(cn, tx, "dbo.sp_citicio_servicios_empleado_guardar", SqlStore.P(
                ("id_usuario_actor", actorId), ("id_empleado", empleadoId),
                ("servicios_json", JsonSerializer.Serialize(ids))), ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch { /* el SP pudo hacer ROLLBACK */ }
            throw;
        }
    }

    public async Task SincronizarSucursalAsync(int actorId, int empresaId, int empleadoId, CancellationToken ct = default)
    {
        await using var cn = await db.OpenAsync(ct);
        var asignados = await SqlStore.ReadTextAsync(cn, null, @"
            SELECT es.id_servicio FROM dbo.empleados_servicios es
            JOIN dbo.empleados em ON em.id_empleado=es.id_empleado
            JOIN dbo.servicios s ON s.id_servicio=es.id_servicio AND s.id_empresa=em.id_empresa
            LEFT JOIN dbo.estados st ON st.id_estado=s.id_estado
            WHERE em.id_empleado=@empleado AND em.id_empresa=@empresa
              AND ISNULL(st.nombre_estado,'')<>'servicio_inactivo'",
            SqlStore.P(("empleado", empleadoId), ("empresa", empresaId)), ct);
        // Cerrar la conexión de lectura ANTES de iniciar la transacción de escritura.
        await cn.CloseAsync();
        if (asignados.Count > 0)
            await GuardarAsync(actorId, empresaId, empleadoId, asignados.Select(x => x.I("id_servicio")), ct);
    }
}
