using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

/// <summary>Perfil público, solo lectura. No muestra empresas sin mensualidad vigente.</summary>
[AllowAnonymous]
public sealed class DirectorioController(SqlStore db, ILogger<DirectorioController> logger) : Controller
{
    [HttpGet("/empresa/{id:int}")]
    public async Task<IActionResult> Empresa(int id)
    {
        if (id <= 0) return NotFound();
        var pars = SqlStore.P(("empresa", id));
        try
        {
            await using var cn = await db.OpenAsync(HttpContext.RequestAborted);
            var principal = (await SqlStore.ReadTextAsync(cn, null, @"
                SELECT TOP(1) em.id_empresa, em.nombre, em.descripcion
                FROM dbo.empresas em
                JOIN dbo.estados st ON st.id_estado=em.id_estado AND st.nombre_estado='empresa_activa'
                WHERE em.id_empresa=@empresa
                  AND EXISTS(SELECT 1 FROM dbo.cobros_empresas ce
                    JOIN dbo.estados es ON es.id_estado=ce.id_estado AND es.nombre_estado='cobro_pagado'
                    WHERE ce.id_empresa=em.id_empresa
                      AND CONVERT(date,GETDATE()) BETWEEN ce.periodo_inicio AND ce.periodo_fin)",
                pars, HttpContext.RequestAborted)).FirstOrDefault();
            if (principal is null) return NotFound();
            var vm = new EmpresaPerfilPublicoVM { Empresa = principal };
            vm.Sucursales = await SqlStore.ReadTextAsync(cn,null,@"
                SELECT s.id_sucursal, s.nombre, s.calle, s.numero_exterior,
                    c.nombre AS colonia, m.nombre AS municipio
                FROM dbo.sucursales s
                JOIN dbo.estados es ON es.id_estado=s.id_estado AND es.nombre_estado='sucursal_activa'
                LEFT JOIN dbo.colonias c ON c.id_colonia=s.id_colonia
                LEFT JOIN dbo.municipios m ON m.id_municipio=c.id_municipio
                WHERE s.id_empresa=@empresa ORDER BY s.nombre",pars,HttpContext.RequestAborted);
            vm.Servicios = await SqlStore.ReadTextAsync(cn,null,@"
                SELECT ss.id_sucursal, sv.id_servicio, sv.nombre AS servicio,
                    sv.descripcion, sv.precio, ss.duracion_minutos, su.nombre AS sucursal
                FROM dbo.sucursales_servicios ss
                JOIN dbo.sucursales su ON su.id_sucursal=ss.id_sucursal AND su.id_empresa=@empresa
                JOIN dbo.estados est ON est.id_estado=su.id_estado AND est.nombre_estado='sucursal_activa'
                JOIN dbo.servicios sv ON sv.id_servicio=ss.id_servicio AND sv.id_empresa=@empresa
                LEFT JOIN dbo.estados esv ON esv.id_estado=sv.id_estado
                WHERE ISNULL(esv.nombre_estado,'')<>'servicio_inactivo'
                  AND EXISTS(SELECT 1 FROM dbo.empleados emp
                      JOIN dbo.empleados_servicios x ON x.id_empleado=emp.id_empleado AND x.id_servicio=sv.id_servicio
                      JOIN dbo.estados ee ON ee.id_estado=emp.id_estado AND ee.nombre_estado='empleado_activo'
                      WHERE emp.id_empresa=@empresa AND emp.id_sucursal_principal=su.id_sucursal)
                ORDER BY sv.nombre, su.nombre",pars,HttpContext.RequestAborted);
            try { vm.Resenas = await SqlStore.ReadTextAsync(cn,null,@"
                SELECT TOP(12) re.calificacion_empresa,re.calificacion_sucursal,
                    re.comentario, re.fecha_resena, su.nombre AS sucursal
                FROM dbo.resenas re
                JOIN dbo.citas ci ON ci.id_cita=re.id_cita
                JOIN dbo.sucursales su ON su.id_sucursal=ci.id_sucursal
                JOIN dbo.estados est ON est.id_estado=ci.id_estado AND est.nombre_estado='cita_completada'
                WHERE su.id_empresa=@empresa ORDER BY re.fecha_resena DESC",pars,HttpContext.RequestAborted); }
            catch(SqlException ex) { logger.LogWarning(ex,"Reseñas temporalmente no disponibles en perfil {Empresa}",id); }
            return View("Perfil",vm);
        }
        catch(SqlException ex)
        {
            logger.LogError(ex,"Error en perfil de empresa {Empresa}",id);
            return StatusCode(503,"El perfil no está disponible temporalmente. Comprueba SQL Server.");
        }
    }
}
