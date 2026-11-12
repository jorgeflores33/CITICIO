using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

/// <summary>
/// CITICIO no recibe el dinero: el cliente paga directamente a la CLABE de la empresa.
/// Reportar una transferencia NUNCA equivale a confirmar el abono.
/// </summary>
[Authorize]
public sealed class SpeiController(SqlStore db, ILogger<SpeiController> logger) : Controller
{
    private const int MaxPdfBytes = 5 * 1024 * 1024;

    // Verificación de dígito de control CLABE según pesos 3,7,1.
    internal static bool ClabeValida(string? clabe)
    {
        if (clabe is null || clabe.Length != 18 || clabe.Any(x => x < '0' || x > '9')) return false;
        int total = 0;
        int[] pesos = [3, 7, 1];
        for (int i = 0; i < 17; i++) total += ((clabe[i] - '0') * pesos[i % 3]) % 10;
        return (10 - total % 10) % 10 == clabe[17] - '0';
    }

    [Authorize(Roles="admin_empresa"), HttpGet]
    public async Task<IActionResult> Cuentas() => View(new CuentaBancariaVM {
        Cuentas = await db.CallAsync("dbo.sp_citicio_cuentas_sucursal_empresa",SqlStore.P(("id_usuario_actor",User.UserId())))
    });

    [Authorize(Roles="admin_empresa"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> GuardarCuenta(CuentaBancariaForm form)
    {
        form.NombreBanco = form.NombreBanco?.Trim() ?? "";
        form.Titular = form.Titular?.Trim() ?? "";
        form.Clabe = form.Clabe?.Trim() ?? "";
        if (!ClabeValida(form.Clabe)) ModelState.AddModelError(nameof(form.Clabe),"El dígito verificador de la CLABE no coincide.");
        if (form.Clabe.Length == 18 && form.CodigoBanco != form.Clabe[..3])
            ModelState.AddModelError(nameof(form.CodigoBanco),"El código de banco debe coincidir con los primeros 3 dígitos de la CLABE.");
        if (form.IdCuentaBancaria is not > 0) ModelState.AddModelError(nameof(form.IdCuentaBancaria),"Selecciona una sucursal de la empresa.");
        if (!ModelState.IsValid)
        {
            return View("Cuentas",new CuentaBancariaVM{ Nueva=form,
                Cuentas=await db.CallAsync("dbo.sp_citicio_cuentas_sucursal_empresa",SqlStore.P(("id_usuario_actor",User.UserId()))) });
        }
        try
        {
            await db.CallAsync("dbo.sp_citicio_cuenta_sucursal_guardar",SqlStore.P(
                ("id_usuario_actor",User.UserId()),("id_sucursal",form.IdCuentaBancaria),
                ("nombre_banco",form.NombreBanco),("titular",form.Titular),("clabe",form.Clabe)));
            TempData["Ok"] = "Cuenta guardada. La titularidad bancaria no está verificada automáticamente.";
        }
        catch (SqlException ex)
        {
            logger.LogWarning("Error al guardar cuenta empresarial. Código {Codigo}",ex.Number);
            TempData["Error"] = MensajeSql(ex,"No se pudo guardar la cuenta. Revisa que no esté duplicada.");
        }
        return RedirectToAction(nameof(Cuentas));
    }

    // La cuenta bancaria es obligatoria para una sucursal. No se desactiva
    // mientras la sucursal existe; se sustituye en Cuentas con nueva CLABE.
    // Todas las lecturas de datos de citas provienen de un procedimiento que filtra por id_usuario.
    private async Task<(DbRow? cita,int empresaId,decimal saldo)> ObtenerCitaCliente(int citaId)
    {
        if(citaId <= 0) return (null,0,0);
        var cita=(await db.CallAsync("dbo.sp_citas_panel_usuario",SqlStore.P(("id_usuario",User.UserId()))))
            .FirstOrDefault(x=>x.I("id_cita")==citaId);
        if(cita is null) return (null,0,0);
        var empresa=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","empresa_de_sucursal"),("id1",cita.I("id_sucursal"))))).FirstOrDefault()?.I("id_empresa") ?? 0;
        var checkout=(await db.CallAsync("dbo.sp_citicio_lecturas",SqlStore.P(
            ("accion","checkout_cliente"),("id_usuario_actor",User.UserId()),("id1",citaId)))).FirstOrDefault();
        return (cita,empresa,checkout is null?0:Math.Max(0,checkout.M("total")-checkout.M("pagado")));
    }

    [Authorize(Roles="cliente"), HttpGet]
    public async Task<IActionResult> Pagar(int citaId)
    {
        var (cita,empresa,saldo)=await ObtenerCitaCliente(citaId);
        if(cita is null || empresa<=0) return NotFound();
        var cuentas=await db.CallAsync("dbo.sp_citicio_sucursal_destino_cliente",SqlStore.P(
            ("id_usuario_cliente",User.UserId()),("id_cita",citaId)));
        var transferencias=await db.CallAsync("dbo.sp_spei_consultar_pago_cliente",SqlStore.P(
            ("id_usuario_cliente",User.UserId()),("id_cita",citaId)));
        return View(new PagoSpeiVM{ Cita=cita, Saldo=saldo, Cuentas=cuentas, Transferencias=transferencias,
            Form=new ReporteSpeiForm{ CitaId=citaId, Monto=saldo } });
    }

    [Authorize(Roles="cliente"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reportar(ReporteSpeiForm form)
    {
        var (cita,empresa,saldo)=await ObtenerCitaCliente(form.CitaId);
        if(cita is null || empresa<=0)return NotFound();
        form.ClaveRastreo=(form.ClaveRastreo??"").Trim().ToUpperInvariant();
        if (cita.S("nombre_estado") is not ("cita_pendiente" or "cita_confirmada"))
            ModelState.AddModelError("","La cita ya no acepta pagos.");
        if(form.FechaOperacion.Date>MexicoClock.Today || form.FechaOperacion.Date<MexicoClock.Today.AddDays(-180))
            ModelState.AddModelError(nameof(form.FechaOperacion),"La fecha debe estar dentro de los últimos 180 días.");
        if(form.Monto>saldo || saldo<=0)ModelState.AddModelError(nameof(form.Monto),"El importe no puede superar el saldo disponible.");
        var cuentas=await db.CallAsync("dbo.sp_citicio_sucursal_destino_cliente",SqlStore.P(
            ("id_usuario_cliente",User.UserId()),("id_cita",form.CitaId)));
        if(!cuentas.Any(x=>x.I("id_cuenta_bancaria")==form.CuentaId))
            ModelState.AddModelError(nameof(form.CuentaId),"Selecciona una cuenta activa de esta empresa.");
        if(!ModelState.IsValid)
        {
            var historial=await db.CallAsync("dbo.sp_spei_consultar_pago_cliente",SqlStore.P(
                ("id_usuario_cliente",User.UserId()),("id_cita",form.CitaId)));
            return View("Pagar",new PagoSpeiVM{ Form=form,Cita=cita,Saldo=saldo,Cuentas=cuentas,Transferencias=historial });
        }
        try
        {
            await db.CallAsync("dbo.sp_spei_reportar_pago",SqlStore.P(
                ("id_usuario_cliente",User.UserId()),("id_cita",form.CitaId),
                ("id_cuenta_bancaria",form.CuentaId),("fecha_operacion",form.FechaOperacion.Date),
                ("codigo_banco_emisor",form.BancoEmisor),("clave_rastreo",form.ClaveRastreo),
                ("monto",form.Monto),("nota_cliente",form.Nota?.Trim())));
            TempData["Ok"]="Transferencia reportada. La empresa debe comprobar el depósito antes de confirmar el pago.";
        }
        catch(SqlException ex)
        {
            logger.LogWarning("No se pudo registrar transferencia SPEI. Código {Codigo}",ex.Number);
            TempData["Error"]=MensajeSql(ex,"No se pudo registrar la transferencia. Comprueba monto, fecha y clave de rastreo.");
        }
        return RedirectToAction(nameof(Pagar),new{citaId=form.CitaId});
    }

    [Authorize(Roles="admin_empresa,encargado_sucursal"), HttpGet]
    public async Task<IActionResult> Pendientes()
    {
        try
        {
            return View(await db.CallAsync("dbo.sp_spei_pendientes_empresa",SqlStore.P(
                ("id_usuario_actor",User.UserId()))));
        }
        catch(SqlException ex)
        {
            logger.LogWarning("Falló consulta de transferencias; número SQL {Numero}",ex.Number);
            TempData["Error"]=MensajeSql(ex,"No fue posible consultar los pagos pendientes.");
            return View(new List<DbRow>());
        }
    }

    [Authorize(Roles="admin_empresa,encargado_sucursal"), HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Resolver(RevisionSpeiForm form)
    {
        if(form.Decision is not ("confirmar" or "rechazar"))ModelState.AddModelError("","Acción inválida.");
        if(form.Decision=="confirmar" && !form.AbonoConfirmado)
            ModelState.AddModelError(nameof(form.AbonoConfirmado),"Debes comprobar el abono en el banco antes de confirmar.");
        if(form.Decision=="rechazar" && string.IsNullOrWhiteSpace(form.Observaciones))
            ModelState.AddModelError(nameof(form.Observaciones),"Describe la razón del rechazo.");
        if(!ModelState.IsValid)
        {
            TempData["Error"]=string.Join(" ",ModelState.Values.SelectMany(x=>x.Errors).Select(x=>x.ErrorMessage));
            return RedirectToAction(nameof(Pendientes));
        }
        try
        {
            await db.CallAsync("dbo.sp_spei_resolver_pago",SqlStore.P(
                ("id_usuario_actor",User.UserId()),("id_transferencia",form.TransferenciaId),
                ("decision",form.Decision),("abono_confirmado",form.AbonoConfirmado),
                ("cep_consultado",form.CepConsultado),("observaciones",form.Observaciones?.Trim())));
            TempData["Ok"]=form.Decision=="confirmar"
                ? "Pago confirmado tras comprobar el depósito. Se programó el correo del ticket si está configurado."
                : "Transferencia rechazada. El cliente podrá consultar el motivo.";
        }
        catch(SqlException ex)
        {
            logger.LogWarning("Error al resolver transferencia {Transferencia}. Código {Codigo}",form.TransferenciaId,ex.Number);
            TempData["Error"]=MensajeSql(ex,"No se pudo resolver la transferencia.");
        }
        return RedirectToAction(nameof(Pendientes));
    }

    private static string MensajeSql(SqlException ex,string otro) => ex.Number switch {
        2812 => "Faltan procedimientos SPEI en la base conectada; el sitio no modifica SQL automáticamente.",
        62101 or 62103 or 62105 or 62106 or 62120 or 62121 or 62122 or 62123 or 62124 or 62125 or 62126 or 62127
          or 62130 or 62140 or 62141 or 62142 or 62143 or 62144 or 62145 or 62146 or 62147 or 62148 or 62149 or 62150
          or 63021 or 63022 or 63023 or 63024 or 63025 => ex.Message,
        _ => otro
    };
}
