using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

internal static class ProductosValidacion
{
    public static Producto Validar(ProductoSolicitud solicitud)
    {
        string codigo = (solicitud.Codigo ?? "").Trim().ToUpperInvariant();
        string nombre = (solicitud.Nombre ?? "").Trim();
        if (codigo.Length == 0 || codigo.Length > 20) throw new ArgumentException("El código debe tener entre 1 y 20 caracteres.");
        if (nombre.Length == 0 || nombre.Length > 100) throw new ArgumentException("El nombre debe tener entre 1 y 100 caracteres.");
        if (solicitud.Precio <= 0) throw new ArgumentException("El precio debe ser mayor que cero.");
        decimal precio = decimal.Round(solicitud.Precio, 2, MidpointRounding.AwayFromZero);
        if (precio <= 0) throw new ArgumentException("El precio mínimo es S/ 0.01.");
        if (precio > 9999999999999999.99m) throw new ArgumentException("El precio excede el límite permitido.");
        if (solicitud.StockMinimo < 0) throw new ArgumentException("El stock mínimo no puede ser negativo.");
        return new() { Codigo = codigo, Nombre = nombre, Precio = precio, StockMinimo = solicitud.StockMinimo };
    }
}
