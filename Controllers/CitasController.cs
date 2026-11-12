using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
namespace Citicio.Controllers;
[Authorize]
public sealed class CitasController(SqlStore db,BookingService booking,TicketPdfService pdf,IConfiguration config):Controller {
    [Authorize(Roles="cliente"),HttpGet] public async Task<IActionResult> Nueva(int servicioId,int sucursalId) {
        var item=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","servicio_sucursal"),("id1",servicioId),("id2",sucursalId)))).FirstOrDefault();
        if(item==null) return NotFound();
        await using var cn=await db.OpenAsync();
        if(!await SuscripcionAccesoService.PermitidoAsync(cn,null,sucursalId,servicioId))
            return NotFound();
        ViewBag.Servicio=item;
        return View(new ReservaForm{ServicioId=servicioId,SucursalId=sucursalId});
    }
    [Authorize(Roles="cliente"),HttpGet] public async Task<IActionResult> Disponibilidad(int sucursalId,int servicioId,DateTime fecha,string hora) {
        if(!TimeSpan.TryParse(hora,out var t)||fecha.Date+t<=MexicoClock.Now || fecha.Date+t>MexicoClock.Now.AddDays(60)) return Json(Array.Empty<object>());
        try {var list=await booking.DisponibilidadAsync(sucursalId,servicioId,fecha.Date,t);
          return Json(list.Select(x=>new {id=x.I("id_empleado"),nombre=$"{x.S("nombre")} {x.S("apellido_paterno")}",fin=x.D("hora_fin").ToString("HH:mm")}));}
        catch(SqlException){return Json(Array.Empty<object>());}
    }
    [Authorize(Roles="cliente"),HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Nueva(ReservaForm form) {
        if(!ModelState.IsValid) {TempData["Error"]="Verifica los datos de la reserva.";return RedirectToAction("Nueva",new {servicioId=form.ServicioId,sucursalId=form.SucursalId});}
        try {var id=await booking.CrearAsync(User.ClienteId(),form);TempData["Ok"]=$"Cita #{id} registrada correctamente.";return RedirectToAction(nameof(MisCitas));}
        catch(Exception ex) when(ex is SqlException or InvalidOperationException) {TempData["Error"]=ex.Message;return RedirectToAction("Nueva",new {servicioId=form.ServicioId,sucursalId=form.SucursalId});}
    }
    [Authorize(Roles="cliente"),HttpGet]
    public async Task<IActionResult> Comprobante(int id)
    {
        if(id<1)return BadRequest();
        await using var cn=await db.OpenAsync();
        var propietario=(await SqlStore.ReadTextAsync(cn,null,@"
            SELECT TOP(1) c.id_cita FROM dbo.citas c
            WHERE c.id_cita=@id AND c.id_cliente=@cliente",
            SqlStore.P(("id",id),("cliente",User.ClienteId())))).FirstOrDefault();
        if(propietario is null)return NotFound();
        var datos=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","notificacion_detalle"),("id1",id),("id2",0)))).FirstOrDefault();
        if(datos is null)return NotFound();
        if(datos.M("total_cita")>0 && datos.M("pago_confirmado")<datos.M("total_cita"))
            return BadRequest("El ticket de pago estará disponible cuando Stripe confirme el cobro.");
        var baseUrl=config["Notify:PublicBaseUrl"] ?? config["Stripe:PublicBaseUrl"];
        var enlace=!string.IsNullOrWhiteSpace(baseUrl) && Uri.TryCreate(baseUrl,UriKind.Absolute,out var url) &&
            (url.Scheme=="https"||url.IsLoopback)
            ? $"{baseUrl.TrimEnd('/')}/Citas/Comprobante/{id}" : $"https://citicio.invalid/Citas/Comprobante/{id}";
        return File(pdf.Crear(id,datos,enlace),"application/pdf",$"CITICIO-Cita-{id}.pdf");
    }

    [Authorize(Roles="cliente"),HttpGet] public async Task<IActionResult> MisCitas() {
        var rows=await db.CallAsync("dbo.sp_citas_panel_usuario",SqlStore.P(("id_usuario",User.UserId())));
        var pagadas=new Dictionary<int,bool>();
        foreach(var c in rows) {
            var id=c.I("id_cita");
            var estado=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
                ("accion","checkout_cliente"),("id_usuario_actor",User.UserId()),("id1",id)))).FirstOrDefault();
            pagadas[id]=estado is not null && estado.M("total")>0 && estado.M("pagado")>=estado.M("total");
        }
        ViewBag.Pagadas=pagadas;
        return View(rows);
    }
    [Authorize(Roles="cliente,admin_citicio,admin_empresa,encargado_sucursal"),HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstado(int id,string accion) {
        var c=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","cita_para_estado"),("id_usuario_actor",User.UserId()),("id1",id)))).FirstOrDefault();
        if(c==null)return NotFound();
        if(User.IsInRole("cliente") && (c.I("id_cliente")!=User.ClienteId() || accion!="cancelar" || c.D("hora_inicio")<=MexicoClock.Now)) return Forbid();
        if(User.IsInRole("admin_empresa") && c.I("id_empresa")!=User.EmpresaId())return Forbid();
        if(User.IsInRole("encargado_sucursal") && c.I("id_sucursal")!=User.SucursalId())return Forbid();
        if(!new[]{"cancelar","confirmar","completar"}.Contains(accion))return BadRequest();
        var destino=accion switch {"cancelar"=>"cita_cancelada","confirmar"=>"cita_confirmada",_=>"cita_completada"};
        if(c.S("nombre_estado")=="cita_cancelada" || c.S("nombre_estado")=="cita_completada")return BadRequest("La cita ya está finalizada.");
        if(destino=="cita_completada" && c.D("hora_inicio")>MexicoClock.Now)return BadRequest("La cita todavía no inicia.");
        var estado=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(("accion","estado_por_nombre"),("texto",destino)))).First();
        await db.CallAsync("dbo.sp_citas",SqlStore.P(("accion","cambiar_estado"),("id_cita",id),("id_estado",estado.I("id_estado"))));
        TempData["Ok"]="Estado de cita actualizado.";
        return User.IsInRole("cliente")?RedirectToAction(nameof(MisCitas)):RedirectToAction("Citas","Panel");
    }
}
