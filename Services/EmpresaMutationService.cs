using System.Data;
using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Microsoft.AspNetCore.Identity;

namespace Citicio.Services;

/// <summary>Operaciones con permisos por usuario, servicio, empresa y sucursal.</summary>
public sealed class EmpresaMutationService(CiticioDb db)
{
    // Precio es global por servicio/empresa; dbo.sp_servicios sincroniza sus sucursales.
    public Task<bool> CambiarPrecioAsync(CurrentUser user, PrecioData data) =>
        db.UpdatePriceIfAuthorizedAsync(user.Id, data.IdServicio, data.Precio);

    public Task<Dictionary<string, object?>?> CrearServicioAsync(CurrentUser user, CrearServicioData data) =>
        db.FirstAsync("dbo.sp_servicios", CommandType.StoredProcedure,
            CiticioDb.Param("@accion", "insertar"),
            CiticioDb.Param("@id_empresa", user.IdEmpresa),
            CiticioDb.Param("@nombre", data.Nombre.Trim()),
            CiticioDb.Param("@descripcion", data.Descripcion),
            CiticioDb.Param("@precio", data.Precio));

    public Task<bool> CambiarEstadoCitaAsync(CurrentUser user, CitaEstadoData data) =>
        db.ChangeAppointmentStatusAsync(data.IdCita, user.IdEmpresa, user.IdSucursal, user.Rol, data.Estado);

    public Task<Dictionary<string, object?>?> CrearAccesoAsync(CurrentUser user, NuevoAccesoData data)
    {
        var hash = new PasswordHasher<object>().HashPassword(new object(), data.Contrasena);
        // El SP sp_usuarios_registrar_personal vuelve a verificar la empresa del actor.
        return db.FirstAsync("dbo.sp_usuarios_registrar_personal", CommandType.StoredProcedure,
            CiticioDb.Param("@id_usuario_actor", user.Id),
            CiticioDb.Param("@id_empleado", data.IdEmpleado),
            CiticioDb.Param("@nombre_usuario", data.NombreUsuario.Trim()),
            CiticioDb.Param("@correo_acceso", data.Correo.Trim().ToLowerInvariant()),
            CiticioDb.Param("@contrasena_hash", hash),
            CiticioDb.Param("@nombre_rol", data.Rol));
    }
}
