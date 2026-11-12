using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Citicio.Data;

namespace Citicio.Services;

// Compartido por la confirmacion del pago y los recordatorios, con el remitente de SendGrid ya configurado.
public sealed class EnvioCorreoCitasService(IConfiguration config, IHttpClientFactory http, TicketPdfService pdf)
{
    public async Task<string> EnviarAsync(string destinatario, string asunto, string cuerpo,
        int idCita, DbRow detalle, CancellationToken ct)
    {
        var apiKey = (config["SendGrid:ApiKey"] ?? "").Replace("\r", "").Replace("\n", "").Trim();
        var remitente = (config["SendGrid:FromEmail"] ?? "").Trim();
        if (!apiKey.StartsWith("SG.", StringComparison.Ordinal) ||
            !System.Net.Mail.MailAddress.TryCreate(remitente, out _) ||
            !System.Net.Mail.MailAddress.TryCreate(destinatario, out _))
            throw new InvalidOperationException("Configura SendGrid y un destinatario valido para correos de citas.");

        byte[]? adjunto = null;
        var baseUrl = config["Notify:PublicBaseUrl"] ?? config["Stripe:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var url) &&
            (url.Scheme == "https" || url.IsLoopback))
        {
            adjunto = pdf.Crear(idCita, detalle, baseUrl.TrimEnd('/') + "/Citas/Comprobante/" + idCita);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var mensaje = new Dictionary<string, object>
        {
            ["personalizations"] = new[] { new { to = new[] { new { email = destinatario } } } },
            ["from"] = new { email = remitente, name = config["SendGrid:FromName"] ?? "CITICIO" },
            ["subject"] = asunto,
            ["content"] = new[] { new { type = "text/plain", value = cuerpo } }
        };
        if (adjunto is not null)
            mensaje["attachments"] = new[] { new
            {
                content = Convert.ToBase64String(adjunto),
                filename = $"CITICIO-Cita-{idCita}.pdf",
                type = "application/pdf",
                disposition = "attachment"
            } };
        request.Content = new StringContent(JsonSerializer.Serialize(mensaje), Encoding.UTF8, "application/json");
        using var response = await http.CreateClient().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"SendGrid no acepto el correo de cita (HTTP {(int)response.StatusCode}).");
        return response.Headers.TryGetValues("X-Message-Id", out var values)
            ? values.First() : Guid.NewGuid().ToString("N");
    }
}
