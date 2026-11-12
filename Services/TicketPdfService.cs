using Citicio.Data;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;
using PdfSharpCore.Utils;
using QRCoder;

namespace Citicio.Services;

// El QR abre una página autenticada: no contiene datos personales ni tokens de acceso.
// La descarga comprueba que la cita pertenezca al cliente conectado.
public sealed class TicketPdfService
{
    private static readonly object FontLock = new();
    public byte[] Crear(int cita,DbRow datos,string enlace)
    {
        lock(FontLock)
        {
            if(GlobalFontSettings.FontResolver is null)
                GlobalFontSettings.FontResolver=new FontResolver();
        }
        using var documento=new PdfDocument();
        documento.Info.Title=$"CITICIO - Comprobante de cita {cita}";
        var pagina=documento.AddPage();pagina.Width=430;pagina.Height=630;
        using(var gfx=XGraphics.FromPdfPage(pagina))
        {
            var titulo=new XFont("Arial",20,XFontStyle.Bold);
            var texto=new XFont("Arial",11,XFontStyle.Regular);
            var negrita=new XFont("Arial",12,XFontStyle.Bold);
            gfx.DrawString("CITICIO",titulo,XBrushes.DarkBlue,new XRect(25,25,380,35),XStringFormats.TopLeft);
            gfx.DrawString("Comprobante de cita",negrita,XBrushes.Black,new XRect(25,70,380,25),XStringFormats.TopLeft);
            void Linea(string etiqueta,string valor,int y)
            {
                // Limitar la longitud que podría romper el diseño.
                var recortado=valor.Length>52?valor[..52]+"...":valor;
                gfx.DrawString(etiqueta+": "+recortado,texto,XBrushes.Black,
                    new XRect(25,y,385,26),XStringFormats.TopLeft);
            }
            Linea("Folio",cita.ToString(),108);
            Linea("Cliente",datos.S("cliente"),138);
            Linea("Empresa",datos.S("empresa"),168);
            Linea("Sucursal",datos.S("sucursal"),198);
            Linea("Profesional",datos.S("empleado"),228);
            Linea("Fecha",datos.D("hora_inicio").ToString("dd/MM/yyyy HH:mm"),258);
            Linea("Total",datos.M("total_cita").ToString("N2")+" MXN",288);
            var png=PngByteQRCodeHelper.GetQRCode(enlace,QRCodeGenerator.ECCLevel.Q,10);
            using var qr=XImage.FromStream(()=>new MemoryStream(png));
            gfx.DrawImage(qr,146,331,138,138);
            gfx.DrawString("Escanea para consultar tu cita en CITICIO",texto,XBrushes.Gray,
                new XRect(35,485,365,30),XStringFormats.Center);
            gfx.DrawString("La cita esta sujeta a su estatus actual.",texto,XBrushes.Gray,
                new XRect(35,530,365,30),XStringFormats.Center);
        }
        using var salida=new MemoryStream();
        documento.Save(salida,false);
        return salida.ToArray();
    }
}
