using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

// El actor se obtiene de la cookie. Nunca acepta un identificador de otra persona.
[Authorize(Roles="admin_empresa,encargado_sucursal,empleado")]
public sealed class MiPerfilController(SqlStore db, CuentaService cuentas, ILogger<MiPerfilController> log) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        await using var cn=await db.OpenAsync();
        var filas=await SqlStore.ReadTextAsync(cn,null,@"
            SELECT TOP(1) e.nombre,e.apellido_paterno,e.apellido_materno,e.telefono,u.correo_acceso
            FROM dbo.usuarios u JOIN dbo.empleados e ON e.id_empleado=u.id_empleado
            WHERE u.id_usuario=@usuario AND e.id_empresa=@empresa",
            SqlStore.P(("usuario",User.UserId()),("empresa",User.EmpresaId())));
        var x=filas.FirstOrDefault();
        if(x is null)return NotFound();
        return View(new MiPerfilForm {Nombre=x.S("nombre"),Paterno=x.S("apellido_paterno"),
            Materno=x.S("apellido_materno"),Telefono=x.S("telefono"),Correo=x.S("correo_acceso")});
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(MiPerfilForm form)
    {
        if(!ModelState.IsValid)return View(form);
        await using var cn=await db.OpenAsync();
        var actual=(await SqlStore.ReadTextAsync(cn,null,@"
            SELECT TOP(1) u.correo_acceso FROM dbo.usuarios u
            JOIN dbo.empleados e ON e.id_empleado=u.id_empleado
            WHERE u.id_usuario=@usuario AND e.id_empresa=@empresa",
            SqlStore.P(("usuario",User.UserId()),("empresa",User.EmpresaId())))).FirstOrDefault();
        if(actual is null)return Forbid();
        var autenticado=await cuentas.LoginAsync(actual.S("correo_acceso"),form.ContrasenaActual);
        if(autenticado is null || autenticado.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value!=User.UserId().ToString())
        {
            ModelState.AddModelError(nameof(form.ContrasenaActual),"La contraseña actual no es correcta.");
            return View(form);
        }
        var cambioCorreo=!string.Equals(actual.S("correo_acceso"),form.Correo.Trim(),StringComparison.OrdinalIgnoreCase);
        await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
        try
        {
            var afectados=await SqlStore.ReadTextAsync(cn,tx,@"
                UPDATE e SET nombre=@nombre,apellido_paterno=@paterno,apellido_materno=@materno,
                    telefono=@telefono,correo=@correo
                FROM dbo.empleados e JOIN dbo.usuarios u ON u.id_empleado=e.id_empleado
                WHERE u.id_usuario=@usuario AND e.id_empresa=@empresa;
                IF @@ROWCOUNT<>1 THROW 51240,'Cuenta fuera de tu empresa.',1;
                UPDATE u SET correo_acceso=@correo FROM dbo.usuarios u
                JOIN dbo.empleados e ON e.id_empleado=u.id_empleado
                WHERE u.id_usuario=@usuario AND e.id_empresa=@empresa;",
                SqlStore.P(("usuario",User.UserId()),("empresa",User.EmpresaId()),
                   ("nombre",form.Nombre.Trim()),("paterno",form.Paterno.Trim()),
                   ("materno",form.Materno?.Trim()),("telefono",form.Telefono.Trim()),
                   ("correo",form.Correo.Trim().ToLowerInvariant())));
            await tx.CommitAsync();
        }
        catch(SqlException ex)
        {
            await tx.RollbackAsync();
            log.LogWarning(ex,"Perfil no actualizado");
            ModelState.AddModelError("","No se pudo actualizar el perfil. Comprueba si el correo ya está registrado.");
            return View(form);
        }
        if(cambioCorreo)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            TempData["Ok"]="Datos guardados. Inicia sesión con tu nuevo correo.";
            return RedirectToAction("Ingresar","Cuenta");
        }
        TempData["Ok"]="Tu perfil se actualizó correctamente.";
        return RedirectToAction(nameof(Index));
    }
}
