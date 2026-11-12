using System.ComponentModel.DataAnnotations;

namespace Citicio.Models;
public sealed class ConfiguracionMensualForm
{
    [Range(typeof(decimal),"1","9999999",ErrorMessage="Establece un importe positivo.")]
    public decimal TarifaMensual {get;set;}
    [Required,StringLength(100)] public string BancoPersonal {get;set;}="";
    [Required,StringLength(200)] public string TitularPersonal {get;set;}="";
    [Required,RegularExpression("^[0-9]{18}$",ErrorMessage="Escribe 18 dígitos.")]
    public string ClabePersonal {get;set;}="";
    public string? ClabeEnmascarada {get;set;}
}
public sealed class SuscripcionPagoForm
{
    public bool RenovacionAutomatica {get;set;}
}
