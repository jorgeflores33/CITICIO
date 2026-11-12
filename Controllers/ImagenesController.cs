using Citicio.Auth;
using Citicio.Data;
using Citicio.Models;
using Citicio.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Citicio.Controllers;

[Authorize(Roles="admin_empresa,cliente")]
public sealed class ImagenesController(SqlStore db,ILogger<ImagenesController> logger, IHostEnvironment entorno) : Controller
{
    [HttpGet]
    public IActionResult Subir() => View(new ImagenForm {
        Uso=User.IsInRole("cliente")?"foto_cliente":"logo_empresa",
        IdObjeto=User.IsInRole("cliente")?User.ClienteId():User.EmpresaId()
    });

    [HttpPost,ValidateAntiForgeryToken,RequestSizeLimit(2621440)]
    public async Task<IActionResult> Subir(ImagenForm form)
    {
        if(User.IsInRole("cliente")){
            if(form.Uso!="foto_cliente" || form.IdObjeto!=User.ClienteId())return Forbid();
        }else{
            if(form.Uso is not ("logo_empresa" or "portada_empresa") || form.IdObjeto!=User.EmpresaId())return Forbid();
        }
        if(!ModelState.IsValid)return View(form);
        try {
            var imagen=await ImagenArchivoService.PrepararAsync(form.Archivo);
            if(imagen is null)throw new InvalidOperationException("Selecciona una imagen de máximo 2 MB.");
            await using var cn=await db.OpenAsync();
            await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
            try {
                await ImagenArchivoService.GuardarAsync(cn,tx,User.UserId(),form.Uso,form.IdObjeto,imagen,entorno.ContentRootPath);
                if(form.Uso is "logo_empresa" or "portada_empresa") {
                    // Actualizar ÚNICAMENTE la columna de la imagen seleccionada.
                    // Antes se enviaban ambos campos al SP con uno en NULL:
                    // al cambiar portada podía resetearse el logo y viceversa.
                    var columna = form.Uso == "logo_empresa" ? "logo_url" : "imagen_portada_url";
                    await SqlStore.ReadTextAsync(cn,tx,$"UPDATE dbo.empresas SET {columna}=@ruta WHERE id_empresa=@empresa",
                        SqlStore.P(("ruta",ImagenArchivoService.Ruta(form.Uso,form.IdObjeto)),("empresa",form.IdObjeto)));
                }
                await tx.CommitAsync();
            }catch {await tx.RollbackAsync();throw;}
            TempData["Ok"]="Imagen almacenada correctamente (máximo 2 MB).";
            return RedirectToAction(nameof(Subir));
        } catch(InvalidOperationException ex) {
            ModelState.AddModelError(nameof(form.Archivo),ex.Message);
            return View(form);
        } catch(SqlException ex) {
            logger.LogWarning("No fue posible guardar la imagen; SQL {Error}",ex.Number);
            ModelState.AddModelError("", "No se pudo almacenar la imagen en la base de datos.");
            return View(form);
        }
    }

    [Authorize(Roles="admin_empresa"),HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarLogo()
    {
        var empresa=User.EmpresaId();
        if(empresa<=0)return Forbid();
        try
        {
            await using var cn=await db.OpenAsync();
            await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
            try
            {
                // La empresa sale exclusivamente de la sesión, nunca de un campo enviado.
                await SqlStore.ReadTextAsync(cn,tx,@"UPDATE dbo.empresas SET logo_url=NULL WHERE id_empresa=@empresa",
                    SqlStore.P(("empresa",empresa)));
                try
                {
                    await SqlStore.ReadTextAsync(cn,tx,@"DELETE FROM dbo.imagenes
                        WHERE uso='logo_empresa' AND id_empresa=@empresa",
                        SqlStore.P(("empresa",empresa)));
                }
                catch(SqlException ex) when(ex.Number==208) { /* instalación que guarda imágenes localmente */ }
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync();throw; }
            var directorio=Path.Combine(entorno.ContentRootPath,"App_Data","imagenes");
            foreach(var extension in new[]{".jpg",".png",".webp"})
            {
                var local=Path.Combine(directorio,$"logo_empresa_{empresa}{extension}");
                if(System.IO.File.Exists(local)) System.IO.File.Delete(local);
            }
            TempData["Ok"]="Logotipo eliminado. Puedes subir una nueva imagen.";
        }
        catch(Exception ex) when(ex is SqlException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex,"No se pudo eliminar el logotipo");
            TempData["Error"]="No fue posible eliminar el logotipo. Revisa los permisos de la carpeta de imágenes y SQL Server.";
        }
        return RedirectToAction(nameof(Subir));
    }

    [Authorize(Roles="admin_empresa"),HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminarPortada()
    {
        var empresa=User.EmpresaId();
        if(empresa<=0)return Forbid();
        try
        {
            await using var cn=await db.OpenAsync();
            await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
            try
            {
                await SqlStore.ReadTextAsync(cn,tx,@"UPDATE dbo.empresas SET imagen_portada_url=NULL WHERE id_empresa=@empresa",
                    SqlStore.P(("empresa",empresa)));
                try
                {
                    await SqlStore.ReadTextAsync(cn,tx,@"DELETE FROM dbo.imagenes
                        WHERE uso='portada_empresa' AND id_empresa=@empresa",SqlStore.P(("empresa",empresa)));
                }
                catch(SqlException ex) when(ex.Number==208) { /* respaldo local */ }
                await tx.CommitAsync();
            }
            catch {await tx.RollbackAsync();throw;}
            var ruta=Path.Combine(entorno.ContentRootPath,"App_Data","imagenes");
            foreach(var ext in new[]{".jpg",".png",".webp"})
            {
                var local=Path.Combine(ruta,$"portada_empresa_{empresa}{ext}");
                if(System.IO.File.Exists(local))System.IO.File.Delete(local);
            }
            TempData["Ok"]="Imagen de portada eliminada.";
        }
        catch(Exception ex) when(ex is SqlException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex,"No se pudo eliminar la portada de empresa");
            TempData["Error"]="No se pudo eliminar la portada. Comprueba la conexión y permisos.";
        }
        return RedirectToAction(nameof(Subir));
    }

    // Solo las imágenes comerciales son públicas; fotos de clientes no se exponen aquí.
    [AllowAnonymous,HttpGet]
    public async Task<IActionResult> Ver(string uso,int id)
    {
        var columna=uso switch {
            "logo_empresa" or "portada_empresa"=>"id_empresa",
            "foto_sucursal"=>"id_sucursal",
            "imagen_servicio"=>"id_servicio",
            _=>null
        };
        if(columna is null || id<=0)return NotFound();
        // Nombre de columna seleccionado únicamente del switch local (no viene del usuario).
        var sql=$"SELECT TOP (1) tipo_mime,contenido FROM dbo.imagenes " +
            $"WHERE uso=@uso AND {columna}=@id ORDER BY id_imagen DESC";
        try {
            var local = await ImagenArchivoService.LeerLocalAsync(entorno.ContentRootPath, uso, id, HttpContext.RequestAborted);
            if (local is not null) {
                Response.Headers["Cache-Control"]="public,max-age=60";
                return File(local.Value.Bytes, local.Value.Mime);
            }
            await using var cn=await db.OpenAsync();
            var imagen=(await SqlStore.ReadTextAsync(cn,null,sql,SqlStore.P(
                ("uso",uso),("id",id)))).FirstOrDefault();
            if(imagen is null)return NotFound();
            var mime=imagen.S("tipo_mime");
            if(mime is not ("image/png" or "image/jpeg" or "image/webp"))return NotFound();
            if(!imagen.Cells.TryGetValue("contenido",out var contenido) || contenido is not byte[] bytes)
                return NotFound();
            Response.Headers["Cache-Control"]="public,max-age=60";
            return File(bytes,mime);
        } catch(SqlException ex) {
            logger.LogWarning("Imagen comercial no disponible; SQL {Error}",ex.Number);
            return StatusCode(503);
        }
    }
}
