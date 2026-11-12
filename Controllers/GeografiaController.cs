using Citicio.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citicio.Controllers;

/// <summary>Catálogo público de direcciones de México. Todas las lecturas se realizan con SP.</summary>
[AllowAnonymous]
[Route("Geografia/[action]")]
public sealed class GeografiaController(SqlStore db):Controller {
    [HttpGet]
    public async Task<IActionResult> Estados() {
        var list=await db.CallAsync("dbo.sp_geo_estados");
        return Json(list.Select(x=>new { id=x.I("id"),nombre=x.S("nombre") }));
    }
    [HttpGet]
    public async Task<IActionResult> Municipios(int estadoId) {
        if(estadoId<1)return BadRequest();
        var list=await db.CallAsync("dbo.sp_geo_municipios",SqlStore.P(("id_entidad_federativa",estadoId)));
        return Json(list.Select(x=>new { id=x.I("id"),nombre=x.S("nombre") }));
    }
    [HttpGet]
    public async Task<IActionResult> Colonias(int municipioId,string? cp=null,string? buscar=null) {
        if(municipioId<1)return BadRequest();
        if(cp is not null && cp.Length!=0 && (cp.Length!=5 || cp.Any(c=>!char.IsAsciiDigit(c))))return BadRequest();
        if(buscar?.Length>80)return BadRequest();
        var list=await db.CallAsync("dbo.sp_geo_colonias",SqlStore.P(
            ("id_municipio",municipioId),("cp",string.IsNullOrEmpty(cp)?null:cp),
            ("buscar",string.IsNullOrWhiteSpace(buscar)?null:buscar.Trim())));
        return Json(list.Select(x=>new { id=x.I("id"),nombre=x.S("nombre"),
            cp=x.S("codigo_postal"),tipo=x.S("tipo_asentamiento") }));
    }
    [HttpGet]
    public async Task<IActionResult> CodigoPostal(string? cp) {
        if(cp is null || cp.Length!=5 || cp.Any(c=>!char.IsAsciiDigit(c)))return BadRequest();
        var list=await db.CallAsync("dbo.sp_geo_buscar_cp",SqlStore.P(("codigo_postal",cp)));
        return Json(list.Select(x=>new { id=x.I("id_colonia"),nombre=x.S("nombre"),cp=x.S("codigo_postal"),
            municipio=x.S("municipio"),municipioId=x.I("id_municipio"),
            estado=x.S("estado"),estadoId=x.I("id_entidad_federativa") }));
    }
}
