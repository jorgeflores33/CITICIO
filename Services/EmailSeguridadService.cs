using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Citicio.Data;
using Microsoft.AspNetCore.WebUtilities;

namespace Citicio.Services;

/// <summary>Tokens aleatorios de un solo uso: el servidor guarda únicamente SHA-256 en SQL.</summary>
public sealed class EmailSeguridadService(SqlStore db, IConfiguration configuration, IHttpClientFactory clients,
    ILogger<EmailSeguridadService> logger)
{
    // Evita caracteres CR/LF en el header Authorization si la clave fue pegada en varias líneas.
    // Nunca se registra ni se muestra la clave API.
    private string ApiKey => (configuration["SendGrid:ApiKey"] ?? "").Replace("\r", "").Replace("\n", "").Trim();
    private string Remitente => (configuration["SendGrid:FromEmail"] ?? "").Trim();
    public bool Configurado => ApiKey.StartsWith("SG.", StringComparison.Ordinal)
        && System.Net.Mail.MailAddress.TryCreate(Remitente, out _);

    private static byte[] ObtenerHash(string token)
    {
        if (token.Length > 300 || token.Length < 30) throw new InvalidOperationException("Enlace inválido.");
        try
        {
            var bytes=WebEncoders.Base64UrlDecode(token);
            if(bytes.Length!=32)throw new InvalidOperationException("Enlace inválido.");
            return SHA256.HashData(bytes);
        }
        catch(FormatException){throw new InvalidOperationException("Enlace inválido.");}
    }

    public async Task SolicitarAsync(string correo,string proposito,string destinoBase,CancellationToken ct=default)
    {
        // Siempre se muestra el mismo resultado al visitante, exista o no el correo.
        if (!Configurado) throw new InvalidOperationException(
            "Falta configurar SendGrid:ApiKey (SG...) o SendGrid:FromEmail (remitente válido y verificado).");
        correo=correo.Trim().ToLowerInvariant();
        var usuario=(await db.CallAsync("dbo.sp_usuarios",SqlStore.P(("accion","obtener_correo"),("correo_acceso",correo)),ct)).FirstOrDefault();
        if(usuario is null) return;
        var token=WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var hash=ObtenerHash(token);
        var minutos=proposito=="recuperar_clave"?30:120;
        await db.CallAsync("dbo.sp_seguridad_token_crear",SqlStore.P(
            ("id_usuario",usuario.I("id_usuario")),("proposito",proposito),
            ("hash_token",hash),("vigencia_minutos",minutos)),ct);
        var url=destinoBase+(destinoBase.Contains('?')?'&':'?')+"token="+Uri.EscapeDataString(token);
        var asunto=proposito=="recuperar_clave"?"CITICIO: restablecer contraseña":"CITICIO: confirmar correo";
        var texto=$"Este enlace de CITICIO es privado y de un solo uso. Caduca en {minutos} minutos.\n\n{url}\n\nSi no solicitaste esta operación, ignora el correo.";
        try {await EnviarAsync(correo,asunto,texto,ct);}
        catch(Exception ex){logger.LogError(ex,"No se pudo enviar el correo de seguridad");throw new InvalidOperationException("El correo de seguridad no pudo enviarse. Revisa la configuración SendGrid.");}
    }

    public Task ConfirmarCorreoAsync(string token,CancellationToken ct=default) =>
        db.CallAsync("dbo.sp_seguridad_confirmar_correo",SqlStore.P(("hash_token",ObtenerHash(token))),ct);

    public async Task RestablecerAsync(string token,string nuevoHash,CancellationToken ct=default)
    {
        await db.CallAsync("dbo.sp_seguridad_restablecer_contrasena",SqlStore.P(
            ("hash_token",ObtenerHash(token)),("nuevo_hash",nuevoHash)),ct);
    }

    private async Task EnviarAsync(string correo,string asunto,string texto,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.sendgrid.com/v3/mail/send");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",ApiKey);
        request.Content=new StringContent(JsonSerializer.Serialize(new{
            personalizations=new[]{new{to=new[]{new{email=correo}}}},
            from=new{email=Remitente,name=configuration["SendGrid:FromName"]??"CITICIO"},
            subject=asunto,content=new[]{new{type="text/plain",value=texto}}
        }),Encoding.UTF8,"application/json");
        using var response=await clients.CreateClient().SendAsync(request,ct);
        if(!response.IsSuccessStatusCode)
        {
            // Código solamente. No se imprimen los datos de la cuenta ni el token de recuperación.
            logger.LogWarning("SendGrid rechazó correo de seguridad: HTTP {Estado}", (int)response.StatusCode);
            var motivo = (int)response.StatusCode switch
            {
                400 => "Verifica la estructura del mensaje y el remitente.",
                401 => "La clave API de SendGrid no es válida o está revocada.",
                403 => "SendGrid no autorizó el envío. Verifica Sender Authentication y permisos Mail Send.",
                429 => "SendGrid limitó temporalmente las solicitudes; intenta más tarde.",
                _ => "Revisa el estado de tu cuenta y los registros de SendGrid."
            };
            throw new InvalidOperationException($"SendGrid HTTP {(int)response.StatusCode}. {motivo}");
        }
    }
}
