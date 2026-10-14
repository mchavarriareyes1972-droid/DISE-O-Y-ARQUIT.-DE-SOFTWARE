using Microsoft.AspNetCore.Mvc;
using Tienda_Giovanna.Models;
using Tienda_Giovanna.Services;

namespace Tienda_Giovanna.Controllers;

[ApiController]
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
    [HttpPost("productos"), ValidateAntiForgeryToken] public IActionResult RegistrarProducto(RegistroProductoSolicitud solicitud) => Operacion(() => tienda.RegistrarProducto(solicitud));
    [HttpPost("productos/{id:int}/editar"), ValidateAntiForgeryToken] public IActionResult EditarProducto(int id, ProductoSolicitud solicitud) => Operacion(() => tienda.EditarProducto(id, solicitud));
    [HttpPost("productos/{id:int}/estado"), ValidateAntiForgeryToken] public IActionResult EstadoProducto(int id, EstadoProductoSolicitud solicitud) => Operacion(() => tienda.CambiarEstadoProducto(id, solicitud));
    [HttpGet("clientes")] public IActionResult Clientes() => Ok(tienda.ConsultarClientes());
    [HttpGet("pagos")] public IActionResult Pagos(int? clienteId) => Ok(tienda.ConsultarPagos(clienteId));
    [HttpGet("compras")] public IActionResult Compras() => Ok(tienda.ConsultarCompras());
    [HttpGet("ventas")] public IActionResult Ventas(DateOnly? desde, DateOnly? hasta) => Operacion(() => tienda.ConsultarVentas(desde, hasta));
    [HttpGet("ventas/{id:int}")] public IActionResult Venta(int id) => Operacion(() => tienda.ConsultarVenta(id));
    [HttpGet("reporte")] public IActionResult Reporte(DateOnly? desde, DateOnly? hasta) => Operacion(() => tienda.Reporte(desde, hasta));
    [HttpPost("ventas"), ValidateAntiForgeryToken] public IActionResult Venta(VentaSolicitud solicitud) => Operacion(() => tienda.RegistrarVenta(solicitud));
    [HttpPost("pagos"), ValidateAntiForgeryToken] public IActionResult Pago(PagoSolicitud solicitud) => Operacion(() => tienda.RegistrarPago(solicitud));
    [HttpPost("compras"), ValidateAntiForgeryToken] public IActionResult Compra(CompraSolicitud solicitud) => Operacion(() => tienda.RegistrarCompra(solicitud));
    [HttpPost("stock-minimo"), ValidateAntiForgeryToken] public IActionResult StockMinimo(StockMinimoSolicitud solicitud) => Operacion(() => tienda.ActualizarStockMinimo(solicitud));
    [HttpPost("alternativas"), ValidateAntiForgeryToken] public IActionResult Alternativas(AlternativasSolicitud solicitud) => Operacion(() => tienda.RegistrarAlternativas(solicitud));
}
