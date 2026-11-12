using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

/// <summary>Altas autorizadas del administrador empresarial, siempre con ámbito desde su sesión.</summary>
[Authorize]
[ApiController]
[Route("api/empresa")]
public sealed class EmpresaGestionController(CurrentUserService current, CiticioDb db, SqlStore store, IAntiforgery antiforgery) : ControllerBase
{
    private async Task<IActionResult?> ValidateAdminAsync()
    {
        if (!await antiforgery.IsRequestValidAsync(HttpContext))
            return BadRequest(new { error = "Token de seguridad inválido. Se renovará automáticamente.", code = "csrf_invalid" });
        var user = await current.GetAsync(User);
        if (user is null || user.Rol != EmpresaRoles.AdminEmpresa) return Forbid();
        return null;
    }

    [HttpPost("sucursales")]
    public async Task<IActionResult> CrearSucursal([FromBody] NuevaSucursalRequest input)
    {
        var reject = await ValidateAdminAsync(); if (reject is not null) return reject;
        if (string.IsNullOrWhiteSpace(input.Nombre) || input.Nombre.Length > 150 ||
            input.IdEstado <= 0 || input.IdMunicipio <= 0 || input.IdColonia <= 0 ||
            string.IsNullOrWhiteSpace(input.Calle) || input.Calle.Length > 200 ||
            string.IsNullOrWhiteSpace(input.NumeroExterior) || input.NumeroExterior.Length > 20 ||
            input.NumeroInterior?.Length > 20 ||
            string.IsNullOrWhiteSpace(input.BancoSucursal) || input.BancoSucursal.Length>100 ||
            string.IsNullOrWhiteSpace(input.TitularSucursal) || input.TitularSucursal.Length>200 ||
            !SpeiController.ClabeValida(input.ClabeSucursal) ||
            (input.CodigoPostal is not null && input.CodigoPostal.Length > 0 &&
             !Regex.IsMatch(input.CodigoPostal, @"\A\d{5}\z")))
            return BadRequest(new { error = "Completa correctamente el nombre, estado, municipio, colonia, calle y número." });
        var actor = (await current.GetAsync(User))!;
        // Validar código postal contra la colonia elegida; no se confían IDs del cliente.
        var coincidencia = await db.FirstAsync(@"
            SELECT TOP(1) id_colonia FROM dbo.colonias c JOIN dbo.municipios m ON m.id_municipio=c.id_municipio
            WHERE c.id_colonia=@colonia AND m.id_municipio=@municipio
              AND m.id_entidad_federativa=@estado
              AND (@cp IS NULL OR c.codigo_postal=@cp)", CommandType.Text,
            CiticioDb.Param("@colonia", input.IdColonia), CiticioDb.Param("@municipio", input.IdMunicipio),
            CiticioDb.Param("@estado", input.IdEstado),
            CiticioDb.Param("@cp", string.IsNullOrWhiteSpace(input.CodigoPostal) ? null : input.CodigoPostal));
        if (coincidencia is null) return BadRequest(new { error = "Estado, municipio, colonia y CP no coinciden." });
        // Alta de sucursal y CLABE en UNA transacción; si falla la cuenta, no queda sucursal incompleta.
        await using var conn=await store.OpenAsync();
        await using var tx=(SqlTransaction)await conn.BeginTransactionAsync();
        try {
          var result=(await SqlStore.ReadAsync(conn,tx,"dbo.sp_sucursales",SqlStore.P(
            ("accion","insertar"),("id_empresa",actor.IdEmpresa),("nombre",input.Nombre.Trim()),
            ("id_colonia",input.IdColonia),("id_municipio",input.IdMunicipio),
            ("id_entidad_federativa",input.IdEstado),("calle",input.Calle.Trim()),
            ("numero_exterior",input.NumeroExterior.Trim()),
            ("numero_interior",input.NumeroInterior?.Trim())))).First();
          await SqlStore.ReadAsync(conn,tx,"dbo.sp_citicio_cuenta_sucursal_guardar",SqlStore.P(
            ("id_usuario_actor",actor.Id),("id_sucursal",result.I("id_sucursal")),
            ("nombre_banco",input.BancoSucursal.Trim()),("titular",input.TitularSucursal.Trim()),
            ("clabe",input.ClabeSucursal)));
          await tx.CommitAsync();
          return Ok(new { ok=true,sucursal=result.Cells });
        }catch {
          await tx.RollbackAsync();
          throw;
        }

    }

    [HttpGet("empleados/sin-acceso")]
    public async Task<IActionResult> SinAcceso()
    {
        var actor = await current.GetAsync(User);
        if (actor is null || actor.Rol != EmpresaRoles.AdminEmpresa) return Forbid();
        return Ok(await db.QueryAsync(@"
            SELECT e.id_empleado, CONCAT(e.nombre, ' ',e.apellido_paterno) AS empleado,
               e.correo, s.nombre AS sucursal
            FROM dbo.empleados e LEFT JOIN dbo.sucursales s ON s.id_sucursal=e.id_sucursal_principal
            WHERE e.id_empresa=@empresa AND NOT EXISTS
                (SELECT 1 FROM dbo.usuarios u WHERE u.id_empleado=e.id_empleado)
            ORDER BY e.nombre", CommandType.Text, CiticioDb.Param("@empresa", actor.IdEmpresa)));
    }

    [HttpGet("horarios/catalogos")]
    public async Task<IActionResult> HorariosCatalogos()
    {
        var actor = await current.GetAsync(User);
        if (actor is null || actor.Rol is not (EmpresaRoles.AdminEmpresa or EmpresaRoles.EncargadoSucursal)) return Forbid();
        var sucursales = await db.QueryAsync(@"
            SELECT s.id_sucursal,s.nombre FROM dbo.sucursales s JOIN dbo.estados es ON es.id_estado=s.id_estado
            WHERE s.id_empresa=@empresa AND es.nombre_estado='sucursal_activa'
              AND (@rol='admin_empresa' OR s.id_sucursal=@sucursal)
            ORDER BY s.nombre", CommandType.Text,
            CiticioDb.Param("@empresa", actor.IdEmpresa), CiticioDb.Param("@rol",actor.Rol),
            CiticioDb.Param("@sucursal",actor.IdSucursal));
        var empleados = await db.QueryAsync(@"
            SELECT e.id_empleado,CONCAT(e.nombre,' ',e.apellido_paterno) AS nombre,
                e.id_sucursal_principal AS id_sucursal
            FROM dbo.empleados e JOIN dbo.estados es ON es.id_estado=e.id_estado
            WHERE e.id_empresa=@empresa AND es.nombre_estado='empleado_activo'
              AND (@rol='admin_empresa' OR e.id_sucursal_principal=@sucursal)
            ORDER BY e.nombre", CommandType.Text,
            CiticioDb.Param("@empresa",actor.IdEmpresa),CiticioDb.Param("@rol",actor.Rol),
            CiticioDb.Param("@sucursal",actor.IdSucursal));
        var dias = await db.QueryAsync("SELECT id_dia, nombre FROM dbo.dias_semana ORDER BY id_dia");
        var sucursalHorarios = await db.QueryAsync(@"
            SELECT h.id_sucursal,h.id_dia,CONVERT(VARCHAR(5),h.hora_apertura,108) AS apertura,
                   CONVERT(VARCHAR(5),h.hora_cierre,108) AS cierre
            FROM dbo.horarios_sucursales h JOIN dbo.sucursales s ON s.id_sucursal=h.id_sucursal
            WHERE s.id_empresa=@empresa AND (@rol='admin_empresa' OR s.id_sucursal=@sucursal)
            ORDER BY h.id_sucursal,h.id_dia,h.hora_apertura", CommandType.Text,
            CiticioDb.Param("@empresa",actor.IdEmpresa),CiticioDb.Param("@rol",actor.Rol),
            CiticioDb.Param("@sucursal",actor.IdSucursal));
        return Ok(new { sucursales, empleados, dias, horariosSucursal = sucursalHorarios });
    }

    [HttpPost("horarios/sucursal")]
    public async Task<IActionResult> CrearHorarioSucursal([FromBody] NuevoHorarioSucursalRequest input)
    {
        var reject = await ValidateAdminAsync(); if (reject is not null) return reject;
        if (input.IdSucursal <= 0 || input.IdDia is < 1 or > 7 ||
            !TimeOnly.TryParseExact(input.Apertura, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var apertura) ||
            !TimeOnly.TryParseExact(input.Cierre, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var cierre) || cierre <= apertura)
            return BadRequest(new { error = "Sucursal, día u horas inválidas." });
        var actor = (await current.GetAsync(User))!;
        if (!await SucursalDeEmpresaAsync(input.IdSucursal, actor.IdEmpresa)) return Forbid();
        try
        {
            var result = await db.FirstAsync("dbo.sp_horarios_sucursales", CommandType.StoredProcedure,
                CiticioDb.Param("@accion", "insertar"), CiticioDb.Param("@id_sucursal", input.IdSucursal),
                CiticioDb.Param("@id_dia", input.IdDia), CiticioDb.Param("@hora_apertura", apertura.ToTimeSpan()),
                CiticioDb.Param("@hora_cierre", cierre.ToTimeSpan()));
            return Ok(new { ok = true, horario = result });
        }
        catch (SqlException ex) when (ex.Number >= 50001 && ex.Number <= 50008)
        { return BadRequest(new { error = "Verifica el horario: puede superponerse con otra franja de la sucursal." }); }
    }

    [HttpPost("horarios/empleado")]
    public async Task<IActionResult> CrearHorarioEmpleado([FromBody] NuevoHorarioEmpleadoRequest input)
    {
        var reject = await ValidateAdminAsync(); if (reject is not null) return reject;
        if (input.IdSucursal <= 0 || input.IdEmpleado <= 0 || input.IdDia is < 1 or > 7 ||
            !TimeOnly.TryParseExact(input.Entrada, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var entrada) ||
            !TimeOnly.TryParseExact(input.Salida, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var salida) || salida <= entrada)
            return BadRequest(new { error = "Empleado, sucursal, día u horas inválidas." });
        var actor = (await current.GetAsync(User))!;
        var scope = await db.FirstAsync(@"
            SELECT e.id_empleado FROM dbo.empleados e JOIN dbo.sucursales s ON s.id_empresa=e.id_empresa
            WHERE e.id_empleado=@empleado AND e.id_empresa=@empresa AND s.id_sucursal=@sucursal
              AND e.id_sucursal_principal=@sucursal", CommandType.Text,
              CiticioDb.Param("@empleado", input.IdEmpleado), CiticioDb.Param("@empresa", actor.IdEmpresa),
              CiticioDb.Param("@sucursal", input.IdSucursal));
        if (scope is null) return BadRequest(new { error = "El empleado debe estar asignado a una sucursal de tu empresa." });
        try
        {
            var result = await db.FirstAsync("dbo.sp_horarios_empleados", CommandType.StoredProcedure,
                CiticioDb.Param("@accion", "insertar"), CiticioDb.Param("@id_empleado", input.IdEmpleado),
                CiticioDb.Param("@id_sucursal", input.IdSucursal), CiticioDb.Param("@id_dia", input.IdDia),
                CiticioDb.Param("@hora_entrada", entrada.ToTimeSpan()), CiticioDb.Param("@hora_salida", salida.ToTimeSpan()));
            return Ok(new { ok = true, horario = result });
        }
        catch (SqlException ex) when (ex.Number >= 50001 && ex.Number <= 50011)
        { return BadRequest(new { error = "El horario del empleado debe quedar dentro del horario de la sucursal y no superponerse." }); }
    }

    private async Task<bool> SucursalDeEmpresaAsync(int idSucursal, int idEmpresa) =>
        await db.FirstAsync("SELECT id_sucursal FROM dbo.sucursales WHERE id_sucursal=@sucursal AND id_empresa=@empresa",
            CommandType.Text, CiticioDb.Param("@sucursal", idSucursal), CiticioDb.Param("@empresa", idEmpresa)) is not null;
}
