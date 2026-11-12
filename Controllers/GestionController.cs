using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
namespace Citicio.Controllers;
[Authorize(Roles="admin_citicio,admin_empresa,encargado_sucursal")]
public sealed class GestionController(SqlStore db,CuentaService cuenta, IHostEnvironment entorno):Controller {
    private bool Global=>User.IsInRole("admin_citicio");
    private bool EmpresaAdmin=>User.IsInRole("admin_empresa");
    private bool PuedeEmpresa(int empresa)=>Global || (EmpresaAdmin&&empresa==User.EmpresaId());
    private async Task<int> EmpresaDeSucursal(int id) => (await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(("accion","empresa_de_sucursal"),("id1",id)))).FirstOrDefault()?.I("id_empresa")??0;
    private async Task<int> EmpresaDeServicio(int id) => (await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(("accion","empresa_de_servicio"),("id1",id)))).FirstOrDefault()?.I("id_empresa")??0;
    private async Task PrepararSelects() {
        var args=SqlStore.P(("id_usuario_actor",User.UserId()),("accion","empresas_selector"));
        ViewBag.Empresas=await db.CallAsync("dbo.sp_citicio_lecturas",args);
        ViewBag.Sucursales=await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","sucursales_selector"),("id_usuario_actor",User.UserId())));
        ViewBag.Servicios=await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","servicios_selector"),("id_usuario_actor",User.UserId())));
        ViewBag.Empleados=await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","empleados_selector"),("id_usuario_actor",User.UserId())));
    }
    // El procedimiento existente sp_imagen_guardar exige un usuario admin_empresa.
    // Si el operador es admin_citicio, seleccionamos al propietario de esa empresa
    // exclusivamente para guardar su imagen desde el panel administrativo.
    private async Task<int> ActorImagenEmpresaAsync(SqlConnection cn, SqlTransaction tx, int idEmpresa)
    {
        if(!Global) return User.UserId();
        var rows=await SqlStore.ReadTextAsync(cn,tx,@"
            SELECT TOP (1) u.id_usuario
            FROM dbo.usuarios u
            JOIN dbo.empleados e ON e.id_empleado=u.id_empleado
            JOIN dbo.roles r ON r.id_rol=u.id_rol
            WHERE e.id_empresa=@id_empresa AND r.nombre_rol='admin_empresa'
            ORDER BY u.id_usuario",SqlStore.P(("id_empresa",idEmpresa)));
        var actor=rows.FirstOrDefault()?.I("id_usuario") ?? 0;
        if(actor<=0)throw new InvalidOperationException("La empresa no tiene propietario administrador para guardar la imagen.");
        return actor;
    }

    // Registro administrativo completo: mismo flujo que el alta pública.
    // sp_registro_empresa_publico crea empresa, domicilio, propietario y acceso en una transacción.
    [Authorize(Roles="admin_citicio"),HttpGet]
    public async Task<IActionResult> EmpresaNueva() {
        await PrepararGirosRegistroEmpresa();
        return View(new RegistroEmpresaForm());
    }

    private async Task PrepararGirosRegistroEmpresa() {
        ViewBag.Giros = await cuenta.GirosAsync();
    }

    [Authorize(Roles="admin_citicio"),HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(4718592)]
    public async Task<IActionResult> EmpresaNueva(RegistroEmpresaForm f) {
        if(!f.AceptaTerminos)
            ModelState.AddModelError(nameof(f.AceptaTerminos), "Debes confirmar los datos y aceptar los términos de registro.");
        if(!ModelState.IsValid) {
            await PrepararGirosRegistroEmpresa();
            return View(f);
        }

        try {
            await cuenta.RegistrarEmpresaAsync(f);
            TempData["Ok"] = "Empresa registrada y activa, con domicilio y propietario administrador de empresa. El acceso empresarial requiere una mensualidad pagada.";
            return RedirectToAction("Empresas", "AdminGeneral");
        }
        catch(SqlException ex) {
            ModelState.AddModelError("", CuentaService.MensajeErrorRegistro(ex));
        }
        catch(InvalidOperationException ex) {
            ModelState.AddModelError("", ex.Message);
        }
        await PrepararGirosRegistroEmpresa();
        return View(f);
    }
    [Authorize(Roles="admin_citicio"),HttpGet] public async Task<IActionResult> CobroEmpresaNuevo() {
        await PrepararSelects();return View(new CobroEmpresaForm());
    }
    [Authorize(Roles="admin_citicio"),HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> CobroEmpresaNuevo(CobroEmpresaForm f) {
        if(f.Fin.Date<f.Inicio.Date)ModelState.AddModelError("Fin","El periodo final debe ser posterior al inicial.");
        if(f.Vencimiento.Date<f.Inicio.Date)ModelState.AddModelError("Vencimiento","La fecha de vencimiento debe ser válida.");
        if(!ModelState.IsValid){await PrepararSelects();return View(f);}
        var estado=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(("accion","estado_por_nombre"),("texto","cobro_pendiente")))).FirstOrDefault();
        if(estado==null)throw new InvalidOperationException("Falta cobro_pendiente en dbo.estados.");
        try {await db.CallAsync("dbo.sp_cobros_empresas",SqlStore.P(("accion","insertar"),
         ("id_empresa",f.EmpresaId),("id_estado",estado.I("id_estado")),("monto",f.Monto),
         ("periodo_inicio",f.Inicio.Date),("periodo_fin",f.Fin.Date),("fecha_vencimiento",f.Vencimiento.Date)));
         TempData["Ok"]="Cobro registrado como pendiente; la pasarela debe confirmar el pago.";return RedirectToAction("Cobros","Panel");}
        catch(SqlException e){ModelState.AddModelError("",e.Message);await PrepararSelects();return View(f);}
    }
    [Authorize(Roles="admin_citicio,admin_empresa"),HttpGet] public async Task<IActionResult> GiroEmpresa() {
        await PrepararSelects();ViewBag.Giros=await db.CallAsync("dbo.sp_giros",SqlStore.P(("accion","listar")));
        return View(new GiroEmpresaForm {EmpresaId=Global?0:User.EmpresaId()});
    }
    [Authorize(Roles="admin_citicio,admin_empresa"),HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> GiroEmpresa(GiroEmpresaForm f) {
        if(!PuedeEmpresa(f.EmpresaId))return Forbid();
        if(!ModelState.IsValid){await PrepararSelects();ViewBag.Giros=await db.CallAsync("dbo.sp_giros",SqlStore.P(("accion","listar")));return View(f);}
        try {await db.CallAsync("dbo.sp_giros",SqlStore.P(("accion","asignar"),("id_empresa",f.EmpresaId),("id_giro",f.GiroId)));
           TempData["Ok"]="Categoría asignada. Aparecerá en el catálogo cuando la mensualidad esté pagada y vigente.";return RedirectToAction("Index","Panel");}
        catch(SqlException e){ModelState.AddModelError("",e.Message);await PrepararSelects();ViewBag.Giros=await db.CallAsync("dbo.sp_giros",SqlStore.P(("accion","listar")));return View(f);}
    }
    [Authorize(Roles="admin_citicio,admin_empresa"),HttpGet] public async Task<IActionResult> SucursalNueva() {await PrepararSelects();return View(new SucursalForm {EmpresaId=Global?0:User.EmpresaId()});}
    [Authorize(Roles="admin_citicio,admin_empresa"),HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(2621440)]
    public async Task<IActionResult> SucursalNueva(SucursalForm f) {
        if(!SpeiController.ClabeValida(f.ClabeSucursal))ModelState.AddModelError(nameof(f.ClabeSucursal),"El dígito verificador de CLABE es incorrecto.");
        if(!PuedeEmpresa(f.EmpresaId))return Forbid();
        if(!ModelState.IsValid){await PrepararSelects();return View(f);}
        try {
            var imagen=await ImagenArchivoService.PrepararAsync(f.ImagenArchivo);
            await using var cn=await db.OpenAsync();
            await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
            try {
                var resultado=(await SqlStore.ReadAsync(cn,tx,"dbo.sp_sucursales",SqlStore.P(
                    ("accion","insertar"),("id_empresa",f.EmpresaId),
                    ("id_colonia",f.ColoniaId),("nombre",f.Nombre),("calle",f.Calle),
                    ("numero_exterior",f.NumeroExterior),("numero_interior",f.NumeroInterior)))).First();
                var id=resultado.I("id_sucursal");
                if(imagen is not null) {
                    var actor=await ActorImagenEmpresaAsync(cn,tx,f.EmpresaId);
                    await ImagenArchivoService.GuardarAsync(cn,tx,actor,"foto_sucursal",id,imagen,entorno.ContentRootPath);
                    await SqlStore.ReadAsync(cn,tx,"dbo.sp_sucursales",SqlStore.P(
                       ("accion","actualizar"),("id_sucursal",id),("id_empresa",f.EmpresaId),
                       ("id_colonia",f.ColoniaId),("nombre",f.Nombre),("calle",f.Calle),
                       ("numero_exterior",f.NumeroExterior),("numero_interior",f.NumeroInterior),
                       ("imagen_url",ImagenArchivoService.Ruta("foto_sucursal",id))));
                }
                await SqlStore.ReadTextAsync(cn,tx,@"UPDATE dbo.sucursales
                    SET telefono=@telefono,entre_calle_1=@entre1,entre_calle_2=@entre2
                    WHERE id_sucursal=@sucursal AND id_empresa=@empresa",SqlStore.P(
                    ("telefono",f.TelefonoSucursal),("entre1",f.EntreCalle1),("entre2",f.EntreCalle2),
                    ("sucursal",id),("empresa",f.EmpresaId)));
                await SqlStore.ReadAsync(cn,tx,"dbo.sp_citicio_cuenta_sucursal_guardar",SqlStore.P(
                    ("id_usuario_actor",User.UserId()),("id_sucursal",id),
                    ("nombre_banco",f.BancoSucursal.Trim()),("titular",f.TitularSucursal.Trim()),
                    ("clabe",f.ClabeSucursal)));
                await tx.CommitAsync();
            } catch {await tx.RollbackAsync();throw;}
            TempData["Ok"]="Sucursal creada con su cuenta bancaria receptora.";return RedirectToAction("Sucursales","Panel");
        } catch(Exception e) when(e is SqlException or InvalidOperationException) {
            ModelState.AddModelError("",e is InvalidOperationException ? e.Message : "No se pudo guardar la sucursal ni su imagen.");
            await PrepararSelects();return View(f);
        }
    }
    [Authorize(Roles="admin_citicio,admin_empresa"),HttpGet] public async Task<IActionResult> ServicioNuevo() {await PrepararSelects();return View(new ServicioForm{EmpresaId=Global?0:User.EmpresaId()});}
    [Authorize(Roles="admin_citicio,admin_empresa"),HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(2621440)]
    public async Task<IActionResult> ServicioNuevo(ServicioForm f) {
        if(!PuedeEmpresa(f.EmpresaId))return Forbid();
        if(!ModelState.IsValid){await PrepararSelects();return View(f);}
        try {
            var imagen=await ImagenArchivoService.PrepararAsync(f.ImagenArchivo);
            await using var cn=await db.OpenAsync();
            await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
            try {
                var resultado=(await SqlStore.ReadAsync(cn,tx,"dbo.sp_servicios",SqlStore.P(
                    ("accion","insertar"),("id_empresa",f.EmpresaId),("nombre",f.Nombre),
                    ("descripcion",f.Descripcion),("precio",f.Precio)))).First();
                var id=resultado.I("id_servicio");
                if(imagen is not null) {
                    var actor=await ActorImagenEmpresaAsync(cn,tx,f.EmpresaId);
                    await ImagenArchivoService.GuardarAsync(cn,tx,actor,"imagen_servicio",id,imagen,entorno.ContentRootPath);
                    await SqlStore.ReadAsync(cn,tx,"dbo.sp_servicios",SqlStore.P(
                        ("accion","actualizar"),("id_servicio",id),("id_empresa",f.EmpresaId),
                        ("imagen_url",ImagenArchivoService.Ruta("imagen_servicio",id))));
                }
                await tx.CommitAsync();
            } catch {await tx.RollbackAsync();throw;}
            TempData["Ok"]="Servicio creado con precio único por empresa.";return RedirectToAction("Servicios","Panel");
        } catch(Exception e) when(e is SqlException or InvalidOperationException) {
            ModelState.AddModelError("",e is InvalidOperationException ? e.Message : "No se pudo guardar el servicio ni su imagen.");
            await PrepararSelects();return View(f);
        }
    }
    [Authorize(Roles="admin_citicio,admin_empresa"),HttpGet] public async Task<IActionResult> ServicioEditar(int id) {
        var sv=(await db.CallAsync("dbo.sp_servicios",SqlStore.P(("accion","obtener"),("id_servicio",id)))).FirstOrDefault();
        if(sv==null)return NotFound();
        if(!PuedeEmpresa(sv.I("id_empresa")))return Forbid();
        return View(new ServicioForm {Id=id,EmpresaId=sv.I("id_empresa"),Nombre=sv.S("nombre"),
            Descripcion=sv.S("descripcion"),Precio=sv.M("precio")});
    }
    [Authorize(Roles="admin_citicio,admin_empresa"),HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(2621440)]
    public async Task<IActionResult> ServicioEditar(ServicioForm f) {
        if(!ModelState.IsValid)return View(f);
        var empresa=await EmpresaDeServicio(f.Id);
        if(empresa==0)return NotFound();
        if(!PuedeEmpresa(empresa) || empresa!=f.EmpresaId)return Forbid();
        try {
            var imagen=await ImagenArchivoService.PrepararAsync(f.ImagenArchivo);
            await using var cn=await db.OpenAsync();
            await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
            try {
                if(imagen is not null) {
                    var actor=await ActorImagenEmpresaAsync(cn,tx,empresa);
                    await ImagenArchivoService.GuardarAsync(cn,tx,actor,"imagen_servicio",f.Id,imagen,entorno.ContentRootPath);
                }
                await SqlStore.ReadAsync(cn,tx,"dbo.sp_servicios",SqlStore.P(
                    ("accion","actualizar"),("id_servicio",f.Id),("id_empresa",empresa),
                    ("nombre",f.Nombre),("descripcion",f.Descripcion),("precio",f.Precio),
                    ("imagen_url",imagen is null?null:ImagenArchivoService.Ruta("imagen_servicio",f.Id))));
                await tx.CommitAsync();
            } catch {await tx.RollbackAsync();throw;}
            TempData["Ok"]="Servicio, precio e imagen actualizados.";
            return RedirectToAction("Servicios","Panel");
        } catch(Exception e) when(e is SqlException or InvalidOperationException) {
            ModelState.AddModelError("",e is InvalidOperationException ? e.Message : "No se pudo actualizar el servicio ni su imagen.");
            return View(f);
        }
    }
    [Authorize(Roles="admin_citicio,admin_empresa"),HttpGet] public async Task<IActionResult> EmpleadoNuevo() {await PrepararSelects();return View(new EmpleadoForm{EmpresaId=Global?0:User.EmpresaId()});}
    [Authorize(Roles="admin_citicio,admin_empresa"),HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> EmpleadoNuevo(EmpleadoForm f) {
        if(!PuedeEmpresa(f.EmpresaId))return Forbid();
        if(f.Rol is not ("empleado" or "encargado_sucursal" or "admin_empresa") || (!Global && f.Rol=="admin_empresa"))return Forbid();
        if(f.SucursalId is int suc && (await EmpresaDeSucursal(suc))!=f.EmpresaId)return Forbid();
        if(f.Rol=="encargado_sucursal" && f.SucursalId is null)ModelState.AddModelError("SucursalId","El encargado debe estar asignado a una sucursal.");
        if(!ModelState.IsValid){await PrepararSelects();return View(f);}
        await using var conn=await db.OpenAsync();
        await using var tx=(SqlTransaction)await conn.BeginTransactionAsync();
        try {
            var empleado=(await SqlStore.ReadAsync(conn,tx,"dbo.sp_empleados",SqlStore.P(("accion","insertar"),
              ("id_empresa",f.EmpresaId),("nombre",f.Nombre),("apellido_paterno",f.Paterno),("apellido_materno",f.Materno),
              ("telefono",f.Telefono),("correo",f.Correo),("id_sucursal_principal",f.SucursalId)))).First();
            await SqlStore.ReadAsync(conn,tx,"dbo.sp_usuarios_registrar_personal",SqlStore.P(
              ("id_usuario_actor",User.UserId()),("id_empleado",empleado.I("id_empleado")),
              ("nombre_usuario",f.Correo),("correo_acceso",f.Correo),
              ("contrasena_hash",cuenta.Hash(f.Correo,f.ContrasenaTemporal)),("nombre_rol",f.Rol)));
            await tx.CommitAsync();TempData["Ok"]="Personal registrado con cuenta de acceso.";
            return RedirectToAction("Empleados","Panel");
        } catch(Exception e) when(e is SqlException or InvalidOperationException){await tx.RollbackAsync();ModelState.AddModelError("",e.Message);await PrepararSelects();return View(f);}
    }
    [HttpGet] public async Task<IActionResult> AsignarServicio() {await PrepararSelects();return View(new AsociarForm{SucursalId=User.IsInRole("encargado_sucursal")?User.SucursalId():0});}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> AsignarServicio(AsociarForm f) {
        var empresa=await EmpresaDeSucursal(f.SucursalId);
        if(empresa==0||empresa!=await EmpresaDeServicio(f.ServicioId))return Forbid();
        if(!Global && (User.IsInRole("encargado_sucursal") ? f.SucursalId!=User.SucursalId() : empresa!=User.EmpresaId()))return Forbid();
        if(!ModelState.IsValid){await PrepararSelects();return View(f);}
        try {await db.CallAsync("dbo.sp_sucursales_servicios",SqlStore.P(("accion","asignar"),("id_sucursal",f.SucursalId),
           ("id_servicio",f.ServicioId),("duracion_minutos",f.Duracion)));
            TempData["Ok"]="Servicio habilitado en la sucursal; conserva el precio de empresa.";return RedirectToAction("Servicios","Panel");}
        catch(SqlException e){ModelState.AddModelError("",e.Message);await PrepararSelects();return View(f);}
    }
    private async Task<bool> PuedeSucursalAsync(int suc) {
        if(suc<1)return false;
        if(User.IsInRole("encargado_sucursal"))return suc==User.SucursalId();
        var empresa=await EmpresaDeSucursal(suc);
        return empresa>0 && (Global || empresa==User.EmpresaId());
    }
    [HttpGet] public async Task<IActionResult> HorarioSucursal() {await PrepararSelects();return View(new HorarioSucursalForm{SucursalId=User.IsInRole("encargado_sucursal")?User.SucursalId():0});}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> HorarioSucursal(HorarioSucursalForm f) {
        if(!await PuedeSucursalAsync(f.SucursalId))return Forbid();
        if(f.Cierre<=f.Apertura)ModelState.AddModelError("Cierre","El cierre debe ser posterior a la apertura.");
        if(!ModelState.IsValid){await PrepararSelects();return View(f);}
        try {await db.CallAsync("dbo.sp_horarios_sucursales",SqlStore.P(("accion","insertar"),("id_sucursal",f.SucursalId),
         ("id_dia",f.Dia),("hora_apertura",f.Apertura),("hora_cierre",f.Cierre)));
         TempData["Ok"]="Horario de sucursal guardado.";return RedirectToAction("Sucursales","Panel");}
        catch(SqlException e){ModelState.AddModelError("",e.Message);await PrepararSelects();return View(f);}
    }
    [HttpGet] public async Task<IActionResult> HorarioEmpleado() {await PrepararSelects();return View(new HorarioEmpleadoForm{SucursalId=User.IsInRole("encargado_sucursal")?User.SucursalId():0});}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> HorarioEmpleado(HorarioEmpleadoForm f) {
        if(!await PuedeSucursalAsync(f.SucursalId))return Forbid();
        var employee=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(("accion","empleado_ubicacion"),("id_usuario_actor",User.UserId()),("id1",f.EmpleadoId)))).FirstOrDefault();
        if(employee==null || employee.I("id_empresa")!=await EmpresaDeSucursal(f.SucursalId) ||
           (User.IsInRole("encargado_sucursal") && employee.I("id_sucursal_principal")!=f.SucursalId))return Forbid();
        if(f.Salida<=f.Entrada)ModelState.AddModelError("Salida","La salida debe ser posterior a la entrada.");
        if(!ModelState.IsValid){await PrepararSelects();return View(f);}
        try {await db.CallAsync("dbo.sp_horarios_empleados",SqlStore.P(("accion","insertar"),("id_empleado",f.EmpleadoId),
          ("id_sucursal",f.SucursalId),("id_dia",f.Dia),("hora_entrada",f.Entrada),("hora_salida",f.Salida)));
          TempData["Ok"]="Horario del empleado guardado.";return RedirectToAction("Empleados","Panel");}
        catch(SqlException e){ModelState.AddModelError("",e.Message);await PrepararSelects();return View(f);}
    }
    [HttpGet] public async Task<IActionResult> ServicioEmpleado() {await PrepararSelects();return View(new EmpleadoServicioForm());}
    [HttpPost,ValidateAntiForgeryToken] public async Task<IActionResult> ServicioEmpleado(EmpleadoServicioForm f) {
        var em=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(("accion","empleado_ubicacion"),("id_usuario_actor",User.UserId()),("id1",f.EmpleadoId)))).FirstOrDefault();
        if(em is null || em.I("id_empresa")!=await EmpresaDeServicio(f.ServicioId))return Forbid();
        if(!Global && (User.IsInRole("encargado_sucursal") ? em.I("id_sucursal_principal")!=User.SucursalId() : em.I("id_empresa")!=User.EmpresaId()))return Forbid();
        if(!ModelState.IsValid){await PrepararSelects();return View(f);}
        try {await db.CallAsync("dbo.sp_empleados_servicios",SqlStore.P(("accion","asignar"),("id_empleado",f.EmpleadoId),("id_servicio",f.ServicioId)));
          TempData["Ok"]="Servicio asignado al profesional.";return RedirectToAction("Empleados","Panel");}
        catch(SqlException e){ModelState.AddModelError("",e.Message);await PrepararSelects();return View(f);}
    }
}
