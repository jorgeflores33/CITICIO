using Citicio.Auth;
using Citicio.Data;
using Citicio.Services;
using Citicio.Controllers;
using Citicio.Models;
using Microsoft.AspNetCore.Identity;
using System.Threading.RateLimiting;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
var conexion = builder.Configuration.GetConnectionString("AgenciaCitas");
if (string.IsNullOrWhiteSpace(conexion) || conexion.Contains("CONFIGURAR_EN_USER_SECRETS",StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Falta configurar la conexión SQL. Ejecuta: dotnet user-secrets set \"ConnectionStrings:AgenciaCitas\" \"Server=TU_SERVIDOR;Database=agencia_citas;User Id=programador;Password=TU_CLAVE;Encrypt=True;TrustServerCertificate=True;\"");
builder.Services.AddControllersWithViews();
builder.Services.AddAntiforgery(o => {
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "Citicio.Csrf";
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 12, Window = TimeSpan.FromMinutes(5), QueueLimit = 0, AutoReplenishment = true }));
    o.AddPolicy("citas", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 18, Window = TimeSpan.FromMinutes(5), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.AddScoped<SqlStore>();
builder.Services.AddScoped<EmpresaIdentidadVisible>();
builder.Services.AddScoped<AsignacionAutomaticaService>();
// Los tres módulos residen en este único proyecto.
builder.Services.AddScoped<CiticioDb>();
builder.Services.AddScoped<CurrentUserService>();
builder.Services.AddScoped<EmpresaPanelService>();
builder.Services.AddScoped<EmpresaMutationService>();
builder.Services.AddScoped<RegistroPublicoService>();
builder.Services.AddScoped<ImagenEmpresaService>();
builder.Services.AddSingleton<ClienteSqlStore>();
builder.Services.AddSingleton<IPasswordHasher<LoginIdentity>, PasswordHasher<LoginIdentity>>();

builder.Services.AddScoped<CuentaService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddSingleton<TicketPdfService>();
builder.Services.AddScoped<StripeCheckoutService>();
builder.Services.AddScoped<StripeConnectService>();
builder.Services.AddScoped<EmailSeguridadService>();
builder.Services.AddScoped<CookieRevalidationEvents>();
builder.Services.AddScoped<SessionVersionCookieEvents>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<EnvioCorreoCitasService>();
builder.Services.AddSingleton<NotificationWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NotificationWorker>());
builder.Services.AddHostedService<RecordatorioCorreoWorker>();
builder.Services.AddAuthentication(o => {
    o.DefaultAuthenticateScheme = "CiticioSelector";
    o.DefaultChallengeScheme = "CiticioSelector";
    o.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddPolicyScheme("CiticioSelector", "Selección por área CITICIO", o => {
    o.ForwardDefaultSelector = ctx => {
        var path = ctx.Request.Path;
        if (path.StartsWithSegments("/api/empresa") || path.StartsWithSegments("/empresa/panel") || path.StartsWithSegments("/EmpresaSuscripcion")) return "Citicio.Empresa";
        if (path.StartsWithSegments("/api/auth") || path.StartsWithSegments("/api/cliente")) return "Citicio.Clientes";
        return CookieAuthenticationDefaults.AuthenticationScheme;
    };
})
.AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o => {
    o.LoginPath = "/Cuenta/Ingresar";
    o.AccessDeniedPath = "/Cuenta/SinPermiso";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    o.Cookie.Name = "Citicio.Auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.EventsType = typeof(CookieRevalidationEvents);
})
.AddCookie("Citicio.Empresa", o => {
    o.LoginPath = "/empresa/index.html";
    o.ExpireTimeSpan = TimeSpan.FromHours(6);
    o.SlidingExpiration = true;
    o.Cookie.Name = "Citicio.Empresa.Sesion";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.EventsType = typeof(SessionVersionCookieEvents);
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
})
.AddCookie("Citicio.Clientes", o => {
    o.LoginPath = "/clientes";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    o.Cookie.Name = "Citicio.Clientes.Auth";
    o.Cookie.HttpOnly = true;
    o.EventsType = typeof(SessionVersionCookieEvents);
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
});
builder.Services.AddAuthorization(o =>
    o.AddPolicy("Cliente", p => p.RequireAuthenticatedUser().RequireRole("cliente")));
var app = builder.Build();

if (args.Contains("--crear-admin", StringComparer.OrdinalIgnoreCase)) {
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<CuentaService>().CrearAdminInicialAsync(
        Environment.GetEnvironmentVariable("CITICIO_ADMIN_EMAIL"),
        Environment.GetEnvironmentVariable("CITICIO_ADMIN_PASSWORD"));
    return;
}
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); }
// En desarrollo se usa HTTP local (5197); solo redirigir a HTTPS cuando
// el despliegue disponga del endpoint HTTPS correspondiente.
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.UseMiddleware<EmpresaApiExceptionMiddleware>();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<SuscripcionGateMiddleware>();
app.UseRateLimiter();
// Antiforgery para las API minimal del módulo de clientes.
app.Use(async (ctx, next) => {
    var p = ctx.Request.Path;
    if ((p.StartsWithSegments("/api/auth") || p.StartsWithSegments("/api/cliente")) &&
        !HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && !HttpMethods.IsOptions(ctx.Request.Method)) {
        try { await ctx.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx); }
        catch (AntiforgeryValidationException) {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { error = "Sesión de seguridad vencida. Actualiza la página." });
            return;
        }
    }
    await next(ctx);
});
app.MapControllers();
app.MapCatalogoEndpoints();
app.MapAutenticacionEndpoints();
app.MapClienteEndpoints();
app.MapControllerRoute(name:"default", pattern:"{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program { }
