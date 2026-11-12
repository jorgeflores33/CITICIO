using System.Threading.Channels;
using Citicio.Data;

namespace Citicio.Services;

// Correo de confirmacion y comprobantes: usa el trabajo persistente de SQL Server.
// El webhook firmado de CITICIO Citas despierta la cola; los reintentos siguen a cargo de SQL.
public sealed class NotificationWorker(IServiceScopeFactory scopes, IConfiguration config,
    EnvioCorreoCitasService correo, ILogger<NotificationWorker> logger) : BackgroundService
{
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

    private readonly Channel<bool> _avisos = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });

    public void ProcesarAhora() => _avisos.Writer.TryWrite(true);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue<bool>("Notify:Enabled"))
        {
            logger.LogWarning("Notificaciones por correo desactivadas: configura Notify:Enabled=true.");
            return;
        }
        var intervalo = TimeSpan.FromSeconds(Math.Clamp(config.GetValue<int?>("Notify:PollSeconds") ?? 5, 2, 300));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcesarColaAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Error en la cola de correos de CITICIO"); }
            using var espera = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            espera.CancelAfter(intervalo);
            try { await _avisos.Reader.WaitToReadAsync(espera.Token); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            while (_avisos.Reader.TryRead(out _)) { }
        }
    }

    private async Task ProcesarColaAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlStore>();
        var lease = Guid.NewGuid();
        await db.CallAsync("dbo.sp_notificaciones_reactivar_tickets", null, ct);
        var jobs = await db.CallAsync("dbo.sp_notificaciones_tomar_pendientes",
            SqlStore.P(("fecha_actual", Ahora()), ("lease", lease)), ct);
        foreach (var job in jobs)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var tipo = job.S("tipo");
                var canal = job.S("canal");
                // Los recordatorios ahora son programados por RecordatorioCorreoWorker para
                // llegar a todos los clientes con e-mail, tengan o no WhatsApp habilitado.
                // No se usa Twilio para confirmaciones o recordatorios de esta version.
                if (tipo == "recordatorio_10h" || canal != "correo")
                {
                    await FinalizarAsync(db, job, lease, "cancelado", null,
                        tipo == "recordatorio_10h"
                          ? "Recordatorio trasladado al programador de correo"
                          : "Notificaciones habilitadas solamente por correo", ct);
                    continue;
                }

                var citaId = job.I("id_cita");
                var detalle = (await db.CallAsync("dbo.sp_citicio_lecturas", SqlStore.P(
                    ("accion", "notificacion_detalle"), ("id1", citaId),
                    ("id2", job.I("id_pago"))), ct)).FirstOrDefault();
                var ticket = tipo == "ticket_pago";
                var pagoCompleto = detalle is not null && detalle.M("total_cita") > 0m &&
                                   detalle.M("pago_confirmado") >= detalle.M("total_cita");
                var confirmado = tipo.Contains("confirm", StringComparison.OrdinalIgnoreCase);
                bool valido = detalle is not null &&
                    (ticket ? pagoCompleto && job.I("id_pago") > 0
                            : detalle.I("version_cita") == job.I("version_cita") &&
                              detalle.S("nombre_estado") != "cita_cancelada") &&
                    (!confirmado || pagoCompleto);
                if (!valido)
                {
                    await FinalizarAsync(db, job, lease, "cancelado", null,
                        "El estado, version o pago de la cita no permiten enviar este mensaje", ct);
                    continue;
                }
                var asunto = ticket ? $"CITICIO | Pago confirmado y ticket de tu cita #{citaId}"
                                     : $"CITICIO | Confirmacion de tu cita #{citaId}";
                var cuerpo = $"CITICIO\n\nCita: #{citaId}\nCliente: {detalle!.S("cliente")}\n" +
                    $"Empresa: {detalle.S("empresa")}\nSucursal: {detalle.S("sucursal")}\n" +
                    $"Profesional: {detalle.S("empleado")}\nFecha: {detalle.D("hora_inicio"):dd/MM/yyyy HH:mm}\n" +
                    (ticket ? $"Pago confirmado: {detalle.M("pago_confirmado"):0.00} MXN\n" : "") +
                    "Tu comprobante PDF con codigo QR va adjunto. Conserva este correo para tu cita.\n";
                var proveedor = await correo.EnviarAsync(job.S("destinatario"), asunto, cuerpo,
                    citaId, detalle, ct);
                await FinalizarAsync(db, job, lease, "enviado", proveedor, null, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "No se pudo procesar notificacion {Id}", job.I("id_notificacion"));
                await db.CallAsync("dbo.sp_notificaciones_marcar_error", SqlStore.P(
                    ("id_notificacion", job.I("id_notificacion")), ("lease", lease),
                    ("fecha_actual", Ahora()), ("error", ex.Message)), ct);
            }
        }
    }

    private static Task FinalizarAsync(SqlStore db, DbRow job, Guid lease, string estado,
        string? idProveedor, string? error, CancellationToken ct) =>
        db.CallAsync("dbo.sp_notificaciones_finalizar", SqlStore.P(
            ("estado", estado), ("fecha_actual", Ahora()),
            ("id_mensaje_proveedor", idProveedor), ("error", error),
            ("id_notificacion", job.I("id_notificacion")), ("lease", lease)), ct);
}
