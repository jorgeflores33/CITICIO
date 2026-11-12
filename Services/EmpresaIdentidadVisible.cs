using System.Security.Claims;
using Citicio.Auth;
using Citicio.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;

/// <summary>Datos de presentación obtenidos de la empresa real, nunca de un ID enviado por el navegador.</summary>
public sealed class EmpresaIdentidadVisible(SqlStore db, ILogger<EmpresaIdentidadVisible> logger)
{
    public async Task<(string Usuario, string Empresa)> LeerAsync(ClaimsPrincipal usuario)
    {
        var nombre = usuario.Identity?.Name ?? "Mi cuenta";
        if (!usuario.IsInRole("admin_empresa") && !usuario.IsInRole("encargado_sucursal") && !usuario.IsInRole("empleado"))
            return (nombre, "");
        if (usuario.UserId() <= 0 || usuario.EmpresaId() <= 0) return (nombre, "");
        try
        {
            await using var cn = await db.OpenAsync();
            var filas = await SqlStore.ReadTextAsync(cn, null, @"
                SELECT TOP (1)
                    LTRIM(RTRIM(CONCAT(e.nombre, ' ', e.apellido_paterno, ' ', ISNULL(e.apellido_materno,'')))) AS usuario,
                    em.nombre AS empresa
                FROM dbo.usuarios u
                JOIN dbo.empleados e ON e.id_empleado = u.id_empleado
                JOIN dbo.empresas em ON em.id_empresa = e.id_empresa
                WHERE u.id_usuario = @usuario AND e.id_empresa = @empresa",
                SqlStore.P(("usuario", usuario.UserId()), ("empresa", usuario.EmpresaId())));
            var row = filas.FirstOrDefault();
            return row is null ? (nombre, "") : (row.S("usuario"), row.S("empresa"));
        }
        catch (SqlException ex)
        {
            logger.LogWarning("No fue posible cargar el nombre visible; SQL {Numero}", ex.Number);
            return (nombre, "");
        }
    }
}
