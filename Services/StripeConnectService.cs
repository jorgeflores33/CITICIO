using System.Data;
using System.Net.Http.Headers;
using System.Text.Json;
using Citicio.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;

/// <summary>Onboarding alojado por Stripe; las cuentas bancarias de pago nunca se crean con datos de tarjeta en CITICIO.</summary>
public sealed class StripeConnectService(SqlStore db, IConfiguration cfg,IHttpClientFactory http)
{
    private static string Limpiar(string? valor) => (valor ?? "").Replace("\r", "").Replace("\n", "").Trim();
    private string Secret => Limpiar(cfg["Stripe:SecretKey"]);
    private string BaseUrl => Limpiar(cfg["Stripe:PublicBaseUrl"]).TrimEnd('/');
    private void ValidarConfiguracion()
    {
        var clavePrueba = Secret.StartsWith("sk_test_", StringComparison.Ordinal);
        var claveLive = Secret.StartsWith("sk_live_", StringComparison.Ordinal);
        if ((!clavePrueba && !claveLive) || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out var u)
            || !(u.Scheme == "https" || (clavePrueba && u.Scheme == "http" && u.IsLoopback)))
            throw new InvalidOperationException(
                "Stripe Connect: configura una clave sk_test_ y URL localhost para pruebas; en producción se requiere HTTPS.");
    }
    private async Task<JsonDocument> ApiAsync(HttpMethod method,string endpoint,Dictionary<string,string>? form=null)
    {
        using var req=new HttpRequestMessage(method,"https://api.stripe.com/v1/"+endpoint);
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Secret);
        if(form!=null)req.Content=new FormUrlEncodedContent(form);
        using var resp=await http.CreateClient().SendAsync(req);
        var body=await resp.Content.ReadAsStringAsync();
        if(!resp.IsSuccessStatusCode)
        {
            var code = "stripe_error";
            try
            {
                using var detalle = JsonDocument.Parse(body);
                if(detalle.RootElement.TryGetProperty("error", out var error) &&
                   error.TryGetProperty("code", out var codigo))
                    code = codigo.GetString() ?? code;
            }
            catch(JsonException) { /* no se filtra el cuerpo completo de Stripe */ }
            code = System.Text.RegularExpressions.Regex.Replace(code, @"[^a-zA-Z0-9_\-]", "");
            throw new InvalidOperationException($"Stripe Connect devolvió HTTP {(int)resp.StatusCode}; código={code}.");
        }
        return JsonDocument.Parse(body);
    }
    private async Task<DbRow> ObtenerSucursalAsync(int usuario,int sucursal)
    {
        await using var cn=await db.OpenAsync();
        var rows=await SqlStore.ReadTextAsync(cn,null,@"SELECT TOP(1) cs.id_sucursal,cs.stripe_account_id
          FROM dbo.citicio_cuentas_sucursal cs
          JOIN dbo.sucursales s ON s.id_sucursal=cs.id_sucursal
          JOIN dbo.empleados e ON e.id_empresa=s.id_empresa
          JOIN dbo.usuarios u ON u.id_empleado=e.id_empleado
          JOIN dbo.roles r ON r.id_rol=u.id_rol AND r.nombre_rol='admin_empresa'
          WHERE u.id_usuario=@usuario AND cs.id_sucursal=@sucursal",
          SqlStore.P(("usuario",usuario),("sucursal",sucursal)));
        return rows.FirstOrDefault()??throw new InvalidOperationException("La sucursal no tiene cuenta bancaria registrada o no pertenece a tu empresa.");
    }
    public async Task<string> CrearOnboardingAsync(int usuario,int sucursal,bool panelEmpresa=false)
    {
        ValidarConfiguracion();
        var row=await ObtenerSucursalAsync(usuario,sucursal);
        var account=row.S("stripe_account_id");
        if(string.IsNullOrWhiteSpace(account))
        {
            using var doc=await ApiAsync(HttpMethod.Post,"accounts",new(){
                ["type"]="express",["country"]="MX",
                ["capabilities[card_payments][requested]"]="true",
                ["capabilities[transfers][requested]"]="true",
                ["metadata[citicio_id_sucursal]"]=sucursal.ToString()
            });
            account=doc.RootElement.GetProperty("id").GetString()??throw new InvalidOperationException("Stripe no devolvió cuenta.");
            await db.CallAsync("dbo.sp_citicio_stripe_sucursal_actualizar",SqlStore.P(
                ("id_usuario_actor",usuario),("id_sucursal",sucursal),
                ("stripe_account_id",account),("charges_enabled",false)));
        }
        var regresar=panelEmpresa ? $"{BaseUrl}/api/empresa/stripe/regresar/{sucursal}"
            : $"{BaseUrl}/CuentaStripe/Regresar?id={sucursal}";
        using var link=await ApiAsync(HttpMethod.Post,"account_links",new(){
            ["account"]=account,["type"]="account_onboarding",
            ["return_url"]=regresar,
            ["refresh_url"]=regresar
        });
        var url=link.RootElement.GetProperty("url").GetString();
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https")
            throw new InvalidOperationException("Stripe no proporcionó un enlace seguro de onboarding.");
        return url!;
    }
    public async Task<bool> VerificarAsync(int usuario,int sucursal)
    {
        ValidarConfiguracion();
        var row=await ObtenerSucursalAsync(usuario,sucursal);
        var account=row.S("stripe_account_id");
        if(!account.StartsWith("acct_",StringComparison.Ordinal))return false;
        using var doc=await ApiAsync(HttpMethod.Get,"accounts/"+Uri.EscapeDataString(account));
        var enabled=doc.RootElement.GetProperty("charges_enabled").GetBoolean();
        await db.CallAsync("dbo.sp_citicio_stripe_sucursal_actualizar",SqlStore.P(
           ("id_usuario_actor",usuario),("id_sucursal",sucursal),
           ("stripe_account_id",account),("charges_enabled",enabled)));
        return enabled;
    }
}
