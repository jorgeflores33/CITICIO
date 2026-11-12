using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
namespace Citicio.Controllers;
[Authorize(Roles="cliente")]
public sealed class ClienteController(SqlStore db):Controller {
    [HttpGet] public async Task<IActionResult> Favoritos() {
        var list=await db.CallAsync("dbo.sp_favoritos",SqlStore.P(("accion","listar_por_cliente"),("id_cliente",User.ClienteId())));
        return View(list);
    }
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Favorito(int servicioId,bool agregar=true) {
        if(servicioId<1)return BadRequest();
        try {await db.CallAsync("dbo.sp_favoritos",SqlStore.P(("accion",agregar?"agregar":"quitar"),("id_cliente",User.ClienteId()),("id_servicio",servicioId)));
          TempData["Ok"]=agregar?"Guardado en favoritos.":"Eliminado de favoritos.";}
        catch(SqlException e){TempData["Error"]=e.Message;}
        return Redirect(Request.Headers.Referer.ToString() is var r && Uri.TryCreate(r,UriKind.Absolute,out var uri)
          && uri.Host==Request.Host.Host ? uri.PathAndQuery : Url.Action("Favoritos","Cliente")!);
    }
    [HttpGet] public async Task<IActionResult> Resenas() {
        var rows=await db.CallAsync("dbo.sp_panel_resenas_usuario",SqlStore.P(("id_usuario",User.UserId())));
        return View(rows);
    }
    [HttpGet] public async Task<IActionResult> Reseñar(int id) {
        var c=await CitaPropiaCompletada(id);
        if(!c)return Forbid();
        return View(new ReseñaForm{CitaId=id});
    }
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> Reseñar(ReseñaForm f) {
        if(!await CitaPropiaCompletada(f.CitaId))return Forbid();
        if(!ModelState.IsValid)return View(f);
        try {await db.CallAsync("dbo.sp_resenas",SqlStore.P(("accion","insertar"),("id_cita",f.CitaId),
            ("calificacion_empresa",f.CalificacionEmpresa),("calificacion_sucursal",f.CalificacionSucursal),
            ("comentario",f.Comentario)));
            TempData["Ok"]="Gracias por tu reseña.";return RedirectToAction(nameof(Resenas));}
        catch(SqlException e){ModelState.AddModelError("",e.Message);return View(f);}
    }
    private async Task<bool> CitaPropiaCompletada(int cita) {
        var row=await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
           ("accion","cita_completada_cliente"),("id_usuario_actor",User.UserId()),("id1",cita)));
        return row.Count!=0;
    }
}
