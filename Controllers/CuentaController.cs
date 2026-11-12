using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;
[Route("Cuenta/[action]")]
public sealed class CuentaController(CuentaService accounts, EmailSeguridadService seguridad, ILogger<CuentaController> logger):Controller {
    [HttpGet,AllowAnonymous] public IActionResult Ingresar(string? returnUrl=null) {ViewBag.ReturnUrl=returnUrl;return View(new LoginForm());}
    [HttpPost,AllowAnonymous,ValidateAntiForgeryToken,EnableRateLimiting("auth")] public async Task<IActionResult> Ingresar(LoginForm form,string? returnUrl=null) {
        if(!ModelState.IsValid) return View(form);
        try {
            var principal=await accounts.LoginAsync(form.Correo,form.Contrasena);
            if(principal==null) {ModelState.AddModelError("","No fue posible iniciar sesión. Revisa el correo, contraseña o estado de tu cuenta.");return View(form);}
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,principal);
            if(!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
            if(principal.IsInRole("cliente"))return RedirectToAction("Index","Home");
            return RedirectToAction("Index","Panel");
        } catch(SqlException){ModelState.AddModelError("","No fue posible acceder a la base de datos.");return View(form);}
    }
    [HttpGet,AllowAnonymous] public IActionResult Registrarse()=>View(new RegistroForm());
    [HttpPost,AllowAnonymous,ValidateAntiForgeryToken] public async Task<IActionResult> Registrarse(RegistroForm form) {
        if(!ModelState.IsValid) return View(form);
        try {
            await accounts.RegistrarClienteAsync(form);
            TempData["Ok"]="Cuenta creada. Ya puedes iniciar sesión.";
            await AvisarVerificacionAsync(form.Correo);
            return RedirectToAction(nameof(Ingresar));
        }
        catch(Exception e) when(e is SqlException or InvalidOperationException) {ModelState.AddModelError("",e is SqlException?"El teléfono, correo o usuario ya está registrado, o hay un problema de datos.":e.Message);return View(form);}
    }
    [HttpGet,AllowAnonymous] public IActionResult TipoCuenta()=>View();
    [HttpGet,AllowAnonymous] public async Task<IActionResult> RegistrarEmpresa() {
        await PrepararGiros();return View(new RegistroEmpresaForm());
    }
    private async Task PrepararGiros() {
        try { ViewBag.Giros=await accounts.GirosAsync(); }
        catch(SqlException ex) {
            logger.LogWarning("No se pudo consultar giros SQL {Codigo}",ex.Number);
            ViewBag.Giros=new List<Citicio.Data.DbRow>();
            ViewBag.CatalogoGirosNoDisponible=true;
        }
    }
    [HttpPost,AllowAnonymous,ValidateAntiForgeryToken,RequestSizeLimit(4718592)] public async Task<IActionResult> RegistrarEmpresa(RegistroEmpresaForm form) {
        if(!form.AceptaTerminos)ModelState.AddModelError(nameof(form.AceptaTerminos),"Debes aceptar los términos de registro de CITICIO.");
        if(!ModelState.IsValid){await PrepararGiros();return View(form);}
        try {
            await accounts.RegistrarEmpresaAsync(form);
            TempData["Ok"]="Tu empresa está registrada. Inicia sesión y realiza el pago mensual para habilitar las herramientas de CITICIO.";
            await AvisarVerificacionAsync(form.Correo);
            return RedirectToAction(nameof(Ingresar));
        } catch(SqlException ex) {
            logger.LogError(ex, "Registro de empresa falló en SQL Server; código {Codigo}; traza {Traza}",
                ex.Number, HttpContext.TraceIdentifier);
            ModelState.AddModelError("", CuentaService.MensajeErrorRegistro(ex));
        } catch(InvalidOperationException ex) {
            logger.LogWarning(ex, "Registro empresarial no completado; traza {Traza}", HttpContext.TraceIdentifier);
            ModelState.AddModelError("",ex.Message);
        }
        await PrepararGiros();return View(form);
    }
    private async Task AvisarVerificacionAsync(string email) {
        if (!seguridad.Configurado) return;
        try {
            await seguridad.SolicitarAsync(email, "verificar_correo",
                Url.Action("ConfirmarCorreo", "Seguridad", values:null, protocol:Request.Scheme)!);
            TempData["Ok"] = "Cuenta registrada. Revisa tu correo para completar la verificación.";
        }
        catch(Exception ex) when(ex is InvalidOperationException or SqlException) {
            logger.LogWarning("No se pudo enviar verificación tras registro: {Tipo}",ex.GetType().Name);
            TempData["Error"] = "La cuenta se registró, pero el correo de verificación no se pudo enviar. Solicita otro desde tu sesión.";
        }
    }
    [Authorize,HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Salir() {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignOutAsync("Citicio.Empresa");
        await HttpContext.SignOutAsync("Citicio.Clientes");
        return RedirectToAction("Index","Home");
    }
    [AllowAnonymous,HttpGet] public IActionResult SinPermiso()=>View();
}
