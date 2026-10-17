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
Compra compra = tienda.RegistrarCompra(new() { ProveedorId = 1, Items = new() { new() { ProductoId = 6, Cantidad = 2, CostoUnitario = 1m } } });
Verificar(compra.Total == 2m && tienda.BuscarProductos("P006").Single().Stock == 2 && tienda.ConsultarCompras().Count == 1, "Compra con historial y reposición de stock.");
tienda.RegistrarVenta(Solicitud(6, 2, 3m));
Verificar(tienda.BuscarProductos("P006").Single().Stock == 0, "Se puede vender el stock repuesto.");
// Gestión de productos: cambios de catálogo sin alterar el historial ni las existencias.
TiendaService gestion = new();
RegistroProductoSolicitud Nuevo(string codigo = "P008") => new()
    { Codigo = codigo, Nombre = "Atún en lata", Precio = 6.50m, StockInicial = 20, StockMinimo = 5 };
Producto atun = gestion.RegistrarProducto(Nuevo(" p008 "));
Verificar(atun.Id == 8 && atun.Codigo == "P008" && atun.Stock == 20 && atun.Activo, "Registrar producto y normalizar código.");
Rechaza(() => gestion.RegistrarProducto(Nuevo("p008")), "Rechazar códigos duplicados sin distinguir mayúsculas.");
var invalido = Nuevo("P009"); invalido.Nombre = "   ";
Rechaza(() => gestion.RegistrarProducto(invalido), "Rechazar nombre vacío.");
invalido = Nuevo("P009"); invalido.Precio = 0;
Rechaza(() => gestion.RegistrarProducto(invalido), "Rechazar precio cero.");
invalido.Precio = .001m;
Rechaza(() => gestion.RegistrarProducto(invalido), "Rechazar precio que se redondea a cero.");
invalido = Nuevo("P009"); invalido.StockInicial = -1;
Rechaza(() => gestion.RegistrarProducto(invalido), "Rechazar stock inicial negativo.");
invalido = Nuevo("P009"); invalido.StockMinimo = -1;
Rechaza(() => gestion.RegistrarProducto(invalido), "Rechazar stock mínimo negativo.");
Verificar(gestion.BuscarProductos(null).Count == 8, "Los registros rechazados no modifican el catálogo.");
Venta historica = gestion.RegistrarVenta(Solicitud(atun.Id, 2, 20m));
Producto editado = gestion.EditarProducto(atun.Id, new() { Codigo = "P008", Nombre = "Atún premium", Precio = 7m, StockMinimo = 3 });
Verificar(editado.Stock == 18 && editado.Precio == 7m, "Editar conserva stock y actualiza precio.");
Venta anterior = gestion.ConsultarVenta(historica.Id);
Verificar(anterior.Total == 13m && anterior.Detalles.Single().PrecioUnitario == 6.50m && anterior.Detalles.Single().Nombre == "Atún en lata", "El historial conserva el precio y nombre originales.");
Verificar(gestion.RegistrarVenta(Solicitud(atun.Id, 1, 10m)).Total == 7m, "Las ventas nuevas utilizan el nuevo precio.");
Rechaza(() => gestion.EditarProducto(atun.Id, new() { Codigo = "P001", Nombre = "Duplicado", Precio = 1m }), "No permitir duplicados al editar.");
Verificar(gestion.BuscarProductos("P008").Single().Nombre == "Atún premium", "Una edición rechazada no cambia datos.");
gestion.CambiarEstadoProducto(atun.Id, new() { Activo = false });
Verificar(gestion.BuscarProductos("P008").Count == 0 && !gestion.BuscarProductos("P008", true).Single().Activo, "Ocultar inactivos y conservarlos para administración.");
Rechaza(() => gestion.RegistrarVenta(Solicitud(atun.Id, 1)), "Rechazar venta de producto inactivo incluso desde un carrito anterior.");
Rechaza(() => gestion.RegistrarCompra(new() { ProveedorId = 1, Items = new() { new() { ProductoId = atun.Id, Cantidad = 1, CostoUnitario = 1m } } }), "Rechazar compra de inactivo.");
Rechaza(() => gestion.RegistrarProducto(Nuevo()), "Los códigos de inactivos también son únicos.");
Verificar(gestion.BuscarProductos("P008", true).Single().Stock == 17 && gestion.ConsultarVenta(historica.Id).Total == 13m, "Desactivar no borra stock ni ventas anteriores.");
gestion.CambiarEstadoProducto(7, new() { Activo = false });
Verificar(JsonSerializer.SerializeToElement(gestion.ConsultarProducto(6)).GetProperty("Alternativas").GetArrayLength() == 0, "No ofrecer alternativas inactivas.");
Rechaza(() => gestion.RegistrarAlternativas(new() { ProductoId = 6, AlternativosIds = new() { 7 } }), "No registrar alternativas inactivas.");
gestion.CambiarEstadoProducto(2, new() { Activo = false });
Verificar(JsonSerializer.SerializeToElement(gestion.Reporte(null, null)).GetProperty("ProductosStockBajo").GetInt32() == 1, "Las alertas excluyen productos inactivos.");
gestion.CambiarEstadoProducto(atun.Id, new() { Activo = true });
Verificar(gestion.BuscarProductos("P008").Single().Stock == 17, "Reactivar conserva datos y devuelve el producto al catálogo.");
TiendaService modulos = new();
Cliente clienteNuevo = modulos.RegistrarCliente(new() { Documento = "33333333", Nombre = "Cliente de prueba" });
Verificar(clienteNuevo.Deuda == 0 && clienteNuevo.Activo, "Un cliente nuevo inicia sin deuda.");
Rechaza(() => modulos.RegistrarCliente(new() { Documento = "33333333", Nombre = "Duplicado" }), "DNI único.");
Rechaza(() => modulos.RegistrarCliente(new() { Documento = "123", Nombre = "Incorrecto" }), "Validar DNI.");
var ventaFiada = Solicitud(1, 1); ventaFiada.FormaPago = "Fiado"; ventaFiada.ClienteId = clienteNuevo.Id;
Venta ventaCliente = modulos.RegistrarVenta(ventaFiada);
modulos.EditarCliente(clienteNuevo.Id, new() { Documento = "33333333", Nombre = "Cliente editado" });
Verificar(modulos.ConsultarClientes().Single(c => c.Id == clienteNuevo.Id).Deuda == 4.50m && modulos.ConsultarVenta(ventaCliente.Id).ClienteNombre == "Cliente de prueba", "Editar conserva deuda e historial.");
modulos.CambiarEstadoCliente(clienteNuevo.Id, new() { Activo = false });
Rechaza(() => modulos.RegistrarVenta(ventaFiada), "Un cliente inactivo no recibe fiados.");
Verificar(modulos.RegistrarPago(new() { ClienteId = clienteNuevo.Id, Monto = 4.50m, Metodo = "Efectivo" }).SaldoRestante == 0, "Un cliente inactivo puede pagar su deuda.");

Proveedor proveedor = modulos.RegistrarProveedor(new() { Ruc = "20999999999", RazonSocial = "Proveedor de prueba", Correo = "prueba@example.com" });
Rechaza(() => modulos.RegistrarProveedor(new() { Ruc = proveedor.Ruc, RazonSocial = "Duplicado" }), "RUC único.");
Rechaza(() => modulos.RegistrarProveedor(new() { Ruc = "20888888888", RazonSocial = "Inválido", Correo = "no-es-correo" }), "Correo válido.");
var solicitudCompra = new CompraSolicitud { ProveedorId = proveedor.Id, Items = new() {
    new() { ProductoId = 1, Cantidad = 2, CostoUnitario = 3m }, new() { ProductoId = 2, Cantidad = 3, CostoUnitario = 2m } } };
Compra multiple = modulos.RegistrarCompra(solicitudCompra);
Verificar(multiple.Total == 12m && multiple.Detalles.Count == 2 && modulos.BuscarProductos("P001").Single().Stock == 19 && modulos.BuscarProductos("P002").Single().Stock == 6, "Compra múltiple actualiza ambos productos.");
solicitudCompra.Items[1].ProductoId = 999;
Rechaza(() => modulos.RegistrarCompra(solicitudCompra), "Rechazar compra con una línea inválida.");
Verificar(modulos.BuscarProductos("P001").Single().Stock == 19 && modulos.ConsultarCompras().Count == 1, "Una compra rechazada no cambia stock ni historial.");
solicitudCompra.Items[1].ProductoId = 1;
Rechaza(() => modulos.RegistrarCompra(solicitudCompra), "Rechazar líneas duplicadas.");
modulos.EditarProveedor(proveedor.Id, new() { Ruc = proveedor.Ruc, RazonSocial = "Proveedor editado" });
Verificar(modulos.ConsultarCompra(multiple.Id).ProveedorNombre == "Proveedor de prueba", "El historial conserva el proveedor original.");
modulos.CambiarEstadoProveedor(proveedor.Id, new() { Activo = false });
Rechaza(() => modulos.RegistrarCompra(new() { ProveedorId = proveedor.Id, Items = new() { new() { ProductoId = 1, Cantidad = 1, CostoUnitario = 1m } } }), "No comprar a un proveedor inactivo.");

UsuariosService usuarios = new();
var admin = usuarios.Autenticar("ADMIN", "Giovanna2026!");
Verificar(admin.HasValue && admin.Value.Usuario.Rol == Roles.Administrador, "Autenticar administrador.");
Verificar(!usuarios.Autenticar("admin", "incorrecta").HasValue, "Rechazar contraseña incorrecta.");
Rechaza(() => usuarios.EditarUsuario(1, new() { Login = "admin", Nombre = "Admin", Rol = Roles.Cajero, Activo = true }), "No quitar el último administrador.");
UsuarioResumen cuenta = usuarios.RegistrarUsuario(new() { Login = "auxiliar", Nombre = "Auxiliar", Rol = Roles.Cajero, Password = "Prueba2026!" });
Rechaza(() => usuarios.RegistrarUsuario(new() { Login = "AUXILIAR", Nombre = "Otro", Rol = Roles.Cajero, Password = "Prueba2026!" }), "Usuario único.");
Rechaza(() => usuarios.RegistrarUsuario(new() { Login = "invalido", Nombre = "Otro", Rol = "Dueño", Password = "Prueba2026!" }), "Solo aceptar los dos roles definidos.");
var sesion = usuarios.Autenticar("auxiliar", "Prueba2026!")!.Value;
usuarios.EditarUsuario(cuenta.Id, new() { Login = "auxiliar", Nombre = "Auxiliar", Rol = Roles.Administrador, Activo = true, Password = "Nueva2026!" });
Verificar(!usuarios.ValidarSesion(cuenta.Id, sesion.Version) && !usuarios.Autenticar("auxiliar", "Prueba2026!").HasValue, "Cambiar permisos y contraseña invalida la sesión anterior.");
usuarios.EditarUsuario(cuenta.Id, new() { Login = "auxiliar", Nombre = "Auxiliar", Rol = Roles.Administrador, Activo = false });
Verificar(!usuarios.Autenticar("auxiliar", "Nueva2026!").HasValue, "Un usuario inactivo no inicia sesión.");
Verificar(!JsonSerializer.Serialize(usuarios.ConsultarUsuarios()).Contains("Password", StringComparison.OrdinalIgnoreCase), "Las consultas no devuelven contraseñas ni hashes.");
for (int i = 0; i < 5; i++) usuarios.Autenticar("cajero", "incorrecta");
Verificar(!usuarios.Autenticar("cajero", "Cajero2026!").HasValue, "Limitar intentos de inicio de sesión.");

ExportacionService exportacion = new();
for (int i = 0; i < 15; i++) modulos.RegistrarVenta(Solicitud(5, 1));
ReporteVentas reporteExportar = modulos.Reporte(null, null);
string csv = System.Text.Encoding.UTF8.GetString(exportacion.Csv(reporteExportar));
Verificar(csv.Contains("Resumen") && csv.Contains("Abono") && csv.Contains("Cliente de prueba"), "CSV incluye ventas, abonos e historial.");
var peligroso = new ReporteVentas { Ventas = new() { new() { Id = 1, Fecha = DateTimeOffset.UtcNow, ClienteNombre = "=1+1", FormaPago = "Fiado" } } };
Verificar(System.Text.Encoding.UTF8.GetString(exportacion.Csv(peligroso)).Contains("'=1+1"), "El CSV neutraliza fórmulas ingresadas como texto.");
byte[] pdf = exportacion.Pdf(reporteExportar);
Verificar(System.Text.Encoding.ASCII.GetString(pdf, 0, 5) == "%PDF-", "Generar un PDF real.");
string salida = Path.Combine(Path.GetTempPath(), "giovanna-qa"); Directory.CreateDirectory(salida);
File.WriteAllBytes(Path.Combine(salida, "reporte_servicio.pdf"), pdf);
File.WriteAllBytes(Path.Combine(salida, "reporte_vacio.pdf"), exportacion.Pdf(modulos.Reporte(new DateOnly(2000, 1, 1), new DateOnly(2000, 1, 1))));
Console.WriteLine($"OK: {comprobaciones} comprobaciones de reglas de negocio superadas.");
