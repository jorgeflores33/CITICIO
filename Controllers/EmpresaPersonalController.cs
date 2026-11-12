using System.Data;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

/// <summary>Un admin_empresa registra encargados y empleados de SU empresa.</summary>
[Authorize]
[ApiController]
[Route("api/empresa")]
public sealed class EmpresaPersonalController(CurrentUserService actual, CiticioDb db,
    IAntiforgery antiforgery, ILogger<EmpresaPersonalController> log) : ControllerBase
{
    [HttpPost("personal")]
    public async Task<IActionResult> Registrar([FromBody] PersonalNuevoRequest input)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return BadRequest(new { error = "Token de seguridad inválido. Se renovará automáticamente.", code = "csrf_invalid" });
        var user = await actual.GetAsync(User);
        if (user is null || user.Rol != EmpresaRoles.AdminEmpresa) return Forbid();
        if (input.IdSucursal <= 0 || input.Rol is not ("empleado" or "encargado_sucursal") ||
            string.IsNullOrWhiteSpace(input.Nombre) || input.Nombre.Trim().Length > 100 ||
            string.IsNullOrWhiteSpace(input.ApellidoPaterno) || input.ApellidoPaterno.Trim().Length > 100 ||
            input.ApellidoMaterno?.Length > 100 ||
            input.Telefono is null || !Regex.IsMatch(input.Telefono, @"\A[0-9]{10,15}\z") ||
            string.IsNullOrWhiteSpace(input.Correo) || input.Correo.Length > 254 || !MailAddress.TryCreate(input.Correo, out _) ||
            string.IsNullOrWhiteSpace(input.NombreUsuario) || input.NombreUsuario.Trim().Length > 100 ||
            !Citicio.Services.PasswordPolicy.Valida(input.Contrasena) ||
            input.IdServicios is null || input.IdServicios.Count > 150 ||
            input.IdServicios.Any(x => x < 1) || input.IdServicios.Distinct().Count() != input.IdServicios.Count ||
            (input.Rol == EmpresaRoles.Empleado && input.IdServicios.Count == 0))
            return BadRequest(new { error = "Verifica los datos, sucursal, rol, servicios seleccionados y contraseña con mayúscula, minúscula, número y símbolo." });
        try
        {
            var hash = new PasswordHasher<object>().HashPassword(new object(), input.Contrasena);
            var result = await db.FirstAsync("dbo.sp_citicio_personal_con_servicios", CommandType.StoredProcedure,
                CiticioDb.Param("@id_usuario_actor", user.Id),
                CiticioDb.Param("@id_sucursal", input.IdSucursal),
                CiticioDb.Param("@rol_destino", input.Rol),
                CiticioDb.Param("@nombre", input.Nombre.Trim()),
                CiticioDb.Param("@apellido_paterno", input.ApellidoPaterno.Trim()),
                CiticioDb.Param("@apellido_materno", input.ApellidoMaterno?.Trim()),
                CiticioDb.Param("@telefono", input.Telefono.Trim()),
                CiticioDb.Param("@correo", input.Correo.Trim().ToLowerInvariant()),
                CiticioDb.Param("@nombre_usuario", input.NombreUsuario.Trim()),
                CiticioDb.Param("@contrasena_hash", hash),
                CiticioDb.Param("@servicios_json", System.Text.Json.JsonSerializer.Serialize(input.IdServicios)));
            return Ok(new { ok = true, personal = result });
        }
        catch (SqlException ex)
        {
            log.LogWarning(ex, "Error creando personal en empresa del usuario {Actor}", user.Id);
            return ex.Number switch
            {
                2601 or 2627 => Conflict(new { error = "Correo, teléfono o nombre de usuario ya registrados. Cada persona necesita credenciales propias; utiliza un usuario y correo diferentes." }),
                59020 or 59021 or 59022 => StatusCode(403, new { error = "Rol o sucursal no autorizados para tu empresa." }),
                >= 59540 and <= 59543 => BadRequest(new { error = ex.Message }),
                2812 or 208 => StatusCode(503, new { error = "Instala primero SQL/03 y después SQL/08_GESTION_SEMANAL_SERVICIOS_Y_PERMISOS.sql." }),
                _ => StatusCode(503, new { error = "No se pudo registrar personal. Revisa datos y procedimientos SQL." })
            };
        }
    }
}
