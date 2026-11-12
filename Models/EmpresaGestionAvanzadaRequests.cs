namespace Citicio.Models;

// El id_empresa NUNCA se recibe desde el navegador; se deriva de la sesión.
public sealed record EditarSucursalRequest(int IdEstado, int IdMunicipio, int IdColonia,
    string? CodigoPostal, string Nombre, string Calle, string NumeroExterior, string? NumeroInterior);
public sealed record HorarioDiaRequest(int IdDia, string Inicio, string Fin);
public sealed record HorarioSemanaRequest(string Tipo, int IdSucursal, int? IdEmpleado, List<HorarioDiaRequest> Dias);
public sealed record ServiciosEmpleadoRequest(List<int> IdServicios);
public sealed record ServicioSucursalRequest(int IdServicio, int DuracionMinutos);
public sealed record ServiciosSucursalRequest(List<ServicioSucursalRequest> Servicios);
