using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

[Authorize(Roles="admin_empresa,encargado_sucursal")]
public sealed class DuracionServiciosController(SqlStore db,ILogger<DuracionServiciosController> log) : Controller
{
    private async Task<List<DbRow>> Leer(string sql,params (string,object?)[] args)
    {
        await using var cn=await db.OpenAsync();
        return await SqlStore.ReadTextAsync(cn,null,sql,SqlStore.P(args));
    }
    private async Task<bool> PrepararAsync(int sucursal)
    {
        ViewBag.Sucursales=await Leer(@"SELECT su.id_sucursal,su.nombre FROM dbo.sucursales su
            JOIN dbo.estados st ON st.id_estado=su.id_estado
            WHERE su.id_empresa=@empresa AND st.nombre_estado='sucursal_activa'
            AND (@admin=1 OR su.id_sucursal=@asignada) ORDER BY su.nombre",
            ("empresa",User.EmpresaId()),("admin",User.IsInRole("admin_empresa")),("asignada",User.SucursalId()));
        var ok=((List<DbRow>)ViewBag.Sucursales).Any(x=>x.I("id_sucursal")==sucursal);
        ViewBag.Servicios=ok?await Leer(@"SELECT ss.id_servicio,sv.nombre,ss.duracion_minutos
            FROM dbo.sucursales_servicios ss JOIN dbo.servicios sv ON sv.id_servicio=ss.id_servicio
            WHERE ss.id_sucursal=@sucursal AND sv.id_empresa=@empresa ORDER BY sv.nombre",
            ("sucursal",sucursal),("empresa",User.EmpresaId())):new List<DbRow>();
        return ok;
    }
    [HttpGet]
    public async Task<IActionResult> Index(int? sucursal=null)
    {
        var branch=sucursal??(User.IsInRole("encargado_sucursal")?User.SucursalId():0);
        await PrepararAsync(branch);
        return View(new DuracionServicioForm{IdSucursal=branch});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar(DuracionServicioForm f)
    {
        if(!await PrepararAsync(f.IdSucursal))return Forbid();
        if(!ModelState.IsValid)return View("Index",f);
        try
        {
            await using var cn=await db.OpenAsync();
            await SqlStore.ReadTextAsync(cn,null,@"
                UPDATE ss SET duracion_minutos=@minutos
                FROM dbo.sucursales_servicios ss
                JOIN dbo.sucursales su ON su.id_sucursal=ss.id_sucursal
                JOIN dbo.servicios sv ON sv.id_servicio=ss.id_servicio
                WHERE ss.id_sucursal=@sucursal AND ss.id_servicio=@servicio
                  AND su.id_empresa=@empresa AND sv.id_empresa=@empresa;
                IF @@ROWCOUNT<>1 THROW 51255,'El servicio no está asignado a esta sucursal.',1;",
                SqlStore.P(("minutos",f.Minutos),("sucursal",f.IdSucursal),
                    ("servicio",f.IdServicio),("empresa",User.EmpresaId())));
            TempData["Ok"]="Duración guardada. Se utilizará para calcular disponibilidad y evitar citas superpuestas.";
            return RedirectToAction(nameof(Index),new{sucursal=f.IdSucursal});
        }
        catch(SqlException ex)
        {
            log.LogWarning(ex,"Duración no guardada");
            ModelState.AddModelError("","El servicio no está habilitado en tu sucursal.");
            return View("Index",f);
        }
    }
}
