using Citicio.Data;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

// Vista pública del servicio; se valida vigencia de la empresa y de la sucursal.
[AllowAnonymous]
public sealed class ServicioDetalleController(SqlStore db, ILogger<ServicioDetalleController> log) : Controller
{
    [HttpGet("/servicio/{sucursalId:int}/{servicioId:int}")]
    public async Task<IActionResult> Index(int sucursalId,int servicioId)
    {
        if(sucursalId<=0 || servicioId<=0)return NotFound();
        try
        {
            await using var cn=await db.OpenAsync(HttpContext.RequestAborted);
            if(!await SuscripcionAccesoService.PermitidoAsync(cn,null,sucursalId,servicioId,HttpContext.RequestAborted))
                return NotFound();
            var datos=(await SqlStore.ReadTextAsync(cn,null,@"
                SELECT TOP (1) sv.id_servicio,sv.nombre AS servicio,sv.descripcion,
                    sv.precio,ss.duracion_minutos,su.id_sucursal,su.nombre AS sucursal,
                    em.id_empresa,em.nombre AS empresa,co.nombre AS colonia,
                    mu.nombre AS municipio
                FROM dbo.servicios sv
                JOIN dbo.sucursales_servicios ss ON ss.id_servicio=sv.id_servicio
                JOIN dbo.sucursales su ON su.id_sucursal=ss.id_sucursal
                JOIN dbo.empresas em ON em.id_empresa=su.id_empresa AND em.id_empresa=sv.id_empresa
                LEFT JOIN dbo.colonias co ON co.id_colonia=su.id_colonia
                LEFT JOIN dbo.municipios mu ON mu.id_municipio=co.id_municipio
                WHERE sv.id_servicio=@servicio AND su.id_sucursal=@sucursal
                  AND EXISTS (SELECT 1 FROM dbo.empleados e
                    JOIN dbo.empleados_servicios es ON es.id_empleado=e.id_empleado
                    JOIN dbo.estados estado ON estado.id_estado=e.id_estado
                    WHERE e.id_empresa=em.id_empresa AND e.id_sucursal_principal=su.id_sucursal
                        AND es.id_servicio=sv.id_servicio AND estado.nombre_estado='empleado_activo')",
                SqlStore.P(("sucursal",sucursalId),("servicio",servicioId)),HttpContext.RequestAborted)).FirstOrDefault();
            if(datos is null)return NotFound();
            return View(datos);
        }
        catch(SqlException ex)
        {
            log.LogWarning(ex,"Error consultando servicio publicado");
            return StatusCode(503,"El servicio no está disponible temporalmente.");
        }
    }
}
