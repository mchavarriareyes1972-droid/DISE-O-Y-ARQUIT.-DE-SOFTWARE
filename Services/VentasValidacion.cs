using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

public static class VentasValidacion
{
    public const decimal ImporteMaximo = 9999999999999999.99m;

    public static List<LineaVentaSolicitud> Validar(VentaSolicitud solicitud)
    {
        if (solicitud.Items == null || solicitud.Items.Count == 0 ||
            solicitud.Items.Any(i => i == null || i.ProductoId <= 0 || i.Cantidad <= 0))
            throw new ArgumentException("Agrega productos con cantidades enteras mayores que cero.");
        if (solicitud.FormaPago is not ("Efectivo" or "Digital" or "Fiado"))
            throw new ArgumentException("Selecciona una forma de pago válida.");
        if (solicitud.MontoRecibido < 0 || solicitud.MontoRecibido > ImporteMaximo)
            throw new ArgumentException("El monto recibido está fuera del límite permitido.");
        if (solicitud.FormaPago == "Digital" &&
            (string.IsNullOrWhiteSpace(solicitud.ReferenciaPago) || solicitud.ReferenciaPago.Length > 100))
            throw new ArgumentException("Confirma el pago digital e ingresa una referencia de hasta 100 caracteres.");
        if (solicitud.FormaPago == "Fiado" && (!solicitud.ClienteId.HasValue || solicitud.ClienteId <= 0))
            throw new ArgumentException("Selecciona un cliente para la venta al fiado.");
        var items = new List<LineaVentaSolicitud>();
        foreach (var grupo in solicitud.Items.GroupBy(i => i.ProductoId).OrderBy(g => g.Key))
        {
            long cantidad = grupo.Sum(i => (long)i.Cantidad);
            if (cantidad > int.MaxValue) throw new ArgumentException("La cantidad excede el límite del inventario.");
            items.Add(new() { ProductoId = grupo.Key, Cantidad = (int)cantidad });
        }
        return items;
    }
}
