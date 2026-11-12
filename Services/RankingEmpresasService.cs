using Citicio.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;

/// <summary>Top 10 por empresa, no por sucursal. Usa solamente tablas existentes.</summary>
public static class RankingEmpresasService
{
    private const string Sql = @"
        WITH empresas_publicadas AS (
            SELECT em.id_empresa, em.nombre AS empresa, em.descripcion
            FROM dbo.empresas em
            JOIN dbo.estados ee ON ee.id_estado=em.id_estado AND ee.nombre_estado='empresa_activa'
            WHERE EXISTS (
                SELECT 1 FROM dbo.cobros_empresas ce
                JOIN dbo.estados ec ON ec.id_estado=ce.id_estado AND ec.nombre_estado='cobro_pagado'
                WHERE ce.id_empresa=em.id_empresa
                  AND CONVERT(date,GETDATE()) BETWEEN ce.periodo_inicio AND ce.periodo_fin
            ) AND EXISTS (
                SELECT 1 FROM dbo.sucursales su
                JOIN dbo.estados se ON se.id_estado=su.id_estado AND se.nombre_estado='sucursal_activa'
                JOIN dbo.colonias col ON col.id_colonia=su.id_colonia
                JOIN dbo.sucursales_servicios ss ON ss.id_sucursal=su.id_sucursal
                JOIN dbo.servicios sv ON sv.id_servicio=ss.id_servicio AND sv.id_empresa=em.id_empresa
                LEFT JOIN dbo.estados esv ON esv.id_estado=sv.id_estado
                WHERE su.id_empresa=em.id_empresa AND ISNULL(esv.nombre_estado,'')<>'servicio_inactivo'
                  AND (@municipio IS NULL OR col.id_municipio=@municipio)
                  AND EXISTS(
                    SELECT 1 FROM dbo.empleados emp
                    JOIN dbo.empleados_servicios x ON x.id_empleado=emp.id_empleado AND x.id_servicio=sv.id_servicio
                    JOIN dbo.estados est ON est.id_estado=emp.id_estado AND est.nombre_estado='empleado_activo'
                    WHERE emp.id_empresa=em.id_empresa AND emp.id_sucursal_principal=su.id_sucursal
                  )
            )
        )
        SELECT TOP(10) p.id_empresa,p.empresa,
            (SELECT COUNT(*) FROM dbo.sucursales su JOIN dbo.estados st ON st.id_estado=su.id_estado
             WHERE su.id_empresa=p.id_empresa AND st.nombre_estado='sucursal_activa') AS sucursales,
            (SELECT COUNT(*) FROM dbo.citas ci JOIN dbo.sucursales su ON su.id_sucursal=ci.id_sucursal
             JOIN dbo.estados ec ON ec.id_estado=ci.id_estado AND ec.nombre_estado='cita_completada'
             WHERE su.id_empresa=p.id_empresa) AS citas_completadas,
            (SELECT COUNT(*) FROM dbo.resenas re JOIN dbo.citas ci ON ci.id_cita=re.id_cita
             JOIN dbo.sucursales su ON su.id_sucursal=ci.id_sucursal
             WHERE su.id_empresa=p.id_empresa) AS cantidad_resenas,
            (SELECT CONVERT(DECIMAL(5,2),AVG(CONVERT(DECIMAL(6,2),re.calificacion_empresa)))
             FROM dbo.resenas re JOIN dbo.citas ci ON ci.id_cita=re.id_cita
             JOIN dbo.sucursales su ON su.id_sucursal=ci.id_sucursal
             WHERE su.id_empresa=p.id_empresa) AS calificacion_promedio
        FROM empresas_publicadas p
        ORDER BY citas_completadas DESC, cantidad_resenas DESC, calificacion_promedio DESC, p.empresa;
    ";
    public static Task<List<DbRow>> ObtenerAsync(SqlConnection cn, CancellationToken ct = default, int? municipio = null) =>
        SqlStore.ReadTextAsync(cn,null,Sql,SqlStore.P(("municipio",municipio)),ct);
}
