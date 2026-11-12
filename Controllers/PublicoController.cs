using System.Data;
using Citicio.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Citicio.Services;

namespace Citicio.Controllers;

/// <summary>Portal público CITICIO; independiente del panel empresarial y de sus permisos.</summary>
[AllowAnonymous]
public sealed class PublicoController(CiticioDb db, SqlStore store, Citicio.Services.ImagenEmpresaService imagenes, ILogger<PublicoController> logger) : Controller
{
    // La entrada del portal público y el menú MVC usan ahora un único catálogo.
    [HttpGet("/empresas-servicios")]
    public IActionResult Catalogo([FromQuery] string? texto = null, [FromQuery] int? idGiro = null)
        => RedirectToAction("Empresas","Home",new { buscar = texto, giro = idGiro });

    [HttpGet("/top10")]
    public IActionResult Top10() => View("Top10");

    [HttpGet("/registrar-empresa")]
    public IActionResult RegistrarEmpresa() => View("RegistrarEmpresa");

    [HttpGet("/crear-cuenta")]
    [HttpGet("/registro-publico/cliente")]
    public IActionResult CrearCuenta() => View("CrearCuenta");

    [HttpGet("/iniciar-sesion")]
    public IActionResult IniciarSesion() => Redirect("/Cuenta/Ingresar");

    [HttpGet("/api/publico/catalogo")]
    public async Task<IActionResult> Servicios([FromQuery] string? texto, [FromQuery] int? idGiro)
    {
        if (texto?.Length > 150 || idGiro < 1) return BadRequest(new { error = "Filtro inválido." });
        try
        {
            var rows = await db.QueryAsync("dbo.sp_citicio_catalogo_publicado", CommandType.StoredProcedure,
                CiticioDb.Param("@texto", null),
                CiticioDb.Param("@id_giro", idGiro));
            return Ok(rows.Where(r=>BusquedaFlexible.Coincide(texto,
                Convert.ToString(r.GetValueOrDefault("empresa")), Convert.ToString(r.GetValueOrDefault("servicio")),
                Convert.ToString(r.GetValueOrDefault("sucursal")), Convert.ToString(r.GetValueOrDefault("giro"))))
                .Take(100).ToList());
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "No se pudo consultar el catálogo público");
            return StatusCode(503, new { error = "No se pudo cargar el catálogo desde SQL Server. Comprueba la conexión y el ajuste SQL/03 de publicaciones." });
        }
    }

    [HttpGet("/api/publico/top10")]
    public async Task<IActionResult> Ranking([FromQuery] int? idMunicipio)
    {
        if (idMunicipio < 1) return BadRequest(new { error = "Municipio inválido." });
        try
        {
            await using var cn = await store.OpenAsync(HttpContext.RequestAborted);
            return Ok((await RankingEmpresasService.ObtenerAsync(cn,HttpContext.RequestAborted,idMunicipio))
                .Select(x=>x.Cells).ToList());
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "No se pudo consultar Top 10");
            return StatusCode(503, new { error = "No se pudo cargar Top 10. Comprueba SQL Server e instala el ajuste SQL/03 de publicaciones." });
        }
    }

    [HttpGet("/api/publico/giros")]
    public async Task<IActionResult> Giros()
    {
        try
        {
            return Ok(await db.QueryAsync("SELECT id_giro, nombre_giro FROM dbo.giros ORDER BY nombre_giro"));
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "No se pudo consultar el catálogo de giros");
            return StatusCode(503, new { error = "No fue posible consultar los giros desde SQL Server." });
        }
    }

    // Catálogos nacionales. Solo se devuelven los IDs reales de SQL Server.
    [HttpGet("/api/publico/geo/estados")]
    public Task<IActionResult> Estados() => GeoAsync("dbo.sp_geo_estados");

    [HttpGet("/api/publico/geo/municipios")]
    public Task<IActionResult> Municipios([FromQuery] int idEstado) => idEstado <= 0
        ? Task.FromResult<IActionResult>(BadRequest(new { error = "Selecciona un estado válido." }))
        : GeoAsync("dbo.sp_geo_municipios", CiticioDb.Param("@id_entidad_federativa", idEstado));

    [HttpGet("/api/publico/geo/colonias")]
    public Task<IActionResult> Colonias([FromQuery] int idMunicipio, [FromQuery] string? cp = null) =>
        idMunicipio <= 0 || (cp is not null && !System.Text.RegularExpressions.Regex.IsMatch(cp, @"\A[0-9]{0,5}\z"))
        ? Task.FromResult<IActionResult>(BadRequest(new { error = "Municipio o código postal inválido." }))
        : GeoAsync("dbo.sp_geo_colonias", CiticioDb.Param("@id_municipio", idMunicipio),
            CiticioDb.Param("@cp", string.IsNullOrWhiteSpace(cp) ? null : cp));

    private async Task<IActionResult> GeoAsync(string sp, params SqlParameter[] parameters)
    {
        try { return Ok(await db.QueryAsync(sp, CommandType.StoredProcedure, parameters)); }
        catch (SqlException ex)
        {
            logger.LogError(ex, "Error consultando catálogo geográfico: {Sp}", sp);
            return StatusCode(503, new { error = "No se pudo consultar el catálogo geográfico de agencia_citas. Ejecuta los scripts e importa los municipios y colonias de SEPOMEX." });
        }
    }

    [HttpGet("/api/publico/empresa/{id:int}/logo")]
    public async Task<IActionResult> Logo(int id)
    {
        if (id <= 0) return NotFound();
        try
        {
            var logo = await imagenes.ObtenerLogoPublicadoAsync(id);
            if (logo is null) return NotFound();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            Response.Headers["Cache-Control"] = "public, max-age=3600";
            return File(logo.Value.Bytes, logo.Value.Mime);
        }
        catch (SqlException ex)
        {
            logger.LogError(ex, "No se pudo cargar logotipo desde SQL Server");
            return StatusCode(503);
        }
    }
}
