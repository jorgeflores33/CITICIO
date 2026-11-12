namespace Citicio.Auth;

public sealed record CurrentUser(
    int Id, string Nombre, string Rol, int IdEmpresa, int? IdSucursal,
    int IdEmpleado, string Empresa, string Sucursal);
