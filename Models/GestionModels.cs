using System.ComponentModel.DataAnnotations;

namespace Tienda_Giovanna.Models;

public class ClienteSolicitud
{
    [Required, RegularExpression(@"^[0-9]{8}$", ErrorMessage = "El DNI debe tener 8 dígitos.")] public string Documento { get; set; } = "";
    [Required, StringLength(100)] public string Nombre { get; set; } = "";
    [StringLength(20)] public string? Telefono { get; set; }
    [StringLength(150)] public string? Direccion { get; set; }
}

public class Proveedor
{
    public int Id { get; set; }
    public string Ruc { get; set; } = "";
    public string RazonSocial { get; set; } = "";
    public string Contacto { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Correo { get; set; } = "";
    public string Direccion { get; set; } = "";
    public bool Activo { get; set; } = true;
}

public class ProveedorSolicitud
{
    [Required, RegularExpression(@"^[0-9]{11}$", ErrorMessage = "El RUC debe tener 11 dígitos.")] public string Ruc { get; set; } = "";
    [Required, StringLength(100)] public string RazonSocial { get; set; } = "";
    [StringLength(100)] public string? Contacto { get; set; }
    [StringLength(20)] public string? Telefono { get; set; }
    [EmailAddress, StringLength(100)] public string? Correo { get; set; }
    [StringLength(150)] public string? Direccion { get; set; }
}

public class EstadoSolicitud
{
    [Required] public bool? Activo { get; set; }
}

public class ReporteVentas
{
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
    public DateTimeOffset Generado { get; set; }
    public int CantidadVentas { get; set; }
    public decimal TotalVendido { get; set; }
    public decimal TotalFiado { get; set; }
    public decimal CobradoEnVentas { get; set; }
    public decimal Abonos { get; set; }
    public int ProductosStockBajo { get; set; }
    public List<Venta> Ventas { get; set; } = new();
    public List<Pago> Pagos { get; set; } = new();
}
