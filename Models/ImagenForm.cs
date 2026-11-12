using System.ComponentModel.DataAnnotations;
namespace Citicio.Models;
public sealed class ImagenForm
{
    [Required] public string Uso {get;set;}="logo_empresa";
    public int IdObjeto {get;set;}
    [Required] public IFormFile? Archivo {get;set;}
}
