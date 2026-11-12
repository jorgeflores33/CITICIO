namespace Citicio.Auth;

/// <summary>Nombres EXACTOS de la tabla dbo.roles en CITICIO.</summary>
public static class EmpresaRoles
{
    public const string AdminEmpresa = "admin_empresa";
    public const string EncargadoSucursal = "encargado_sucursal";
    public const string Empleado = "empleado";

    public static bool EsRolEmpresa(string? rol) => rol is AdminEmpresa or EncargadoSucursal or Empleado;

    private static readonly Dictionary<string, HashSet<string>> Secciones = new(StringComparer.Ordinal)
    {
        [AdminEmpresa] = new HashSet<string>(["dashboard", "citas", "servicios", "sucursales", "empleados", "pagos", "resenas", "horarios"], StringComparer.OrdinalIgnoreCase),
        [EncargadoSucursal] = new HashSet<string>(["dashboard", "citas", "servicios", "sucursales", "empleados", "pagos", "resenas", "horarios"], StringComparer.OrdinalIgnoreCase),
        [Empleado] = new HashSet<string>(["dashboard", "citas", "servicios", "horarios"], StringComparer.OrdinalIgnoreCase)
    };
    public static bool PermiteSeccion(string? rol, string seccion) =>
        rol != null && Secciones.TryGetValue(rol, out var secciones) && secciones.Contains(seccion);
}
