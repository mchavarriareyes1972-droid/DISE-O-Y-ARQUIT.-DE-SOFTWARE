using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Tienda_Giovanna.Models;
using Tienda_Giovanna.Services;

namespace Tienda_Giovanna.Controllers;

[ApiController]
[Authorize]
[Route("api/tienda")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TiendaController : ControllerBase
{
    private readonly TiendaService tienda;
    public TiendaController(TiendaService tienda) { this.tienda = tienda; }
    private IActionResult Operacion(Func<object> accion)
    {
        try { return Ok(accion()); }
        catch (ArgumentException error) { return BadRequest(new { mensaje = error.Message }); }
        catch (OverflowException) { return BadRequest(new { mensaje = "El importe excede el límite permitido." }); }
    }
    [HttpGet("productos")] public IActionResult Productos(string? busqueda, bool incluirInactivos = false) => Ok(tienda.BuscarProductos(busqueda, incluirInactivos));
    [HttpGet("productos/{id:int}")] public IActionResult Producto(int id) => Operacion(() => tienda.ConsultarProducto(id));
    [Authorize(Roles = Roles.Administrador), HttpPost("productos"), ValidateAntiForgeryToken] public IActionResult RegistrarProducto(RegistroProductoSolicitud solicitud) => Operacion(() => tienda.RegistrarProducto(solicitud));
    [Authorize(Roles = Roles.Administrador), HttpPost("productos/{id:int}/editar"), ValidateAntiForgeryToken] public IActionResult EditarProducto(int id, ProductoSolicitud solicitud) => Operacion(() => tienda.EditarProducto(id, solicitud));
    [Authorize(Roles = Roles.Administrador), HttpPost("productos/{id:int}/estado"), ValidateAntiForgeryToken] public IActionResult EstadoProducto(int id, EstadoProductoSolicitud solicitud) => Operacion(() => tienda.CambiarEstadoProducto(id, solicitud));
    [HttpGet("clientes")] public IActionResult Clientes(bool incluirInactivos = false) => Ok(tienda.ConsultarClientes(incluirInactivos));
    [Authorize(Roles = Roles.Administrador), HttpPost("clientes"), ValidateAntiForgeryToken] public IActionResult RegistrarCliente(ClienteSolicitud solicitud) => Operacion(() => tienda.RegistrarCliente(solicitud));
    [Authorize(Roles = Roles.Administrador), HttpPost("clientes/{id:int}/editar"), ValidateAntiForgeryToken] public IActionResult EditarCliente(int id, ClienteSolicitud solicitud) => Operacion(() => tienda.EditarCliente(id, solicitud));
    [Authorize(Roles = Roles.Administrador), HttpPost("clientes/{id:int}/estado"), ValidateAntiForgeryToken] public IActionResult EstadoCliente(int id, EstadoSolicitud solicitud) => Operacion(() => tienda.CambiarEstadoCliente(id, solicitud));
    [Authorize(Roles = Roles.Administrador), HttpGet("proveedores")] public IActionResult Proveedores(bool incluirInactivos = false) => Ok(tienda.ConsultarProveedores(incluirInactivos));
    [Authorize(Roles = Roles.Administrador), HttpPost("proveedores"), ValidateAntiForgeryToken] public IActionResult RegistrarProveedor(ProveedorSolicitud solicitud) => Operacion(() => tienda.RegistrarProveedor(solicitud));
    [Authorize(Roles = Roles.Administrador), HttpPost("proveedores/{id:int}/editar"), ValidateAntiForgeryToken] public IActionResult EditarProveedor(int id, ProveedorSolicitud solicitud) => Operacion(() => tienda.EditarProveedor(id, solicitud));
    [Authorize(Roles = Roles.Administrador), HttpPost("proveedores/{id:int}/estado"), ValidateAntiForgeryToken] public IActionResult EstadoProveedor(int id, EstadoSolicitud solicitud) => Operacion(() => tienda.CambiarEstadoProveedor(id, solicitud));
    [HttpGet("pagos")] public IActionResult Pagos(int? clienteId) => Ok(tienda.ConsultarPagos(clienteId));
    [Authorize(Roles = Roles.Administrador), HttpGet("compras")] public IActionResult Compras() => Ok(tienda.ConsultarCompras());
    [Authorize(Roles = Roles.Administrador), HttpGet("compras/{id:int}")] public IActionResult DetalleCompra(int id) => Operacion(() => tienda.ConsultarCompra(id));
    [HttpGet("ventas")] public IActionResult Ventas(DateOnly? desde, DateOnly? hasta) => Operacion(() => tienda.ConsultarVentas(desde, hasta));
    [HttpGet("ventas/{id:int}")] public IActionResult Venta(int id) => Operacion(() => tienda.ConsultarVenta(id));
    [HttpGet("reporte")] public IActionResult Reporte(DateOnly? desde, DateOnly? hasta) => Operacion(() => tienda.Reporte(desde, hasta));
    [HttpGet("reporte/exportar/{formato}")]
    public IActionResult Exportar(string formato, DateOnly? desde, DateOnly? hasta, [FromServices] ExportacionService exportacion)
    {
        try
        {
            ReporteVentas reporte = tienda.Reporte(desde, hasta);
            string nombre = "Ventas_Giovanna_" + reporte.Generado.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyyMMdd");
            return formato switch
            {
                "csv" => File(exportacion.Csv(reporte), "text/csv; charset=utf-8", nombre + ".csv"),
                "pdf" => File(exportacion.Pdf(reporte), "application/pdf", nombre + ".pdf"),
                _ => BadRequest(new { mensaje = "Selecciona CSV o PDF." })
            };
        }
        catch (ArgumentException error) { return BadRequest(new { mensaje = error.Message }); }
    }
    [HttpPost("ventas"), ValidateAntiForgeryToken]
    public IActionResult Venta(VentaSolicitud solicitud)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int usuarioId) || usuarioId <= 0) return Unauthorized();
        return Operacion(() => tienda.RegistrarVenta(solicitud, usuarioId));
    }
    [HttpPost("pagos"), ValidateAntiForgeryToken] public IActionResult Pago(PagoSolicitud solicitud) => Operacion(() => tienda.RegistrarPago(solicitud));
    [Authorize(Roles = Roles.Administrador), HttpPost("compras"), ValidateAntiForgeryToken]
    public IActionResult Compra(CompraSolicitud solicitud)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out int usuarioId) || usuarioId <= 0) return Unauthorized();
        return Operacion(() => tienda.RegistrarCompra(solicitud, usuarioId));
    }
    [Authorize(Roles = Roles.Administrador), HttpPost("stock-minimo"), ValidateAntiForgeryToken] public IActionResult StockMinimo(StockMinimoSolicitud solicitud) => Operacion(() => tienda.ActualizarStockMinimo(solicitud));
    [Authorize(Roles = Roles.Administrador), HttpPost("alternativas"), ValidateAntiForgeryToken] public IActionResult Alternativas(AlternativasSolicitud solicitud) => Operacion(() => tienda.RegistrarAlternativas(solicitud));
}
