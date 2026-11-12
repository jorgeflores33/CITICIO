using Citicio.Data;

namespace Citicio.Models;

/// <summary>Datos públicos de una empresa con suscripción vigente.</summary>
public sealed class EmpresaPerfilPublicoVM
{
    public DbRow Empresa { get; set; } = new();
    public List<DbRow> Sucursales { get; set; } = [];
    public List<DbRow> Servicios { get; set; } = [];
    public List<DbRow> Resenas { get; set; } = [];
}
