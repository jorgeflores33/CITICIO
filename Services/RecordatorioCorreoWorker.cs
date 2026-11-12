using System.Text;
using Citicio.Data;

namespace Citicio.Services;

// Recordatorios por e-mail, sin nuevas tablas ni permisos de WhatsApp.
// En esta version de prueba la proteccion de duplicados se guarda en LocalAppData del servidor.
// Ejecutar una sola instancia de CITICIO; para produccion usar una cola transaccional en SQL.
public sealed class RecordatorioCorreoWorker(IServiceScopeFactory scopes, IConfiguration config,
    EnvioCorreoCitasService correo, ILogger<RecordatorioCorreoWorker> logger) : BackgroundService
{
    private readonly HashSet<string> _enviados = new(StringComparer.Ordinal);
    private static readonly TimeZoneInfo Zona = BuscarZona();
    private static TimeZoneInfo BuscarZona()
    {
        foreach (var nombre in new[] { "Central Standard Time (Mexico)", "America/Mexico_City" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(nombre); }
            catch (TimeZoneNotFoundException) { }
        }
        throw new InvalidOperationException("No se encontro la zona horaria de Mexico.");
    }
    private static DateTime Ahora() => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Zona).DateTime;
    private string Ruta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CITICIO", "recordatorios_correo_enviados.txt");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue<bool>("Notify:Enabled") ||
            !config.GetValue<bool?>("Notify:EmailRemindersEnabled").GetValueOrDefault(true))
        {
            logger.LogInformation("Recordatorios por correo desactivados en configuracion.");
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            if (File.Exists(Ruta))
                foreach (var line in File.ReadLines(Ruta))
                    if (!string.IsNullOrWhiteSpace(line)) _enviados.Add(line.Trim());
        }
        catch (Exception ex)
        {
            // Sin un historial fiable no enviar: reiniciar aqui podria duplicar correos masivamente.
            logger.LogError(ex, "No se pudo leer el historial local de recordatorios. No se enviaran recordatorios.");
            return;
        }
        var intervalo = TimeSpan.FromSeconds(Math.Clamp(
            config.GetValue<int?>("Notify:EmailReminderPollSeconds") ?? 60, 15, 600));
        using var timer = new PeriodicTimer(intervalo);
        do
        {
            try { await EnviarRecordatoriosAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Error revisando recordatorios de citas por correo"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task EnviarRecordatoriosAsync(CancellationToken ct)
    {
        var ahora = Ahora();
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlStore>();
        List<DbRow> proximas;
        await using (var cn = await db.OpenAsync(ct))
        {
            // A partir de 10 horas antes de la cita; si el servidor estuvo apagado,
            // permite recuperarla hasta una hora antes. Solo citas con usuario de correo.
            proximas = await SqlStore.ReadTextAsync(cn, null, @"
                SELECT TOP (500) c.id_cita, c.hora_inicio, u.correo_acceso
                FROM dbo.citas c
                INNER JOIN dbo.usuarios u ON u.id_cliente = c.id_cliente
                INNER JOIN dbo.estados e ON e.id_estado = c.id_estado
                WHERE c.hora_inicio > @despues_de
                  AND c.hora_inicio <= @antes_de
                  AND e.nombre_estado IN (N'cita_pendiente', N'cita_confirmada')
                  AND u.correo_acceso IS NOT NULL
                ORDER BY c.hora_inicio, c.id_cita;",
                SqlStore.P(("despues_de", ahora.AddHours(1)),
                           ("antes_de", ahora.AddHours(10))), ct);
        }
        foreach (var cita in proximas)
        {
            ct.ThrowIfCancellationRequested();
            var citaId = cita.I("id_cita");
            var hora = cita.D("hora_inicio");
            var clave = $"{citaId}:{hora:yyyyMMddHHmmss}";
            if (_enviados.Contains(clave)) continue;
            var to = cita.S("correo_acceso").Trim();
            if (!System.Net.Mail.MailAddress.TryCreate(to, out _)) continue;
            try
            {
                var datos = (await db.CallAsync("dbo.sp_citicio_lecturas", SqlStore.P(
                    ("accion", "notificacion_detalle"), ("id1", citaId), ("id2", 0)), ct)).FirstOrDefault();
                if (datos is null || datos.S("nombre_estado") == "cita_cancelada" ||
                    datos.M("total_cita") <= 0m ||
                    datos.M("pago_confirmado") < datos.M("total_cita"))
                    continue; // Recordar solo citas totalmente pagadas.

                var asunto = $"CITICIO | Recordatorio de tu cita #{citaId}";
                var cuerpo = $"CITICIO - RECORDATORIO DE CITA\n\n" +
                    $"Tu cita confirmada se aproxima.\n" +
                    $"Cita: #{citaId}\nCliente: {datos.S("cliente")}\n" +
                    $"Empresa: {datos.S("empresa")}\nSucursal: {datos.S("sucursal")}\n" +
                    $"Profesional: {datos.S("empleado")}\n" +
                    $"Fecha: {datos.D("hora_inicio"):dd/MM/yyyy HH:mm}\n" +
                    $"Pago confirmado: {datos.M("pago_confirmado"):0.00} MXN\n\n" +
                    "Adjuntamos tu comprobante PDF con QR. Te esperamos en la sucursal.\n";
                await correo.EnviarAsync(to, asunto, cuerpo, citaId, datos, ct);
                // Solo marcar despues de que SendGrid acepto el mensaje.
                await File.AppendAllTextAsync(Ruta, clave + Environment.NewLine, Encoding.UTF8, ct);
                _enviados.Add(clave);
                logger.LogInformation("Recordatorio por correo enviado para cita {CitaId}", citaId);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "No se pudo enviar recordatorio de cita {CitaId}; se reintentara", citaId);
            }
        }
    }
}
