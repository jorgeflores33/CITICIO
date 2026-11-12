using Citicio.Auth;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;
[Route("Seguridad/[action]")]
public sealed class SeguridadController(EmailSeguridadService correo, CuentaService cuentas, ILogger<SeguridadController> logger) : Controller
{
    [AllowAnonymous,HttpGet]
    public IActionResult Recuperar() => View(new SolicitudRecuperacionForm());

    [AllowAnonymous,HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Recuperar(SolicitudRecuperacionForm form)
    {
        if (!ModelState.IsValid) return View(form);
        try
        {
            await correo.SolicitarAsync(form.Correo,"recuperar_clave",
                Url.Action(nameof(Restablecer),"Seguridad",values:null,protocol:Request.Scheme)!,HttpContext.RequestAborted);
            TempData["Ok"]="Si el correo está registrado, recibirás instrucciones para recuperar la cuenta.";
            return RedirectToAction("Ingresar","Cuenta");
        }
        catch(Exception ex) when(ex is SqlException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            // La advertencia anterior ocultaba incluso el código de error de SQL/SendGrid.
            // No se registran correos, claves ni tokens de recuperación.
            var sqlCode = (ex as SqlException)?.Number;
            logger.LogWarning("Recuperación fallida. Tipo={Tipo}; SQL={SqlCode}; Motivo={Motivo}",
                ex.GetType().Name, sqlCode,
                ex is InvalidOperationException ? ex.Message : "Problema SQL o de comunicación con SendGrid");
            var development = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
            var mensaje = development && ex is InvalidOperationException
                ? ex.Message : "No fue posible enviar el enlace en este momento. Inténtalo más tarde.";
            ModelState.AddModelError("", mensaje);
            return View(form);
        }
    }

    [AllowAnonymous,HttpGet]
    public IActionResult Restablecer(string? token) =>
        View(new RestablecerContrasenaForm {Token=token??""});

    [AllowAnonymous,HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Restablecer(RestablecerContrasenaForm form)
    {
        if (!ModelState.IsValid || !PasswordPolicy.Valida(form.Contrasena))
        {
            ModelState.AddModelError(nameof(form.Contrasena), PasswordPolicy.Mensaje);
            return View(form);
        }
        try
        {
            // El objeto usuario no afecta al hash v3; el login verifica con PasswordHasher del mismo tipo.
            await correo.RestablecerAsync(form.Token,cuentas.Hash("citicio-reset",form.Contrasena),HttpContext.RequestAborted);
            TempData["Ok"]="Contraseña actualizada. Inicia sesión con la nueva clave.";
            return RedirectToAction("Ingresar","Cuenta");
        }
        catch(Exception ex) when(ex is SqlException or InvalidOperationException)
        {
            logger.LogWarning("Intento de recuperación no válido: {Tipo}",ex.GetType().Name);
            ModelState.AddModelError("","El enlace ha caducado o ya fue utilizado. Solicita uno nuevo.");
            return View(form);
        }
    }

    [Authorize,HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> EnviarVerificacion()
    {
        if(!correo.Configurado){TempData["Error"]="Configura SendGrid para verificar el correo.";return RedirectToAction("Index","Panel");}
        try
        {
            await correo.SolicitarAsync(User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "",
                "verificar_correo",Url.Action(nameof(ConfirmarCorreo),"Seguridad",values:null,protocol:Request.Scheme)!,HttpContext.RequestAborted);
            TempData["Ok"]="Te enviamos un enlace para confirmar tu correo.";
        }
        catch(Exception ex) when(ex is InvalidOperationException or SqlException)
        {
            logger.LogWarning("Fallo enviar verificación: {Tipo}",ex.GetType().Name);
            TempData["Error"]="No se pudo enviar el enlace de verificación.";
        }
        return RedirectToAction("Index",User.IsInRole("admin_citicio")?"AdminGeneral":"Panel");
    }

    [AllowAnonymous,HttpGet]
    public async Task<IActionResult> ConfirmarCorreo(string? token)
    {
        if(string.IsNullOrWhiteSpace(token))return BadRequest();
        try
        {
            await correo.ConfirmarCorreoAsync(token,HttpContext.RequestAborted);
            TempData["Ok"]="Correo verificado. Ya puedes iniciar sesión.";
        }
        catch(Exception ex) when(ex is InvalidOperationException or SqlException)
        {
            logger.LogWarning("Enlace de verificación vencido o inválido: {Tipo}",ex.GetType().Name);
            TempData["Error"]="No se pudo validar el enlace de correo. Solicita uno nuevo.";
        }
        return RedirectToAction("Ingresar","Cuenta");
    }
}
