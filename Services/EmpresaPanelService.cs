using System.Data;
using Citicio.Auth;
using Citicio.Data;

namespace Citicio.Services;

/// <summary>
/// Consultas del panel conectadas a los nombres reales de CITICIO.
/// Todos los filtros se aplican usando el usuario autenticado.
/// </summary>
public sealed class EmpresaPanelService(CiticioDb db)
{
    public async Task<List<Dictionary<string, object?>>> ObtenerAsync(CurrentUser user, string seccion)
    {
        if (!EmpresaRoles.PermiteSeccion(user.Rol, seccion))
            throw new UnauthorizedAccessException("El rol no tiene permiso para consultar la sección.");
        switch (seccion.ToLowerInvariant())
        {
            case "dashboard":
                if (user.Rol == EmpresaRoles.AdminEmpresa)
                    return await db.QueryAsync("dbo.sp_dashboard_empresa", CommandType.StoredProcedure,
                        CiticioDb.Param("@id_empresa", user.IdEmpresa));
                if (user.Rol == EmpresaRoles.EncargadoSucursal)
                    return await db.QueryAsync("dbo.sp_dashboard_sucursal_usuario", CommandType.StoredProcedure,
                        CiticioDb.Param("@id_usuario", user.Id));
                return await db.QueryAsync(@"
                    SELECT
                        (SELECT COUNT(*) FROM dbo.citas WHERE id_empleado=@empleado) AS citas,
                        (SELECT COUNT(*) FROM dbo.citas c JOIN dbo.estados s ON s.id_estado=c.id_estado
                          WHERE c.id_empleado=@empleado AND s.nombre_estado='cita_pendiente') AS pendientes,
                        (SELECT COUNT(*) FROM dbo.empleados_servicios WHERE id_empleado=@empleado) AS servicios,
                        (SELECT COUNT(*) FROM dbo.horarios_empleados WHERE id_empleado=@empleado) AS horarios",
                    CommandType.Text, CiticioDb.Param("@empleado", user.IdEmpleado));

            case "citas":
                return await db.QueryAsync("dbo.sp_citas_panel_usuario", CommandType.StoredProcedure,
                    CiticioDb.Param("@id_usuario", user.Id));
            case "servicios":
                return await db.QueryAsync("dbo.sp_panel_servicios_usuario", CommandType.StoredProcedure,
                    CiticioDb.Param("@id_usuario", user.Id));
            case "empleados":
                return await db.QueryAsync("dbo.sp_panel_empleados_usuario", CommandType.StoredProcedure,
                    CiticioDb.Param("@id_usuario", user.Id));
            case "pagos":
                return await db.QueryAsync("dbo.sp_panel_pagos_usuario", CommandType.StoredProcedure,
                    CiticioDb.Param("@id_usuario", user.Id));
            case "resenas":
                return await db.QueryAsync("dbo.sp_panel_resenas_usuario", CommandType.StoredProcedure,
                    CiticioDb.Param("@id_usuario", user.Id));
            case "cobros":
                return await db.QueryAsync("dbo.sp_panel_cobros_empresas_usuario", CommandType.StoredProcedure,
                    CiticioDb.Param("@id_usuario", user.Id));
            case "sucursales":
                return await db.QueryAsync(@"
                    SELECT su.id_sucursal, su.nombre, est.nombre_estado, su.calle, su.numero_exterior,
                        col.nombre AS colonia, mun.nombre AS municipio, ef.nombre AS estado
                    FROM dbo.sucursales su
                    JOIN dbo.estados est ON est.id_estado=su.id_estado
                    JOIN dbo.colonias col ON col.id_colonia=su.id_colonia
                    JOIN dbo.municipios mun ON mun.id_municipio=col.id_municipio
                    JOIN dbo.entidades_federativas ef ON ef.id_entidad_federativa=mun.id_entidad_federativa
                    WHERE su.id_empresa=@empresa AND
                        (@rol='admin_empresa' OR su.id_sucursal=@sucursal)
                    ORDER BY su.nombre", CommandType.Text,
                    CiticioDb.Param("@empresa", user.IdEmpresa),
                    CiticioDb.Param("@rol", user.Rol),
                    CiticioDb.Param("@sucursal", user.IdSucursal));
            case "horarios":
                // Vista combinada: apertura semanal de la sucursal y turnos del personal.
                // Empleados solamente consultan sus propios turnos; el encargado su sucursal.
                return await db.QueryAsync(@"
                    SELECT CAST('empleado' AS VARCHAR(20)) AS tipo,
                        h.id_horario_empleado AS id_horario, h.id_empleado, h.id_sucursal,
                        CONCAT(emp.nombre,' ',emp.apellido_paterno) AS empleado,
                        su.nombre AS sucursal, dia.nombre AS dia, h.id_dia,
                        h.hora_entrada, h.hora_salida
                    FROM dbo.horarios_empleados h
                    JOIN dbo.empleados emp ON emp.id_empleado=h.id_empleado
                    JOIN dbo.sucursales su ON su.id_sucursal=h.id_sucursal
                    JOIN dbo.dias_semana dia ON dia.id_dia=h.id_dia
                    WHERE emp.id_empresa=@empresa AND
                        (@rol='admin_empresa' OR
                          (@rol='encargado_sucursal' AND h.id_sucursal=@sucursal) OR
                          (@rol='empleado' AND h.id_empleado=@empleado))
                    UNION ALL
                    SELECT CAST('sucursal' AS VARCHAR(20)) AS tipo,
                        h.id_horario_sucursal AS id_horario, CAST(NULL AS INT) AS id_empleado,
                        h.id_sucursal, CAST('Horario de atención' AS VARCHAR(200)) AS empleado,
                        su.nombre AS sucursal, dia.nombre AS dia, h.id_dia,
                        h.hora_apertura AS hora_entrada, h.hora_cierre AS hora_salida
                    FROM dbo.horarios_sucursales h
                    JOIN dbo.sucursales su ON su.id_sucursal=h.id_sucursal
                    JOIN dbo.dias_semana dia ON dia.id_dia=h.id_dia
                    WHERE su.id_empresa=@empresa AND @rol IN ('admin_empresa','encargado_sucursal')
                      AND (@rol='admin_empresa' OR h.id_sucursal=@sucursal)
                    ORDER BY sucursal,id_dia,tipo,empleado", CommandType.Text,
                    CiticioDb.Param("@empresa", user.IdEmpresa),
                    CiticioDb.Param("@rol", user.Rol),
                    CiticioDb.Param("@sucursal", user.IdSucursal),
                    CiticioDb.Param("@empleado", user.IdEmpleado));
            default:
                throw new ArgumentOutOfRangeException(nameof(seccion));
        }
    }
}
