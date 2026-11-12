namespace Citicio.Models;

public sealed record NuevaSucursalRequest(int IdEstado, int IdMunicipio, int IdColonia,
    string? CodigoPostal, string Nombre, string Calle, string NumeroExterior, string? NumeroInterior,
    string BancoSucursal, string TitularSucursal, string ClabeSucursal);
public sealed record NuevoHorarioSucursalRequest(int IdSucursal, int IdDia, string Apertura, string Cierre);
public sealed record NuevoHorarioEmpleadoRequest(int IdEmpleado, int IdSucursal, int IdDia, string Entrada, string Salida);
public sealed record SolicitudTransferenciaRequest(int IdCobroEmpresa, string Referencia);
