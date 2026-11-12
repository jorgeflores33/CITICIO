using Citicio.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;

public static class SuscripcionAccesoService
{
    // Solo empresas y sucursales activas con cuota mensual pagada y vigente.
    private const string Sql = @"
        SELECT TOP(1) CAST(1 AS int) AS permitido
        FROM dbo.sucursales_servicios ss
        JOIN dbo.sucursales su ON su.id_sucursal=ss.id_sucursal
        JOIN dbo.empresas em ON em.id_empresa=su.id_empresa
        JOIN dbo.estados esu ON esu.id_estado=su.id_estado
        JOIN dbo.estados eem ON eem.id_estado=em.id_estado
        WHERE ss.id_sucursal=@sucursal AND ss.id_servicio=@servicio
          AND esu.nombre_estado='sucursal_activa'
          AND eem.nombre_estado='empresa_activa'
          AND EXISTS(SELECT 1 FROM dbo.cobros_empresas ce
            JOIN dbo.estados ep ON ep.id_estado=ce.id_estado AND ep.nombre_estado='cobro_pagado'
            WHERE ce.id_empresa=em.id_empresa
              AND CONVERT(date,GETDATE()) BETWEEN ce.periodo_inicio AND ce.periodo_fin)";

    public static async Task<bool> PermitidoAsync(SqlConnection conexion, SqlTransaction? transaccion,
        int sucursal, int servicio, CancellationToken ct = default)
    {
        if (sucursal<=0 || servicio<=0) return false;
        var result=await SqlStore.ReadTextAsync(conexion,transaccion,Sql,
            SqlStore.P(("sucursal",sucursal),("servicio",servicio)),ct);
        return result.Count>0;
    }
}
