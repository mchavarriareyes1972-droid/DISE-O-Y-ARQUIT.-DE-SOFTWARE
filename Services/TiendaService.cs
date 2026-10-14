using System.Text.Json;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

// Almacenamiento temporal para practicar los RF. Más adelante se conectará a la BD.
public class TiendaService
{
    private readonly object bloqueo = new();
    private readonly Func<DateTimeOffset> reloj;
    private readonly List<Producto> productos = new()
    {
        new() { Id = 1, Codigo = "P001", Nombre = "Arroz 1 kg", Precio = 4.50m, Stock = 18 },
        new() { Id = 2, Codigo = "P002", Nombre = "Leche en lata", Precio = 4.20m, Stock = 3 },
        new() { Id = 3, Codigo = "P003", Nombre = "Aceite 900 ml", Precio = 9.20m, Stock = 12 },
        new() { Id = 4, Codigo = "P004", Nombre = "Azúcar 1 kg", Precio = 4.00m, Stock = 16 },
        new() { Id = 5, Codigo = "P005", Nombre = "Galletas", Precio = 2.50m, Stock = 24 },
        new() { Id = 6, Codigo = "P006", Nombre = "Agua 625 ml", Precio = 1.50m, Stock = 0, AlternativosIds = new() { 7 } },
        new() { Id = 7, Codigo = "P007", Nombre = "Agua 1 litro", Precio = 2.50m, Stock = 10 }
    };
    private readonly List<Cliente> clientes = new()
    {
        new() { Id = 1, Nombre = "María López", Telefono = "999 111 222", Deuda = 12.50m },
        new() { Id = 2, Nombre = "José Pérez", Telefono = "988 333 444", Deuda = 8.00m }
    };
    private readonly List<Venta> ventas = new();
    private readonly List<Pago> pagos = new();
    private readonly List<Compra> compras = new();

    public TiendaService() : this(() => DateTimeOffset.UtcNow) { }
    public TiendaService(Func<DateTimeOffset> reloj) { this.reloj = reloj; }
    private static T Copia<T>(T valor) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(valor))!;
    private static decimal Moneda(decimal valor) => decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
    private Producto ObtenerProducto(int id) => productos.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException("El producto no existe.");

    public List<Producto> BuscarProductos(string? busqueda)
    {
        lock (bloqueo)
        {
            string texto = (busqueda ?? "").Trim();
            return Copia(productos.Where(p => p.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase)
                || p.Codigo.Contains(texto, StringComparison.OrdinalIgnoreCase)).ToList());
        }
    }

    public object ConsultarProducto(int id)
    {
        lock (bloqueo)
        {
            Producto producto = ObtenerProducto(id);
            return new { Producto = Copia(producto), Alternativas = Copia(productos.Where(p =>
                producto.AlternativosIds.Contains(p.Id) && p.Stock > 0).ToList()) };
        }
    }

    public List<Cliente> ConsultarClientes() { lock (bloqueo) { return Copia(clientes); } }
    public List<Pago> ConsultarPagos(int? clienteId)
    {
        lock (bloqueo) { return Copia(pagos.Where(p => !clienteId.HasValue || p.ClienteId == clienteId).OrderByDescending(p => p.Id).ToList()); }
    }
    public List<Compra> ConsultarCompras() { lock (bloqueo) { return Copia(compras.OrderByDescending(c => c.Id).ToList()); } }

    public Venta RegistrarVenta(VentaSolicitud solicitud)
    {
        lock (bloqueo)
        {
            if (solicitud.Items == null || solicitud.Items.Count == 0 || solicitud.Items.Any(i => i == null || i.Cantidad <= 0))
                throw new ArgumentException("Agrega productos con cantidades enteras mayores que cero.");
            if (!new[] { "Efectivo", "Digital", "Fiado" }.Contains(solicitud.FormaPago))
                throw new ArgumentException("Selecciona una forma de pago válida.");
            if (solicitud.MontoRecibido < 0) throw new ArgumentException("El monto recibido no puede ser negativo.");
            var detalles = new List<DetalleVenta>();
            foreach (var grupo in solicitud.Items.GroupBy(i => i.ProductoId))
            {
                Producto producto = ObtenerProducto(grupo.Key);
                long cantidad = grupo.Sum(i => (long)i.Cantidad);
                if (cantidad > producto.Stock) throw new ArgumentException($"Stock insuficiente de {producto.Nombre}. Disponible: {producto.Stock}.");
                detalles.Add(new DetalleVenta { ProductoId = producto.Id, Codigo = producto.Codigo,
                    Nombre = producto.Nombre, Cantidad = (int)cantidad, PrecioUnitario = producto.Precio,
                    Subtotal = Moneda(producto.Precio * cantidad) });
            }
            decimal total = detalles.Sum(d => d.Subtotal);
            decimal recibido = Moneda(solicitud.MontoRecibido);
            if (solicitud.FormaPago == "Efectivo" && recibido < total)
                throw new ArgumentException("El monto recibido no cubre el total.");
            Cliente? cliente = null;
            if (solicitud.FormaPago == "Fiado")
                cliente = clientes.FirstOrDefault(c => c.Id == solicitud.ClienteId)
                    ?? throw new ArgumentException("Selecciona un cliente para la venta al fiado.");
            if (solicitud.FormaPago == "Digital" && string.IsNullOrWhiteSpace(solicitud.ReferenciaPago))
                throw new ArgumentException("Confirma el pago digital e ingresa su referencia.");

            // Se valida todo antes de descontar stock o modificar la deuda.
            Venta venta = new() { Id = ventas.Count + 1, Fecha = reloj(), Detalles = detalles,
                FormaPago = solicitud.FormaPago, ClienteId = cliente?.Id, ClienteNombre = cliente?.Nombre,
                ReferenciaPago = solicitud.FormaPago == "Digital" ? solicitud.ReferenciaPago!.Trim() : null,
                Total = total, MontoRecibido = solicitud.FormaPago == "Efectivo" ? recibido : solicitud.FormaPago == "Digital" ? total : 0,
                Vuelto = solicitud.FormaPago == "Efectivo" ? Moneda(recibido - total) : 0 };
            foreach (DetalleVenta detalle in detalles) ObtenerProducto(detalle.ProductoId).Stock -= detalle.Cantidad;
            if (cliente != null) cliente.Deuda = Moneda(cliente.Deuda + total);
            ventas.Add(venta);
            return Copia(venta);
        }
    }

    public Pago RegistrarPago(PagoSolicitud solicitud)
    {
        lock (bloqueo)
        {
            Cliente cliente = clientes.FirstOrDefault(c => c.Id == solicitud.ClienteId)
                ?? throw new ArgumentException("El cliente no existe.");
            decimal monto = Moneda(solicitud.Monto);
            if (monto <= 0 || monto > cliente.Deuda) throw new ArgumentException("El abono debe ser mayor que cero y no superar la deuda.");
            if (!new[] { "Efectivo", "Digital" }.Contains(solicitud.Metodo)) throw new ArgumentException("Selecciona el método de pago.");
            cliente.Deuda = Moneda(cliente.Deuda - monto);
            Pago pago = new() { Id = pagos.Count + 1, ClienteId = cliente.Id, ClienteNombre = cliente.Nombre,
                Fecha = reloj(), Monto = monto, Metodo = solicitud.Metodo, SaldoRestante = cliente.Deuda };
            pagos.Add(pago);
            return Copia(pago);
        }
    }

    public Compra RegistrarCompra(CompraSolicitud solicitud)
    {
        lock (bloqueo)
        {
            if (string.IsNullOrWhiteSpace(solicitud.Proveedor) || solicitud.Proveedor.Trim().Length > 80 || solicitud.Cantidad <= 0 || solicitud.CostoUnitario <= 0)
                throw new ArgumentException("Ingresa proveedor, cantidad y costo unitario mayores que cero.");
            Producto producto = ObtenerProducto(solicitud.ProductoId);
            if ((long)producto.Stock + solicitud.Cantidad > int.MaxValue) throw new ArgumentException("La cantidad excede el límite del inventario.");
            decimal costo = Moneda(solicitud.CostoUnitario);
            if (costo <= 0) throw new ArgumentException("El costo mínimo es S/ 0.01.");
            Compra compra = new() { Id = compras.Count + 1, Fecha = reloj(), Proveedor = solicitud.Proveedor.Trim(),
                ProductoId = producto.Id, ProductoNombre = producto.Nombre, Cantidad = solicitud.Cantidad,
                CostoUnitario = costo, Total = Moneda(costo * solicitud.Cantidad) };
            producto.Stock += solicitud.Cantidad;
            compras.Add(compra);
            return Copia(compra);
        }
    }

    public Producto ActualizarStockMinimo(StockMinimoSolicitud solicitud)
    {
        lock (bloqueo)
        {
            if (solicitud.StockMinimo < 0) throw new ArgumentException("El stock mínimo no puede ser negativo.");
            Producto producto = ObtenerProducto(solicitud.ProductoId);
            producto.StockMinimo = solicitud.StockMinimo;
            return Copia(producto);
        }
    }

    public Producto RegistrarAlternativas(AlternativasSolicitud solicitud)
    {
        lock (bloqueo)
        {
            Producto producto = ObtenerProducto(solicitud.ProductoId);
            List<int> ids = (solicitud.AlternativosIds ?? new()).Distinct().ToList();
            foreach (int id in ids)
            {
                ObtenerProducto(id);
                if (id == producto.Id) throw new ArgumentException("Un producto no puede ser su propia alternativa.");
            }
            producto.AlternativosIds = ids;
            return Copia(producto);
        }
    }

    private static void ValidarPeriodo(DateOnly? desde, DateOnly? hasta)
    {
        if (desde.HasValue && hasta.HasValue && desde.Value > hasta.Value)
            throw new ArgumentException("La fecha inicial no puede ser posterior a la final.");
    }
    private static bool EnPeriodo(DateTimeOffset fecha, DateOnly? desde, DateOnly? hasta)
    {
        DateOnly dia = DateOnly.FromDateTime(fecha.ToOffset(TimeSpan.FromHours(-5)).Date);
        return (!desde.HasValue || dia >= desde.Value) && (!hasta.HasValue || dia <= hasta.Value);
    }

    public List<Venta> ConsultarVentas(DateOnly? desde, DateOnly? hasta)
    {
        lock (bloqueo) { ValidarPeriodo(desde, hasta); return Copia(ventas.Where(v => EnPeriodo(v.Fecha, desde, hasta)).OrderByDescending(v => v.Id).ToList()); }
    }
    public Venta ConsultarVenta(int id)
    {
        lock (bloqueo) { return Copia(ventas.FirstOrDefault(v => v.Id == id) ?? throw new ArgumentException("La venta no existe.")); }
    }
    public object Reporte(DateOnly? desde, DateOnly? hasta)
    {
        lock (bloqueo)
        {
            ValidarPeriodo(desde, hasta);
            var filtradas = ventas.Where(v => EnPeriodo(v.Fecha, desde, hasta)).ToList();
            return new { CantidadVentas = filtradas.Count, TotalVendido = filtradas.Sum(v => v.Total),
                TotalFiado = filtradas.Where(v => v.FormaPago == "Fiado").Sum(v => v.Total),
                CobradoEnVentas = filtradas.Where(v => v.FormaPago != "Fiado").Sum(v => v.Total),
                Abonos = pagos.Where(p => EnPeriodo(p.Fecha, desde, hasta)).Sum(p => p.Monto),
                ProductosStockBajo = productos.Count(p => p.StockBajo), Ventas = Copia(filtradas.OrderByDescending(v => v.Id).ToList()) };
        }
    }
}
