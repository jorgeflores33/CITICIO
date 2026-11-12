using System.Buffers.Binary;
using Citicio.Data;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;

/// <summary>Imágenes locales; se almacenan como VARBINARY en dbo.imagenes mediante sp_imagen_guardar.</summary>
public static class ImagenArchivoService
{
    public const int LimiteBytes = 2 * 1024 * 1024;
    public sealed record ImagenValidada(string Nombre, string Mime, byte[] Bytes);

    public static async Task<ImagenValidada?> PrepararAsync(IFormFile? archivo, CancellationToken ct = default)
    {
        if (archivo is null) return null;
        if (archivo.Length < 1 || archivo.Length > LimiteBytes)
            throw new InvalidOperationException("La imagen debe pesar entre 1 byte y 2 MB (2 MiB) como máximo.");

        await using var memoria = new MemoryStream((int)archivo.Length);
        await archivo.CopyToAsync(memoria, ct);
        var datos = memoria.ToArray();
        if (datos.Length < 1 || datos.Length > LimiteBytes)
            throw new InvalidOperationException("La imagen supera el máximo permitido de 2 MB.");
        var mime = DetectarMime(datos);
        if (mime is null)
            throw new InvalidOperationException("Selecciona una imagen real JPG, PNG o WebP. No se admiten otros archivos.");
        var nombre = Path.GetFileName(archivo.FileName);
        if (string.IsNullOrWhiteSpace(nombre)) nombre = "imagen";
        if (nombre.Length > 180) nombre = nombre[..180];
        return new ImagenValidada(nombre, mime, datos);
    }

    // Si el servidor aún tiene dbo.sp_imagen_guardar se conserva el almacenamiento original.
    // Las instalaciones que no tienen el SP/tabla usan archivos locales privados,
    // fuera de wwwroot. No se modifican las tablas ni el contrato de las rutas.
    public static async Task GuardarAsync(SqlConnection cn, SqlTransaction tx, int idUsuario,
        string uso, int idObjeto, ImagenValidada imagen, string contentRoot, CancellationToken ct = default)
    {
        if (idUsuario <= 0 || idObjeto <= 0)
            throw new InvalidOperationException("No fue posible identificar al propietario de la imagen.");
        if (!UsosPermitidos.Contains(uso))
            throw new InvalidOperationException("Uso de imagen no permitido.");
        try
        {
            await SqlStore.ReadAsync(cn, tx, "dbo.sp_imagen_guardar", SqlStore.P(
                ("id_usuario_actor", idUsuario), ("uso", uso), ("id_objeto", idObjeto),
                ("nombre_archivo", imagen.Nombre), ("tipo_mime", imagen.Mime),
                ("contenido", imagen.Bytes)), ct);
        }
        catch (SqlException ex) when (ex.Number is 2812 or 208)
        {
            // Un error de servidor, permisos, bloqueo o formato NO activa el respaldo:
            // solo ausencia del procedimiento/tabla de imágenes original.
            var directorio = Path.Combine(contentRoot, "App_Data", "imagenes");
            Directory.CreateDirectory(directorio);
            var extension = imagen.Mime switch
            {
                "image/png" => ".png", "image/jpeg" => ".jpg", "image/webp" => ".webp",
                _ => throw new InvalidOperationException("Formato de imagen no válido.")
            };
            var destino = Path.Combine(directorio, $"{uso}_{idObjeto}{extension}");
            var temporal = destino + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporal, imagen.Bytes, ct);
                File.Move(temporal, destino, overwrite: true);
                // Evita mostrar la imagen anterior si la nueva usa otra extensión.
                foreach (var ext in new[] { ".jpg", ".png", ".webp" })
                {
                    var anterior=Path.Combine(directorio, $"{uso}_{idObjeto}{ext}");
                    if (!string.Equals(anterior,destino,StringComparison.OrdinalIgnoreCase) && File.Exists(anterior))
                        File.Delete(anterior);
                }
            }
            finally
            {
                if (File.Exists(temporal)) File.Delete(temporal);
            }
        }
    }

    private static readonly HashSet<string> UsosPermitidos = new(StringComparer.Ordinal)
    { "logo_empresa", "portada_empresa", "foto_sucursal", "imagen_servicio", "foto_cliente" };

    public static async Task<(byte[] Bytes, string Mime)?> LeerLocalAsync(
        string contentRoot, string uso, int idObjeto, CancellationToken ct = default)
    {
        if (!UsosPermitidos.Contains(uso) || idObjeto <= 0) return null;
        var directorio = Path.Combine(contentRoot, "App_Data", "imagenes");
        foreach (var (ext, mime) in new[] { (".png", "image/png"), (".jpg", "image/jpeg"), (".webp", "image/webp") })
        {
            var archivo = Path.Combine(directorio, $"{uso}_{idObjeto}{ext}");
            if (File.Exists(archivo)) return (await File.ReadAllBytesAsync(archivo, ct), mime);
        }
        return null;
    }

    // Se guarda la ruta interna en la columna *_url para reutilizar las vistas existentes.
    // El usuario nunca necesita proporcionar una URL.
    public static string Ruta(string uso, int idObjeto) => $"/Imagenes/Ver?uso={uso}&id={idObjeto}";

    private static string? DetectarMime(byte[] b)
    {
        if (b.Length >= 24 && b.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
        {
            var width = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16,4));
            var height = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20,4));
            return width is >= 1 and <= 8192 && height is >= 1 and <= 8192 ? "image/png" : null;
        }
        if (b.Length >= 4 && b[0] == 0xff && b[1] == 0xd8 && b[2] == 0xff && b[^2] == 0xff && b[^1] == 0xd9)
            return "image/jpeg";
        if (b.Length >= 20 && b.AsSpan(0,4).SequenceEqual("RIFF"u8) && b.AsSpan(8,4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}
