namespace Citicio.Models;

// Propiedades serializadas en JSON camelCase para el frontend /empresa/app.js.
public sealed record LoginData(string Correo, string Contrasena);
public sealed record PrecioData(int IdServicio, decimal Precio);
public sealed record CrearServicioData(string Nombre, string? Descripcion, decimal Precio);
public sealed record CitaEstadoData(int IdCita, string Estado);
public sealed record NuevoAccesoData(int IdEmpleado, string Rol, string NombreUsuario,
    string Correo, string Contrasena);

public sealed record EmpresaPanelViewModel(string Titulo, string Rol, string Nombre,
    string Empresa, string Sucursal);
