using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Citicio.Auth;

internal static class SessionHelpers
{
    public static int AuthId(ClaimsPrincipal user) => int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    public static int ClientId(ClaimsPrincipal user) => int.Parse(user.FindFirstValue("id_cliente")!, CultureInfo.InvariantCulture);
    public static async Task LoginCookie(HttpContext ctx, int idUser, int idClient, string name, string email) {
        var claims = new List<Claim> {
            new(ClaimTypes.NameIdentifier, idUser.ToString(CultureInfo.InvariantCulture)),
            new("id_cliente", idClient.ToString(CultureInfo.InvariantCulture)),
            new(ClaimTypes.Role, "cliente"), new(ClaimTypes.Name, name), new(ClaimTypes.Email, email)
        };
        await ctx.SignInAsync("Citicio.Clientes",
            new ClaimsPrincipal(new ClaimsIdentity(claims, "Citicio.Clientes")));
    }


}
