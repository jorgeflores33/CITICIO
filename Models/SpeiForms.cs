using System.ComponentModel.DataAnnotations;
using Citicio.Data;

namespace Citicio.Models;

public sealed class CuentaBancariaForm
{
    public int? IdCuentaBancaria { get; set; }
    [Required, RegularExpression(@"^[0-9]{3}$", ErrorMessage="El código bancario debe contener tres números.")]
    public string CodigoBanco { get; set; } = "";
    [Required, StringLength(100)] public string NombreBanco { get; set; } = "";
    [Required, StringLength(200)] public string Titular { get; set; } = "";
    [Required, RegularExpression(@"^[0-9]{18}$",ErrorMessage="Ingresa una CLABE de 18 dígitos.")]
    public string Clabe { get; set; } = "";
    public bool EsPrincipal { get; set; } = true;
}

public sealed class CuentaBancariaVM
{
    public CuentaBancariaForm Nueva { get; set; } = new();
    public List<DbRow> Cuentas { get; set; } = [];
}

public sealed class ReporteSpeiForm
{
    [Range(1,int.MaxValue)] public int CitaId { get; set; }
    [Range(1,int.MaxValue,ErrorMessage="Selecciona una cuenta bancaria.")] public int CuentaId { get; set; }
    [Required,DataType(DataType.Date)] public DateTime FechaOperacion { get; set; } = DateTime.Today;
    [Required,RegularExpression(@"^[0-9]{3}$",ErrorMessage="Código bancario de tres dígitos.")]
    public string BancoEmisor { get; set; } = "";
    [Required,StringLength(100,MinimumLength=6),RegularExpression(@"^[A-Za-z0-9\-]+$",ErrorMessage="Utiliza la clave de rastreo alfanumérica, sin espacios.")]
    public string ClaveRastreo { get; set; } = "";
    [Range(typeof(decimal),"0.01","9999999",ErrorMessage="Introduce un importe positivo.")]
    public decimal Monto { get; set; }
    [StringLength(500)] public string? Nota { get; set; }
}

public sealed class PagoSpeiVM
{
    public ReporteSpeiForm Form { get; set; } = new();
    public DbRow Cita { get; set; } = new();
    public decimal Saldo { get; set; }
    public List<DbRow> Cuentas { get; set; } = [];
    public List<DbRow> Transferencias { get; set; } = [];
}

public sealed class RevisionSpeiForm
{
    [Range(1,long.MaxValue)] public long TransferenciaId { get; set; }
    [Required] public string Decision { get; set; } = "";
    public bool AbonoConfirmado { get; set; }
    public bool CepConsultado { get; set; }
    [StringLength(500)] public string? Observaciones { get; set; }
}
