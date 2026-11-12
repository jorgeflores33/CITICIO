using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

// No necesita procedimientos adicionales: usa exclusivamente la base CITICIO ya instalada.
[Authorize(Roles = "admin_citicio")]
public sealed class AdminGeneralController(SqlStore db, ILogger<AdminGeneralController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var resumen = await db.CallAsync("dbo.sp_dashboard_citicio");
        return View(resumen.FirstOrDefault() ?? new DbRow());
    }

    [HttpGet]
    public async Task<IActionResult> TransferenciasPendientes()
    {
        await using var conexion=await db.OpenAsync();
        var pendientes=await SqlStore.ReadTextAsync(conexion,null,@"
            SELECT s.id_solicitud,s.referencia,s.fecha_solicitud_utc,
                ce.id_cobro_empresa,ce.monto,ce.periodo_inicio,ce.periodo_fin,
                em.nombre AS empresa
            FROM dbo.solicitudes_pago_citicio s
            JOIN dbo.cobros_empresas ce ON ce.id_cobro_empresa=s.id_cobro_empresa
            JOIN dbo.empresas em ON em.id_empresa=ce.id_empresa
            WHERE s.estado='en_revision' ORDER BY s.fecha_solicitud_utc");
        return View(pendientes);
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolverTransferencia(long idSolicitud,bool aprobar,bool abonoVerificado)
    {
        if(idSolicitud<1)return BadRequest();
        if(aprobar && !abonoVerificado)
        {
            TempData["Error"]="Antes de aprobar, comprueba que el dinero ingresó realmente en tu banco.";
            return RedirectToAction(nameof(TransferenciasPendientes));
        }
        try
        {
            await db.CallAsync("dbo.sp_citicio_revisar_transferencia",SqlStore.P(
                ("id_usuario_admin",User.UserId()),("id_solicitud",idSolicitud),("aprobar",aprobar)));
            TempData["Ok"]=aprobar?"Mensualidad confirmada tras revisar el depósito.":"Transferencia rechazada.";
        }
        catch(SqlException ex)
        {
            logger.LogWarning("Transferencia CITICIO {Solicitud}: error SQL {Numero}",idSolicitud,ex.Number);
            TempData["Error"]="No fue posible completar la revisión de la transferencia. Verifica la referencia y el estado del cobro.";
        }
        return RedirectToAction(nameof(TransferenciasPendientes));
    }

    [HttpGet]
    public async Task<IActionResult> Conciliaciones()
    {
        var pendientes = await db.CallAsync("dbo.sp_citicio_conciliaciones_pendientes",
            SqlStore.P(("id_usuario_actor",User.UserId())));
        return View(pendientes);
    }

    [HttpGet]
    public async Task<IActionResult> Empresas(string? buscar = null)
    {
        var filtro = string.IsNullOrWhiteSpace(buscar) ? null : buscar.Trim();
        if (filtro?.Length > 120) filtro = filtro[..120];
        var todas = await db.CallAsync("dbo.sp_empresas", SqlStore.P(("accion", "listar")));
        var filtradas = filtro is null ? todas : todas.Where(e => e.S("nombre").Contains(filtro, StringComparison.OrdinalIgnoreCase)).ToList();
        ViewBag.Buscar = filtro ?? "";
        return View(filtradas.Take(200).ToList());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CambiarEstado(int idEmpresa, string nuevoEstado, string? motivo)
    {
        if (idEmpresa <= 0 || nuevoEstado is not ("empresa_activa" or "empresa_inactiva"))
            return BadRequest("Solicitud incorrecta.");
        if (nuevoEstado == "empresa_inactiva" && (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10))
        {
            TempData["Error"] = "Para desactivar una empresa, indica un motivo de al menos 10 caracteres.";
            return RedirectToAction(nameof(Empresas));
        }
        if (motivo?.Length > 500) return BadRequest("Motivo demasiado largo.");
        try
        {
            var estados = await db.CallAsync("dbo.sp_estados", SqlStore.P(("accion", "listar")));
            int destino = estados.FirstOrDefault(x => x.S("nombre_estado") == nuevoEstado)?.I("id_estado") ?? 0;
            if (destino == 0) throw new InvalidOperationException("Estado empresarial no configurado.");
            var existente = (await db.CallAsync("dbo.sp_empresas", SqlStore.P(("accion", "obtener"), ("id_empresa", idEmpresa)))).FirstOrDefault();
            if (existente is null) return NotFound();
            if (existente.S("nombre_estado") == nuevoEstado)
            {
                TempData["Error"] = "La empresa ya tiene ese estado.";
                return RedirectToAction(nameof(Empresas));
            }
            await db.CallAsync("dbo.sp_empresas", SqlStore.P(
                ("accion", "cambiar_estado"), ("id_empresa", idEmpresa), ("id_estado", destino)));
            logger.LogWarning("Administracion CITICIO: usuario {Usuario} cambio la empresa {Empresa} de {EstadoAnterior} a {EstadoNuevo}. Motivo: {Motivo}",
                User.UserId(), idEmpresa, existente.S("nombre_estado"), nuevoEstado, motivo?.Trim() ?? "No indicado");
            TempData["Ok"] = "Estado de empresa actualizado. Consulta los registros del servidor para revisar el evento.";
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            logger.LogError(ex, "No se pudo actualizar la empresa {Empresa}", idEmpresa);
            TempData["Error"] = "No se pudo modificar el estado de la empresa. Comprueba permisos y datos.";
        }
        return RedirectToAction(nameof(Empresas));
    }

    [HttpGet]
    public async Task<IActionResult> Usuarios()
    {
        var roles = await db.CallAsync("dbo.sp_roles", SqlStore.P(("accion", "listar")));
        var rolesPorId = roles.ToDictionary(r => r.I("id_rol"), r => r.S("nombre_rol"));
        var usuarios = await db.CallAsync("dbo.sp_usuarios", SqlStore.P(("accion", "listar")));
        foreach (var u in usuarios)
            u.Cells["nombre_rol"] = rolesPorId.GetValueOrDefault(u.I("id_rol"), "sin_rol");
        return View(usuarios.OrderByDescending(r => r.I("id_usuario")).Take(200).ToList());
    }

    // Catalogo global de giros: exclusivamente el administrador de CITICIO.
    // Se reutiliza dbo.sp_giros de la base actual: no se requieren cambios SQL.
    [HttpGet]
    public async Task<IActionResult> Giros()
    {
        return await MostrarGiros(new GiroCatalogoForm());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CrearGiro(GiroCatalogoForm form)
    {
        form.NombreGiro = (form.NombreGiro ?? "").Trim();
        form.Descripcion = form.Descripcion?.Trim();
        if (!ModelState.IsValid)
            return await MostrarGiros(form);

        try
        {
            await db.CallAsync("dbo.sp_giros", SqlStore.P(
                ("accion", "insertar"),
                ("nombre_giro", form.NombreGiro),
                ("descripcion", form.Descripcion ?? "")));
            TempData["Ok"] = "Giro registrado correctamente. Ya aparece al registrar una empresa.";
            return RedirectToAction(nameof(Giros));
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "No fue posible registrar el giro {Giro}", form.NombreGiro);
            ModelState.AddModelError(nameof(form.NombreGiro),
                ex.Number is 52012 or 2601 or 2627
                    ? "Ese giro ya existe en el catálogo."
                    : "No se pudo registrar el giro. Comprueba los datos e inténtalo de nuevo.");
            return await MostrarGiros(form);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ActualizarGiro(GiroCatalogoForm form)
    {
        form.NombreGiro = (form.NombreGiro ?? "").Trim();
        form.Descripcion = form.Descripcion?.Trim();
        if (form.IdGiro <= 0 || !ModelState.IsValid)
        {
            TempData["Error"] = "Revisa el identificador, el nombre y la descripción del giro.";
            return RedirectToAction(nameof(Giros));
        }

        try
        {
            await db.CallAsync("dbo.sp_giros", SqlStore.P(
                ("accion", "actualizar"),
                ("id_giro", form.IdGiro),
                ("nombre_giro", form.NombreGiro),
                ("descripcion", form.Descripcion ?? "")));
            TempData["Ok"] = "Giro actualizado correctamente.";
        }
        catch (SqlException ex)
        {
            logger.LogWarning(ex, "No se pudo actualizar el giro {GiroId}", form.IdGiro);
            TempData["Error"] = ex.Number is 2601 or 2627
                ? "Ya existe otro giro con ese nombre."
                : "No se pudo actualizar el giro. Comprueba sus datos.";
        }
        return RedirectToAction(nameof(Giros));
    }

    private async Task<IActionResult> MostrarGiros(GiroCatalogoForm form)
    {
        ViewBag.Giros = await db.CallAsync("dbo.sp_giros", SqlStore.P(("accion", "listar")));
        return View("Giros", form);
    }

    [HttpGet]
    public async Task<IActionResult> Roles()
    {
        var roles = await db.CallAsync("dbo.sp_roles", SqlStore.P(("accion", "listar")));
        var asignaciones = await db.CallAsync("dbo.sp_roles_funcionalidades", SqlStore.P(("accion", "listar")));
        var usuarios = await db.CallAsync("dbo.sp_usuarios", SqlStore.P(("accion", "listar")));
        foreach (var r in roles)
        {
            int id = r.I("id_rol");
            r.Cells["funcionalidades"] = asignaciones.Count(x => x.I("id_rol") == id);
            r.Cells["usuarios"] = usuarios.Count(x => x.I("id_rol") == id);
        }
        return View(roles);
    }
}
