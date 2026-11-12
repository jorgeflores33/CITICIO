using System.Security.Claims;
namespace Citicio.Auth;
public static class Sesion {
    public static int UserId(this ClaimsPrincipal u) => int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier),out var id)?id:0;
    public static int ClienteId(this ClaimsPrincipal u) => int.TryParse(u.FindFirstValue("cliente"),out var id)?id:0;
    public static int EmpleadoId(this ClaimsPrincipal u) => int.TryParse(u.FindFirstValue("empleado"),out var id)?id:0;
    public static int EmpresaId(this ClaimsPrincipal u) => int.TryParse(u.FindFirstValue("empresa"),out var id)?id:0;
    public static int SucursalId(this ClaimsPrincipal u) => int.TryParse(u.FindFirstValue("sucursal"),out var id)?id:0;
    public static bool Es(this ClaimsPrincipal u,params string[] roles) => roles.Any(u.IsInRole);
}
