using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

internal static class ComprasValidacion
{
    public static List<LineaCompraSolicitud> Validar(CompraSolicitud solicitud)
    {
        if (solicitud.ProveedorId <= 0) throw new ArgumentException("Selecciona un proveedor activo registrado.");
        if (solicitud.Items == null || solicitud.Items.Count == 0 || solicitud.Items.Any(i => i == null || i.ProductoId <= 0 || i.Cantidad <= 0 || i.CostoUnitario <= 0))
            throw new ArgumentException("Agrega productos con cantidades y costos mayores que cero.");
        if (solicitud.Items.Select(i => i.ProductoId).Distinct().Count() != solicitud.Items.Count)
            throw new ArgumentException("Cada producto debe aparecer una sola vez en la compra. Edita su cantidad en el detalle.");
        var items = new List<LineaCompraSolicitud>();
        decimal total = 0;
        foreach (var linea in solicitud.Items.OrderBy(i => i.ProductoId))
        {
            decimal costo = decimal.Round(linea.CostoUnitario, 2, MidpointRounding.AwayFromZero);
            if (costo <= 0) throw new ArgumentException("El costo mínimo es S/ 0.01.");
            if (costo > 9999999999999999.99m) throw new ArgumentException("El importe excede el límite permitido.");
            decimal subtotal = costo * linea.Cantidad;
            total += subtotal;
            if (total > 9999999999999999.99m) throw new ArgumentException("El importe excede el límite permitido.");
            items.Add(new() { ProductoId = linea.ProductoId, Cantidad = linea.Cantidad, CostoUnitario = costo });
        }
        return items;
    }
}
