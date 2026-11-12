using System.Data;
using System.Globalization;
using System.Text.Json;
using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

[Authorize(Roles="admin_empresa,encargado_sucursal")]
public sealed class HorariosPanelController(SqlStore db, ILogger<HorariosPanelController> log) : Controller
{
    private static readonly string[] Nombres={"Lunes","Martes","Miércoles","Jueves","Viernes","Sábado","Domingo"};
    private async Task<List<DbRow>> Leer(string sql,params (string,object?)[] args)
    {
        await using var cn=await db.OpenAsync();
        return await SqlStore.ReadTextAsync(cn,null,sql,SqlStore.P(args));
    }
    private async Task<bool> PrepararAsync(int sucursal)
    {
        ViewBag.Sucursales=await Leer(@"SELECT s.id_sucursal,s.nombre FROM dbo.sucursales s
            JOIN dbo.estados es ON es.id_estado=s.id_estado WHERE s.id_empresa=@empresa
            AND es.nombre_estado='sucursal_activa' AND (@admin=1 OR s.id_sucursal=@asignada)
            ORDER BY s.nombre",("empresa",User.EmpresaId()),("admin",User.IsInRole("admin_empresa")),("asignada",User.SucursalId()));
        var autorizada=((List<DbRow>)ViewBag.Sucursales).Any(x=>x.I("id_sucursal")==sucursal);
        ViewBag.Empleados=autorizada?await Leer(@"SELECT e.id_empleado,CONCAT(e.nombre,' ',e.apellido_paterno) AS nombre
            FROM dbo.empleados e WHERE e.id_empresa=@empresa AND e.id_sucursal_principal=@sucursal
            ORDER BY e.nombre",("empresa",User.EmpresaId()),("sucursal",sucursal)):new List<DbRow>();
        return autorizada;
    }
    [HttpGet]
    public async Task<IActionResult> Index(string tipo="sucursal",int? sucursal=null,int? empleado=null)
    {
        if(tipo is not ("sucursal" or "empleado"))return BadRequest();
        var suc=sucursal??(User.IsInRole("encargado_sucursal")?User.SucursalId():0);
        await PrepararAsync(suc);
        var f=new HorarioPanelForm {Tipo=tipo,IdSucursal=suc,IdEmpleado=tipo=="empleado"?empleado:null,
           Dias=Enumerable.Range(1,7).Select(n=>new HorarioDiaPanelForm {IdDia=n,Nombre=Nombres[n-1]}).ToList()};
        if(suc>0 && ((List<DbRow>)ViewBag.Sucursales).Any(x=>x.I("id_sucursal")==suc))
        {
            var esEmpleado=tipo=="empleado";
            if(!esEmpleado || (empleado.HasValue && ((List<DbRow>)ViewBag.Empleados).Any(x=>x.I("id_empleado")==empleado.Value)))
            {
                var rows=await Leer(esEmpleado?
                    @"SELECT h.id_dia,CONVERT(varchar(5),h.hora_entrada,108) AS inicio,CONVERT(varchar(5),h.hora_salida,108) AS fin
                      FROM dbo.horarios_empleados h WHERE h.id_sucursal=@sucursal AND h.id_empleado=@empleado":
                    @"SELECT h.id_dia,CONVERT(varchar(5),h.hora_apertura,108) AS inicio,CONVERT(varchar(5),h.hora_cierre,108) AS fin
                      FROM dbo.horarios_sucursales h WHERE h.id_sucursal=@sucursal",
                    ("sucursal",suc),("empleado",empleado));
                foreach(var row in rows)
                {
                    var day=f.Dias.FirstOrDefault(x=>x.IdDia==row.I("id_dia"));
                    if(day!=null){day.Trabaja=true;day.Inicio=row.S("inicio");day.Fin=row.S("fin");}
                }
            }
        }
        return View(f);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Guardar(HorarioPanelForm f)
    {
        var validSucursal=await PrepararAsync(f.IdSucursal);
        if(!validSucursal)return Forbid();
        if(f.Tipo is not ("sucursal" or "empleado") || f.Dias.Count!=7 ||
           f.Dias.Select(x=>x.IdDia).Distinct().Count()!=7 || f.Dias.Any(x=>x.IdDia is <1 or >7))return BadRequest();
        if(f.Tipo=="empleado" && (!f.IdEmpleado.HasValue || !((List<DbRow>)ViewBag.Empleados).Any(x=>x.I("id_empleado")==f.IdEmpleado.Value)))return Forbid();
        var dias=new List<object>();
        foreach(var d in f.Dias.Where(x=>x.Trabaja))
        {
            if(!TimeOnly.TryParseExact(d.Inicio,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var inicio) ||
               !TimeOnly.TryParseExact(d.Fin,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var fin) || fin<=inicio)
            {
                ModelState.AddModelError("","Verifica las horas. Cada cierre debe ser posterior a su apertura.");
                return View("Index",f);
            }
            dias.Add(new { idDia=d.IdDia,inicio=d.Inicio,fin=d.Fin });
        }
        try
        {
            await db.CallAsync("dbo.sp_citicio_horario_semanal_guardar",SqlStore.P(
                 ("id_usuario_actor",User.UserId()),("tipo",f.Tipo),("id_sucursal",f.IdSucursal),
                 ("id_empleado",f.Tipo=="empleado"?f.IdEmpleado:null),("dias_json",JsonSerializer.Serialize(dias))));
            TempData["Ok"]="Horario semanal actualizado. Los días desmarcados quedan sin atención.";
            return RedirectToAction(nameof(Index),new {tipo=f.Tipo,sucursal=f.IdSucursal,empleado=f.IdEmpleado});
        }
        catch(SqlException ex)
        {
            log.LogWarning(ex,"No se pudo guardar horario semanal");
            ModelState.AddModelError("","El horario no es válido para esta sucursal o empleado. Comprueba superposiciones.");
            return View("Index",f);
        }
    }
}
