using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Citicio.Data;

namespace Citicio.Services;

/// <summary>Valida bytes, formato real y límite estricto de 2 MiB antes de insertar en SQL.</summary>
public sealed class ImagenEmpresaService(CiticioDb db)
{
    public const int MaxBytes = 2 * 1024 * 1024;

    public static async Task<(byte[]? Bytes, string? Mime)> LeerLogotipoAsync(IFormFile? logo)
    {
        if (logo is null || logo.Length == 0) return (null, null);
        if (logo.Length >= MaxBytes) throw new ArgumentException("El logotipo debe pesar menos de 2 MB.");
        await using var stream = logo.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        if (buffer.Length == 0 || buffer.Length >= MaxBytes) throw new ArgumentException("La imagen excede el límite de 2 MB.");
        var bytes = buffer.ToArray();
        var mime = DetectarMime(bytes);
        if (mime is null) throw new ArgumentException("Usa una imagen JPG, PNG o WebP válida (máximo 2 MB).");
        return (bytes, mime);
    }

    public static string? DetectarMime(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 8 && b[..8].SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})) return "image/png";
        if (b.Length >= 3 && b[0]==255 && b[1]==216 && b[2]==255) return "image/jpeg";
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    public async Task<(byte[] Bytes, string Mime)?> ObtenerLogoPublicadoAsync(int empresaId)
    {
        var row = await db.FirstAsync("dbo.sp_citicio_logo_publicado", CommandType.StoredProcedure,
            CiticioDb.Param("@id_empresa", empresaId));
        if (row is null || row["contenido"] is not byte[] bytes || row["tipo_mime"] is not string mime) return null;
        return (bytes, mime);
    }
}
