using System.ComponentModel.DataAnnotations;

namespace Tienda_Giovanna.Models;

public class Producto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public decimal Precio { get; set; }
    public int Stock { get; set; }
    public int StockMinimo { get; set; } = 5;
    public bool Activo { get; set; } = true;
    public List<int> AlternativosIds { get; set; } = new();
    public bool StockBajo => Stock <= StockMinimo;
}

public class Cliente
{
    public int Id { get; set; }
    public string Documento { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Direccion { get; set; } = "";
    public bool Activo { get; set; } = true;
    public decimal Deuda { get; set; }
}

public class ProductoSolicitud
{
    [Required, StringLength(20)] public string Codigo { get; set; } = "";
    [Required, StringLength(100)] public string Nombre { get; set; } = "";
    public decimal Precio { get; set; }
    [Range(0, int.MaxValue)] public int StockMinimo { get; set; }
}

public class RegistroProductoSolicitud : ProductoSolicitud
{
    [Range(0, int.MaxValue)] public int StockInicial { get; set; }
}

public class EstadoProductoSolicitud
{
    [Required] public bool? Activo { get; set; }
}

public class DetalleVenta
{
    public int ProductoId { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal Subtotal { get; set; }
}

public class Venta
{
    public int? UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public int Id { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public List<DetalleVenta> Detalles { get; set; } = new();
    public string FormaPago { get; set; } = "";
    public int? ClienteId { get; set; }
    public string? ClienteNombre { get; set; }
    public string? ReferenciaPago { get; set; }
    public decimal Total { get; set; }
    public decimal MontoRecibido { get; set; }
    public decimal Vuelto { get; set; }
}

public class Pago
{
    public int? UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public string ClienteNombre { get; set; } = "";
    public DateTimeOffset Fecha { get; set; }
    public decimal Monto { get; set; }
    public string Metodo { get; set; } = "";
    public decimal SaldoRestante { get; set; }
}

public class Compra
{
    public int Id { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public int ProveedorId { get; set; }
    public string ProveedorNombre { get; set; } = "";
    public int? UsuarioId { get; set; }
    public string? UsuarioNombre { get; set; }
    public List<DetalleCompra> Detalles { get; set; } = new();
    public decimal Total { get; set; }
}

public class DetalleCompra
{
    public int ProductoId { get; set; }
    public string Codigo { get; set; } = "";
    public string ProductoNombre { get; set; } = "";
    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal Subtotal { get; set; }
}

public class LineaVentaSolicitud
{
    [Range(1, int.MaxValue)] public int ProductoId { get; set; }
    [Range(1, int.MaxValue)] public int Cantidad { get; set; }
}

public class VentaSolicitud
{
    [Required, MinLength(1)] public List<LineaVentaSolicitud> Items { get; set; } = new();
    [Required] public string FormaPago { get; set; } = "";
    public int? ClienteId { get; set; }
    public decimal MontoRecibido { get; set; }
    [StringLength(100)] public string? ReferenciaPago { get; set; }
}

public class PagoSolicitud
{
    [Range(1, int.MaxValue)] public int ClienteId { get; set; }
    public decimal Monto { get; set; }
    [Required] public string Metodo { get; set; } = "";
}

public class CompraSolicitud
{
    [Range(1, int.MaxValue)] public int ProveedorId { get; set; }
    [Required, MinLength(1)] public List<LineaCompraSolicitud> Items { get; set; } = new();
}

public class LineaCompraSolicitud
{
    [Range(1, int.MaxValue)] public int ProductoId { get; set; }
    [Range(1, int.MaxValue)] public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
}

public class StockMinimoSolicitud
{
    [Range(1, int.MaxValue)] public int ProductoId { get; set; }
    [Range(0, int.MaxValue)] public int StockMinimo { get; set; }
}

public class AlternativasSolicitud
{
    [Range(1, int.MaxValue)] public int ProductoId { get; set; }
    [Required] public List<int> AlternativosIds { get; set; } = new();
}
