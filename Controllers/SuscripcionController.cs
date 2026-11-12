using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;
[Authorize]
public sealed class SuscripcionController(SqlStore db, StripeCheckoutService stripe,ILogger<SuscripcionController> log):Controller
{
    [Authorize(Roles="admin_empresa,encargado_sucursal,empleado"),HttpGet]
    public async Task<IActionResult> Index()
    {
        try {
            var estado=(await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",User.UserId())))).FirstOrDefault();
            if(estado is null)return NotFound();
            if (!estado.B("suscripcion_activa") && User.IsInRole("admin_empresa") && stripe.Disponible)
            {
                try
                {
                    await stripe.ConciliarPagoAnteriorAsync(User.EmpresaId());
                    estado = (await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",User.UserId())))).FirstOrDefault();
                    if (estado?.B("suscripcion_activa") == true) return RedirectToAction("Index", "Panel");
                }
                catch (Exception ex) when (ex is SqlException or HttpRequestException or InvalidOperationException or TaskCanceledException)
                {
                    log.LogWarning(ex,"Conciliación automática de mensualidad pendiente; no se ha generado otro cargo");
                    TempData["Error"] = "No se pudo verificar el pago anterior en Stripe o SQL Server. No realices otro pago hasta resolverlo.";
                }
            }
            ViewBag.PuedePagar=User.IsInRole("admin_empresa");
            var problemaStripe=await stripe.DiagnosticoMensualidadAsync();
            ViewBag.StripeDisponible=problemaStripe is null;
            ViewBag.StripeError=problemaStripe;
            return View(estado);
        }catch(SqlException ex){log.LogError(ex,"No se pudo consultar la suscripción");return StatusCode(503,"No se puede verificar la suscripción. Revisa SQL Server y la migración 11.");}
    }

    [Authorize(Roles="admin_empresa"),HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Pagar(SuscripcionPagoForm form)
    {
        try { return Redirect(await stripe.CrearSuscripcionCheckoutAsync(User.UserId(),form.RenovacionAutomatica)); }
        catch(Exception ex) when (ex is SqlException or InvalidOperationException or HttpRequestException)
        {log.LogError(ex,"Checkout mensualidad fallido. Tipo={Tipo}, SqlNumero={SqlNumero}",ex.GetType().Name,(ex as SqlException)?.Number);
         TempData["Error"] = HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment()
             ? (ex is SqlException sql ? $"Error SQL {sql.Number}: {sql.Message}" : ex.Message)
             : "No se pudo iniciar el cobro. Revisa la configuración de Stripe y la tarifa.";
         return RedirectToAction(nameof(Index));}
    }
    // Verifica el pago anterior contra Stripe + la empresa de la sesión,
    // sin abrir otra sesión de Checkout ni generar un nuevo cargo.
    [Authorize(Roles="admin_empresa"),HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> VerificarPago()
    {
        try
        {
            var conciliado=await stripe.ConciliarPagoAnteriorAsync(User.EmpresaId());
            var estado = (await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",User.UserId())))).FirstOrDefault();
            if (conciliado && estado?.B("suscripcion_activa") == true) return RedirectToAction("Index", "Panel");
            TempData["Error"]="La verificación no confirmó una mensualidad vigente. No se realizó otro cargo.";
        }
        catch(Exception ex) when(ex is SqlException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            log.LogError(ex,"Error conciliando mensualidad anterior");
            TempData["Error"]="No fue posible verificar el cobro anterior. Revisa los registros de Stripe y SQL Server. No se creó un pago nuevo.";
        }
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles="admin_empresa"),HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelarRenovacion()
    {
        try {
          var estado=(await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",User.UserId())))).FirstOrDefault();
          if(estado?.B("suscripcion_activa")!=true)throw new InvalidOperationException("No existe una mensualidad vigente que cancelar.");
          if (estado.B("renovacion_automatica"))
            await stripe.DesactivarRenovacionAsync(User.UserId());
          TempData["Ok"]="No habrá nuevos cargos automáticos. El periodo ya pagado continúa vigente hasta su vencimiento.";
        }
        catch(Exception ex) when(ex is InvalidOperationException or SqlException or HttpRequestException) {
          log.LogWarning("No se pudo cancelar renovación: {Tipo}",ex.GetType().Name);
          TempData["Error"]="No fue posible desactivar el siguiente cargo; revisa Stripe.";}
        return RedirectToAction(nameof(Index));
    }
    // No habilitar la empresa solo por el texto resultado=exito: comprobar SIEMPRE Stripe + SQL.
    [AllowAnonymous,HttpGet]
    public async Task<IActionResult> Resultado(string? resultado=null,string? session_id=null)
    {
        if (resultado == "exito" && User.Identity?.IsAuthenticated == true && User.IsInRole("admin_empresa"))
        {
            try
            {
                if (await stripe.ConciliarSesionRetornoAsync(session_id, User.EmpresaId()))
                {
                    var estado = (await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(("id_usuario_actor",User.UserId())))).FirstOrDefault();
                    if (estado?.B("suscripcion_activa") == true) return RedirectToAction("Index", "Panel");
                }
            }
            catch (Exception ex) when (ex is SqlException or HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                log.LogWarning(ex,"No se pudo conciliar Checkout al regresar de Stripe");
            }
            // Index reintenta la conciliación si no llegó el webhook; sin generar un cargo nuevo.
            return RedirectToAction(nameof(Index));
        }
        return View(model:resultado??"pendiente");
    }
    [Authorize(Roles="admin_citicio"),HttpGet]
    public async Task<IActionResult> Configuracion()
    {
        var r=(await db.CallAsync("dbo.sp_citicio_suscripcion_configuracion",SqlStore.P(
            ("id_usuario_actor",User.UserId()),("accion","obtener")))).FirstOrDefault();
        return View(new ConfiguracionMensualForm { TarifaMensual=r?.M("tarifa_mensual")??0,
            BancoPersonal=r?.S("banco_personal")??"",TitularPersonal=r?.S("titular_personal")??"",
            ClabeEnmascarada=r?.S("clabe_enmascarada")});
    }
    [Authorize(Roles="admin_citicio"),HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Configuracion(ConfiguracionMensualForm form)
    {
        if(!SpeiController.ClabeValida(form.ClabePersonal)) ModelState.AddModelError(nameof(form.ClabePersonal),"CLABE inválida; revisa su dígito de control.");
        if(!ModelState.IsValid)return View(form);
        try {
            await db.CallAsync("dbo.sp_citicio_suscripcion_configuracion",SqlStore.P(
               ("id_usuario_actor",User.UserId()),("accion","guardar"),
               ("tarifa_mensual",form.TarifaMensual),("banco_personal",form.BancoPersonal.Trim()),
               ("titular_personal",form.TitularPersonal.Trim()),("clabe_personal",form.ClabePersonal)));
            TempData["Ok"]="Tarifa mensual y cuenta receptora guardadas. Verifica la cuenta en Stripe antes de cobrar.";
            return RedirectToAction(nameof(Configuracion));
        }catch(SqlException ex){log.LogError("Error SQL configurando suscripción: {Numero}",ex.Number);
          ModelState.AddModelError("","No se pudo guardar la configuración.");return View(form);}
    }
}
