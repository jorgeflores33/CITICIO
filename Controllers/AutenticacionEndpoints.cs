using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Citicio.Services;
using static Citicio.Services.EndpointHelpers;
using static Citicio.Auth.SessionHelpers;

namespace Citicio.Controllers;

public static class AutenticacionEndpoints
{
    public static IEndpointRouteBuilder MapAutenticacionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/csrf", (HttpContext ctx, IAntiforgery antiforgery) => {
            var tokens = antiforgery.GetAndStoreTokens(ctx);
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = tokens.RequestToken });
        });
        app.MapPost("/api/auth/registro", async Task<IResult> (RegisterDto input, ClienteSqlStore db,
            IPasswordHasher<LoginIdentity> hasher, HttpContext http, CancellationToken ct) => {
            if (!ValidPerson(input.Nombre, 100) || !ValidPerson(input.ApellidoPaterno, 100) || !ValidPerson(input.ApellidoMaterno, 100)
                || !ValidEmail(input.Correo) || !ValidPhone(input.Telefono) || !ValidPassword(input.Contrasena)
                || input.FechaNacimiento > DateOnly.FromDateTime(MexicoClock.Today) || input.FechaNacimiento.Year < 1900
                || (input.AceptaWhatsapp && !ValidWhatsApp(input.TelefonoWhatsapp)))
                return Results.BadRequest(new { error = "Revisa nombre, apellidos, fecha, teléfono, correo y contraseña segura (8+ caracteres, mayúscula, minúscula, número y símbolo)." });
            var email = input.Correo.Trim().ToLowerInvariant();
            var username = email; // Un único identificador, sin colisiones inventadas.
            await using var cn = await db.Open(ct);
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(ct);
            var role = await db.Sp("dbo.sp_citicio_lecturas", ct, cn, tx, P("@accion", "rol_cliente"));
            if (role.Count == 0) return Results.Problem("Falta el rol cliente en SQL Server. Ejecuta tus scripts originales.", statusCode: 503);
            var idRol = Val.Int(role[0], "id_rol");
            var person = await db.Sp("dbo.sp_clientes", ct, cn, tx,
                P("@accion", "insertar"), P("@nombre", input.Nombre.Trim()),
                P("@apellido_paterno", input.ApellidoPaterno.Trim()), P("@apellido_materno", input.ApellidoMaterno.Trim()),
                P("@telefono", input.Telefono.Trim()), P("@correo", email),
                DateParam("@fecha_nacimiento", input.FechaNacimiento),
                P("@telefono_whatsapp", input.AceptaWhatsapp ? input.TelefonoWhatsapp?.Trim() : null),
                P("@acepta_whatsapp", input.AceptaWhatsapp));
            var idCliente = Val.Int(person[0], "id_cliente");
            var passHash = hasher.HashPassword(new LoginIdentity(), input.Contrasena);
            var created = await db.Sp("dbo.sp_usuarios", ct, cn, tx,
                P("@accion", "insertar"), P("@id_cliente", idCliente), P("@id_rol", idRol),
                P("@nombre_usuario", username), P("@correo_acceso", email), P("@contrasena_hash", passHash));
            var idUsuario = Val.Int(created[0], "id_usuario");
            await tx.CommitAsync(ct);
            await LoginCookie(http, idUsuario, idCliente, input.Nombre.Trim(), email);
            return Results.Ok(new { mensaje = "Cuenta creada correctamente", usuario = new { idUsuario, idCliente, nombre = input.Nombre, correo = email } });
        }).RequireRateLimiting("auth");

        app.MapPost("/api/auth/login", async Task<IResult> (LoginDto input, ClienteSqlStore db,
            IPasswordHasher<LoginIdentity> hasher, HttpContext http, CancellationToken ct) => {
            if (!ValidEmail(input.Correo) || string.IsNullOrEmpty(input.Contrasena)) return Results.BadRequest(new { error = "Introduce correo y contraseña." });
            var row = await db.Sp("dbo.sp_usuarios", ct, P("@accion", "obtener_correo"), P("@correo_acceso", input.Correo.Trim().ToLowerInvariant()));
            if (row.Count != 1) return Results.Unauthorized();
            var hash = Val.Str(row[0], "contrasena_hash");
            PasswordVerificationResult verified;
            try { verified = hasher.VerifyHashedPassword(new LoginIdentity(), hash, input.Contrasena); }
            catch (FormatException) { verified = PasswordVerificationResult.Failed; }
            if (verified == PasswordVerificationResult.Failed) return Results.Unauthorized();
            var idUsuario = Val.Int(row[0], "id_usuario");
            var context = await db.Sp("dbo.sp_login_contexto", ct, P("@id_usuario", idUsuario));
            if (context.Count != 1 || Val.Str(context[0], "nombre_rol") != "cliente") return Results.Forbid();
            var idCliente = Val.Int(context[0], "id_cliente");
            var perfil = await db.Sp("dbo.sp_clientes", ct, P("@accion", "obtener"), P("@id_cliente", idCliente));
            var nombre = perfil.Count > 0 ? Val.Str(perfil[0], "nombre") : Val.Str(context[0], "nombre_usuario");
            await db.Sp("dbo.sp_usuarios", ct, P("@accion", "actualizar_acceso"), P("@id_usuario", idUsuario));
            await LoginCookie(http, idUsuario, idCliente, nombre, input.Correo.Trim().ToLowerInvariant());
            return Results.Ok(new { mensaje = "Bienvenido a CITICIO", usuario = new { idUsuario, idCliente, nombre, correo = input.Correo } });
        }).RequireRateLimiting("auth");
        app.MapPost("/api/auth/logout", async Task<IResult> (HttpContext ctx) => {
            await ctx.SignOutAsync("Citicio.Clientes");
            return Results.Ok(new { mensaje = "Sesión cerrada" });
        });
        app.MapGet("/api/auth/me", (ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true
            ? (IResult)Results.Ok(new { idUsuario = AuthId(user), idCliente = ClientId(user), nombre = user.FindFirstValue(ClaimTypes.Name), correo = user.FindFirstValue(ClaimTypes.Email) })
            : Results.Unauthorized());


        return app;
    }
}
