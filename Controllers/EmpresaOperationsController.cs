using Citicio.Auth;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citicio.Controllers;

[Authorize]
[ApiController]
[Route("api/empresa")]
public sealed class EmpresaOperationsController(
    CurrentUserService current, EmpresaMutationService service, IAntiforgery antiforgery) : ControllerBase
{
    private Task<bool> CsrfOkAsync() => antiforgery.IsRequestValidAsync(HttpContext);

    [HttpPost("precios")]
    public async Task<IActionResult> CambiarPrecio([FromBody] PrecioData input)
    {
        if (!await CsrfOkAsync()) return BadRequest(new { error = "Token de seguridad inválido. Se renovará automáticamente.", code = "csrf_invalid" });
        var user = await current.GetAsync(User);
        if (user is null || user.Rol is not (EmpresaRoles.AdminEmpresa or EmpresaRoles.Empleado)) return Forbid();
        if (input.IdServicio <= 0 || input.Precio < 0 || input.Precio > 99999999m)
            return BadRequest(new { error = "Servicio o precio inválido." });
        var actualizo = await service.CambiarPrecioAsync(user, input);
        if (!actualizo) return Forbid();
        return Ok(new { ok = true, aviso = "El precio es único para todas las sucursales." });
    }

    [HttpPost("servicios")]
    public async Task<IActionResult> NuevoServicio([FromBody] CrearServicioData input)
    {
        if (!await CsrfOkAsync()) return BadRequest(new { error = "Token de seguridad inválido. Se renovará automáticamente.", code = "csrf_invalid" });
        var user = await current.GetAsync(User);
        if (user is null || user.Rol != EmpresaRoles.AdminEmpresa) return Forbid();
        if (string.IsNullOrWhiteSpace(input.Nombre) || input.Nombre.Length > 150 ||
            input.Precio < 0 || input.Precio > 99999999m || (input.Descripcion?.Length ?? 0) > 500)
            return BadRequest(new { error = "Datos del servicio inválidos." });
        return Ok(new { ok = true, servicio = await service.CrearServicioAsync(user, input) });
    }

    [HttpPost("citas/estado")]
    public async Task<IActionResult> CambiarEstado([FromBody] CitaEstadoData input)
    {
        if (!await CsrfOkAsync()) return BadRequest(new { error = "Token de seguridad inválido. Se renovará automáticamente.", code = "csrf_invalid" });
        var user = await current.GetAsync(User);
        if (user is null || user.Rol is not (EmpresaRoles.AdminEmpresa or EmpresaRoles.EncargadoSucursal))
            return Forbid();
        if (input.IdCita <= 0 || input.Estado is not ("cita_confirmada" or "cita_cancelada" or "cita_completada"))
            return BadRequest(new { error = "Estado de cita inválido." });
        if (!await service.CambiarEstadoCitaAsync(user, input))
            return Conflict(new { error = "Cita fuera de alcance o transición no permitida." });
        return Ok(new { ok = true });
    }

    [HttpPost("accesos")]
    public async Task<IActionResult> CrearAcceso([FromBody] NuevoAccesoData input)
    {
        if (!await CsrfOkAsync()) return BadRequest(new { error = "Token de seguridad inválido. Se renovará automáticamente.", code = "csrf_invalid" });
        var user = await current.GetAsync(User);
        if (user is null || user.Rol != EmpresaRoles.AdminEmpresa) return Forbid();
        if (input.IdEmpleado <= 0 || input.Rol is not (EmpresaRoles.EncargadoSucursal or EmpresaRoles.Empleado) ||
            string.IsNullOrWhiteSpace(input.NombreUsuario) || input.NombreUsuario.Length > 100 ||
            string.IsNullOrWhiteSpace(input.Correo) || input.Correo.Length > 254 || !input.Correo.Contains('@') ||
            !Citicio.Services.PasswordPolicy.Valida(input.Contrasena))
            return BadRequest(new { error = "Datos de acceso incorrectos; contraseña con mayúscula, minúscula, número y símbolo." });
        return Ok(new { ok = true, cuenta = await service.CrearAccesoAsync(user, input) });
    }
}
