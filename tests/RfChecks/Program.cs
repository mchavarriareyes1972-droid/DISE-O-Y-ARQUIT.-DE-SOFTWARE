using Tienda_Giovanna.Models;
using Tienda_Giovanna.Services;
using System.Text.Json;

int comprobaciones = 0;
void Verificar(bool condicion, string mensaje)
{
    if (!condicion) throw new Exception(mensaje);
    comprobaciones++;
}
void Rechaza(Action accion, string mensaje)
{
    try { accion(); }
    catch (ArgumentException) { comprobaciones++; return; }
    throw new Exception(mensaje);
}
VentaSolicitud Solicitud(int id, int cantidad, decimal recibido = 100m) => new()
{
    FormaPago = "Efectivo", MontoRecibido = recibido,
    Items = new() { new() { ProductoId = id, Cantidad = cantidad } }
};

DateTimeOffset momento = new(2026, 10, 14, 4, 59, 0, TimeSpan.Zero);
TiendaService tienda = new(() => momento);
Verificar(tienda.BuscarProductos("p001").Single().Id == 1, "RF01: buscar por código.");
Verificar(tienda.BuscarProductos("leche").Single().Id == 2, "RF01: buscar por nombre.");
Verificar(tienda.BuscarProductos("no existe").Count == 0, "RF01: búsqueda sin coincidencias.");
var copia = tienda.BuscarProductos(null); copia[0].Stock = 0;
Verificar(tienda.BuscarProductos("P001").Single().Stock == 18, "Las consultas no deben permitir modificar el almacenamiento.");
Venta efectivo = tienda.RegistrarVenta(Solicitud(1, 2, 10m));
Verificar(efectivo.Total == 9m && efectivo.Vuelto == 1m && efectivo.Detalles.Single().Cantidad == 2, "RF08: total y vuelto con decimales.");
Verificar(tienda.BuscarProductos("P001").Single().Stock == 16, "La venta descuenta stock.");
Rechaza(() => tienda.RegistrarVenta(Solicitud(1, 1, 4m)), "No debe aceptar pago insuficiente.");
Rechaza(() => tienda.RegistrarVenta(Solicitud(1, -1)), "No debe aceptar cantidades negativas.");
var demasiadas = Solicitud(1, 10); demasiadas.Items.Add(new() { ProductoId = 1, Cantidad = 10 });
Rechaza(() => tienda.RegistrarVenta(demasiadas), "Debe sumar cantidades repetidas antes de validar stock.");
var incompleta = Solicitud(1, 1); incompleta.Items.Add(new() { ProductoId = 6, Cantidad = 1 });
Rechaza(() => tienda.RegistrarVenta(incompleta), "Una línea sin stock debe impedir toda la venta.");
Verificar(tienda.BuscarProductos("P001").Single().Stock == 16 && tienda.ConsultarVentas(null, null).Count == 1, "Una venta rechazada no debe modificar datos.");

momento = new DateTimeOffset(2026, 10, 14, 5, 0, 0, TimeSpan.Zero);
var fiado = Solicitud(2, 1); fiado.FormaPago = "Fiado"; fiado.ClienteId = 1;
Venta credito = tienda.RegistrarVenta(fiado);
Verificar(credito.ClienteNombre == "María López" && tienda.ConsultarClientes().Single(c => c.Id == 1).Deuda == 16.70m, "La venta al fiado suma la deuda y conserva el cliente.");
Verificar(tienda.ConsultarVentas(new DateOnly(2026, 10, 13), new DateOnly(2026, 10, 13)).Single().Id == efectivo.Id, "RF03: fecha local de Lima al final del día.");
Verificar(tienda.ConsultarVentas(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 14)).Single().Id == credito.Id, "RF03: fechas inclusivas al comenzar el día.");
Verificar(tienda.ConsultarVentas(new DateOnly(2026, 10, 15), null).Count == 0, "RF03: periodo sin ventas.");
Rechaza(() => tienda.ConsultarVentas(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 14)), "Debe rechazar periodos invertidos.");
Pago abono = tienda.RegistrarPago(new() { ClienteId = 1, Monto = 6.70m, Metodo = "Efectivo" });
Verificar(abono.SaldoRestante == 10m && abono.Fecha == momento, "RF04: abono parcial con fecha.");
Pago cancelacion = tienda.RegistrarPago(new() { ClienteId = 1, Monto = 10m, Metodo = "Digital" });
Verificar(cancelacion.SaldoRestante == 0 && tienda.ConsultarPagos(1).Count == 2, "RF04: pago total e historial.");
Rechaza(() => tienda.RegistrarPago(new() { ClienteId = 1, Monto = .01m, Metodo = "Efectivo" }), "No debe abonar por encima de la deuda.");
Rechaza(() => tienda.RegistrarPago(new() { ClienteId = 2, Monto = 0, Metodo = "Efectivo" }), "No debe aceptar abonos cero.");

var digital = Solicitud(5, 1); digital.FormaPago = "Digital";
Rechaza(() => tienda.RegistrarVenta(digital), "El pago digital requiere referencia confirmada.");
digital.ReferenciaPago = "OPERACION-01"; tienda.RegistrarVenta(digital);
var reporte = JsonSerializer.SerializeToElement(tienda.Reporte(null, null));
Verificar(reporte.GetProperty("CantidadVentas").GetInt32() == 3 && reporte.GetProperty("TotalVendido").GetDecimal() == 15.70m, "RF06: resumen de ventas y total correcto.");
Verificar(reporte.GetProperty("CobradoEnVentas").GetDecimal() == 11.50m && reporte.GetProperty("TotalFiado").GetDecimal() == 4.20m && reporte.GetProperty("Abonos").GetDecimal() == 16.70m, "RF06: ventas al fiado y abonos separados.");

Verificar(tienda.BuscarProductos("P002").Single().StockBajo, "RF05: alerta por stock mínimo.");
tienda.ActualizarStockMinimo(new() { ProductoId = 2, StockMinimo = 0 });
Verificar(!tienda.BuscarProductos("P002").Single().StockBajo, "RF05: mínimo configurable por producto.");
Rechaza(() => tienda.ActualizarStockMinimo(new() { ProductoId = 1, StockMinimo = -1 }), "Mínimo no negativo.");
tienda.RegistrarAlternativas(new() { ProductoId = 6, AlternativosIds = new() { 7, 7 } });
var ficha = JsonSerializer.SerializeToElement(tienda.ConsultarProducto(6));
Verificar(ficha.GetProperty("Producto").GetProperty("Codigo").GetString() == "P006" && ficha.GetProperty("Alternativas").GetArrayLength() == 1, "RF02/RF07: ficha y alternativa disponible.");
Rechaza(() => tienda.RegistrarAlternativas(new() { ProductoId = 6, AlternativosIds = new() { 6 } }), "Un producto no puede ser su propia alternativa.");
Rechaza(() => tienda.RegistrarAlternativas(new() { ProductoId = 6, AlternativosIds = new() { 999 } }), "Las alternativas deben existir.");
Compra compra = tienda.RegistrarCompra(new() { Proveedor = "Proveedor de prueba", ProductoId = 6, Cantidad = 2, CostoUnitario = 1m });
Verificar(compra.Total == 2m && tienda.BuscarProductos("P006").Single().Stock == 2 && tienda.ConsultarCompras().Count == 1, "Compra con historial y reposición de stock.");
tienda.RegistrarVenta(Solicitud(6, 2, 3m));
Verificar(tienda.BuscarProductos("P006").Single().Stock == 0, "Se puede vender el stock repuesto.");
Console.WriteLine($"OK: {comprobaciones} comprobaciones de RF superadas.");
