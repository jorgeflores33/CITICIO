using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;
[Authorize(AuthenticationSchemes="Citicio.Empresa")]
public sealed class EmpresaSuscripcionController(CurrentUserService current,SqlStore db,StripeCheckoutService stripe,
 ILogger<EmpresaSuscripcionController> log):Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user=await current.GetAsync(User); if(user is null)return Forbid();
        var estado=(await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",user.Id)))).FirstOrDefault();
        if(estado is null)return NotFound();
        if (!estado.B("suscripcion_activa") && user.Rol==EmpresaRoles.AdminEmpresa && stripe.Disponible)
        {
            try
            {
                await stripe.ConciliarPagoAnteriorAsync(user.IdEmpresa);
                estado=(await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",user.Id)))).FirstOrDefault();
                if (estado?.B("suscripcion_activa") == true) return Redirect("/empresa/panel");
            }
            catch (Exception ex) when (ex is SqlException or HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                log.LogWarning(ex,"Conciliación automática empresarial pendiente; no se creó otro cargo");
                TempData["Error"]="No se pudo verificar el pago anterior en Stripe o SQL Server. No realices otro pago hasta resolverlo.";
            }
        }
        ViewBag.PuedePagar=user.Rol==EmpresaRoles.AdminEmpresa;
        var problemaStripe=await stripe.DiagnosticoMensualidadAsync();
            ViewBag.StripeDisponible=problemaStripe is null;
            ViewBag.StripeError=problemaStripe;
        return View("~/Views/Suscripcion/Index.cshtml",estado);
    }
    // Permite salir incluso cuando la mensualidad está vencida.
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Salir()
    {
        await HttpContext.SignOutAsync("Citicio.Empresa");
        await HttpContext.SignOutAsync("Citicio.Clientes");
        await HttpContext.SignOutAsync("Cookies");
        return Redirect("/");
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Pagar(SuscripcionPagoForm form)
    {
        var user=await current.GetAsync(User);
        if(user is null || user.Rol!=EmpresaRoles.AdminEmpresa)return Forbid();
        try {return Redirect(await stripe.CrearSuscripcionCheckoutAsync(user.Id,form.RenovacionAutomatica,panelEmpresa:true));}
        catch(Exception ex) when(ex is InvalidOperationException or SqlException or HttpRequestException)
        {log.LogError(ex,"Checkout empresarial fallido. Tipo={Tipo}, SqlNumero={SqlNumero}",ex.GetType().Name,(ex as SqlException)?.Number);
         TempData["Error"] = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
             ? (ex is SqlException sql ? $"Error SQL {sql.Number}: {sql.Message}" : ex.Message)
             : "El cobro no está disponible. Verifica la configuración de Stripe.";
         return RedirectToAction(nameof(Index));}
    }
    // Compatible con la cookie empresarial independiente del panel MVC.
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> VerificarPago()
    {
        var user=await current.GetAsync(User);
        if(user is null || user.Rol!=EmpresaRoles.AdminEmpresa)return Forbid();
        try
        {
            var conciliado=await stripe.ConciliarPagoAnteriorAsync(user.IdEmpresa);
            var estado=(await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",user.Id)))).FirstOrDefault();
            if (conciliado && estado?.B("suscripcion_activa") == true) return Redirect("/empresa/panel");
            TempData["Error"]="La verificación no confirmó una mensualidad vigente. No se realizó otro cargo.";
        }
        catch(Exception ex) when(ex is SqlException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            log.LogError(ex,"Error conciliando pago anterior de empresa");
            TempData["Error"]="No fue posible verificar la mensualidad anterior. Revisa Stripe y SQL Server. No se creó otro cobro.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Resultado(string? resultado=null,string? session_id=null)
    {
        var user=await current.GetAsync(User);
        if (user is null)return Forbid();
        if (resultado=="exito" && user.Rol==EmpresaRoles.AdminEmpresa)
        {
            try
            {
                if (await stripe.ConciliarSesionRetornoAsync(session_id,user.IdEmpresa))
                {
                    var estado=(await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",user.Id)))).FirstOrDefault();
                    if (estado?.B("suscripcion_activa")==true)return Redirect("/empresa/panel");
                }
            }
            catch (Exception ex) when (ex is SqlException or InvalidOperationException or HttpRequestException or TaskCanceledException)
            {
                log.LogWarning(ex,"No se pudo conciliar la sesión empresarial al volver de Stripe");
            }
            return RedirectToAction(nameof(Index));
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarRenovacion()
    {
        var user=await current.GetAsync(User);
        if(user is null || user.Rol!=EmpresaRoles.AdminEmpresa) return Forbid();
        try {
          var estado=(await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",user.Id)))).FirstOrDefault();
          if(estado?.B("suscripcion_activa")!=true)throw new InvalidOperationException("No existe una mensualidad vigente que cancelar.");
          if(estado.B("renovacion_automatica"))
            await stripe.DesactivarRenovacionAsync(user.Id);
          TempData["Ok"]="Sin nuevos cargos automáticos. El periodo ya pagado sigue vigente hasta su fecha de término.";
        }
        catch(Exception ex) when(ex is InvalidOperationException or SqlException or HttpRequestException) {
          log.LogWarning("No se pudo cancelar próxima renovación: {Tipo}",ex.GetType().Name);
          TempData["Error"]="No fue posible cancelar el próximo cargo en Stripe.";}
        return RedirectToAction(nameof(Index));
    }
}
