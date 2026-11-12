using System.Data;
using System.Security.Claims;
using Citicio.Data;
using Microsoft.AspNetCore.Identity;

namespace Citicio.Auth;

/// <summary>
/// Identifica al usuario por su cookie y vuelve a cargar rol y ámbito desde SQL
/// en CADA solicitud. Ningún id_empresa ni id_empleado proviene del frontend.
/// </summary>
public sealed class CurrentUserService(CiticioDb db)
{
    private const string LookupSql = @"
        SELECT u.id_usuario, u.nombre_usuario, u.correo_acceso, u.contrasena_hash,
            r.nombre_rol, u.id_empleado, em.id_empresa, em.id_sucursal_principal,
            ISNULL(emp.nombre,'') AS empresa, ISNULL(su.nombre,'') AS sucursal,
            es.nombre_estado AS estado_empleado,
            ee.nombre_estado AS estado_empresa,
            ess.nombre_estado AS estado_sucursal
        FROM dbo.usuarios u
        JOIN dbo.roles r ON r.id_rol = u.id_rol
        LEFT JOIN dbo.empleados em ON em.id_empleado = u.id_empleado
        LEFT JOIN dbo.estados es ON es.id_estado = em.id_estado
        LEFT JOIN dbo.empresas emp ON emp.id_empresa = em.id_empresa
        LEFT JOIN dbo.estados ee ON ee.id_estado = emp.id_estado
        LEFT JOIN dbo.sucursales su ON su.id_sucursal = em.id_sucursal_principal
        LEFT JOIN dbo.estados ess ON ess.id_estado=su.id_estado";

    public async Task<CurrentUser?> GetAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true ||
            !int.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)) return null;
        var row = await db.FirstAsync(LookupSql + " WHERE u.id_usuario=@id", CommandType.Text,
            CiticioDb.Param("@id", id));
        return FromRow(row);
    }

    public async Task<CurrentUser?> AuthenticateAsync(string login, string password)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password)) return null;
        var row = await db.FirstAsync(LookupSql +
            " WHERE LOWER(u.correo_acceso)=@login OR LOWER(u.nombre_usuario)=@login", CommandType.Text,
            CiticioDb.Param("@login", login.Trim().ToLowerInvariant()));
        var user = FromRow(row);
        if (user is null || row is null) return null;
        PasswordVerificationResult result;
        try
        {
            result = new PasswordHasher<object>().VerifyHashedPassword(new object(), GetString(row, "contrasena_hash"), password);
        }
        catch { result = PasswordVerificationResult.Failed; }
        return result is PasswordVerificationResult.Failed ? null : user;
    }

    private static CurrentUser? FromRow(Dictionary<string, object?>? row)
    {
        if (row is null || !EmpresaRoles.EsRolEmpresa(GetString(row, "nombre_rol")) ||
            GetString(row, "estado_empleado") != "empleado_activo" ||
            GetString(row, "estado_empresa") != "empresa_activa") return null;
        var empresa = GetInt(row, "id_empresa");
        var empleado = GetInt(row, "id_empleado");
        var usuario = GetInt(row, "id_usuario");
        if (empresa is null || empleado is null || usuario is null) return null;
        var rol = GetString(row, "nombre_rol");
        var sucursal = GetInt(row, "id_sucursal_principal");
        if (rol == EmpresaRoles.EncargadoSucursal && (sucursal is null || GetString(row,"estado_sucursal") != "sucursal_activa")) return null;
        return new CurrentUser(usuario.Value, GetString(row, "nombre_usuario"), rol,
            empresa.Value, sucursal, empleado.Value, GetString(row, "empresa"), GetString(row, "sucursal"));
    }

    private static string GetString(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) ? Convert.ToString(value) ?? "" : "";
    private static int? GetInt(Dictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null ? Convert.ToInt32(value) : null;
}
