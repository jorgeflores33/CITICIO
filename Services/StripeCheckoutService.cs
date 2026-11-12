using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Citicio.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;
public sealed class StripeCheckoutService(SqlStore db,IConfiguration config,IHttpClientFactory http) {
    // Evita que invoice.paid y checkout.session.completed registren simultáneamente
    // el mismo cobro dentro de una instancia CITICIO (SQL mantiene la autoridad).
    private static readonly SemaphoreSlim ConfirmacionLock = new(1,1);
    // Valores pegados con saltos de línea provocaban "New-line characters are not allowed in header values".
    // Retiramos CR/LF; no registramos ni devolvemos los secretos.
    private static string TokenConfigurado(string? valor) => (valor ?? "").Replace("\r", "").Replace("\n", "").Trim();
    private string Secret => TokenConfigurado(config["Stripe:SecretKey"]);
    private string Webhook => TokenConfigurado(config["Stripe:WebhookSecret"]);
    private string ConnectWebhook => TokenConfigurado(config["Stripe:ConnectWebhookSecret"]);
    // Segunda cuenta Stripe: TODOS los cobros con tarjeta de citas se registran aquí.
    // No sustituye las claves ni el webhook de las suscripciones.
    private string CitasSecret => TokenConfigurado(config["StripeCitas:SecretKey"]);
    private string CitasWebhook => TokenConfigurado(config["StripeCitas:WebhookSecret"]);
    // Stripe Checkout alojado NO requiere la clave publicable (pk_test).
    // El ID de Tarjeta se busca en el catálogo SQL si no se configuró explícitamente.
    private string? ErrorBasicoStripe() {
        if (!Secret.Trim().StartsWith("sk_test_",StringComparison.Ordinal) &&
            !Secret.Trim().StartsWith("sk_live_",StringComparison.Ordinal))
            return "Falta Stripe:SecretKey (sk_test_) en los secretos locales de CITICIO.";
        if (!Webhook.Trim().StartsWith("whsec_",StringComparison.Ordinal))
            return "Falta Stripe:WebhookSecret (whsec_) en los secretos locales de CITICIO.";
        if (!Uri.TryCreate(config["Stripe:PublicBaseUrl"],UriKind.Absolute,out var url) ||
            (url.Scheme!="https" && !(url.Scheme=="http" && url.IsLoopback)))
            return "Falta Stripe:PublicBaseUrl. En pruebas locales utiliza http://localhost:5197.";
        return null;
    }
    public bool Disponible => ErrorBasicoStripe() is null;
    private string? ErrorBasicoCitas() {
        if(!CitasSecret.StartsWith("sk_test_", StringComparison.Ordinal) &&
           !CitasSecret.StartsWith("sk_live_", StringComparison.Ordinal))
            return "Falta StripeCitas:SecretKey. Configura la clave secreta de la cuenta CITICIO Citas.";
        if(!CitasWebhook.StartsWith("whsec_", StringComparison.Ordinal))
            return "Falta StripeCitas:WebhookSecret. Configura el webhook propio de la cuenta CITICIO Citas.";
        if(!Uri.TryCreate(config["Stripe:PublicBaseUrl"],UriKind.Absolute,out var url) ||
           (url.Scheme!="https" && !(url.Scheme=="http" && url.IsLoopback)))
            return "Configura Stripe:PublicBaseUrl (en local: http://localhost:5197).";
        return null;
    }
    public bool ClientesDisponible => ErrorBasicoCitas() is null;

    // No modifica ni inserta datos: consulta el catálogo ya existente.
    private async Task<int> MetodoTarjetaAsync() {
        var configurado=config.GetValue<int>("Stripe:PaymentMethodId");
        await using var cn=await db.OpenAsync();
        var metodos=await SqlStore.ReadTextAsync(cn,null,@"
            SELECT TOP (1) id_metodo_pago FROM dbo.metodos_pago
            WHERE (@id_configurado > 0 AND id_metodo_pago=@id_configurado
                   AND LOWER(nombre_metodo) LIKE N'%tarjeta%')
               OR (@id_configurado <= 0 AND LOWER(nombre_metodo) LIKE N'%tarjeta%')
            ORDER BY CASE WHEN LOWER(LTRIM(RTRIM(nombre_metodo)))=N'tarjeta' THEN 0 ELSE 1 END,
                     id_metodo_pago;",
            SqlStore.P(("id_configurado",configurado)));
        if(metodos.Count==0)
            throw new InvalidOperationException("No se encontró el método de pago Tarjeta en dbo.metodos_pago. Verifica el catálogo o Stripe:PaymentMethodId.");
        return metodos[0].I("id_metodo_pago");
    }
    public async Task<string?> DiagnosticoMensualidadAsync() {
        var basico=ErrorBasicoStripe();
        if(basico is not null)return basico;
        try {await MetodoTarjetaAsync();return null;}
        catch(SqlException) {return "No fue posible consultar el método de pago Tarjeta en SQL Server.";}
        catch(InvalidOperationException ex) {return ex.Message;}
    }
    // La autorización, titularidad y saldo se obtienen exclusivamente de SQL.
    // Sin Stripe-Account y sin transfer_data: el cobro llega a la cuenta CITICIO Citas.
    public async Task<string> CrearCheckoutAsync(int citaId,int idCliente,int idClienteUsuario,bool origenSpa=false) {
        var error=ErrorBasicoCitas();
        if(error is not null)throw new InvalidOperationException(error);
        if(citaId<=0 || idCliente<=0 || idClienteUsuario<=0)
            throw new InvalidOperationException("La cita o la sesión de cliente no son válidas.");
        var rows=await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","checkout_cliente"),("id_usuario_actor",idClienteUsuario),("id1",citaId)));
        var item=rows.FirstOrDefault();
        if(item is null)throw new InvalidOperationException("La cita no pertenece a este usuario.");
        if(item.S("nombre_estado") is not ("cita_pendiente" or "cita_confirmada"))
            throw new InvalidOperationException("La cita no está disponible para pago.");
        var faltante=item.M("total")-item.M("pagado");
        if(faltante<=0m)throw new InvalidOperationException("La cita ya está pagada o no tiene saldo pendiente.");
        var centavos=decimal.Round(faltante*100m,0,MidpointRounding.AwayFromZero);
        if(centavos<50m || centavos>9999999999m)
            throw new InvalidOperationException("El importe de la cita está fuera del rango permitido para Checkout.");
        var baseUrl=config["Stripe:PublicBaseUrl"]!.TrimEnd('/');
        var idMetodo=await MetodoTarjetaAsync();
        var form=new Dictionary<string,string> {
            ["mode"]="payment",
            ["success_url"]=origenSpa
                ? $"{baseUrl}/clientes?checkout=verificando&citaId={citaId}#citas"
                : $"{baseUrl}/Pago/Resultado?resultado=verificando&citaId={citaId}&session_id={{CHECKOUT_SESSION_ID}}",
            ["cancel_url"]=origenSpa
                ? $"{baseUrl}/clientes?checkout=cancelado#citas"
                : $"{baseUrl}/Pago/Resultado?resultado=cancelado",
            ["client_reference_id"]=citaId.ToString(CultureInfo.InvariantCulture),
            ["metadata[tipo]"]="cita_citicio",
            ["metadata[id_cita]"]=citaId.ToString(CultureInfo.InvariantCulture),
            ["metadata[id_metodo_pago]"]=idMetodo.ToString(CultureInfo.InvariantCulture),
            ["payment_intent_data[metadata][tipo]"]="cita_citicio",
            ["payment_intent_data[metadata][id_cita]"]=citaId.ToString(CultureInfo.InvariantCulture),
            ["line_items[0][price_data][currency]"]="mxn",
            ["line_items[0][price_data][unit_amount]"]=centavos.ToString("0",CultureInfo.InvariantCulture),
            ["line_items[0][price_data][product_data][name]"]=$"CITICIO | Cita #{citaId}",
            ["line_items[0][quantity]"]="1"
        };
        using var request=new HttpRequestMessage(HttpMethod.Post,"https://api.stripe.com/v1/checkout/sessions") {
            Content=new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",CitasSecret);
        // Reintentar una conexión no crea sesiones duplicadas dentro de esta ventana corta.
        var ventana=DateTimeOffset.UtcNow.ToUnixTimeSeconds()/600L;
        request.Headers.TryAddWithoutValidation("Idempotency-Key",
            $"citicio-citas-central-v1-{citaId}-{centavos:0}-{ventana}");
        using var response=await http.CreateClient().SendAsync(request);
        if(!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ErrorStripeCheckoutAsync(response));
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url=JsonString(doc.RootElement,"url");
        if(string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url,UriKind.Absolute,out var uri) ||
           uri.Scheme!="https" || uri.Host!="checkout.stripe.com")
            throw new InvalidOperationException("Stripe devolvió una URL de pago inválida.");
        return url;
    }

    // La empresa paga una sola mensualidad fija. Opcional: Stripe Billing recurrente.
    // No capturamos ni guardamos números de tarjeta; Checkout los procesa en Stripe.
    public async Task<string> CrearSuscripcionCheckoutAsync(int idUsuario,bool automatico,bool panelEmpresa=false) {
        if(!Disponible)throw new InvalidOperationException("La pasarela Stripe no está configurada.");
        if(automatico) {
            var estado=(await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(
                ("id_usuario_actor",idUsuario)))).FirstOrDefault();
            var actual=estado?.S("stripe_subscription_id")??"";
            if(actual.StartsWith("sub_",StringComparison.Ordinal)) {
                // Consultar a Stripe: la base no determina por sí sola si la suscripción fue cancelada.
                using var solicitud=new HttpRequestMessage(HttpMethod.Get,
                    "https://api.stripe.com/v1/subscriptions/"+Uri.EscapeDataString(actual));
                solicitud.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Secret);
                using var respuesta=await http.CreateClient().SendAsync(solicitud);
                if(!respuesta.IsSuccessStatusCode)
                    throw new InvalidOperationException("No fue posible verificar la suscripción existente en Stripe.");
                using var verificacion=JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
                var item=verificacion.RootElement;
                var status=JsonString(item,"status");
                if(status=="canceled") {
                    await db.CallAsync("dbo.sp_citicio_suscripcion_cancelacion_confirmar",SqlStore.P(
                        ("stripe_subscription_id",actual)));
                } else if((status is "active" or "trialing") &&
                          item.TryGetProperty("cancel_at_period_end",out var proximo) &&
                          proximo.ValueKind==JsonValueKind.True) {
                    using var reactivar=new HttpRequestMessage(HttpMethod.Post,
                        "https://api.stripe.com/v1/subscriptions/"+Uri.EscapeDataString(actual)) {
                        Content=new FormUrlEncodedContent(new Dictionary<string,string>{["cancel_at_period_end"]="false"})
                    };
                    reactivar.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Secret);
                    using var resultado=await http.CreateClient().SendAsync(reactivar);
                    if(!resultado.IsSuccessStatusCode)
                        throw new InvalidOperationException("Stripe no pudo reactivar la próxima renovación.");
                    await db.CallAsync("dbo.sp_citicio_suscripcion_preferencia",SqlStore.P(
                        ("id_usuario_actor",idUsuario),("renovacion_automatica",true)));
                    var retorno=(config["Stripe:PublicBaseUrl"]??"").TrimEnd('/');
                    return retorno+(panelEmpresa?"/EmpresaSuscripcion":"/Suscripcion");
                } else {
                    throw new InvalidOperationException("Ya existe una suscripción en Stripe. Verifica su estado antes de intentar otra.");
                }
            }
        }
        // Si Stripe cobró y el listener local no estaba conectado, nunca emitir otra cuota
        // antes de intentar conciliar el pago ya realizado.
        var empresaActual=await EmpresaDeUsuarioAsync(idUsuario);
        if(await ConciliarPagoAnteriorAsync(empresaActual)) {
            var urlExistente=(config["Stripe:PublicBaseUrl"]??"").TrimEnd('/');
            return urlExistente+(panelEmpresa?"/EmpresaSuscripcion":"/Suscripcion");
        }
        var idMetodo=await MetodoTarjetaAsync();
        var charge=(await db.CallAsync("dbo.sp_citicio_suscripcion_emitir",SqlStore.P(
            ("id_usuario_actor",idUsuario)))).FirstOrDefault()
            ??throw new InvalidOperationException("No se pudo emitir la mensualidad.");
        var monto=charge.M("monto");if(monto<=0)throw new InvalidOperationException("Tarifa inválida.");
        var cobro=charge.I("id_cobro_empresa");
        var baseUrl=config["Stripe:PublicBaseUrl"]!.TrimEnd('/');
        var datos=new Dictionary<string,string> {
            ["mode"]=automatico?"subscription":"payment",
            ["success_url"]=$"{baseUrl}/{(panelEmpresa ? "EmpresaSuscripcion" : "Suscripcion")}/Resultado?resultado=exito&session_id={{CHECKOUT_SESSION_ID}}",
            ["cancel_url"]=$"{baseUrl}/{(panelEmpresa ? "EmpresaSuscripcion" : "Suscripcion")}/Resultado?resultado=cancelado",
            ["client_reference_id"]=cobro.ToString(),
            ["metadata[tipo]"]="suscripcion_citicio",
            ["metadata[id_cobro_empresa]"]=cobro.ToString(),
            ["metadata[id_metodo_pago]"]=idMetodo.ToString(),
            ["line_items[0][price_data][currency]"]="mxn",
            ["line_items[0][price_data][unit_amount]"]=decimal.Round(monto*100,0,MidpointRounding.AwayFromZero).ToString("0",CultureInfo.InvariantCulture),
            ["line_items[0][price_data][product_data][name]"]="Mensualidad CITICIO",
            ["line_items[0][quantity]"]="1"
        };
        if(automatico) {
            datos["line_items[0][price_data][recurring][interval]"]="month";
            datos["subscription_data[metadata][id_empresa]"]=charge.I("id_empresa").ToString();
        }
        // Stripe Billing emite las facturas recurrentes de modo subscription.
        // Para una mensualidad manual, Checkout puede emitir un recibo/factura Stripe.
        // Una factura de Stripe no es por si sola un CFDI timbrado ante SAT.
        if (!automatico && config.GetValue<bool>("Stripe:CreateCheckoutInvoices")) {
            datos["invoice_creation[enabled]"]="true";
            datos["billing_address_collection"]="required";
        }
        if (config.GetValue<bool>("Stripe:CollectTaxId")) {
            datos["tax_id_collection[enabled]"]="true";
        }
        // Solo activar cuando CITICIO haya configurado fiscalmente Stripe Tax.
        // No aplicar a cargos directos de sucursales sin confirmar su responsabilidad fiscal.
        if (config.GetValue<bool>("Stripe:AutomaticTaxEnabled")) {
            datos["automatic_tax[enabled]"]="true";
            datos["billing_address_collection"]="required";
        }
        using var req=new HttpRequestMessage(HttpMethod.Post,"https://api.stripe.com/v1/checkout/sessions"){
            Content=new FormUrlEncodedContent(datos)
        };
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Secret);
        // Reintentos no duplican el Checkout si la conexión HTTP se corta.
        req.Headers.TryAddWithoutValidation("Idempotency-Key",$"citicio-sus-v2-{cobro}-{(automatico?1:0)}");
        using var response=await http.CreateClient().SendAsync(req);
        if(!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ErrorStripeCheckoutAsync(response));
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url=doc.RootElement.GetProperty("url").GetString();
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.Host!="checkout.stripe.com")
            throw new InvalidOperationException("Checkout devolvió una URL inválida.");
        return url!;
    }
    // Stripe devuelve la causa concreta del rechazo dentro de { error: { code, param, message } }.
    // Nunca incluimos el Authorization header, la clave secreta ni el cuerpo completo.
    private static async Task<string> ErrorStripeCheckoutAsync(HttpResponseMessage response)
    {
        var codigo = "error_stripe";
        var parametro = "";
        var detalle = "";
        var cuerpo = await response.Content.ReadAsStringAsync();
        try
        {
            using var json = JsonDocument.Parse(cuerpo);
            if (json.RootElement.TryGetProperty("error", out var error))
            {
                codigo = JsonString(error, "code") ?? JsonString(error, "type") ?? codigo;
                parametro = JsonString(error, "param") ?? "";
                detalle = JsonString(error, "message") ?? "";
            }
        }
        catch (JsonException)
        {
            detalle = "Respuesta de Stripe no disponible en formato JSON.";
        }
        // Sanitizar posibles credenciales incluso si Stripe llegara a citarlas en el mensaje.
        detalle = System.Text.RegularExpressions.Regex.Replace(detalle,
            @"(?:sk_(?:test|live)_|rk_(?:test|live)_|whsec_)[A-Za-z0-9]+", "[clave oculta]");
        var idSolicitud = response.Headers.TryGetValues("Request-Id", out var ids)
            ? ids.FirstOrDefault() ?? "" : "";
        var codigoSeguro = System.Text.RegularExpressions.Regex.Replace(codigo, @"[^a-zA-Z0-9_\-]", "");
        var parametroSeguro = System.Text.RegularExpressions.Regex.Replace(parametro, @"[^a-zA-Z0-9_\[\]\.\-]", "");
        var idSeguro = System.Text.RegularExpressions.Regex.Replace(idSolicitud, @"[^a-zA-Z0-9_\-]", "");
        return $"Stripe Checkout HTTP {(int)response.StatusCode}; código={codigoSeguro}; " +
            (string.IsNullOrEmpty(parametroSeguro) ? "" : $"parámetro={parametroSeguro}; ") +
            (string.IsNullOrWhiteSpace(detalle) ? "Stripe rechazó la solicitud." : detalle) +
            (string.IsNullOrEmpty(idSeguro) ? "" : $" (Stripe Request-Id: {idSeguro})");
    }

    // Cancelar futuros cargos sin revocar la mensualidad ya pagada.
    public async Task DesactivarRenovacionAsync(int usuario)
    {
        if(!Disponible) throw new InvalidOperationException("Stripe no está configurado.");
        var estado=(await db.CallAsync("dbo.sp_citicio_suscripcion_estado",SqlStore.P(
            ("id_usuario_actor",usuario)))).FirstOrDefault();
        var id=estado?.S("stripe_subscription_id")??"";
        if(!id.StartsWith("sub_",StringComparison.Ordinal) || estado?.B("renovacion_automatica")!=true)
            throw new InvalidOperationException("La empresa no tiene renovación automática activa.");
        using var req=new HttpRequestMessage(HttpMethod.Post,
            "https://api.stripe.com/v1/subscriptions/"+Uri.EscapeDataString(id)) {
            Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"cancel_at_period_end","true"}})
        };
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Secret);
        using var resp=await http.CreateClient().SendAsync(req);
        if(!resp.IsSuccessStatusCode)throw new InvalidOperationException("Stripe no pudo cancelar la próxima renovación.");
        await db.CallAsync("dbo.sp_citicio_suscripcion_preferencia",SqlStore.P(
            ("id_usuario_actor",usuario),("renovacion_automatica",false)));
    }

    public bool FirmaValida(string content,string header) => VerificarFirma(content,header,Webhook);
    public bool FirmaCitasValida(string content,string header) => VerificarFirma(content,header,CitasWebhook);
    public bool FirmaConectadaValida(string content,string header) => VerificarFirma(content,header,ConnectWebhook);
    private static bool VerificarFirma(string content,string header,string secreto) {
        if(string.IsNullOrEmpty(secreto)||string.IsNullOrEmpty(header))return false;
        var parts=header.Split(',',StringSplitOptions.RemoveEmptyEntries).Select(p=>p.Trim().Split('=',2))
            .Where(p=>p.Length==2).ToArray();
        if(!long.TryParse(parts.FirstOrDefault(p=>p[0]=="t")?.ElementAtOrDefault(1),out var ts))return false;
        if(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds()-ts)>300)return false;
        var sigs=parts.Where(p=>p[0]=="v1").Select(p=>p[1]).ToArray();
        var expected=HMACSHA256.HashData(Encoding.UTF8.GetBytes(secreto),Encoding.UTF8.GetBytes($"{ts}.{content}"));
        foreach(var signature in sigs) {
            try {if(CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature),expected))return true;}
            catch(FormatException){}
        }
        return false;
    }
    // Comprobación independiente contra la API de Stripe (clave privada del servidor).
    // Los enlaces de regreso, los parámetros del navegador y los eventos sin firma
    // NUNCA pueden marcar una mensualidad como pagada.
    private async Task<JsonDocument> StripeGetAsync(string ruta)
    {
        using var req=new HttpRequestMessage(HttpMethod.Get,"https://api.stripe.com/v1/"+ruta);
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Secret);
        using var resp=await http.CreateClient().SendAsync(req);
        if(!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"No fue posible consultar Stripe para verificar el cobro (HTTP {(int)resp.StatusCode}).");
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    // El actor empresarial se resuelve SIEMPRE desde SQL, nunca desde un formulario.
    private async Task<int> EmpresaDeUsuarioAsync(int idUsuario)
    {
        await using var cn=await db.OpenAsync();
        var rows=await SqlStore.ReadTextAsync(cn,null,@"
            SELECT TOP(1) e.id_empresa FROM dbo.usuarios u
            JOIN dbo.empleados e ON e.id_empleado=u.id_empleado
            JOIN dbo.roles r ON r.id_rol=u.id_rol
            WHERE u.id_usuario=@usuario AND r.nombre_rol=N'admin_empresa'",
            SqlStore.P(("usuario",idUsuario)));
        var empresa=rows.FirstOrDefault()?.I("id_empresa")??0;
        if(empresa<=0)throw new InvalidOperationException("El usuario no tiene permisos para conciliar cobros de empresa.");
        return empresa;
    }

    private enum ResultadoConciliacion { NoCoincide, YaRegistrado, Registrado }

    // Regreso seguro desde Stripe Checkout: no acepta un resultado=exito como prueba.
    // Solo Stripe API + empresa del usuario + importe y cobro SQL permiten habilitar el panel.
    public async Task<bool> ConciliarSesionRetornoAsync(string? idSesion, int idEmpresa)
    {
        if (idEmpresa <= 0 || string.IsNullOrWhiteSpace(idSesion) ||
            idSesion.Length > 160 || !idSesion.StartsWith("cs_", StringComparison.Ordinal) ||
            idSesion.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            return false;
        if (!Disponible) throw new InvalidOperationException("Stripe no está configurado para verificar el pago.");
        using var verificada = await StripeGetAsync("checkout/sessions/" + Uri.EscapeDataString(idSesion));
        var resultado = await ConfirmarSesionPagadaAsync(verificada.RootElement, idEmpresa);
        return resultado is ResultadoConciliacion.Registrado or ResultadoConciliacion.YaRegistrado;
    }

    // Recupera el primer pago incluso si el CLI perdió la conexión WebSocket.
    // Busca sesiones recientes y solo acepta las que Stripe declara COMPLETE y PAID.
    // Para empresas con gran volumen también se confirma automáticamente desde invoice.paid.
    public async Task<bool> ConciliarPagoAnteriorAsync(int idEmpresa)
    {
        if(idEmpresa<=0)throw new InvalidOperationException("Empresa inválida.");
        if(!Disponible)throw new InvalidOperationException("Configura primero la clave privada y la firma del webhook de Stripe.");
        var siguiente="";
        for(var pagina=0;pagina<3;pagina++)
        {
            var ruta="checkout/sessions?limit=100&status=complete"+
                (string.IsNullOrEmpty(siguiente)?"":"&starting_after="+Uri.EscapeDataString(siguiente));
            using var listado=await StripeGetAsync(ruta);
            var raiz=listado.RootElement;
            if(!raiz.TryGetProperty("data",out var sesiones) || sesiones.ValueKind!=JsonValueKind.Array)
                throw new InvalidOperationException("Stripe no devolvió sesiones para conciliar.");
            var ultima="";
            foreach(var session in sesiones.EnumerateArray())
            {
                ultima=JsonString(session,"id")??"";
                if(await ConfirmarSesionPagadaAsync(session,idEmpresa)==ResultadoConciliacion.Registrado) return true;
            }
            if(!raiz.TryGetProperty("has_more",out var mas) || mas.ValueKind!=JsonValueKind.True ||
               string.IsNullOrEmpty(ultima))break;
            siguiente=ultima;
        }
        return false;
    }

    // Aplica un cobro SOLO después de validar modo, moneda, referencia, empresa,
    // importe y estado verificados directamente contra Stripe + SQL Server.
    private async Task<ResultadoConciliacion> ConfirmarSesionPagadaAsync(JsonElement sesion,int? empresaEsperada=null)
    {
        if(JsonString(sesion,"status")!="complete" || JsonString(sesion,"payment_status")!="paid" ||
           JsonString(sesion,"currency")!="mxn")return ResultadoConciliacion.NoCoincide;
        var modo=JsonString(sesion,"mode");
        if(modo!="subscription" && modo!="payment")return ResultadoConciliacion.NoCoincide;
        var esLive=Secret.StartsWith("sk_live_",StringComparison.Ordinal);
        if(!sesion.TryGetProperty("livemode",out var live) ||
           (live.ValueKind!=JsonValueKind.True && live.ValueKind!=JsonValueKind.False) ||
           live.GetBoolean()!=esLive)return ResultadoConciliacion.NoCoincide;
        if(!sesion.TryGetProperty("metadata",out var metadata) ||
           JsonString(metadata,"tipo")!="suscripcion_citicio" ||
           !int.TryParse(JsonString(metadata,"id_cobro_empresa"),NumberStyles.None,CultureInfo.InvariantCulture,out var cobro) || cobro<=0)return ResultadoConciliacion.NoCoincide;
        if(JsonString(sesion,"client_reference_id")!=cobro.ToString(CultureInfo.InvariantCulture))return ResultadoConciliacion.NoCoincide;
        var sesionId=JsonString(sesion,"id");
        if(string.IsNullOrWhiteSpace(sesionId) || !sesionId.StartsWith("cs_",StringComparison.Ordinal))return ResultadoConciliacion.NoCoincide;
        if(!sesion.TryGetProperty("amount_total",out var total) || !total.TryGetInt64(out var centavos) || centavos<=0)return ResultadoConciliacion.NoCoincide;

        await ConfirmacionLock.WaitAsync();
        try
        {
        await using var cn=await db.OpenAsync();
        var pendientes=await SqlStore.ReadTextAsync(cn,null,@"
            SELECT TOP(1) c.id_empresa,c.monto,e.nombre_estado
            FROM dbo.cobros_empresas c
            JOIN dbo.estados e ON e.id_estado=c.id_estado
            WHERE c.id_cobro_empresa=@cobro",
            SqlStore.P(("cobro",cobro)));
        var fila=pendientes.FirstOrDefault();
        if(fila is null)return ResultadoConciliacion.NoCoincide;
        if(empresaEsperada.HasValue && fila.I("id_empresa")!=empresaEsperada.Value)return ResultadoConciliacion.NoCoincide;
        if(decimal.Round(fila.M("monto")*100m,0,MidpointRounding.AwayFromZero)!=centavos)
            throw new InvalidOperationException("El importe pagado en Stripe no coincide con la mensualidad emitida en CITICIO.");
        if(fila.S("nombre_estado")=="cobro_pagado")return ResultadoConciliacion.YaRegistrado; // Evitar contabilizar dos veces.
        await db.CallAsync("dbo.sp_citicio_suscripcion_confirmar",SqlStore.P(
          ("id_cobro_empresa",cobro),
          ("id_metodo_pago",await MetodoTarjetaAsync()),
          ("monto",centavos/100m),
          ("referencia",sesionId),
          ("proveedor_pago","stripe"),
          ("stripe_subscription_id",JsonString(sesion,"subscription")),
          ("stripe_customer_id",JsonString(sesion,"customer"))));
        var confirmado=await SqlStore.ReadTextAsync(cn,null,@"
            SELECT TOP(1) e.nombre_estado FROM dbo.cobros_empresas c
            JOIN dbo.estados e ON e.id_estado=c.id_estado
            WHERE c.id_cobro_empresa=@cobro",
            SqlStore.P(("cobro",cobro)));
        if(confirmado.FirstOrDefault()?.S("nombre_estado")!="cobro_pagado")
            throw new InvalidOperationException("SQL Server no marcó la mensualidad como pagada después de validar Stripe. Comprueba dbo.sp_citicio_suscripcion_confirmar.");
        return ResultadoConciliacion.Registrado;
        }
        finally { ConfirmacionLock.Release(); }
    }

    public async Task ConfirmarEventoAsync(string body)
    {
        using var document=JsonDocument.Parse(body);
        var ev=document.RootElement;
        var tipo=JsonString(ev,"type");
        var o=ev.GetProperty("data").GetProperty("object");
        if(tipo=="customer.subscription.deleted")
        {
            var id=JsonString(o,"id");
            if(string.IsNullOrWhiteSpace(id))throw new InvalidOperationException("Cancelación Stripe sin ID.");
            // Verifica con Stripe el estado actual: una firma local expuesta no es suficiente.
            using var real=await StripeGetAsync("subscriptions/"+Uri.EscapeDataString(id));
            if(JsonString(real.RootElement,"status")!="canceled")return;
            await db.CallAsync("dbo.sp_citicio_suscripcion_cancelacion_confirmar",SqlStore.P(
                ("stripe_subscription_id",id)));
            return;
        }
        if(tipo=="invoice.paid")
        {
            var invoiceId=JsonString(o,"id");
            if(string.IsNullOrWhiteSpace(invoiceId) || !invoiceId.StartsWith("in_",StringComparison.Ordinal))return;
            // La confirmación es firmada Y se vuelve a consultar desde Stripe.
            using var doc=await StripeGetAsync("invoices/"+Uri.EscapeDataString(invoiceId));
            var factura=doc.RootElement;
            if(JsonString(factura,"status")!="paid" || JsonString(factura,"currency")!="mxn")return;
            if(!factura.TryGetProperty("amount_paid",out var amount) || !amount.TryGetInt64(out var centavos) || centavos<=0)return;
            var esLive=Secret.StartsWith("sk_live_",StringComparison.Ordinal);
            if(!factura.TryGetProperty("livemode",out var live) || live.GetBoolean()!=esLive)return;
            var subscription=JsonString(factura,"subscription");
            if(string.IsNullOrEmpty(subscription) && factura.TryGetProperty("parent",out var parent) &&
               parent.ValueKind==JsonValueKind.Object && parent.TryGetProperty("subscription_details",out var details))
                subscription=JsonString(details,"subscription");
            if(string.IsNullOrWhiteSpace(subscription))throw new InvalidOperationException("Factura pagada sin identificador de suscripción.");
            var razon=JsonString(factura,"billing_reason");
            if(razon=="subscription_create")
            {
                // En Stripe API 2026 la factura inicial contiene la suscripción bajo parent.
                // Buscar la sesión pagada vinculada y confirmar el cobro ORIGINAL.
                using var sesiones=await StripeGetAsync("checkout/sessions?limit=20&subscription="+Uri.EscapeDataString(subscription));
                if(!sesiones.RootElement.TryGetProperty("data",out var lista) || lista.ValueKind!=JsonValueKind.Array)
                    throw new InvalidOperationException("Stripe no devolvió la sesión de alta de la suscripción.");
                foreach(var sesion in lista.EnumerateArray())
                    if(await ConfirmarSesionPagadaAsync(sesion)!=ResultadoConciliacion.NoCoincide)return;
                // Stripe reintentará la factura si el Checkout aún no está disponible.
                throw new InvalidOperationException("No se encontró la sesión inicial pagada de la suscripción.");
            }
            if(razon=="subscription_cycle")
            {
                await db.CallAsync("dbo.sp_citicio_suscripcion_renovacion_confirmar",SqlStore.P(
                    ("stripe_subscription_id",subscription),
                    ("id_metodo_pago",await MetodoTarjetaAsync()),
                    ("monto",centavos/100m),
                    ("referencia",invoiceId)));
            }
            return;
        }
        if(tipo!="checkout.session.completed" && tipo!="checkout.session.async_payment_succeeded")return;
        var idSesion=JsonString(o,"id");
        if(string.IsNullOrWhiteSpace(idSesion) || !idSesion.StartsWith("cs_",StringComparison.Ordinal))return;
        // El evento fue firmado; además leemos la sesión canónica del servidor Stripe.
        using var verificada=await StripeGetAsync("checkout/sessions/"+Uri.EscapeDataString(idSesion));
        await ConfirmarSesionPagadaAsync(verificada.RootElement);
    }

    private static string? JsonString(JsonElement o,string prop) =>
        o.ValueKind==JsonValueKind.Object && o.TryGetProperty(prop,out var v) && v.ValueKind==JsonValueKind.String
            ? v.GetString() : null;

    // Recibe exclusivamente eventos de CITICIO Citas en /Pago/CitasWebhook.
    // Nunca confirma desde un parámetro de URL, un botón del cliente o un evento sin firma.
    public async Task ConfirmarEventoCitasAsync(string body) {
        using var document=JsonDocument.Parse(body);
        var ev=document.RootElement;
        var tipo=JsonString(ev,"type");
        if(tipo is not ("checkout.session.completed" or "checkout.session.async_payment_succeeded"))return;
        var sesionEvento=ev.GetProperty("data").GetProperty("object");
        var sessionId=JsonString(sesionEvento,"id");
        if(sessionId is null || !sessionId.StartsWith("cs_",StringComparison.Ordinal) || sessionId.Length>160)
            return;

        // Se consulta de NUEVO el cobro usando exclusivamente la segunda cuenta Stripe.
        using var req=new HttpRequestMessage(HttpMethod.Get,
            "https://api.stripe.com/v1/checkout/sessions/"+Uri.EscapeDataString(sessionId));
        req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",CitasSecret);
        using var resp=await http.CreateClient().SendAsync(req);
        if(!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"No se pudo verificar el pago de cita con Stripe (HTTP {(int)resp.StatusCode}).");
        using var sesionDoc=JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var s=sesionDoc.RootElement;
        if(JsonString(s,"payment_status")!="paid" || JsonString(s,"mode")!="payment" ||
           JsonString(s,"currency")!="mxn" || JsonString(s,"status")!="complete")return;
        var esLive=CitasSecret.StartsWith("sk_live_",StringComparison.Ordinal);
        if(!s.TryGetProperty("livemode",out var live) ||
           (live.ValueKind!=JsonValueKind.True && live.ValueKind!=JsonValueKind.False) ||
           live.GetBoolean()!=esLive)
            throw new InvalidOperationException("El modo Stripe de la cita no coincide con la cuenta configurada.");
        if(!s.TryGetProperty("metadata",out var metadata) ||
           JsonString(metadata,"tipo")!="cita_citicio" ||
           !int.TryParse(JsonString(metadata,"id_cita"),NumberStyles.None,CultureInfo.InvariantCulture,out var citaId) ||
           citaId<=0 || JsonString(s,"client_reference_id")!=citaId.ToString(CultureInfo.InvariantCulture))return;
        if(!s.TryGetProperty("amount_total",out var montoJson) || !montoJson.TryGetInt64(out var centavos) || centavos<=0)
            throw new InvalidOperationException("El monto de pago de cita no es válido.");
        if(JsonString(s,"id")!=sessionId)throw new InvalidOperationException("La sesión verificada no coincide.");

        // El procedimiento existente es la autoridad: debe rechazar abonos duplicados,
        // citas canceladas y montos que excedan el saldo. No se modifica SQL Server.
        // Si rechaza una operación, Stripe reintentará; revisar conciliación/reembolso.
        await ConfirmacionLock.WaitAsync();
        try {
            await db.CallAsync("dbo.sp_pagos_confirmar_pasarela",SqlStore.P(
                ("id_cita",citaId),("id_metodo_pago",await MetodoTarjetaAsync()),
                ("monto",centavos/100m),("referencia",sessionId),
                ("proveedor_pago","stripe")));
        }
        finally {ConfirmacionLock.Release();}
        // La cita deja de estar "pendiente" tan pronto el cobro fue confirmado en SQL.
        // No cambia citas canceladas/completadas ni marca asistencia como completada.
        await ConfirmarEstadoCitaPagadaAsync(citaId);
    }

    private async Task ConfirmarEstadoCitaPagadaAsync(int citaId) {
        await using var cn=await db.OpenAsync();
        var actual=(await SqlStore.ReadTextAsync(cn,null,@"
            SELECT TOP(1) e.nombre_estado FROM dbo.citas c
            INNER JOIN dbo.estados e ON e.id_estado=c.id_estado
            WHERE c.id_cita=@id;",SqlStore.P(("id",citaId)))).FirstOrDefault();
        if(actual?.S("nombre_estado")!="cita_pendiente")return;
        var destino=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","estado_por_nombre"),("texto","cita_confirmada")))).FirstOrDefault();
        if(destino is null)throw new InvalidOperationException("Falta el estado cita_confirmada en SQL Server.");
        await db.CallAsync("dbo.sp_citas",SqlStore.P(
            ("accion","cambiar_estado"),("id_cita",citaId),("id_estado",destino.I("id_estado"))));
    }

    // Los webhooks de Connect usan una clave de firma DIFERENTE de la cuenta plataforma.
    public async Task ConfirmarEventoConectadaAsync(string body) {
        using var document=JsonDocument.Parse(body);
        var ev=document.RootElement;
        if(JsonString(ev,"type")!="checkout.session.completed")return;
        var account=JsonString(ev,"account");
        var session=ev.GetProperty("data").GetProperty("object");
        if(string.IsNullOrEmpty(account)||JsonString(session,"payment_status")!="paid" ||
          JsonString(session,"currency")!="mxn")return;
        var meta=session.GetProperty("metadata");
        if(!int.TryParse(JsonString(meta,"id_cita"),out var cita) ||
           !int.TryParse(JsonString(meta,"id_sucursal"),out var sucursal))return;
        await using var cn=await db.OpenAsync();
        var ok=await SqlStore.ReadTextAsync(cn,null,@"SELECT 1 AS permitido FROM dbo.citas c
          JOIN dbo.citicio_cuentas_sucursal s ON s.id_sucursal=c.id_sucursal
          WHERE c.id_cita=@cita AND c.id_sucursal=@sucursal AND s.stripe_account_id=@cuenta",SqlStore.P(("cita",cita),("sucursal",sucursal),("cuenta",account)));
        if(ok.Count==0)throw new InvalidOperationException("La cuenta conectada no coincide con la sucursal de la cita.");
        var centavos=session.GetProperty("amount_total").GetInt64();
        if(centavos<=0)throw new InvalidOperationException("Monto inválido.");
        var referencia=JsonString(session,"id") ?? throw new InvalidOperationException("Stripe no envió referencia de pago.");
        try {
            await db.CallAsync("dbo.sp_pagos_confirmar_pasarela",SqlStore.P(
                ("id_cita",cita),("id_metodo_pago",await MetodoTarjetaAsync()),
                ("monto",centavos/100m),("referencia",referencia),
                ("proveedor_pago","stripe_connect")));
        }
        catch(SqlException ex) when(ex.Number is 54078 or 54077) {
            // El dinero ingresó a Stripe aunque la cita se cancelara o se cubriera por SPEI.
            // Se registra para conciliación/reembolso, en lugar de perder el webhook.
            await db.CallAsync("dbo.sp_citicio_conciliacion_tarjeta_recibir",SqlStore.P(
                ("id_cita",cita),("id_sucursal",sucursal),("stripe_account_id",account),
                ("referencia",referencia),("monto",centavos/100m),
                ("motivo",ex.Number==54078 ? "cita_cancelada" : "saldo_cubierto")));
        }
    }
}
