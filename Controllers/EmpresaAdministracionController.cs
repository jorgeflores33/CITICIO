using System.Text.Json;
using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

/// <summary>
/// Gestión exclusiva de la empresa autenticada. Toda lectura y escritura usa
/// User.EmpresaId(); los IDs que llegan del navegador se comprueban de nuevo.
/// Los registros con historial se desactivan, nunca se borran físicamente.
/// </summary>
[Authorize(Roles = "admin_empresa")]
public sealed class EmpresaAdministracionController(SqlStore db, Citicio.Services.AsignacionAutomaticaService asignacion, ILogger<EmpresaAdministracionController> logger) : Controller
{
    private int Empresa => User.EmpresaId();
    private int Usuario => User.UserId();

    private async Task<List<DbRow>> Leer(string sql, params (string, object?)[] parameters)
    {
        await using var conn = await db.OpenAsync();
        return await SqlStore.ReadTextAsync(conn, null, sql, SqlStore.P(parameters));
    }

    private async Task Ejecutar(string sql, params (string, object?)[] parameters)
    {
        await using var conn = await db.OpenAsync();
        await SqlStore.ReadTextAsync(conn, null, sql, SqlStore.P(parameters));
    }

    [HttpGet]
    public async Task<IActionResult> Servicios()
    {
        var filas = await Leer(@"
            SELECT s.id_servicio,s.nombre,s.descripcion,s.precio,
                   ISNULL(e.nombre_estado,'') AS estatus
            FROM dbo.servicios s
            LEFT JOIN dbo.estados e ON e.id_estado = s.id_estado
            WHERE s.id_empresa=@empresa AND ISNULL(e.nombre_estado,'') <> 'servicio_inactivo'
            ORDER BY s.nombre", ("empresa", Empresa));
        return View(filas);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DesactivarServicio(int id)
    {
        try
        {
            await Ejecutar(@"
                DECLARE @estado int=(SELECT TOP(1) id_estado FROM dbo.estados WHERE nombre_estado='servicio_inactivo');
                IF @estado IS NULL THROW 51001, 'Falta servicio_inactivo en dbo.estados. Ejecuta la migración SQL.', 1;
                UPDATE dbo.servicios SET id_estado=@estado
                WHERE id_servicio=@id AND id_empresa=@empresa;
                IF @@ROWCOUNT=0 THROW 51002, 'Servicio fuera de tu empresa o inexistente.', 1;",
                ("id", id), ("empresa", Empresa));
            TempData["Ok"] = "Servicio desactivado. Su historial de citas se conserva.";
        }
        catch (SqlException ex) { logger.LogWarning(ex,"Baja de servicio fallida"); TempData["Error"]="No fue posible desactivar el servicio: "+ex.Message; }
        return RedirectToAction(nameof(Servicios));
    }

    [HttpGet]
    public async Task<IActionResult> Empleados()
    {
        var filas = await Leer(@"
            SELECT e.id_empleado,e.nombre,e.apellido_paterno,e.apellido_materno,e.correo,e.telefono,
                   ISNULL(s.nombre,'Sin sucursal') AS sucursal, ISNULL(st.nombre_estado,'') AS estatus,
                   ISNULL(rol.nombre_rol,'') AS rol
            FROM dbo.empleados e
            LEFT JOIN dbo.sucursales s ON s.id_sucursal=e.id_sucursal_principal AND s.id_empresa=e.id_empresa
            LEFT JOIN dbo.estados st ON st.id_estado=e.id_estado
            LEFT JOIN dbo.usuarios u ON u.id_empleado=e.id_empleado
            LEFT JOIN dbo.roles rol ON rol.id_rol=u.id_rol
            WHERE e.id_empresa=@empresa
            ORDER BY e.nombre,e.apellido_paterno", ("empresa", Empresa));
        return View(filas);
    }

    private async Task PrepararSucursales()
    {
        ViewBag.Sucursales = await Leer(@"
            SELECT s.id_sucursal,s.nombre FROM dbo.sucursales s
            JOIN dbo.estados st ON st.id_estado=s.id_estado
            WHERE s.id_empresa=@empresa AND st.nombre_estado='sucursal_activa'
            ORDER BY s.nombre", ("empresa", Empresa));
    }

    [HttpGet]
    public async Task<IActionResult> EditarEmpleado(int id)
    {
        var filas = await Leer(@"
            SELECT TOP(1) e.id_empleado,e.nombre,e.apellido_paterno,e.apellido_materno,
                e.correo,e.telefono,e.id_sucursal_principal,st.nombre_estado
            FROM dbo.empleados e JOIN dbo.estados st ON st.id_estado=e.id_estado
            WHERE e.id_empresa=@empresa AND e.id_empleado=@id
              AND EXISTS (SELECT 1 FROM dbo.usuarios u JOIN dbo.roles r ON r.id_rol=u.id_rol
                          WHERE u.id_empleado=e.id_empleado AND r.nombre_rol IN ('empleado','encargado_sucursal'))",
            ("empresa",Empresa),("id",id));
        var r=filas.FirstOrDefault();
        if(r is null) return NotFound();
        await PrepararSucursales();
        return View(new EmpresaEmpleadoEditarForm { Id=id, Nombre=r.S("nombre"),Paterno=r.S("apellido_paterno"),
            Materno=r.S("apellido_materno"),Correo=r.S("correo"),Telefono=r.S("telefono"),
            SucursalId=r.I("id_sucursal_principal")>0 ? r.I("id_sucursal_principal") : null,Activo=r.S("nombre_estado")=="empleado_activo" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarEmpleado(EmpresaEmpleadoEditarForm f)
    {
        await PrepararSucursales();
        if(!ModelState.IsValid) return View(f);
        try
        {
            await Ejecutar(@"
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;
                IF NOT EXISTS (SELECT 1 FROM dbo.empleados e JOIN dbo.usuarios u ON u.id_empleado=e.id_empleado
                  JOIN dbo.roles r ON r.id_rol=u.id_rol
                  WHERE e.id_empleado=@id AND e.id_empresa=@empresa AND r.nombre_rol IN ('empleado','encargado_sucursal'))
                  THROW 51003, 'No puedes modificar este empleado.', 1;
                IF @sucursal IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.sucursales WHERE id_sucursal=@sucursal AND id_empresa=@empresa)
                  THROW 51004, 'La sucursal no pertenece a tu empresa.', 1;
                IF @sucursal IS NULL AND EXISTS (SELECT 1 FROM dbo.usuarios u
                    JOIN dbo.roles r ON r.id_rol=u.id_rol WHERE u.id_empleado=@id AND r.nombre_rol='encargado_sucursal')
                    THROW 51012, 'El encargado necesita una sucursal.', 1;
                DECLARE @estado int=(SELECT TOP(1) id_estado FROM dbo.estados
                    WHERE nombre_estado=CASE WHEN @activo=1 THEN 'empleado_activo' ELSE 'empleado_inactivo' END);
                IF @estado IS NULL THROW 51005, 'Falta el estado empleado_inactivo; ejecuta la migración.', 1;
                UPDATE u SET correo_acceso=@correo,
                    nombre_usuario=CASE WHEN LOWER(u.nombre_usuario)=LOWER(e.correo) THEN @correo ELSE u.nombre_usuario END
                FROM dbo.usuarios u JOIN dbo.empleados e ON e.id_empleado=u.id_empleado
                WHERE e.id_empresa=@empresa AND e.id_empleado=@id;
                UPDATE dbo.empleados
                SET nombre=@nombre,apellido_paterno=@paterno,apellido_materno=@materno,telefono=@telefono,
                    correo=@correo,id_sucursal_principal=@sucursal,id_estado=@estado
                WHERE id_empresa=@empresa AND id_empleado=@id;
                COMMIT TRANSACTION;",
                ("id",f.Id),("empresa",Empresa),("sucursal",f.SucursalId),("activo",f.Activo),
                ("nombre",f.Nombre.Trim()),("paterno",f.Paterno.Trim()),("materno",f.Materno?.Trim()),
                ("telefono",f.Telefono.Trim()),("correo",f.Correo.Trim().ToLowerInvariant()));
            try { await asignacion.SincronizarSucursalAsync(Usuario,Empresa,f.Id,HttpContext.RequestAborted); }
            catch (Exception syncEx) when (syncEx is SqlException or InvalidOperationException) {
                logger.LogWarning(syncEx,"No se pudo sincronizar el catálogo de la nueva sucursal");
                TempData["Error"]="Empleado guardado; revisa sus servicios, porque no fue posible habilitarlos en su nueva sucursal.";
            }
            TempData["Ok"]="Empleado actualizado correctamente.";
            return RedirectToAction(nameof(Empleados));
        }
        catch(SqlException ex)
        {
            logger.LogWarning(ex,"Edición de empleado fallida");
            ModelState.AddModelError("", "No fue posible actualizar el empleado. Verifica correo, sucursal y estado.");
            return View(f);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DesactivarEmpleado(int id)
    {
        try
        {
            await Ejecutar(@"
                DECLARE @estado int=(SELECT TOP(1) id_estado FROM dbo.estados WHERE nombre_estado='empleado_inactivo');
                IF @estado IS NULL THROW 51005, 'Falta el estado empleado_inactivo.', 1;
                UPDATE e SET id_estado=@estado
                FROM dbo.empleados e
                WHERE e.id_empresa=@empresa AND e.id_empleado=@id AND @id <> @usuarioEmpleado
                  AND EXISTS (SELECT 1 FROM dbo.usuarios u JOIN dbo.roles r ON r.id_rol=u.id_rol
                     WHERE u.id_empleado=e.id_empleado AND r.nombre_rol IN ('empleado','encargado_sucursal'));
                IF @@ROWCOUNT=0 THROW 51006, 'No puedes desactivar este usuario.', 1;",
                ("empresa",Empresa),("id",id),("usuarioEmpleado",User.EmpleadoId()));
            TempData["Ok"]="Empleado desactivado; el historial permanece intacto.";
        }
        catch(SqlException ex) { logger.LogWarning(ex,"Baja de empleado fallida");TempData["Error"]="No se pudo desactivar este empleado."; }
        return RedirectToAction(nameof(Empleados));
    }

    [HttpGet]
    public async Task<IActionResult> ServiciosEmpleado(int id)
    {
        var empleado = (await Leer(@"SELECT TOP(1) e.id_empleado,CONCAT(e.nombre,' ',e.apellido_paterno) AS nombre
            FROM dbo.empleados e WHERE e.id_empleado=@id AND e.id_empresa=@empresa AND
            EXISTS(SELECT 1 FROM dbo.usuarios u JOIN dbo.roles r ON r.id_rol=u.id_rol
                   WHERE u.id_empleado=e.id_empleado AND r.nombre_rol IN ('empleado','encargado_sucursal'))",
            ("id",id),("empresa",Empresa))).FirstOrDefault();
        if(empleado is null) return NotFound();
        ViewBag.Empleado = empleado.S("nombre");
        ViewBag.Servicios = await Leer(@"
            SELECT s.id_servicio,s.nombre,
               CAST(CASE WHEN es.id_servicio IS NULL THEN 0 ELSE 1 END AS bit) AS asignado
            FROM dbo.servicios s
            LEFT JOIN dbo.estados st ON st.id_estado=s.id_estado
            LEFT JOIN dbo.empleados_servicios es ON es.id_servicio=s.id_servicio AND es.id_empleado=@empleado
            WHERE s.id_empresa=@empresa AND ISNULL(st.nombre_estado,'') <> 'servicio_inactivo'
            ORDER BY s.nombre", ("empleado",id),("empresa",Empresa));
        var seleccionados = ((List<DbRow>)ViewBag.Servicios).Where(r=>r.B("asignado")).Select(r=>r.I("id_servicio")).ToList();
        return View(new EmpresaServiciosEmpleadoForm{EmpleadoId=id,Servicios=seleccionados});
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ServiciosEmpleado(EmpresaServiciosEmpleadoForm f)
    {
        var ids=(f.Servicios??[]).Distinct().ToList();
        if(f.EmpleadoId<=0 || ids.Count>150 || ids.Any(x=>x<=0)) return BadRequest();
        var empleado=(await Leer(@"SELECT TOP(1) id_empleado FROM dbo.empleados
            WHERE id_empleado=@id AND id_empresa=@empresa", ("id",f.EmpleadoId),("empresa",Empresa))).FirstOrDefault();
        if(empleado is null) return NotFound();
        var validos=await Leer(@"SELECT id_servicio FROM dbo.servicios WHERE id_empresa=@empresa",
            ("empresa",Empresa));
        if(ids.Except(validos.Select(x=>x.I("id_servicio"))).Any()) return Forbid();
        try
        {
            await asignacion.GuardarAsync(Usuario, Empresa, f.EmpleadoId, ids, HttpContext.RequestAborted);
            TempData["Ok"]="Servicios guardados y habilitados automáticamente en la sucursal del empleado.";
            return RedirectToAction(nameof(Empleados));
        }
        catch(Exception ex) when(ex is SqlException or InvalidOperationException)
        {
            logger.LogWarning(ex,"Asignación de servicios fallida");
            TempData["Error"]=ex is InvalidOperationException ? ex.Message :
                "No se pudieron guardar las asignaciones; revisa que el empleado tenga una sucursal activa.";
            return RedirectToAction(nameof(ServiciosEmpleado),new{id=f.EmpleadoId});
        }
    }

    [HttpGet]
    public async Task<IActionResult> Sucursales()
    {
        var filas=await Leer(@"
            SELECT s.id_sucursal,s.nombre,s.calle,s.numero_exterior,s.entre_calle_1,s.entre_calle_2,
                   s.telefono,c.nombre AS colonia,est.nombre_estado AS estatus
            FROM dbo.sucursales s JOIN dbo.colonias c ON c.id_colonia=s.id_colonia
            JOIN dbo.estados est ON est.id_estado=s.id_estado
            WHERE s.id_empresa=@empresa ORDER BY s.nombre", ("empresa",Empresa));
        return View(filas);
    }

    [HttpGet]
    public async Task<IActionResult> EditarSucursal(int id)
    {
        var r=(await Leer(@"
            SELECT TOP(1) s.id_sucursal,s.nombre,s.id_colonia,s.calle,s.numero_exterior,s.numero_interior,
                s.telefono,s.entre_calle_1,s.entre_calle_2,st.nombre_estado,
                c.codigo_postal,m.id_municipio,m.id_entidad_federativa
            FROM dbo.sucursales s JOIN dbo.estados st ON st.id_estado=s.id_estado
            JOIN dbo.colonias c ON c.id_colonia=s.id_colonia
            JOIN dbo.municipios m ON m.id_municipio=c.id_municipio
            WHERE s.id_sucursal=@id AND s.id_empresa=@empresa",
            ("id",id),("empresa",Empresa))).FirstOrDefault();
        if(r is null) return NotFound();
        ViewBag.EstadoInicial=r.I("id_entidad_federativa");
        ViewBag.MunicipioInicial=r.I("id_municipio");
        ViewBag.ColoniaInicial=r.I("id_colonia");
        ViewBag.CpInicial=r.S("codigo_postal");
        return View(new EmpresaSucursalEditarForm{Id=id,Nombre=r.S("nombre"),ColoniaId=r.I("id_colonia"),
            Calle=r.S("calle"),NumeroExterior=r.S("numero_exterior"),NumeroInterior=r.S("numero_interior"),
            Telefono=r.S("telefono"),EntreCalle1=r.S("entre_calle_1"),EntreCalle2=r.S("entre_calle_2"),
            Activa=r.S("nombre_estado")=="sucursal_activa"});
    }

    private async Task CargarDireccionEdicionAsync(int id)
    {
        var r=(await Leer(@"SELECT TOP(1) c.id_colonia,m.id_municipio,m.id_entidad_federativa
            FROM dbo.sucursales s JOIN dbo.colonias c ON c.id_colonia=s.id_colonia
            JOIN dbo.municipios m ON m.id_municipio=c.id_municipio
            WHERE s.id_empresa=@empresa AND s.id_sucursal=@id",
            ("empresa",Empresa),("id",id))).FirstOrDefault();
        if(r is not null)
        {
            ViewBag.EstadoInicial=r.I("id_entidad_federativa");
            ViewBag.MunicipioInicial=r.I("id_municipio");
            ViewBag.ColoniaInicial=r.I("id_colonia");
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> EditarSucursal(EmpresaSucursalEditarForm f)
    {
        if(!ModelState.IsValid) { await CargarDireccionEdicionAsync(f.Id); return View(f); }
        try
        {
            await using var cn=await db.OpenAsync();
            await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
            try
            {
                var datos=(await SqlStore.ReadTextAsync(cn,tx,@"
                    SELECT TOP(1) c.id_colonia,m.id_municipio,m.id_entidad_federativa,
                        (SELECT TOP(1) id_estado FROM dbo.estados
                         WHERE nombre_estado=CASE WHEN @activa=1 THEN 'sucursal_activa' ELSE 'sucursal_inactiva' END) AS id_estado
                    FROM dbo.colonias c JOIN dbo.municipios m ON m.id_municipio=c.id_municipio
                    WHERE c.id_colonia=@colonia
                      AND EXISTS(SELECT 1 FROM dbo.sucursales s
                          WHERE s.id_sucursal=@id AND s.id_empresa=@empresa)",
                    SqlStore.P(("id",f.Id),("empresa",Empresa),("colonia",f.ColoniaId),("activa",f.Activa)))).FirstOrDefault();
                if(datos is null || datos.I("id_estado")<=0)
                    throw new InvalidOperationException("La colonia, sucursal o su estatus no existe. Comprueba los catálogos SQL.");
                // Reutilizar el SP original para mantener sincronizadas colonia,
                // municipio y entidad, y conservar validaciones de la BD.
                await SqlStore.ReadAsync(cn,tx,"dbo.sp_sucursales",SqlStore.P(
                    ("accion","actualizar"),("id_sucursal",f.Id),("id_empresa",Empresa),
                    ("id_estado",datos.I("id_estado")),("id_colonia",f.ColoniaId),
                    ("id_municipio",datos.I("id_municipio")),
                    ("id_entidad_federativa",datos.I("id_entidad_federativa")),
                    ("nombre",f.Nombre.Trim()),("calle",f.Calle.Trim()),
                    ("numero_exterior",f.NumeroExterior.Trim()),("numero_interior",f.NumeroInterior?.Trim()),
                    ("entre_calle_1",f.EntreCalle1?.Trim()),("entre_calle_2",f.EntreCalle2?.Trim())));
                await SqlStore.ReadTextAsync(cn,tx,@"
                    UPDATE dbo.sucursales SET telefono=@telefono
                    WHERE id_sucursal=@id AND id_empresa=@empresa",
                    SqlStore.P(("telefono",f.Telefono?.Trim()),("id",f.Id),("empresa",Empresa)));
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync();throw; }
            TempData["Ok"]="Sucursal actualizada correctamente.";
            return RedirectToAction(nameof(Sucursales));
        }
        catch(Exception ex) when(ex is SqlException or InvalidOperationException)
        {
            logger.LogWarning(ex,"Edición de sucursal fallida");
            ModelState.AddModelError("","No fue posible guardar la sucursal. Revisa la dirección y ejecuta la migración SQL.");
            await CargarDireccionEdicionAsync(f.Id);
            return View(f);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DesactivarSucursal(int id)
    {
        try
        {
            await Ejecutar(@"
                DECLARE @estado int=(SELECT TOP(1) id_estado FROM dbo.estados WHERE nombre_estado='sucursal_inactiva');
                IF @estado IS NULL THROW 51010, 'Falta sucursal_inactiva en dbo.estados.', 1;
                UPDATE dbo.sucursales SET id_estado=@estado WHERE id_sucursal=@id AND id_empresa=@empresa;
                IF @@ROWCOUNT=0 THROW 51011, 'Sucursal fuera de tu empresa o inexistente.', 1;",
                ("id",id),("empresa",Empresa));
            TempData["Ok"]="Sucursal desactivada. No se eliminaron sus citas históricas.";
        }
        catch(SqlException ex) {logger.LogWarning(ex,"Baja de sucursal fallida"); TempData["Error"]="No fue posible desactivar la sucursal.";}
        return RedirectToAction(nameof(Sucursales));
    }
}
