using System.Text.Json;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

// Catalogos conectados por etapas; los movimientos se migran con sus transacciones.
public partial class TiendaService
{
    private readonly object bloqueo = new();
    private readonly Func<DateTimeOffset> reloj;
    private readonly ClientesSqlService? clientesSql;
    private readonly ProveedoresSqlService? proveedoresSql;
    private readonly ProductosSqlService? productosSql;
    private readonly ComprasSqlService? comprasSql;
    private readonly VentasSqlService? ventasSql;
    public bool VentasEnSql => ventasSql != null;
    public bool VentasPendientes => ProductosEnSql && !VentasEnSql;
    public bool FiadosPendientes => ClientesEnSql && !VentasEnSql;
    public bool ClientesEnSql => clientesSql != null;
    public bool ProveedoresEnSql => proveedoresSql != null;
    public bool ProductosEnSql => productosSql != null;
    public bool ComprasEnSql => comprasSql != null;
    public bool ComprasPendientes => ProductosEnSql && !ComprasEnSql;
    public const string StockPendiente = "El registro de nuevas ventas estará disponible al completar su conexión a la base de datos. Puedes consultar los productos, existencias y alternativas.";
    public const string MovimientosPendientes = "El registro de nuevos abonos estará disponible al completar su conexión a la base de datos. Puedes consultar los saldos y pagos existentes.";
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
        new() { Id = 1, Documento = "11111111", Nombre = "María López", Telefono = "999 111 222", Deuda = 12.50m },
        new() { Id = 2, Documento = "22222222", Nombre = "José Pérez", Telefono = "988 333 444", Deuda = 8.00m }
    };
    private readonly List<Venta> ventas = new();
    private readonly List<Pago> pagos = new();
    private readonly List<Compra> compras = new();

    public TiendaService() : this(() => DateTimeOffset.UtcNow) { }
    public TiendaService(Func<DateTimeOffset> reloj) { this.reloj = reloj; }
    public TiendaService(ClientesSqlService? clientesSql, ProveedoresSqlService? proveedoresSql = null, ProductosSqlService? productosSql = null, ComprasSqlService? comprasSql = null, VentasSqlService? ventasSql = null) : this()
    {
        this.clientesSql = clientesSql;
        this.proveedoresSql = proveedoresSql;
        this.productosSql = productosSql;
        this.comprasSql = comprasSql;
        this.ventasSql = ventasSql;
    }
    private static T Copia<T>(T valor) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(valor))!;
    private static decimal Moneda(decimal valor) => decimal.Round(valor, 2, MidpointRounding.AwayFromZero);
    private Producto ObtenerProducto(int id) => productos.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException("El producto no existe.");
    private Producto ObtenerProductoActivo(int id)
    {
        Producto producto = ObtenerProducto(id);
        if (!producto.Activo) throw new ArgumentException("El producto está inactivo. Actívalo desde Inventario para utilizarlo.");
        return producto;
    }

    private (string Codigo, string Nombre, decimal Precio) ValidarProducto(ProductoSolicitud solicitud, int? id = null)
    {
        Producto datos = ProductosValidacion.Validar(solicitud);
        if (productos.Any(p => p.Id != id && p.Codigo.Equals(datos.Codigo, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Ya existe un producto con ese código, incluso si está inactivo.");
        return (datos.Codigo, datos.Nombre, datos.Precio);
    }

    public Producto RegistrarProducto(RegistroProductoSolicitud solicitud)
    {
        if (productosSql != null) return productosSql.RegistrarProducto(solicitud);
        lock (bloqueo)
        {
            var datos = ValidarProducto(solicitud);
            if (solicitud.StockInicial < 0) throw new ArgumentException("El stock inicial no puede ser negativo.");
            Producto producto = new() { Id = productos.Max(p => p.Id) + 1, Codigo = datos.Codigo,
                Nombre = datos.Nombre, Precio = datos.Precio, Stock = solicitud.StockInicial, StockMinimo = solicitud.StockMinimo };
            productos.Add(producto);
            return Copia(producto);
        }
    }

    public Producto EditarProducto(int id, ProductoSolicitud solicitud)
    {
        if (productosSql != null) return productosSql.EditarProducto(id, solicitud);
        lock (bloqueo)
        {
            Producto producto = ObtenerProducto(id);
            var datos = ValidarProducto(solicitud, id);
            producto.Codigo = datos.Codigo; producto.Nombre = datos.Nombre;
            producto.Precio = datos.Precio; producto.StockMinimo = solicitud.StockMinimo;
            // El stock y las ventas anteriores se conservan; las entradas se registran en Compras.
            return Copia(producto);
        }
    }

    public Producto CambiarEstadoProducto(int id, EstadoProductoSolicitud solicitud)
    {
        if (productosSql != null) return productosSql.CambiarEstadoProducto(id, solicitud);
        lock (bloqueo)
        {
            Producto producto = ObtenerProducto(id);
            producto.Activo = solicitud.Activo ?? throw new ArgumentException("Indica si deseas activar o desactivar el producto.");
            return Copia(producto);
        }
    }

    public List<Producto> BuscarProductos(string? busqueda, bool incluirInactivos = false)
    {
        if (productosSql != null) return productosSql.BuscarProductos(busqueda, incluirInactivos);
        lock (bloqueo)
        {
            string texto = (busqueda ?? "").Trim();
            return Copia(productos.Where(p => (incluirInactivos || p.Activo) &&
                (p.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase)
                || p.Codigo.Contains(texto, StringComparison.OrdinalIgnoreCase))).ToList());
        }
    }

    public object ConsultarProducto(int id)
    {
        if (productosSql != null) return productosSql.ConsultarProducto(id);
        lock (bloqueo)
        {
            Producto producto = ObtenerProducto(id);
            return new { Producto = Copia(producto), Alternativas = Copia(productos.Where(p =>
                producto.AlternativosIds.Contains(p.Id) && p.Activo && p.Stock > 0).ToList()) };
        }
    }

    public List<Cliente> ConsultarClientes(bool incluirInactivos = false)
    {
        if (clientesSql != null) return clientesSql.ConsultarClientes(incluirInactivos);
        lock (bloqueo) { return Copia(clientes.Where(c => incluirInactivos || c.Activo).ToList()); }
    }
    public List<Pago> ConsultarPagos(int? clienteId)
    {
        if (clientesSql != null) return clientesSql.ConsultarPagos(clienteId);
        lock (bloqueo) { return Copia(pagos.Where(p => !clienteId.HasValue || p.ClienteId == clienteId).OrderByDescending(p => p.Id).ToList()); }
    }
    public List<Compra> ConsultarCompras()
    {
        if (comprasSql != null) return comprasSql.ConsultarCompras();
        lock (bloqueo) { return Copia(compras.OrderByDescending(c => c.Id).ToList()); }
    }

    public Venta RegistrarVenta(VentaSolicitud solicitud, int? usuarioId = null)
    {
        if (ventasSql != null) return ventasSql.RegistrarVenta(solicitud, usuarioId ?? 0);
        if (ProductosEnSql) throw new ArgumentException(StockPendiente);
        if (ClientesEnSql && solicitud.FormaPago == "Fiado") throw new ArgumentException("El registro de fiados requiere conectar ventas a la base de datos.");
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
                Producto producto = ObtenerProductoActivo(grupo.Key);
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
                cliente = clientes.FirstOrDefault(c => c.Id == solicitud.ClienteId && c.Activo)
                    ?? throw new ArgumentException("Selecciona un cliente para la venta al fiado.");
            if (solicitud.FormaPago == "Digital" && string.IsNullOrWhiteSpace(solicitud.ReferenciaPago))
                throw new ArgumentException("Confirma el pago digital e ingresa su referencia.");

            // Se valida todo antes de descontar stock o modificar la deuda.
            Venta venta = new() { Id = ventas.Count + 1, Fecha = reloj(), UsuarioId = usuarioId, Detalles = detalles,
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
        if (ClientesEnSql) throw new ArgumentException(MovimientosPendientes);
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

    public Compra RegistrarCompra(CompraSolicitud solicitud, int? usuarioId = null)
    {
        if (comprasSql != null) return comprasSql.RegistrarCompra(solicitud, usuarioId ?? 0);
        if (ProductosEnSql) throw new ArgumentException("El registro de compras está pendiente de conexión a la base de datos.");
        lock (bloqueo)
        {
            var items = ComprasValidacion.Validar(solicitud);
            Proveedor proveedor = proveedoresSql != null ? proveedoresSql.ConsultarProveedorActivo(solicitud.ProveedorId)
                : proveedores.FirstOrDefault(p => p.Id == solicitud.ProveedorId && p.Activo)
                ?? throw new ArgumentException("Selecciona un proveedor activo registrado.");
            var detalles = new List<DetalleCompra>();
            foreach (var linea in items)
            {
                Producto producto = ObtenerProductoActivo(linea.ProductoId);
                if ((long)producto.Stock + linea.Cantidad > int.MaxValue) throw new ArgumentException("La cantidad excede el límite del inventario.");
                decimal costo = Moneda(linea.CostoUnitario);
                if (costo <= 0) throw new ArgumentException("El costo mínimo es S/ 0.01.");
                detalles.Add(new() { ProductoId = producto.Id, Codigo = producto.Codigo, ProductoNombre = producto.Nombre,
                    Cantidad = linea.Cantidad, CostoUnitario = costo, Subtotal = Moneda(costo * linea.Cantidad) });
            }
            // La compra completa se valida antes de reponer cualquier producto.
            Compra compra = new() { Id = compras.Count + 1, Fecha = reloj(), ProveedorId = proveedor.Id,
                ProveedorNombre = proveedor.RazonSocial, UsuarioId = usuarioId, Detalles = detalles, Total = detalles.Sum(d => d.Subtotal) };
            foreach (var detalle in detalles) ObtenerProducto(detalle.ProductoId).Stock += detalle.Cantidad;
            compras.Add(compra);
            return Copia(compra);
        }
    }

    public Producto ActualizarStockMinimo(StockMinimoSolicitud solicitud)
    {
        if (productosSql != null) return productosSql.ActualizarStockMinimo(solicitud);
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
        if (productosSql != null) return productosSql.RegistrarAlternativas(solicitud);
        lock (bloqueo)
        {
            Producto producto = ObtenerProductoActivo(solicitud.ProductoId);
            List<int> ids = (solicitud.AlternativosIds ?? new()).Distinct().ToList();
            foreach (int id in ids)
            {
                ObtenerProductoActivo(id);
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
        if (ventasSql != null) return ventasSql.ConsultarVentas(desde, hasta);
        lock (bloqueo) { ValidarPeriodo(desde, hasta); return Copia(ventas.Where(v => EnPeriodo(v.Fecha, desde, hasta)).OrderByDescending(v => v.Id).ToList()); }
    }
    public Venta ConsultarVenta(int id)
    {
        if (ventasSql != null) return ventasSql.ConsultarVenta(id);
        lock (bloqueo) { return Copia(ventas.FirstOrDefault(v => v.Id == id) ?? throw new ArgumentException("La venta no existe.")); }
    }
    public ReporteVentas Reporte(DateOnly? desde, DateOnly? hasta)
    {
        lock (bloqueo)
        {
            ValidarPeriodo(desde, hasta);
            var filtradas = ConsultarVentas(desde, hasta);
            var pagosFiltrados = ConsultarPagos(null).Where(p => EnPeriodo(p.Fecha, desde, hasta)).ToList();
            return new ReporteVentas { Desde = desde, Hasta = hasta, Generado = reloj(), CantidadVentas = filtradas.Count, TotalVendido = filtradas.Sum(v => v.Total),
                TotalFiado = filtradas.Where(v => v.FormaPago == "Fiado").Sum(v => v.Total),
                CobradoEnVentas = filtradas.Where(v => v.FormaPago != "Fiado").Sum(v => v.Total),
                Abonos = pagosFiltrados.Sum(p => p.Monto),
                ProductosStockBajo = productosSql?.ContarStockBajo() ?? productos.Count(p => p.Activo && p.StockBajo), Ventas = Copia(filtradas.OrderByDescending(v => v.Id).ToList()),
                Pagos = Copia(pagosFiltrados.OrderByDescending(p => p.Id).ToList()) };
        }
    }
}
