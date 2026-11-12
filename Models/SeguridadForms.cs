using System.ComponentModel.DataAnnotations;
using Citicio.Services;
namespace Citicio.Models;

public sealed class SolicitudRecuperacionForm
{
    [Required,EmailAddress,StringLength(254)] public string Correo {get;set;}="";
}
public sealed class RestablecerContrasenaForm
{
    [Required] public string Token {get;set;}="";
    [Required,ClaveSegura,DataType(DataType.Password)]
    public string Contrasena {get;set;}="";
    [Required,Compare(nameof(Contrasena)),DataType(DataType.Password)]
    public string Confirmacion {get;set;}="";
}
