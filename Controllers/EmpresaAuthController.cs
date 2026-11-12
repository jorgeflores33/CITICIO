using System.Security.Claims;
using Citicio.Auth;
using Citicio.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Citicio.Controllers;

[ApiController]
[Route("api/empresa")]
public sealed class EmpresaAuthController(CurrentUserService current, Citicio.Data.SqlStore sql, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Csrf() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [HttpPost("login"),EnableRateLimiting("auth")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginData input)
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return BadRequest(new { error = "Token de seguridad inválido. Se renovará automáticamente.", code = "csrf_invalid" });
        if (string.IsNullOrWhiteSpace(input.Correo) || string.IsNullOrWhiteSpace(input.Contrasena))
            return BadRequest(new { error = "Ingresa tu correo y contraseña." });
        var user = await current.AuthenticateAsync(input.Correo, input.Contrasena);
        if (user is null) return Unauthorized(new { error = "Credenciales incorrectas o acceso empresarial no habilitado." });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Nombre)
        }, "Citicio.Empresa"));
        await HttpContext.SignInAsync("Citicio.Empresa", principal);
        return Ok(new { ok = true, rol = user.Rol });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return BadRequest(new { error = "Token de seguridad inválido. Se renovará automáticamente.", code = "csrf_invalid" });
        await HttpContext.SignOutAsync("Citicio.Empresa");
        return Ok(new { ok = true });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await current.GetAsync(User);
        if (user is null) return Forbid();
        var mensualidad=(await sql.CallAsync("dbo.sp_citicio_suscripcion_estado",
          Citicio.Data.SqlStore.P(("id_usuario_actor",user.Id)))).FirstOrDefault();
        return Ok(new
        {
            suscripcionActiva=mensualidad?.B("suscripcion_activa")==true,
            urlPago="/EmpresaSuscripcion",
            id_usuario = user.Id, rol = user.Rol, nombre = user.Nombre,
            empresa = user.Empresa, sucursal = user.Sucursal,
            id_empresa = user.IdEmpresa, id_sucursal = user.IdSucursal,
            id_empleado = user.IdEmpleado
        });
    }
}
