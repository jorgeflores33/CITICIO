using System.Security.Claims;
using Citicio.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Citicio.Auth;

/// <summary>Invalida las tres cookies cuando el hash de contraseña cambia.</summary>
public class SessionVersionCookieEvents(SqlStore db) : CookieAuthenticationEvents
{
    public const string ClaimName = "citicio.version_sesion";

    // La clase derivada reutiliza el mismo SqlStore, evitando capturar el
    // parametro del constructor en dos niveles (CS9107).
    protected SqlStore Database => db;

    private async Task<int?> LeerVersionAsync(int usuario)
    {
        await using var conexion = await db.OpenAsync();
        var filas = await SqlStore.ReadTextAsync(conexion, null,
            "SELECT version_sesion FROM dbo.usuarios WHERE id_usuario=@usuario",
            SqlStore.P(("usuario", usuario)));
        return filas.Count == 1 ? filas[0].I("version_sesion") : null;
    }

    public override async Task SigningIn(CookieSigningInContext context)
    {
        if (context.Principal?.Identity is not ClaimsIdentity identidad ||
            !int.TryParse(context.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var usuario))
            throw new InvalidOperationException("No se puede crear una sesión sin usuario válido.");
        var actual = await LeerVersionAsync(usuario)
            ?? throw new InvalidOperationException("El usuario no existe en SQL Server.");
        foreach (var claim in identidad.FindAll(ClaimName).ToArray()) identidad.RemoveClaim(claim);
        identidad.AddClaim(new Claim(ClaimName, actual.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        await base.SigningIn(context);
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var usuarioTexto = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var versionTexto = context.Principal?.FindFirstValue(ClaimName);
        if (!int.TryParse(usuarioTexto, out var usuario) || !int.TryParse(versionTexto, out var version))
        { await InvalidarAsync(context); return; }
        var actual = await LeerVersionAsync(usuario);
        if (!actual.HasValue || actual.Value != version)
        { await InvalidarAsync(context); return; }
        await base.ValidatePrincipal(context);
    }

    protected static async Task InvalidarAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(context.Scheme.Name);
    }
}
