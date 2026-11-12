using System.Data;
using System.Text.Json;
using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

/// <summary>
/// El encargado puede gestionar exclusivamente a los empleados de su sucursal.
/// No puede editar el rol, trasladarlos a otra sucursal, ni alterar otras cuentas
/// de encargado o administrador. Su perfil personal se modifica en MiPerfil.
/// </summary>
[Authorize(Roles = "encargado_sucursal")]
public sealed class PersonalSucursalController(
    CiticioDb db, SqlStore sql, AsignacionAutomaticaService asignacion,
    ILogger<PersonalSucursalController> log) : Controller
{
    private int Empresa => User.EmpresaId();
    private int Sucursal => User.SucursalId();
    private int Actor => User.UserId();

    private async Task<List<DbRow>> Leer(string texto, params (string, object?)[] extra)
    {
        await using var cn = await sql.OpenAsync();
        var parametros = new List<(string, object?)> { ("empresa", Empresa), ("sucursal", Sucursal) };
        parametros.AddRange(extra);
        return await SqlStore.ReadTextAsync(cn, null, texto, SqlStore.P(parametros.ToArray()));
    }

    // Validación en servidor: cualquier identificador enviado por la página se
    // comprueba otra vez contra la sucursal y la empresa del usuario autenticado.
    private async Task<DbRow?> TrabajadorPermitidoAsync(int empleadoId, bool incluirEncargadoActual = false)
    {
        return (await Leer(@"
            SELECT TOP (1) e.id_empleado, e.nombre, e.apellido_paterno,
                   e.apellido_materno, e.correo, e.telefono, e.id_estado,
                   es.nombre_estado AS estatus, s.nombre AS sucursal,
                   r.nombre_rol AS rol
            FROM dbo.empleados e
            JOIN dbo.sucursales s ON s.id_sucursal=e.id_sucursal_principal AND s.id_empresa=e.id_empresa
            JOIN dbo.estados es ON es.id_estado=e.id_estado
            JOIN dbo.usuarios u ON u.id_empleado=e.id_empleado
            JOIN dbo.roles r ON r.id_rol=u.id_rol
            WHERE e.id_empresa=@empresa AND e.id_sucursal_principal=@sucursal
              AND e.id_empleado=@empleado
              AND (r.nombre_rol='empleado'
                   OR (@incluir_encargado=1 AND r.nombre_rol='encargado_sucursal'
                       AND u.id_usuario=@actor))",
            ("empleado", empleadoId), ("actor", Actor),
            ("incluir_encargado", incluirEncargadoActual ? 1 : 0))).FirstOrDefault();
    }

    private async Task PrepararNuevoAsync()
    {
        ViewBag.Servicios = await Leer(@"
            SELECT sv.id_servicio, sv.nombre FROM dbo.servicios sv
            LEFT JOIN dbo.estados st ON st.id_estado=sv.id_estado
            WHERE sv.id_empresa=@empresa AND ISNULL(st.nombre_estado,'')<>'servicio_inactivo'
              AND EXISTS (SELECT 1 FROM dbo.sucursales_servicios ss
                          WHERE ss.id_sucursal=@sucursal AND ss.id_servicio=sv.id_servicio)
            ORDER BY sv.nombre");
    }

    [HttpGet]
    public async Task<IActionResult> Empleados()
    {
        var rows = await Leer(@"
            SELECT e.id_empleado,e.nombre,e.apellido_paterno,e.apellido_materno,
                   e.correo,e.telefono,s.nombre AS sucursal,es.nombre_estado AS estatus,
                   r.nombre_rol AS rol
            FROM dbo.empleados e
            JOIN dbo.sucursales s ON s.id_sucursal=e.id_sucursal_principal AND s.id_empresa=e.id_empresa
            JOIN dbo.estados es ON es.id_estado=e.id_estado
            LEFT JOIN dbo.usuarios u ON u.id_empleado=e.id_empleado
            LEFT JOIN dbo.roles r ON r.id_rol=u.id_rol
            WHERE e.id_empresa=@empresa AND e.id_sucursal_principal=@sucursal
            ORDER BY e.nombre,e.apellido_paterno");
        ViewBag.EmpleadoActual = User.EmpleadoId();
        return View(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Nuevo()
    {
        await PrepararNuevoAsync();
        return View(new RegistroEmpleadoSucursalForm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Nuevo(RegistroEmpleadoSucursalForm f)
    {
        await PrepararNuevoAsync();
        if (f.Servicios.Count == 0)
            ModelState.AddModelError(nameof(f.Servicios), "Selecciona al menos un servicio que realizará el nuevo empleado.");
        if (!ModelState.IsValid) return View(f);
        var disponibles = ((List<DbRow>)ViewBag.Servicios).Select(x => x.I("id_servicio")).ToHashSet();
        if (f.Servicios.Distinct().Count() != f.Servicios.Count || f.Servicios.Any(x => !disponibles.Contains(x)))
            return Forbid();
        try
        {
            await db.FirstAsync("dbo.sp_citicio_personal_con_servicios", CommandType.StoredProcedure,
                CiticioDb.Param("@id_usuario_actor", Actor), CiticioDb.Param("@id_sucursal", Sucursal),
                CiticioDb.Param("@rol_destino", "empleado"), CiticioDb.Param("@nombre", f.Nombre.Trim()),
                CiticioDb.Param("@apellido_paterno", f.Paterno.Trim()), CiticioDb.Param("@apellido_materno", f.Materno?.Trim()),
                CiticioDb.Param("@telefono", f.Telefono.Trim()), CiticioDb.Param("@correo", f.Correo.Trim().ToLowerInvariant()),
                CiticioDb.Param("@nombre_usuario", f.Correo.Trim().ToLowerInvariant()),
                CiticioDb.Param("@contrasena_hash", new PasswordHasher<object>().HashPassword(new object(), f.Contrasena)),
                CiticioDb.Param("@servicios_json", JsonSerializer.Serialize(f.Servicios)));
            TempData["Ok"] = "Empleado registrado y vinculado únicamente a tu sucursal.";
            return RedirectToAction(nameof(Empleados));
        }
        catch (SqlException ex)
        {
            log.LogWarning(ex, "No se pudo registrar empleado de sucursal");
            ModelState.AddModelError("", ex.Number is 2601 or 2627
                ? "Este correo o teléfono ya está registrado."
                : "La base de datos no autorizó crear el empleado. Comprueba los permisos del encargado.");
            return View(f);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Editar(int id)
    {
        var trabajador = await TrabajadorPermitidoAsync(id);
        if (trabajador is null) return NotFound();
        ViewBag.NombreSucursal = trabajador.S("sucursal");
        return View(new EmpleadoSucursalEditarForm
        {
            Id = id,
            Nombre = trabajador.S("nombre"),
            Paterno = trabajador.S("apellido_paterno"),
            Materno = trabajador.S("apellido_materno"),
            Correo = trabajador.S("correo"),
            Telefono = trabajador.S("telefono"),
            Activo = trabajador.S("estatus").Equals("empleado_activo", StringComparison.OrdinalIgnoreCase)
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(EmpleadoSucursalEditarForm f)
    {
        // No permitir que un encargado cambie su propio rol, ni los datos de
        // otro encargado / administrador por manipulación del formulario.
        var trabajador = await TrabajadorPermitidoAsync(f.Id);
        if (trabajador is null) return NotFound();
        ViewBag.NombreSucursal = trabajador.S("sucursal");
        if (!ModelState.IsValid) return View(f);
        try
        {
            await using var cn = await sql.OpenAsync();
            await SqlStore.ReadTextAsync(cn, null, @"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                IF NOT EXISTS (
                    SELECT 1 FROM dbo.empleados e
                    JOIN dbo.usuarios u ON u.id_empleado=e.id_empleado
                    JOIN dbo.roles r ON r.id_rol=u.id_rol
                    WHERE e.id_empleado=@empleado AND e.id_empresa=@empresa
                      AND e.id_sucursal_principal=@sucursal AND r.nombre_rol='empleado'
                ) THROW 51360, 'Empleado fuera de tu sucursal o rol no permitido.', 1;
                DECLARE @estado INT = (SELECT TOP(1) id_estado FROM dbo.estados
                    WHERE nombre_estado=CASE WHEN @activo=1 THEN 'empleado_activo' ELSE 'empleado_inactivo' END);
                IF @estado IS NULL THROW 51361, 'Falta el estado de empleado en el catálogo.', 1;
                UPDATE u SET correo_acceso=@correo,
                    nombre_usuario=CASE WHEN LOWER(u.nombre_usuario)=LOWER(e.correo)
                                        THEN @correo ELSE u.nombre_usuario END
                FROM dbo.usuarios u
                JOIN dbo.empleados e ON e.id_empleado=u.id_empleado
                JOIN dbo.roles r ON r.id_rol=u.id_rol
                WHERE e.id_empresa=@empresa AND e.id_sucursal_principal=@sucursal
                  AND e.id_empleado=@empleado AND r.nombre_rol='empleado';
                UPDATE e SET nombre=@nombre, apellido_paterno=@paterno,
                    apellido_materno=@materno, telefono=@telefono, correo=@correo, id_estado=@estado
                FROM dbo.empleados e
                WHERE e.id_empresa=@empresa AND e.id_sucursal_principal=@sucursal
                  AND e.id_empleado=@empleado;
                IF @@ROWCOUNT<>1 THROW 51362, 'No se pudo actualizar el empleado.', 1;
                COMMIT TRANSACTION;",
                SqlStore.P(("empresa", Empresa), ("sucursal", Sucursal), ("empleado", f.Id),
                    ("nombre", f.Nombre.Trim()), ("paterno", f.Paterno.Trim()),
                    ("materno", f.Materno?.Trim()), ("correo", f.Correo.Trim().ToLowerInvariant()),
                    ("telefono", f.Telefono.Trim()), ("activo", f.Activo)));
            TempData["Ok"] = "Datos del empleado actualizados correctamente.";
            return RedirectToAction(nameof(Empleados));
        }
        catch (SqlException ex)
        {
            log.LogWarning(ex, "No se pudo editar empleado de sucursal");
            ModelState.AddModelError("", ex.Number is 2601 or 2627
                ? "El correo o teléfono ya está registrado en otra cuenta."
                : "No se pudo guardar el empleado. Comprueba los datos y vuelve a intentarlo.");
            return View(f);
        }
    }

    [HttpGet]
    public async Task<IActionResult> ServiciosEmpleado(int id)
    {
        var trabajador = await TrabajadorPermitidoAsync(id, incluirEncargadoActual: true);
        if (trabajador is null) return NotFound();
        ViewBag.NombreEmpleado = (trabajador.S("nombre") + " " + trabajador.S("apellido_paterno")).Trim();
        ViewBag.NombreSucursal = trabajador.S("sucursal");
        var lista = await Leer(@"
            SELECT s.id_servicio, s.nombre,
                CAST(CASE WHEN es.id_servicio IS NULL THEN 0 ELSE 1 END AS bit) AS asignado
            FROM dbo.servicios s
            LEFT JOIN dbo.estados st ON st.id_estado=s.id_estado
            LEFT JOIN dbo.empleados_servicios es ON es.id_servicio=s.id_servicio AND es.id_empleado=@empleado
            WHERE s.id_empresa=@empresa AND ISNULL(st.nombre_estado,'')<>'servicio_inactivo'
            ORDER BY s.nombre", ("empleado", id));
        ViewBag.Servicios = lista;
        return View(new EmpresaServiciosEmpleadoForm
        {
            EmpleadoId = id,
            Servicios = lista.Where(x => x.B("asignado")).Select(x => x.I("id_servicio")).ToList()
        });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ServiciosEmpleado(EmpresaServiciosEmpleadoForm f)
    {
        if (f.EmpleadoId <= 0 || f.Servicios is null || f.Servicios.Count > 150 ||
            f.Servicios.Any(x => x <= 0) || f.Servicios.Count != f.Servicios.Distinct().Count())
            return BadRequest();
        var trabajador = await TrabajadorPermitidoAsync(f.EmpleadoId, incluirEncargadoActual: true);
        if (trabajador is null) return NotFound();
        try
        {
            await asignacion.GuardarAsync(Actor, Empresa, f.EmpleadoId, f.Servicios,
                HttpContext.RequestAborted, sucursalPermitida: Sucursal);
            TempData["Ok"] = "Servicios guardados. Se habilitan automáticamente en la sucursal del trabajador.";
            return RedirectToAction(nameof(Empleados));
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            log.LogWarning(ex, "No se pudieron asignar servicios al trabajador");
            TempData["Error"] = ex is InvalidOperationException ? ex.Message :
                "SQL Server no pudo guardar las asignaciones. Verifica que el encargado tenga permiso para asignar servicios de su sucursal.";
            return RedirectToAction(nameof(ServiciosEmpleado), new { id = f.EmpleadoId });
        }
    }
}
