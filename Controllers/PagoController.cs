using Citicio.Auth;
using Citicio.Services;
using Citicio.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
namespace Citicio.Controllers;
public sealed class PagoController(StripeCheckoutService stripe,NotificationWorker avisos,SqlStore db,
    ILogger<PagoController> logger):Controller {
    [Authorize(Roles="cliente"),HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Iniciar(int citaId) {
        try {var url=await stripe.CrearCheckoutAsync(citaId,User.ClienteId(),User.UserId());return Redirect(url);}
        catch(Exception ex){logger.LogWarning(ex,"No se pudo iniciar checkout");TempData["Error"]=ex.Message;return RedirectToAction("MisCitas","Citas");}
    }
    [Authorize(Roles="cliente"),HttpGet]
    public async Task<IActionResult> Resultado(string? resultado=null,int citaId=0) {
        if(citaId>0 && await CitaPagadaAsync(citaId))return View(model:"pagado");
        ViewBag.CitaId=citaId;
        return View(model: resultado??"pendiente");
    }
    [Authorize(Roles="cliente"),HttpGet]
    public async Task<IActionResult> EstadoCita(int citaId) {
        if(citaId<=0)return BadRequest();
        var estado=await LeerEstadoPagoAsync(citaId);
        if(estado is null)return NotFound();
        Response.Headers["Cache-Control"]="no-store";
        return Json(new {pagada=estado.Value.pagada,estado=estado.Value.estado});
    }
    private async Task<bool> CitaPagadaAsync(int citaId) =>
        (await LeerEstadoPagoAsync(citaId))?.pagada == true;
    private async Task<(bool pagada,string estado)?> LeerEstadoPagoAsync(int citaId) {
        var rows=await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","checkout_cliente"),("id_usuario_actor",User.UserId()),("id1",citaId)));
        var c=rows.FirstOrDefault();
        if(c is null)return null;
        return (c.M("total")>0m && c.M("pagado")>=c.M("total"),c.S("nombre_estado"));
    }
    // Único endpoint capaz de confirmar una transacción Stripe. Nunca se usa desde el navegador del cliente.
    [AllowAnonymous,HttpPost,IgnoreAntiforgeryToken,RequestSizeLimit(65536)]
    public async Task<IActionResult> Webhook() {
        using var reader=new StreamReader(Request.Body,Encoding.UTF8);
        var body=await reader.ReadToEndAsync();
        if(!stripe.FirmaValida(body,Request.Headers["Stripe-Signature"].ToString())) return Unauthorized();
        try {await stripe.ConfirmarEventoAsync(body);return Ok();}
        catch(Exception ex){logger.LogError(ex,"Webhook Stripe autenticado no procesado; Stripe debe reintentar");return StatusCode(500);}
    }
    // Webhook independiente de la cuenta CITICIO Citas (no reemplaza /Pago/Webhook).
    [AllowAnonymous,HttpPost,IgnoreAntiforgeryToken,RequestSizeLimit(65536)]
    public async Task<IActionResult> CitasWebhook() {
        using var reader=new StreamReader(Request.Body,Encoding.UTF8);
        var body=await reader.ReadToEndAsync();
        if(!stripe.FirmaCitasValida(body,Request.Headers["Stripe-Signature"].ToString())) return Unauthorized();
        try {await stripe.ConfirmarEventoCitasAsync(body);avisos.ProcesarAhora();return Ok();}
        catch(Exception ex) {logger.LogError(ex,"Webhook de CITICIO Citas sin conciliar; revisar Stripe/SQL");return StatusCode(500);}
    }
    [AllowAnonymous,HttpPost,IgnoreAntiforgeryToken,RequestSizeLimit(65536)]
    public async Task<IActionResult> ConnectWebhook() {
        using var reader=new StreamReader(Request.Body,Encoding.UTF8);
        var body=await reader.ReadToEndAsync();
        if(!stripe.FirmaConectadaValida(body,Request.Headers["Stripe-Signature"].ToString()))return Unauthorized();
        try {await stripe.ConfirmarEventoConectadaAsync(body);return Ok();}
        catch(Exception ex){logger.LogError(ex,"Webhook Connect no procesado; se debe reintentar");return StatusCode(500);}
    }
}
