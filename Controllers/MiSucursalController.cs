using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

[Authorize(Roles="encargado_sucursal")]
public sealed class MiSucursalController(SqlStore db,ILogger<MiSucursalController> log) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        await using var cn=await db.OpenAsync();
        var r=(await SqlStore.ReadTextAsync(cn,null,@"
            SELECT TOP(1) s.nombre,s.calle,s.numero_exterior,s.telefono,s.entre_calle_1,s.entre_calle_2
            FROM dbo.sucursales s WHERE s.id_sucursal=@sucursal AND s.id_empresa=@empresa",
            SqlStore.P(("sucursal",User.SucursalId()),("empresa",User.EmpresaId())))).FirstOrDefault();
        if(r is null)return NotFound();
        return View(new MiSucursalForm{Nombre=r.S("nombre"),Calle=r.S("calle"),NumeroExterior=r.S("numero_exterior"),
             Telefono=r.S("telefono"),EntreCalle1=r.S("entre_calle_1"),EntreCalle2=r.S("entre_calle_2")});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(MiSucursalForm f)
    {
        if(!ModelState.IsValid)return View(f);
        try
        {
            await using var cn=await db.OpenAsync();
            await SqlStore.ReadTextAsync(cn,null,@"
                UPDATE dbo.sucursales SET nombre=@nombre,calle=@calle,numero_exterior=@numero,
                    telefono=@telefono,entre_calle_1=@entre1,entre_calle_2=@entre2
                WHERE id_sucursal=@sucursal AND id_empresa=@empresa;
                IF @@ROWCOUNT<>1 THROW 51250,'La sucursal no pertenece al encargado.',1;",
                SqlStore.P(("sucursal",User.SucursalId()),("empresa",User.EmpresaId()),
                     ("nombre",f.Nombre.Trim()),("calle",f.Calle.Trim()),("numero",f.NumeroExterior.Trim()),
                     ("telefono",f.Telefono?.Trim()),("entre1",f.EntreCalle1?.Trim()),("entre2",f.EntreCalle2?.Trim())));
            TempData["Ok"]="Datos de tu sucursal actualizados.";
            return RedirectToAction(nameof(Index));
        }
        catch(SqlException ex)
        {
            log.LogWarning(ex,"Sucursal no actualizada");
            ModelState.AddModelError("","No pudimos guardar los datos de la sucursal.");
            return View(f);
        }
    }
}
