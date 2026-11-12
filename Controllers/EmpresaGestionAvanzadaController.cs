using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Citicio.Controllers;

/// <summary>Gestion empresarial de sucursales, servicios y semanas. Ámbito desde cookie.</summary>
[Authorize]
[ApiController]
[Route("api/empresa")]
public sealed class EmpresaGestionAvanzadaController(CurrentUserService usuarios, CiticioDb db,
    IAntiforgery antiforgery, Citicio.Services.AsignacionAutomaticaService asignacion) : ControllerBase
{
    private async Task<CurrentUser?> ActualAsync() => await usuarios.GetAsync(User);
    private async Task<IActionResult?> ComprobarCsrfAsync() => await antiforgery.IsRequestValidAsync(HttpContext)
        ? null : BadRequest(new { code = "csrf_invalid", error = "Token de seguridad inválido. Se renovará automáticamente." });

    private static bool PuedeSucursal(CurrentUser usuario, int sucursal) =>
        usuario.Rol == EmpresaRoles.AdminEmpresa ||
        (usuario.Rol == EmpresaRoles.EncargadoSucursal && usuario.IdSucursal == sucursal);

    [HttpGet("sucursales/{id:int}/editar")]
    public async Task<IActionResult> DetalleSucursal(int id)
    {
        var u = await ActualAsync();
        if (u is null || u.Rol != EmpresaRoles.AdminEmpresa) return Forbid();
        var row = await db.FirstAsync(@"SELECT s.id_sucursal,s.nombre,s.calle,s.numero_exterior,s.numero_interior,
            c.id_colonia,c.codigo_postal,m.id_municipio,e.id_entidad_federativa
            FROM dbo.sucursales s JOIN dbo.colonias c ON c.id_colonia=s.id_colonia
            JOIN dbo.municipios m ON m.id_municipio=c.id_municipio
            JOIN dbo.entidades_federativas e ON e.id_entidad_federativa=m.id_entidad_federativa
            WHERE s.id_sucursal=@id AND s.id_empresa=@empresa",CommandType.Text,
            CiticioDb.Param("@id", id), CiticioDb.Param("@empresa", u.IdEmpresa));
        return row is null ? NotFound(new { error = "Sucursal no encontrada en tu empresa." }) : Ok(row);
    }

    [HttpPut("sucursales/{id:int}")]
    public async Task<IActionResult> EditarSucursal(int id, [FromBody] EditarSucursalRequest input)
    {
        if (await ComprobarCsrfAsync() is { } reject) return reject;
        var u = await ActualAsync();
        if (u is null || u.Rol != EmpresaRoles.AdminEmpresa) return Forbid();
        if (id < 1 || input.IdEstado < 1 || input.IdMunicipio < 1 || input.IdColonia < 1 ||
            string.IsNullOrWhiteSpace(input.Nombre) || input.Nombre.Length > 150 ||
            string.IsNullOrWhiteSpace(input.Calle) || input.Calle.Length > 200 ||
            string.IsNullOrWhiteSpace(input.NumeroExterior) || input.NumeroExterior.Length > 20 ||
            input.NumeroInterior?.Length > 20 ||
            (!string.IsNullOrWhiteSpace(input.CodigoPostal) && !Regex.IsMatch(input.CodigoPostal, @"\A[0-9]{5}\z")))
            return BadRequest(new { error = "Verifica domicilio, CP y nombre de la sucursal." });
        var row = await db.FirstAsync(@"SELECT TOP(1) s.id_estado,s.entre_calle_1,s.entre_calle_2,s.imagen_url
            FROM dbo.sucursales s JOIN dbo.colonias c ON c.id_colonia=@colonia
            JOIN dbo.municipios m ON m.id_municipio=c.id_municipio
            WHERE s.id_sucursal=@id AND s.id_empresa=@empresa AND m.id_municipio=@municipio
              AND m.id_entidad_federativa=@estado AND (@cp IS NULL OR c.codigo_postal=@cp)",CommandType.Text,
            CiticioDb.Param("@id",id),CiticioDb.Param("@empresa",u.IdEmpresa),
            CiticioDb.Param("@colonia",input.IdColonia),CiticioDb.Param("@municipio",input.IdMunicipio),
            CiticioDb.Param("@estado",input.IdEstado),
            CiticioDb.Param("@cp",string.IsNullOrWhiteSpace(input.CodigoPostal)?null:input.CodigoPostal));
        if(row is null) return BadRequest(new { error = "La sucursal no pertenece a tu empresa o el domicilio no coincide." });
        await db.FirstAsync("dbo.sp_sucursales",CommandType.StoredProcedure,
            CiticioDb.Param("@accion","actualizar"),CiticioDb.Param("@id_sucursal",id),
            CiticioDb.Param("@id_empresa",u.IdEmpresa),CiticioDb.Param("@id_estado",row["id_estado"]),
            CiticioDb.Param("@id_colonia",input.IdColonia),CiticioDb.Param("@id_municipio",input.IdMunicipio),
            CiticioDb.Param("@id_entidad_federativa",input.IdEstado),CiticioDb.Param("@nombre",input.Nombre.Trim()),
            CiticioDb.Param("@calle",input.Calle.Trim()),CiticioDb.Param("@numero_exterior",input.NumeroExterior.Trim()),
            CiticioDb.Param("@numero_interior",input.NumeroInterior?.Trim()),
            CiticioDb.Param("@entre_calle_1",row["entre_calle_1"]),CiticioDb.Param("@entre_calle_2",row["entre_calle_2"]));
        return Ok(new { ok = true, mensaje = "Sucursal actualizada correctamente." });
    }

    [HttpGet("sucursales/{id:int}/servicios")]
    public async Task<IActionResult> CatalogoServiciosSucursal(int id)
    {
        var u = await ActualAsync();
        if (u is null || !PuedeSucursal(u,id) || u.Rol==EmpresaRoles.Empleado) return Forbid();
        var rows = await db.QueryAsync(@"SELECT sv.id_servicio,sv.nombre,sv.precio,
            ss.duracion_minutos,CAST(CASE WHEN ss.id_servicio IS NULL THEN 0 ELSE 1 END AS bit) AS asignado
            FROM dbo.servicios sv LEFT JOIN dbo.sucursales_servicios ss ON ss.id_servicio=sv.id_servicio
                AND ss.id_sucursal=@sucursal
            WHERE sv.id_empresa=@empresa
              AND EXISTS(SELECT 1 FROM dbo.sucursales s WHERE s.id_sucursal=@sucursal AND s.id_empresa=@empresa)
            ORDER BY sv.nombre", CommandType.Text, CiticioDb.Param("@sucursal",id),
            CiticioDb.Param("@empresa",u.IdEmpresa));
        return Ok(rows);
    }

    [HttpPost("sucursales/{id:int}/servicios")]
    public async Task<IActionResult> GuardarServiciosSucursal(int id,[FromBody] ServiciosSucursalRequest input)
    {
        if (await ComprobarCsrfAsync() is { } reject) return reject;
        var u = await ActualAsync();
        if (u is null || !PuedeSucursal(u,id) || u.Rol==EmpresaRoles.Empleado) return Forbid();
        if (id<1 || input.Servicios is null || input.Servicios.Count>150 ||
            input.Servicios.Any(x=>x.IdServicio<1 || x.DuracionMinutos is <1 or >720) ||
            input.Servicios.Select(x=>x.IdServicio).Distinct().Count()!=input.Servicios.Count)
            return BadRequest(new { error = "Servicios o duración inválidos." });
        var json=JsonSerializer.Serialize(input.Servicios.Select(s=>new {idServicio=s.IdServicio,duracionMinutos=s.DuracionMinutos}));
        await db.FirstAsync("dbo.sp_citicio_servicios_sucursal_guardar",CommandType.StoredProcedure,
            CiticioDb.Param("@id_usuario_actor",u.Id),CiticioDb.Param("@id_sucursal",id),
            CiticioDb.Param("@servicios_json",json));
        return Ok(new { ok=true,mensaje="Servicios de la sucursal actualizados." });
    }

    [HttpGet("empleados/{id:int}/servicios")]
    public async Task<IActionResult> CatalogoServiciosEmpleado(int id)
    {
        var u=await ActualAsync();
        if(u is null || u.Rol==EmpresaRoles.Empleado) return Forbid();
        var empleado=await db.FirstAsync(@"SELECT e.id_empleado,e.id_sucursal_principal FROM dbo.empleados e
           WHERE e.id_empleado=@id AND e.id_empresa=@empresa AND (@rol='admin_empresa' OR e.id_sucursal_principal=@sucursal)",
           CommandType.Text,CiticioDb.Param("@id",id),CiticioDb.Param("@empresa",u.IdEmpresa),
           CiticioDb.Param("@rol",u.Rol),CiticioDb.Param("@sucursal",u.IdSucursal));
        if(empleado is null)return NotFound(new { error="Empleado fuera de tu ámbito." });
        var rows=await db.QueryAsync(@"SELECT sv.id_servicio,sv.nombre,sv.precio,
           CAST(CASE WHEN es.id_servicio IS NULL THEN 0 ELSE 1 END AS bit) AS asignado,
           CAST(CASE WHEN ss.id_servicio IS NULL THEN 0 ELSE 1 END AS bit) AS disponible_sucursal
           FROM dbo.servicios sv
           LEFT JOIN dbo.empleados_servicios es ON es.id_empleado=@empleado AND es.id_servicio=sv.id_servicio
           LEFT JOIN dbo.sucursales_servicios ss ON ss.id_sucursal=@sucursal AND ss.id_servicio=sv.id_servicio
           WHERE sv.id_empresa=@empresa ORDER BY sv.nombre",CommandType.Text,
           CiticioDb.Param("@empleado",id),CiticioDb.Param("@sucursal",empleado["id_sucursal_principal"]),
           CiticioDb.Param("@empresa",u.IdEmpresa));
        return Ok(new { empleado, servicios=rows });
    }

    [HttpPost("empleados/{id:int}/servicios")]
    public async Task<IActionResult> GuardarServiciosEmpleado(int id,[FromBody] ServiciosEmpleadoRequest input)
    {
        if(await ComprobarCsrfAsync() is { } reject)return reject;
        var u=await ActualAsync();
        if(u is null || u.Rol==EmpresaRoles.Empleado)return Forbid();
        if(id<1 || input.IdServicios is null || input.IdServicios.Count>150 ||
           input.IdServicios.Any(x=>x<1) || input.IdServicios.Distinct().Count()!=input.IdServicios.Count)
           return BadRequest(new { error="Lista de servicios inválida." });
        // El ámbito del encargado se valida antes de sincronizar.
        var empleado = await db.FirstAsync(@"SELECT TOP(1) id_empleado FROM dbo.empleados
            WHERE id_empleado=@empleado AND id_empresa=@empresa
              AND (@rol='admin_empresa' OR id_sucursal_principal=@sucursal)",CommandType.Text,
            CiticioDb.Param("@empleado",id),CiticioDb.Param("@empresa",u.IdEmpresa),
            CiticioDb.Param("@rol",u.Rol),CiticioDb.Param("@sucursal",u.IdSucursal));
        if(empleado is null) return Forbid();
        await asignacion.GuardarAsync(u.Id,u.IdEmpresa,id,input.IdServicios,HttpContext.RequestAborted);
        return Ok(new {ok=true,mensaje="Servicios habilitados en la sucursal del empleado."});
    }

    [HttpGet("horarios/semana")]
    public async Task<IActionResult> ConsultarSemana([FromQuery] string tipo,[FromQuery] int idSucursal,[FromQuery] int? idEmpleado)
    {
        var u=await ActualAsync();
        if(u is null || tipo is not ("sucursal" or "empleado") || idSucursal<1)return BadRequest(new { error="Consulta inválida." });
        var target = idEmpleado.GetValueOrDefault();
        if(u.Rol==EmpresaRoles.Empleado)
        {
            if(tipo!="empleado" || target!=u.IdEmpleado || idSucursal!=u.IdSucursal)return Forbid();
        }
        else if(!PuedeSucursal(u,idSucursal))return Forbid();
        var scope=await db.FirstAsync(@"SELECT id_sucursal FROM dbo.sucursales WHERE id_sucursal=@sucursal AND id_empresa=@empresa",
            CommandType.Text,CiticioDb.Param("@sucursal",idSucursal),CiticioDb.Param("@empresa",u.IdEmpresa));
        if(scope is null)return NotFound();
        if(tipo=="empleado")
        {
           var belongs=await db.FirstAsync(@"SELECT id_empleado FROM dbo.empleados WHERE id_empleado=@empleado
              AND id_empresa=@empresa AND id_sucursal_principal=@sucursal",CommandType.Text,
              CiticioDb.Param("@empleado",target),CiticioDb.Param("@empresa",u.IdEmpresa),
              CiticioDb.Param("@sucursal",idSucursal));
           if(belongs is null)return NotFound();
        }
        return Ok(await db.QueryAsync(tipo=="sucursal"?
            @"SELECT id_dia,CONVERT(VARCHAR(5),hora_apertura,108) AS inicio,CONVERT(VARCHAR(5),hora_cierre,108) AS fin
              FROM dbo.horarios_sucursales WHERE id_sucursal=@sucursal ORDER BY id_dia, inicio":
            @"SELECT id_dia,CONVERT(VARCHAR(5),hora_entrada,108) AS inicio,CONVERT(VARCHAR(5),hora_salida,108) AS fin
              FROM dbo.horarios_empleados WHERE id_sucursal=@sucursal AND id_empleado=@empleado ORDER BY id_dia, inicio",
            CommandType.Text,CiticioDb.Param("@sucursal",idSucursal),
            CiticioDb.Param("@empleado",target)));
    }

    [HttpPut("horarios/semana")]
    public async Task<IActionResult> GuardarSemana([FromBody] HorarioSemanaRequest input)
    {
        if(await ComprobarCsrfAsync() is { } reject)return reject;
        var u=await ActualAsync();
        if(u is null || u.Rol==EmpresaRoles.Empleado)return Forbid();
        if(input.Tipo is not ("sucursal" or "empleado") || input.IdSucursal<1 ||
            input.Dias is null || input.Dias.Count>7 || input.Dias.Any(d=>d.IdDia is <1 or >7 ||
            !TimeOnly.TryParseExact(d.Inicio,"HH:mm",System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,out var inicio) ||
            !TimeOnly.TryParseExact(d.Fin,"HH:mm",System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,out var fin) || fin<=inicio) ||
            input.Dias.Select(d=>d.IdDia).Distinct().Count()!=input.Dias.Count ||
            (input.Tipo=="empleado" && input.IdEmpleado.GetValueOrDefault()<1))
            return BadRequest(new { error="Selecciona hasta siete días con horas válidas. El cierre debe superar la apertura." });
        if(!PuedeSucursal(u,input.IdSucursal))return Forbid();
        var json=JsonSerializer.Serialize(input.Dias.Select(x=>new {idDia=x.IdDia,inicio=x.Inicio,fin=x.Fin}));
        await db.FirstAsync("dbo.sp_citicio_horario_semanal_guardar",CommandType.StoredProcedure,
            CiticioDb.Param("@id_usuario_actor",u.Id),CiticioDb.Param("@tipo",input.Tipo),
            CiticioDb.Param("@id_sucursal",input.IdSucursal),
            CiticioDb.Param("@id_empleado",input.IdEmpleado),CiticioDb.Param("@dias_json",json));
        return Ok(new {ok=true,mensaje="Semana completa guardada correctamente."});
    }
}
