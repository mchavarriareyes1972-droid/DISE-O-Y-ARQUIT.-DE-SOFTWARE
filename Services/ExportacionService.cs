using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

public class ExportacionService
{
    public ExportacionService() { QuestPDF.Settings.License = LicenseType.Community; }
    private static string Numero(decimal valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);
    private static string Fecha(DateTimeOffset valor) => valor.ToOffset(TimeSpan.FromHours(-5)).ToString("dd/MM/yyyy HH:mm");
    private static string Celda(string valor)
    {
        // Evita que los nombres ingresados se interpreten como fórmulas al abrir el CSV.
        if (valor.TrimStart().StartsWith('=') || valor.TrimStart().StartsWith('+') || valor.TrimStart().StartsWith('-') || valor.TrimStart().StartsWith('@')) valor = "'" + valor;
        return "\"" + valor.Replace("\"", "\"\"") + "\"";
    }
    public byte[] Csv(ReporteVentas reporte)
    {
        var texto = new StringBuilder();
        void Fila(params string[] celdas) => texto.AppendLine(string.Join(';', celdas.Select(Celda)));
        Fila("Tipo", "Número", "Fecha Lima", "Cliente", "Método", "Detalle", "Total vendido", "Cobrado en ventas", "Fiado", "Vuelto", "Abono", "Saldo restante");
        foreach (Venta venta in reporte.Ventas)
        {
            string detalle = string.Join(" | ", venta.Detalles.Select(d => $"{d.Codigo} {d.Nombre} x{d.Cantidad} a S/ {Numero(d.PrecioUnitario)}"));
            Fila("Venta", venta.Id.ToString(), Fecha(venta.Fecha), venta.ClienteNombre ?? "Contado", venta.FormaPago, detalle,
                Numero(venta.Total), Numero(venta.FormaPago == "Fiado" ? 0 : venta.Total), Numero(venta.FormaPago == "Fiado" ? venta.Total : 0), Numero(venta.Vuelto), "", "");
        }
        foreach (Pago pago in reporte.Pagos)
            Fila("Abono", pago.Id.ToString(), Fecha(pago.Fecha), pago.ClienteNombre, pago.Metodo, "", "", "", "", "", Numero(pago.Monto), Numero(pago.SaldoRestante));
        Fila("Resumen", "", "", "", "", $"Periodo: {reporte.Desde?.ToString("dd/MM/yyyy") ?? "inicio"} a {reporte.Hasta?.ToString("dd/MM/yyyy") ?? "actualidad"}",
            Numero(reporte.TotalVendido), Numero(reporte.CobradoEnVentas), Numero(reporte.TotalFiado), "", Numero(reporte.Abonos), "");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(texto.ToString())).ToArray();
    }
    public byte[] Pdf(ReporteVentas reporte)
    {
        return Document.Create(documento => documento.Page(pagina =>
        {
            pagina.Size(PageSizes.A4); pagina.Margin(32);
            pagina.DefaultTextStyle(t => t.FontSize(10));
            pagina.Header().Column(c =>
            {
                c.Item().Text("Bodega Giovanna").FontSize(20).Bold().FontColor("#183c38");
                c.Item().Text("Reporte de ventas y abonos").FontSize(14);
                c.Item().Text($"Desde: {reporte.Desde?.ToString("dd/MM/yyyy") ?? "Inicio"}  |  Hasta: {reporte.Hasta?.ToString("dd/MM/yyyy") ?? "Actualidad"}");
                c.Item().Text($"Generado: {Fecha(reporte.Generado)} (Lima)").FontSize(9);
            });
            pagina.Content().PaddingVertical(15).Column(c =>
            {
                c.Spacing(10);
                c.Item().Text($"Ventas: {reporte.CantidadVentas}  |  Total vendido: S/ {Numero(reporte.TotalVendido)}").Bold();
                c.Item().Text($"Cobrado en ventas: S/ {Numero(reporte.CobradoEnVentas)}  |  Fiado: S/ {Numero(reporte.TotalFiado)}  |  Abonos: S/ {Numero(reporte.Abonos)}");
                c.Item().Text("Los fiados no se suman al dinero cobrado. Los abonos se presentan por separado.").FontSize(9);
                c.Item().Text("Ventas del periodo").Bold();
                if (reporte.Ventas.Count == 0) c.Item().Text("No hay ventas en el periodo consultado.");
                foreach (Venta venta in reporte.Ventas)
                {
                    c.Item().EnsureSpace(70).Column(v =>
                    {
                        v.Item().Background("#edf5ee").Padding(6).Text($"Venta {venta.Id} | {Fecha(venta.Fecha)} | {venta.FormaPago} | {venta.ClienteNombre ?? "Contado"}").Bold();
                        foreach (DetalleVenta d in venta.Detalles)
                            v.Item().PaddingVertical(3).Text($"{d.Codigo} - {d.Nombre} | {d.Cantidad} x S/ {Numero(d.PrecioUnitario)} = S/ {Numero(d.Subtotal)}");
                        v.Item().Text($"Total: S/ {Numero(venta.Total)} | Recibido: S/ {Numero(venta.MontoRecibido)} | Vuelto: S/ {Numero(venta.Vuelto)}");
                        if (!string.IsNullOrEmpty(venta.ReferenciaPago)) v.Item().Text("Referencia digital: " + venta.ReferenciaPago);
                    });
                }
                c.Item().Text("Abonos del periodo").Bold();
                if (reporte.Pagos.Count == 0) c.Item().Text("No hay abonos en el periodo consultado.");
                foreach (Pago pago in reporte.Pagos)
                    c.Item().Text($"Abono {pago.Id} | {Fecha(pago.Fecha)} | {pago.ClienteNombre} | {pago.Metodo} | S/ {Numero(pago.Monto)} | Saldo: S/ {Numero(pago.SaldoRestante)}");
                c.Item().Text($"Productos activos con stock bajo actual: {reporte.ProductosStockBajo}. Este indicador corresponde al inventario actual.").FontSize(9);
            });
            pagina.Footer().AlignCenter().Text(t => { t.Span("Página "); t.CurrentPageNumber(); t.Span(" de "); t.TotalPages(); });
        })).GeneratePdf();
    }
}
