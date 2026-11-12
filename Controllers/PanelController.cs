using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
namespace Citicio.Controllers;
[Authorize]
public sealed class PanelController(SqlStore db, Citicio.Services.EmpresaIdentidadVisible identidad):Controller {
    [HttpGet] public async Task<IActionResult> Index() {
        if(User.IsInRole("cliente")) return RedirectToAction("MisCitas","Citas");
        if(User.IsInRole("admin_citicio")) return RedirectToAction("Index","AdminGeneral");
        if(User.IsInRole("empleado")) return RedirectToAction(nameof(Citas));
        List<DbRow> data;
        if(User.IsInRole("admin_citicio")) data=await db.CallAsync("dbo.sp_dashboard_citicio");
        else if(User.IsInRole("encargado_sucursal"))data=await db.CallAsync("dbo.sp_dashboard_sucursal_usuario",SqlStore.P(("id_usuario",User.UserId())));
        else data=await db.CallAsync("dbo.sp_dashboard_empresa",SqlStore.P(("id_empresa",User.EmpresaId())));
        var perfil = await identidad.LeerAsync(User);
        ViewBag.NombreEmpresa=perfil.Empresa;
        ViewBag.NombreUsuario=perfil.Usuario;
        return View(data.FirstOrDefault()??new DbRow());
    }
    private async Task<IActionResult> Tabla(string proc,string title,string subtitle,string[] cols,Dictionary<string,string>? labels=null) {
        var rows=await db.CallAsync(proc,SqlStore.P(("id_usuario",User.UserId())));
        return View("Tabla",new PanelVM {Titulo=title,Subtitulo=subtitle,Rows=rows,Columns=cols,Captions=labels??new()});
    }
    [HttpGet] public Task<IActionResult> Citas()=>Tabla("dbo.sp_citas_panel_usuario","Agenda de citas","Citas según los permisos de tu cuenta",
        ["id_cita","cliente","empresa","sucursal","empleado","hora_inicio","nombre_estado","total_cita"]);
    [Authorize(Roles="admin_citicio,admin_empresa,encargado_sucursal,cliente"),HttpGet]
    public Task<IActionResult> Pagos()=>Tabla("dbo.sp_panel_pagos_usuario","Pagos por servicios","Transacciones registradas",
        ["id_pago","empresa","sucursal","monto","nombre_metodo","nombre_estado","fecha_pago"]);
    [Authorize(Roles="admin_citicio,admin_empresa,encargado_sucursal,cliente"),HttpGet]
    public Task<IActionResult> Resenas()=>Tabla("dbo.sp_panel_resenas_usuario","Reseñas","Opiniones verificadas por cita",
        ["id_resena","id_cita","sucursal","calificacion_empresa","calificacion_sucursal","comentario","fecha_resena"]);
    [Authorize(Roles="admin_citicio,admin_empresa,encargado_sucursal"),HttpGet]
    public Task<IActionResult> Empleados()=>User.IsInRole("admin_empresa")
        ? Task.FromResult<IActionResult>(RedirectToAction("Empleados", "EmpresaAdministracion")) :
        User.IsInRole("encargado_sucursal") ? Task.FromResult<IActionResult>(RedirectToAction("Empleados","PersonalSucursal")) : Tabla("dbo.sp_panel_empleados_usuario","Empleados","Personal registrado en tu ámbito",
        ["id_empleado","nombre","apellido_paterno","correo","telefono","id_sucursal_principal","nombre_estado"]);
    [Authorize(Roles="admin_citicio,admin_empresa,encargado_sucursal,empleado"),HttpGet]
    public Task<IActionResult> Servicios()=>User.IsInRole("admin_empresa")
        ? Task.FromResult<IActionResult>(RedirectToAction("Servicios", "EmpresaAdministracion")) : Tabla("dbo.sp_panel_servicios_usuario","Servicios","El administrador fija precios y el empleado puede actualizar solamente servicios asignados",
        ["id_servicio","nombre","descripcion","precio","id_empresa"]);
    [Authorize(Roles="admin_citicio"),HttpGet]
    public Task<IActionResult> Cobros()=>Tabla("dbo.sp_panel_cobros_empresas_usuario","Cobros a empresas","Pagos mensuales a CITICIO",
        ["id_cobro_empresa","empresa","monto","periodo_inicio","periodo_fin","fecha_vencimiento","nombre_estado"]);
    [Authorize(Roles="admin_citicio,admin_empresa,encargado_sucursal"),HttpGet]
    public async Task<IActionResult> Sucursales() {
        if (User.IsInRole("admin_empresa")) return RedirectToAction("Sucursales", "EmpresaAdministracion");
        if (User.IsInRole("encargado_sucursal")) return RedirectToAction("Index", "MiSucursal");
        var rows=await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","sucursales_panel"),("id_usuario_actor",User.UserId())));
        return View("Tabla",new PanelVM {Titulo="Sucursales",Subtitulo="Ubicaciones activas e inactivas",Rows=rows,
          Columns=["id_sucursal","empresa","nombre","calle","numero_exterior","colonia","nombre_estado"]});
    }
    [Authorize(Roles="admin_citicio"),HttpGet]
    public async Task<IActionResult> Empresas() {
        var rows=await db.CallAsync("dbo.sp_empresas",SqlStore.P(("accion","listar")));
        return View("Tabla",new PanelVM {Titulo="Empresas",Subtitulo="Administración general de empresas",Rows=rows,
         Columns=["id_empresa","nombre","descripcion","nombre_estado"]});
    }
    [Authorize(Roles="admin_citicio,admin_empresa,encargado_sucursal"),HttpGet]
    public async Task<IActionResult> Reportes(DateTime? inicio=null,DateTime? fin=null) {
        var rows=await db.CallAsync("dbo.sp_reporte_citas",SqlStore.P(
            ("id_empresa",User.IsInRole("admin_citicio")?(int?)null:User.EmpresaId()),
            ("id_sucursal",User.IsInRole("encargado_sucursal")?(int?)User.SucursalId():null),
            ("fecha_inicio",inicio?.Date),("fecha_fin",fin?.Date)));
        return View("Tabla",new PanelVM{Titulo="Reporte de citas",Subtitulo="Filtrado según tus permisos",
            Rows=rows,Columns=["id_cita","cliente","empresa","sucursal","empleado","fecha","nombre_estado","total_cita"]});
    }
}
