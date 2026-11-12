using System.Security.Claims;
using Citicio.Data;
using Citicio.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
namespace Citicio.Auth;
public sealed class CookieRevalidationEvents(SqlStore db,CuentaService accounts) : SessionVersionCookieEvents(db) {
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context) {
        await base.ValidatePrincipal(context);
        if(context.Principal?.Identity?.IsAuthenticated != true) return;
        if(!int.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier),out int id) || id<1) {await Invalid();return;}
        var actual=(await Database.CallAsync("dbo.sp_login_contexto",SqlStore.P(("id_usuario",id)))).FirstOrDefault();
        var role=context.Principal!.FindFirstValue(ClaimTypes.Role);
        var acceso = actual is null ? (Permitido:false, EmpresaPendiente:false) : await accounts.EstadoAccesoAsync(actual);
        if(actual is null || actual.S("nombre_rol")!=role || !acceso.Permitido
          || context.Principal.FindFirstValue("empresa")!=actual.I("id_empresa").ToString()
          || context.Principal.FindFirstValue("sucursal")!=actual.I("id_sucursal_principal").ToString()
          || context.Principal.FindFirstValue("empleado")!=actual.I("id_empleado").ToString()
          || context.Principal.FindFirstValue("cliente")!=actual.I("id_cliente").ToString()) {await Invalid();return;}
        // Las sesiones de la versión anterior se actualizan sin claims de aprobación.
        var esperado = acceso.EmpresaPendiente ? "true" : "false";
        if(context.Principal.FindFirstValue("empresa_pendiente") != esperado) {
            var nuevo = CuentaService.Principal(actual, acceso.EmpresaPendiente);
            // La sustitución del principal por cambios de ámbito no debe eliminar
            // el sello de versión que acabamos de comprobar en SQL Server.
            var version = context.Principal.FindFirstValue(SessionVersionCookieEvents.ClaimName);
            if (nuevo.Identity is ClaimsIdentity identidad && !string.IsNullOrWhiteSpace(version))
                identidad.AddClaim(new Claim(SessionVersionCookieEvents.ClaimName, version));
            context.ReplacePrincipal(nuevo);
            context.ShouldRenew = true;
        }
        async Task Invalid(){context.RejectPrincipal();await context.HttpContext.SignOutAsync(context.Scheme.Name);}
    }
}
