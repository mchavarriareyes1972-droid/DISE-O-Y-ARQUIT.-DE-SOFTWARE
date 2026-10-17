using System.Text.Json;
using Microsoft.Data.SqlClient;
using Tienda_Giovanna.Datos;
using Tienda_Giovanna.Models;
using Tienda_Giovanna.Services;

// Ejecutar solo sobre una base temporal creada y cargada por CI.
string cadena = Environment.GetEnvironmentVariable("GIOVANNA_SQL_PRUEBAS")
    ?? throw new InvalidOperationException("Falta GIOVANNA_SQL_PRUEBAS para la base temporal de pruebas.");
ConexionBD.Instancia.Configurar(cadena);
string claveAdmin=Environment.GetEnvironmentVariable("GIOVANNA_ADMIN_PASSWORD")!;
string claveCaja=Environment.GetEnvironmentVariable("GIOVANNA_CAJERO_PASSWORD")!;
string clavePrueba=claveCaja+"Aux";
string claveNueva=claveCaja+"Nueva";
int comprobaciones=0;
void Verificar(bool condicion, string mensaje)
{
    if (!condicion) throw new Exception(mensaje);
    Console.WriteLine("OK: "+mensaje); comprobaciones++;
}
void Rechazar(Action accion, string mensaje)
{
    try { accion(); } catch (ArgumentException) { Verificar(true,mensaje); return; }
    throw new Exception(mensaje);
}
int Escalar(string sql)
{
    using var c=ConexionBD.Instancia.CrearConexion();c.Open();
    using var cmd=new SqlCommand(sql,c);return Convert.ToInt32(cmd.ExecuteScalar());
}
Verificar(ReferenceEquals(ConexionBD.Instancia,ConexionBD.Instancia),"Singleton conserva su instancia.");
using(var a=ConexionBD.Instancia.CrearConexion())
using(var b=ConexionBD.Instancia.CrearConexion())
    Verificar(!ReferenceEquals(a,b),"Cada operacion recibe su propia conexion.");
Verificar(Escalar("SELECT COUNT(*) FROM Usuarios")==2,"Carga original: un administrador y un cajero.");
Verificar(Escalar("SELECT COUNT(*) FROM Productos")==120,"Carga nativa de 120 productos.");
Verificar(Escalar("SELECT COUNT(*) FROM Ventas")==931,"Carga nativa de 931 ventas.");
Verificar(Escalar("SELECT COUNT(*) FROM Compras")==60 && Escalar("SELECT COUNT(*) FROM Pagos")==124,"Compras y abonos cargados en SQL Server.");
Verificar(Escalar("SELECT COUNT(*) FROM vw_SaldosClientes WHERE Deuda<0")==0,"Saldos no negativos.");
Verificar(Escalar("SELECT COUNT(*) FROM vw_StockBajo")==7,"Vista de stock bajo.");
Verificar(Escalar("SELECT COUNT(*) FROM Productos p WHERE p.Stock<>COALESCE((SELECT SUM(Cantidad) FROM DetalleCompras c WHERE c.IdProducto=p.IdProducto),0)-COALESCE((SELECT SUM(Cantidad) FROM DetalleVentas v WHERE v.IdProducto=p.IdProducto),0)")==0,"Stock corresponde a compras menos ventas.");
var usuarios=new UsuariosSqlService(ConexionBD.Instancia);
Verificar(usuarios.Autenticar("ADMIN",claveAdmin)?.Usuario.Rol==Roles.Administrador,"Hash del administrador del archivo SQL valido en PasswordHasher de .NET.");
Verificar(usuarios.Autenticar("cajero",claveCaja)?.Usuario.Rol==Roles.Cajero,"Hash del cajero del archivo SQL valido en PasswordHasher de .NET.");
Verificar(!usuarios.Autenticar("admin' OR 1=1--","invalida").HasValue,"Entrada maliciosa no inicia sesion.");
Rechazar(()=>usuarios.EditarUsuario(1,new(){Nombre="Admin",Login="admin",Rol=Roles.Cajero,Activo=true}),"No quitar al ultimo administrador.");
var auxiliar=usuarios.RegistrarUsuario(new(){Nombre="Prueba SQL",Login="qa_auxiliar",Rol=Roles.Cajero,Password=clavePrueba});
var otraInstancia=new UsuariosSqlService(ConexionBD.Instancia);
Verificar(otraInstancia.Autenticar("qa_auxiliar",clavePrueba).HasValue,"La cuenta persiste entre instancias del servicio.");
Rechazar(()=>otraInstancia.RegistrarUsuario(new(){Nombre="Repetido",Login="QA_AUXILIAR",Rol=Roles.Cajero,Password=clavePrueba}),"Login unico sin distinguir mayusculas.");
Rechazar(()=>usuarios.RegistrarUsuario(new(){Nombre="Rol invalido",Login="qa_invalido",Rol="Dueno",Password=clavePrueba}),"Solo aceptar roles permitidos.");
var sesion=usuarios.Autenticar("qa_auxiliar",clavePrueba)!.Value;
usuarios.EditarUsuario(auxiliar.Id,new(){Nombre="Prueba SQL",Login="qa_auxiliar",Rol=Roles.Cajero,Activo=true,Password=claveNueva});
Verificar(!otraInstancia.ValidarSesion(auxiliar.Id,sesion.Version),"El cambio de contrasena invalida la sesion en otra instancia.");
Verificar(!otraInstancia.Autenticar("qa_auxiliar",clavePrueba).HasValue && otraInstancia.Autenticar("qa_auxiliar",claveNueva).HasValue,"Cambio de contrasena guardado en SQL.");
usuarios.EditarUsuario(auxiliar.Id,new(){Nombre="Prueba SQL",Login="qa_auxiliar",Rol=Roles.Cajero,Activo=false});
Verificar(!otraInstancia.Autenticar("qa_auxiliar",claveNueva).HasValue,"La baja persiste.");
usuarios.EditarUsuario(auxiliar.Id,new(){Nombre="Prueba SQL",Login="qa_auxiliar",Rol=Roles.Cajero,Activo=true});
for(int i=0;i<5;i++)new UsuariosSqlService(ConexionBD.Instancia).Autenticar("qa_auxiliar","incorrecta");
Verificar(!otraInstancia.Autenticar("qa_auxiliar",claveNueva).HasValue,"El bloqueo de cinco intentos persiste entre instancias.");
Verificar(!JsonSerializer.Serialize(usuarios.ConsultarUsuarios()).Contains("Password",StringComparison.OrdinalIgnoreCase),"Las consultas no exponen hashes.");
// Dos peticiones de registro simultaneas: la restriccion unica protege la cuenta.
var registros=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Task.Run(()=>
{
    try { new UsuariosSqlService(ConexionBD.Instancia).RegistrarUsuario(new(){Nombre="Concurrente",Login="qa_concurrente",Rol=Roles.Cajero,Password=clavePrueba});return true; }
    catch(ArgumentException){return false;}
})));
Verificar(registros.Count(x=>x)==1,"Solo se crea una cuenta ante registros simultaneos.");
// Limpiar unicamente las cuentas temporales sin movimientos historicos.
using(var c=ConexionBD.Instancia.CrearConexion())
{
    c.Open();using var cmd=new SqlCommand("DELETE FROM Usuarios WHERE Login IN ('qa_auxiliar','qa_concurrente')",c);cmd.ExecuteNonQuery();
}
// Clientes: el mismo Singleton y las mismas tablas que utiliza la aplicacion.
var clientesSql = new ClientesSqlService(ConexionBD.Instancia);
var tiendaSql = new TiendaService(clientesSql);
Verificar(tiendaSql.ConsultarClientes(true).Count == 60, "Clientes consulta las 60 personas de SQL, sin mezclar los dos ejemplos en memoria.");
Verificar(tiendaSql.ConsultarPagos(null).Count == 124, "Historial de pagos consultado desde SQL.");
var original = tiendaSql.ConsultarClientes(true).First(c => c.Deuda > 0);
var historial = tiendaSql.ConsultarPagos(original.Id);
Verificar(historial.All(p => p.ClienteId == original.Id), "Filtro de historial por cliente.");
var datosOriginales = new ClienteSolicitud { Documento = original.Documento, Nombre = original.Nombre, Telefono = original.Telefono, Direccion = original.Direccion };
try
{
    var editado = tiendaSql.EditarCliente(original.Id, new() { Documento = original.Documento, Nombre = "Cliente editado en SQL", Telefono = "999000111", Direccion = "Direccion de prueba" });
    Verificar(editado.Deuda == original.Deuda && Escalar($"SELECT COUNT(*) FROM Clientes WHERE IdCliente = {original.Id} AND SaldoInicial >= 0") == 1, "Editar conserva la deuda calculada.");
    Verificar(JsonSerializer.Serialize(tiendaSql.ConsultarPagos(original.Id)) == JsonSerializer.Serialize(historial), "Editar no modifica el historial de pagos ni sus nombres originales.");
    tiendaSql.CambiarEstadoCliente(original.Id, new() { Activo = false });
    Verificar(!tiendaSql.ConsultarClientes().Any(c => c.Id == original.Id) && tiendaSql.ConsultarClientes(true).Single(c => c.Id == original.Id).Deuda == original.Deuda, "Inactivos se filtran y conservan su deuda.");
    Rechazar(() => tiendaSql.RegistrarCliente(datosOriginales), "DNI de cliente inactivo sigue reservado.");
}
finally
{
    tiendaSql.EditarCliente(original.Id, datosOriginales);
    tiendaSql.CambiarEstadoCliente(original.Id, new() { Activo = original.Activo });
}
ClienteSolicitud datosCliente = new() { Documento = "99000001", Nombre = "  Cliente SQL nuevo  " };
try
{
    var nuevo = tiendaSql.RegistrarCliente(datosCliente);
    Verificar(nuevo.Deuda == 0 && nuevo.Activo && nuevo.Nombre == "Cliente SQL nuevo" && nuevo.Telefono == "" && nuevo.Direccion == "", "Cliente nuevo normaliza campos, usa saldo cero y estado activo.");
    var segundaTienda = new TiendaService(new ClientesSqlService(ConexionBD.Instancia));
    Verificar(segundaTienda.ConsultarClientes().Any(c => c.Id == nuevo.Id), "Un servicio nuevo recupera el cliente guardado en SQL.");
    Rechazar(() => tiendaSql.EditarCliente(nuevo.Id, datosOriginales), "No se puede editar a un DNI ya registrado.");
    Rechazar(() => tiendaSql.RegistrarCliente(new() { Documento = "123", Nombre = "Invalido" }), "DNI invalido no se guarda.");
    Rechazar(() => tiendaSql.RegistrarCliente(new() { Documento = "99000003", Nombre = " " }), "Nombre vacio no se guarda.");
    Rechazar(() => tiendaSql.RegistrarCliente(new() { Documento = "99000003", Nombre = new string('x', 101) }), "Nombre demasiado largo no se guarda.");
    Rechazar(() => tiendaSql.EditarCliente(int.MaxValue, datosCliente), "Editar un cliente inexistente devuelve un error controlado.");
    Rechazar(() => tiendaSql.CambiarEstadoCliente(int.MaxValue, new() { Activo = false }), "Estado de un cliente inexistente devuelve un error controlado.");
    Rechazar(() => tiendaSql.CambiarEstadoCliente(nuevo.Id, new()), "El estado es obligatorio.");
    var antes = JsonSerializer.Serialize(tiendaSql.ConsultarClientes(true));
    Rechazar(() => tiendaSql.RegistrarPago(new() { ClienteId = original.Id, Monto = 1, Metodo = "Efectivo" }), "No registrar abonos en memoria sobre clientes de SQL.");
    int stockAntes = tiendaSql.BuscarProductos(null).First(p => p.Id == 1).Stock;
    Rechazar(() => tiendaSql.RegistrarVenta(new() { FormaPago = "Fiado", ClienteId = original.Id, Items = new() { new() { ProductoId = 1, Cantidad = 1 } } }), "No registrar fiados hasta migrar ventas.");
    Verificar(antes == JsonSerializer.Serialize(tiendaSql.ConsultarClientes(true)) && tiendaSql.BuscarProductos(null).First(p => p.Id == 1).Stock == stockAntes && tiendaSql.ConsultarPagos(null).Count == 124, "Operaciones pendientes no cambian deudas, stock ni pagos.");
    var simultaneos = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
    {
        try { new ClientesSqlService(ConexionBD.Instancia).RegistrarCliente(new() { Documento = "99000002", Nombre = "Concurrente" }); return true; }
        catch (ArgumentException) { return false; }
    })));
    Verificar(simultaneos.Count(x => x) == 1, "DNI unico ante registros simultaneos.");
}
finally
{
    using var c = ConexionBD.Instancia.CrearConexion(); c.Open();
    using var cmd = new SqlCommand("DELETE FROM Clientes WHERE Documento IN ('99000001', '99000002')", c); cmd.ExecuteNonQuery();
}
Verificar(tiendaSql.ConsultarClientes(true).Count == 60, "Pruebas de clientes dejan intacta la carga original.");

// Proveedores: persistencia y compatibilidad con las compras de practica.
var proveedoresSql = new ProveedoresSqlService(ConexionBD.Instancia);
var tiendaProveedores = new TiendaService(clientesSql, proveedoresSql);
Verificar(tiendaProveedores.ConsultarProveedores(true).Count == 12, "Proveedores consulta los 12 registros de SQL sin agregar el ejemplo de memoria.");
var proveedorOriginal = tiendaProveedores.ConsultarProveedores().First(p => p.Id != 1);
var proveedorDatos = new ProveedorSolicitud { Ruc = proveedorOriginal.Ruc, RazonSocial = proveedorOriginal.RazonSocial,
    Contacto = proveedorOriginal.Contacto, Telefono = proveedorOriginal.Telefono, Correo = proveedorOriginal.Correo, Direccion = proveedorOriginal.Direccion };
string HistorialProveedor()
{
    using var c = ConexionBD.Instancia.CrearConexion(); c.Open();
    using var cmd = new SqlCommand("SELECT (SELECT * FROM Compras WHERE IdProveedor = @id ORDER BY IdCompra FOR JSON PATH), (SELECT d.* FROM DetalleCompras d INNER JOIN Compras c ON c.IdCompra = d.IdCompra WHERE c.IdProveedor = @id ORDER BY d.IdDetalleCompra FOR JSON PATH)", c);
    cmd.Parameters.AddWithValue("@id", proveedorOriginal.Id);
    using var lector = cmd.ExecuteReader(); lector.Read();
    return lector.GetString(0) + lector.GetString(1);
}
string historialProveedor = HistorialProveedor();
try
{
    var editado = tiendaProveedores.EditarProveedor(proveedorOriginal.Id, new() { Ruc = proveedorOriginal.Ruc, RazonSocial = "Proveedor SQL editado", Correo = "contacto@example.test" });
    Verificar(editado.RazonSocial == "Proveedor SQL editado" && new ProveedoresSqlService(ConexionBD.Instancia).ConsultarProveedorActivo(editado.Id).Correo == "contacto@example.test", "Edicion del proveedor guardada en SQL y visible en otra instancia.");
    var compra = tiendaProveedores.RegistrarCompra(new() { ProveedorId = editado.Id, Items = new() { new() { ProductoId = 1, Cantidad = 1, CostoUnitario = 2 } } });
    Verificar(compra.ProveedorId == editado.Id && compra.ProveedorNombre == editado.RazonSocial, "Compras reconoce un proveedor SQL cuyo identificador no existe en la lista de practica.");
    tiendaProveedores.CambiarEstadoProveedor(editado.Id, new() { Activo = false });
    Verificar(!tiendaProveedores.ConsultarProveedores().Any(p => p.Id == editado.Id) && !tiendaProveedores.ConsultarProveedores(true).Single(p => p.Id == editado.Id).Activo, "Proveedor inactivo solo aparece al incluir inactivos.");
    Rechazar(() => tiendaProveedores.RegistrarProveedor(proveedorDatos), "RUC del proveedor inactivo sigue reservado.");
    int stock = tiendaProveedores.BuscarProductos(null).First(p => p.Id == 1).Stock;
    Rechazar(() => tiendaProveedores.RegistrarCompra(new() { ProveedorId = editado.Id, Items = new() { new() { ProductoId = 1, Cantidad = 1, CostoUnitario = 2 } } }), "Compras vuelve a comprobar el estado actual del proveedor SQL.");
    Verificar(tiendaProveedores.BuscarProductos(null).First(p => p.Id == 1).Stock == stock && tiendaProveedores.ConsultarCompras().Count == 1, "Una compra de proveedor inactivo no cambia stock ni movimientos de practica.");
    Verificar(HistorialProveedor() == historialProveedor, "Editar y desactivar un proveedor conserva todas sus compras SQL y sus detalles historicos.");
    Verificar(Escalar("SELECT COUNT(*) FROM Compras") == 60, "Las compras de practica no escriben movimientos en SQL durante esta etapa.");
}
finally
{
    tiendaProveedores.EditarProveedor(proveedorOriginal.Id, proveedorDatos);
    tiendaProveedores.CambiarEstadoProveedor(proveedorOriginal.Id, new() { Activo = proveedorOriginal.Activo });
}
ProveedorSolicitud datosNuevoProveedor = new() { Ruc = "20990000001", RazonSocial = "  Distribuidor SQL de prueba  " };
try
{
    var nuevo = tiendaProveedores.RegistrarProveedor(datosNuevoProveedor);
    Verificar(nuevo.Activo && nuevo.RazonSocial == "Distribuidor SQL de prueba" && nuevo.Contacto == "" && nuevo.Correo == "" && nuevo.Telefono == "" && nuevo.Direccion == "", "Alta de proveedor normaliza datos y admite campos opcionales vacios.");
    var segundaTienda = new TiendaService(null, new ProveedoresSqlService(ConexionBD.Instancia));
    Verificar(segundaTienda.ConsultarProveedores().Any(p => p.Id == nuevo.Id), "Proveedor nuevo persiste entre instancias del servicio.");
    Rechazar(() => tiendaProveedores.EditarProveedor(nuevo.Id, proveedorDatos), "Edicion rechaza un RUC ya registrado.");
    Rechazar(() => tiendaProveedores.RegistrarProveedor(new() { Ruc = "123", RazonSocial = "Invalido" }), "RUC corto no se guarda.");
    Rechazar(() => tiendaProveedores.RegistrarProveedor(new() { Ruc = "2099000000A", RazonSocial = "Invalido" }), "RUC con letras no se guarda.");
    Rechazar(() => tiendaProveedores.RegistrarProveedor(new() { Ruc = "20990000003", RazonSocial = " " }), "Razon social vacia no se guarda.");
    Rechazar(() => tiendaProveedores.RegistrarProveedor(new() { Ruc = "20990000003", RazonSocial = new string('x', 101) }), "Razon social demasiado larga no se guarda.");
    Rechazar(() => tiendaProveedores.RegistrarProveedor(new() { Ruc = "20990000003", RazonSocial = "Invalido", Correo = "sin-arroba" }), "Correo invalido no se guarda.");
    Rechazar(() => tiendaProveedores.EditarProveedor(int.MaxValue, datosNuevoProveedor), "Editar proveedor inexistente devuelve un error controlado.");
    Rechazar(() => tiendaProveedores.CambiarEstadoProveedor(int.MaxValue, new() { Activo = true }), "Estado de proveedor inexistente devuelve un error controlado.");
    Rechazar(() => tiendaProveedores.CambiarEstadoProveedor(nuevo.Id, new()), "El estado del proveedor es obligatorio.");
    var actualizado = tiendaProveedores.EditarProveedor(nuevo.Id, new() { Ruc = "20990000004", RazonSocial = "Distribuidor O'Connor", Contacto = "  Contacto  ", Telefono = "999000111", Correo = "correo@example.test", Direccion = "Av. de prueba 100" });
    Verificar(actualizado.Ruc == "20990000004" && actualizado.RazonSocial == "Distribuidor O'Connor" && actualizado.Contacto == "Contacto" && actualizado.Direccion == "Av. de prueba 100", "Editar conserva todos los campos y trata apostrofos como datos.");
    var simultaneos = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
    {
        try { new ProveedoresSqlService(ConexionBD.Instancia).RegistrarProveedor(new() { Ruc = "20990000002", RazonSocial = "Concurrente" }); return true; }
        catch (ArgumentException) { return false; }
    })));
    Verificar(simultaneos.Count(x => x) == 1, "RUC unico ante altas simultaneas.");
}
finally
{
    using var c = ConexionBD.Instancia.CrearConexion(); c.Open();
    using var cmd = new SqlCommand("DELETE FROM Proveedores WHERE Ruc IN ('20990000001', '20990000002', '20990000004')", c); cmd.ExecuteNonQuery();
}
Verificar(tiendaProveedores.ConsultarProveedores(true).Count == 12, "Pruebas de proveedores dejan intacta la carga original.");

// Productos y alternativas: no modificar movimientos ni existencias al editar el catalogo.
var productosSql = new ProductosSqlService(ConexionBD.Instancia);
var tiendaCatalogo = new TiendaService(clientesSql, proveedoresSql, productosSql);
var catalogoSql = tiendaCatalogo.BuscarProductos(null, true);
Verificar(catalogoSql.Count == 120 && catalogoSql.Sum(p => p.AlternativosIds.Count) == 56, "Catalogo SQL de 120 productos y 56 relaciones, sin agregar ejemplos en memoria.");
Verificar(tiendaCatalogo.BuscarProductos(" p001 ").Single().Nombre == "Arroz extra bolsa 1 kg" && tiendaCatalogo.BuscarProductos("ARROZ EXTRA").Single().Id == 1, "Buscar codigo y nombre sin distinguir mayusculas.");
Verificar(productosSql.ContarStockBajo() == 7 && tiendaCatalogo.Reporte(null, null).ProductosStockBajo == 7, "Alertas calculadas con la vista SQL de stock bajo.");
Verificar(catalogoSql.Count(p => p.Activo && p.StockBajo) == 7, "Las alertas de la lista coinciden con la vista SQL.");
Verificar(JsonSerializer.SerializeToElement(tiendaCatalogo.ConsultarProducto(120)).GetProperty("Producto").GetProperty("Codigo").GetString() == "P120", "La ficha reconoce productos que no existian en memoria.");
var productoOriginal = catalogoSql.First(p => p.Activo && p.StockBajo);
ProductoSolicitud datosProductoOriginal = new() { Codigo = productoOriginal.Codigo, Nombre = productoOriginal.Nombre, Precio = productoOriginal.Precio, StockMinimo = productoOriginal.StockMinimo };
string HistorialProducto()
{
    using var c = ConexionBD.Instancia.CrearConexion(); c.Open();
    using var cmd = new SqlCommand("SELECT (SELECT * FROM DetalleVentas WHERE IdProducto = @id ORDER BY IdDetalleVenta FOR JSON PATH), (SELECT * FROM DetalleCompras WHERE IdProducto = @id ORDER BY IdDetalleCompra FOR JSON PATH), (SELECT Categoria, FechaVencimiento FROM Productos WHERE IdProducto = @id FOR JSON PATH)", c);
    cmd.Parameters.AddWithValue("@id", productoOriginal.Id);
    using var lector = cmd.ExecuteReader(); lector.Read();
    return lector.GetString(0) + lector.GetString(1) + lector.GetString(2);
}
string historialProducto = HistorialProducto();
try
{
    var editado = tiendaCatalogo.EditarProducto(productoOriginal.Id, new() { Codigo = productoOriginal.Codigo, Nombre = "Producto editado SQL", Precio = 12.345m, StockMinimo = 0 });
    Verificar(editado.Stock == productoOriginal.Stock && editado.Precio == 12.35m, "Edicion conserva existencias y redondea precio a dos decimales.");
    Verificar(HistorialProducto() == historialProducto && editado.AlternativosIds.SequenceEqual(productoOriginal.AlternativosIds), "Editar no altera ventas, compras, categoria, vencimiento ni relaciones existentes.");
    tiendaCatalogo.CambiarEstadoProducto(productoOriginal.Id, new() { Activo = false });
    Verificar(!tiendaCatalogo.BuscarProductos(productoOriginal.Codigo).Any() && !tiendaCatalogo.BuscarProductos(productoOriginal.Codigo, true).Single().Activo, "Inactivos excluidos de busqueda normal y conservados en administracion.");
    Rechazar(() => tiendaCatalogo.RegistrarProducto(new() { Codigo = productoOriginal.Codigo.ToLowerInvariant(), Nombre = "Duplicado", Precio = 1 }), "Codigo de producto inactivo sigue reservado sin distinguir mayusculas.");
    Verificar(productosSql.ContarStockBajo() == 6, "Las alertas excluyen un producto desactivado.");
}
finally
{
    tiendaCatalogo.EditarProducto(productoOriginal.Id, datosProductoOriginal);
    tiendaCatalogo.CambiarEstadoProducto(productoOriginal.Id, new() { Activo = productoOriginal.Activo });
}
Producto Ficha(int id) => JsonSerializer.SerializeToElement(tiendaCatalogo.ConsultarProducto(id)).GetProperty("Producto").Deserialize<Producto>()!;
int Disponibles(int id) => JsonSerializer.SerializeToElement(tiendaCatalogo.ConsultarProducto(id)).GetProperty("Alternativas").GetArrayLength();
try
{
    var originalNuevo = tiendaCatalogo.RegistrarProducto(new() { Codigo = " qa_sql_01 ", Nombre = " Aceite 100% [SQL] ~ lote_ ", Precio = 1.235m, StockInicial = 0, StockMinimo = 5 });
    var sustituto = tiendaCatalogo.RegistrarProducto(new() { Codigo = "QA_SQL_02", Nombre = "Sustituto SQL con stock", Precio = 2, StockInicial = 10, StockMinimo = 0 });
    var agotado = tiendaCatalogo.RegistrarProducto(new() { Codigo = "QA_SQL_03", Nombre = "Sustituto SQL agotado", Precio = 2, StockInicial = 0, StockMinimo = 0 });
    Verificar(originalNuevo.Codigo == "QA_SQL_01" && originalNuevo.Nombre == "Aceite 100% [SQL] ~ lote_" && originalNuevo.Precio == 1.24m, "Alta normaliza texto y precio, y guarda el stock inicial.");
    Verificar(tiendaCatalogo.BuscarProductos("100% [SQL] ~ lote_").Single().Id == originalNuevo.Id && !tiendaCatalogo.BuscarProductos(new string('a', 101)).Any(), "Busqueda literal segura de comodines y textos largos.");
    var segundaTienda = new TiendaService(null, null, new ProductosSqlService(ConexionBD.Instancia));
    Verificar(segundaTienda.BuscarProductos("QA_SQL_02").Single().Stock == 10, "Un servicio nuevo recupera productos y existencias desde SQL.");
    Rechazar(() => tiendaCatalogo.EditarProducto(originalNuevo.Id, datosProductoOriginal), "No editar a un codigo existente.");
    Rechazar(() => tiendaCatalogo.RegistrarProducto(new() { Codigo = "QA_ERROR", Nombre = " ", Precio = 1 }), "Nombre vacio rechazado.");
    Rechazar(() => tiendaCatalogo.RegistrarProducto(new() { Codigo = "QA_ERROR", Nombre = "Error", Precio = .001m }), "Precio que redondea a cero rechazado.");
    Rechazar(() => tiendaCatalogo.RegistrarProducto(new() { Codigo = "QA_ERROR", Nombre = "Error", Precio = 10000000000000000m }), "Importe fuera de DECIMAL(18,2) rechazado antes de enviar SQL.");
    Rechazar(() => tiendaCatalogo.RegistrarProducto(new() { Codigo = "QA_ERROR", Nombre = "Error", Precio = 1, StockInicial = -1 }), "Stock inicial negativo rechazado.");
    Rechazar(() => tiendaCatalogo.EditarProducto(int.MaxValue, datosProductoOriginal), "Editar producto inexistente devuelve error controlado.");
    Rechazar(() => tiendaCatalogo.CambiarEstadoProducto(originalNuevo.Id, new()), "Estado de producto obligatorio.");
    Rechazar(() => tiendaCatalogo.ActualizarStockMinimo(new() { ProductoId = originalNuevo.Id, StockMinimo = -1 }), "Stock minimo negativo rechazado.");
    Verificar(tiendaCatalogo.ActualizarStockMinimo(new() { ProductoId = sustituto.Id, StockMinimo = 10 }).StockBajo, "El minimo configurable actualiza la alerta real.");
    var relaciones = tiendaCatalogo.RegistrarAlternativas(new() { ProductoId = originalNuevo.Id, AlternativosIds = new() { sustituto.Id, sustituto.Id, agotado.Id } });
    Verificar(relaciones.AlternativosIds.Count == 2 && Disponibles(originalNuevo.Id) == 1, "Alternativas sin duplicados; solo sugerir productos con stock.");
    var antes = Ficha(originalNuevo.Id).AlternativosIds;
    Rechazar(() => tiendaCatalogo.RegistrarAlternativas(new() { ProductoId = originalNuevo.Id, AlternativosIds = new() { sustituto.Id, int.MaxValue } }), "Alternativa inexistente no reemplaza las relaciones previas.");
    Rechazar(() => tiendaCatalogo.RegistrarAlternativas(new() { ProductoId = originalNuevo.Id, AlternativosIds = new() { originalNuevo.Id } }), "Producto no puede ser su propia alternativa.");
    Verificar(Ficha(originalNuevo.Id).AlternativosIds.SequenceEqual(antes), "Validacion fallida conserva todas las alternativas anteriores.");
    tiendaCatalogo.CambiarEstadoProducto(sustituto.Id, new() { Activo = false });
    Verificar(Disponibles(originalNuevo.Id) == 0 && Ficha(originalNuevo.Id).AlternativosIds.Count == 2, "Desactivar sustituto lo oculta sin borrar la relacion.");
    Rechazar(() => tiendaCatalogo.RegistrarAlternativas(new() { ProductoId = originalNuevo.Id, AlternativosIds = new() { sustituto.Id } }), "No registrar sustitutos inactivos.");
    tiendaCatalogo.CambiarEstadoProducto(sustituto.Id, new() { Activo = true });
    tiendaCatalogo.CambiarEstadoProducto(originalNuevo.Id, new() { Activo = false });
    Verificar(Disponibles(originalNuevo.Id) == 0, "Producto original inactivo no ofrece alternativas.");
    tiendaCatalogo.CambiarEstadoProducto(originalNuevo.Id, new() { Activo = true });
    await Task.WhenAll(new[] { sustituto.Id, agotado.Id }.Select(id => Task.Run(() => productosSql.RegistrarAlternativas(new() { ProductoId = originalNuevo.Id, AlternativosIds = new() { id } }))));
    Verificar(Ficha(originalNuevo.Id).AlternativosIds.Count == 1, "Reemplazos simultaneos de alternativas quedan completos y no se mezclan.");
    tiendaCatalogo.RegistrarAlternativas(new() { ProductoId = originalNuevo.Id, AlternativosIds = new() });
    Verificar(Ficha(originalNuevo.Id).AlternativosIds.Count == 0, "Se pueden quitar todas las alternativas.");
    var altas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
    {
        try { productosSql.RegistrarProducto(new() { Codigo = "qa_sql_dupe", Nombre = "Concurrente", Precio = 1 }); return true; }
        catch (ArgumentException) { return false; }
    })));
    Verificar(altas.Count(x => x) == 1, "Codigo unico ante altas simultaneas.");
    string stockAntes = JsonSerializer.Serialize(tiendaCatalogo.BuscarProductos(null, true).Select(p => new { p.Id, p.Stock }));
    Rechazar(() => tiendaCatalogo.RegistrarVenta(new() { FormaPago = "Efectivo", MontoRecibido = 100, Items = new() { new() { ProductoId = sustituto.Id, Cantidad = 1 } } }), "No vender stock SQL hasta migrar el registro de ventas.");
    Rechazar(() => tiendaCatalogo.RegistrarCompra(new() { ProveedorId = 1, Items = new() { new() { ProductoId = sustituto.Id, Cantidad = 1, CostoUnitario = 1 } } }), "No reponer stock SQL hasta migrar el registro de compras.");
    Verificar(stockAntes == JsonSerializer.Serialize(tiendaCatalogo.BuscarProductos(null, true).Select(p => new { p.Id, p.Stock })) && Escalar("SELECT COUNT(*) FROM Ventas") == 931 && Escalar("SELECT COUNT(*) FROM Compras") == 60, "Operaciones pendientes conservan stock y movimientos SQL.");
}
finally
{
    using var c = ConexionBD.Instancia.CrearConexion(); c.Open();
    using var cmd = new SqlCommand("DELETE FROM ProductosAlternativos WHERE IdProducto IN (SELECT IdProducto FROM Productos WHERE Codigo IN ('QA_SQL_01','QA_SQL_02','QA_SQL_03','QA_SQL_DUPE')) OR IdAlternativo IN (SELECT IdProducto FROM Productos WHERE Codigo IN ('QA_SQL_01','QA_SQL_02','QA_SQL_03','QA_SQL_DUPE')); DELETE FROM Productos WHERE Codigo IN ('QA_SQL_01','QA_SQL_02','QA_SQL_03','QA_SQL_DUPE')", c); cmd.ExecuteNonQuery();
}
Verificar(tiendaCatalogo.BuscarProductos(null, true).Count == 120 && Escalar("SELECT COUNT(*) FROM ProductosAlternativos") == 56 && productosSql.ContarStockBajo() == 7, "Pruebas de productos dejan intactos el catalogo, las alternativas y las alertas originales.");

// Compras: cabecera, detalles y stock se confirman o revierten juntos.
var comprasSql = new ComprasSqlService(ConexionBD.Instancia);
var tiendaCompras = new TiendaService(clientesSql, proveedoresSql, productosSql, comprasSql);
Verificar(tiendaCompras.ConsultarCompras().Count == 60 && tiendaCompras.ConsultarCompras().All(c => c.Detalles.Count > 0 && c.UsuarioId > 0), "Historial SQL de 60 compras con sus detalles y usuarios.");
Verificar(tiendaCompras.ConsultarCompra(1).Total == tiendaCompras.ConsultarCompra(1).Detalles.Sum(d => d.Subtotal), "La ficha de una compra original coincide con sus detalles.");
Rechazar(() => tiendaCompras.ConsultarCompra(int.MaxValue), "Consultar compra inexistente devuelve error controlado.");
var proveedorCompra = proveedoresSql.RegistrarProveedor(new() { Ruc = "20990000100", RazonSocial = "Proveedor de compras SQL" });
var productoA = productosSql.RegistrarProducto(new() { Codigo = "QA_COMPRA_A", Nombre = "Producto compra A", Precio = 9, StockInicial = 0, StockMinimo = 2 });
var productoB = productosSql.RegistrarProducto(new() { Codigo = "QA_COMPRA_B", Nombre = "Producto compra B", Precio = 8, StockInicial = 10, StockMinimo = 2 });
var productoLimite = productosSql.RegistrarProducto(new() { Codigo = "QA_COMPRA_MAX", Nombre = "Producto al limite", Precio = 1, StockInicial = int.MaxValue });
CompraSolicitud SolicitudCompraSql() => new() { ProveedorId = proveedorCompra.Id, Items = new()
    { new() { ProductoId = productoB.Id, Cantidad = 3, CostoUnitario = 1.235m }, new() { ProductoId = productoA.Id, Cantidad = 2, CostoUnitario = 2.50m } } };
void SqlCompraPrueba(string sql)
{
    using var c = ConexionBD.Instancia.CrearConexion(); c.Open();
    using var cmd = new SqlCommand(sql, c); cmd.ExecuteNonQuery();
}
try
{
    Rechazar(() => tiendaCompras.RegistrarCompra(SolicitudCompraSql()), "SQL exige identificar al usuario que registra la compra.");
    Rechazar(() => tiendaCompras.RegistrarCompra(SolicitudCompraSql(), 2), "El servicio SQL tambien rechaza registrar como cajero.");
    Rechazar(() => tiendaCompras.RegistrarCompra(SolicitudCompraSql(), int.MaxValue), "Usuario inexistente no puede registrar compras.");
    DateTimeOffset antesCompra = DateTimeOffset.UtcNow;
    Compra compraSql = tiendaCompras.RegistrarCompra(SolicitudCompraSql(), 1);
    Verificar(compraSql.Total == 8.72m && compraSql.Detalles.Count == 2 && compraSql.Detalles.Sum(d => d.Subtotal) == compraSql.Total, "Compra multiple guarda costos redondeados y totales exactos.");
    Verificar(compraSql.UsuarioId == 1 && !string.IsNullOrWhiteSpace(compraSql.UsuarioNombre) && compraSql.Fecha >= antesCompra.AddSeconds(-2) && compraSql.Fecha <= DateTimeOffset.UtcNow.AddSeconds(2), "SQL guarda usuario real y fecha de la operacion.");
    Verificar(Ficha(productoA.Id).Stock == 2 && Ficha(productoB.Id).Stock == 13 && Ficha(productoA.Id).Precio == 9, "La compra repone todos los productos sin cambiar sus precios de venta.");
    var compraPersistida = new ComprasSqlService(ConexionBD.Instancia).ConsultarCompra(compraSql.Id);
    Verificar(JsonSerializer.Serialize(compraPersistida) == JsonSerializer.Serialize(compraSql), "Una instancia nueva recupera la compra y sus detalles desde SQL.");
    proveedoresSql.EditarProveedor(proveedorCompra.Id, new() { Ruc = proveedorCompra.Ruc, RazonSocial = "Proveedor renombrado" });
    productosSql.EditarProducto(productoA.Id, new() { Codigo = productoA.Codigo, Nombre = "Articulo renombrado", Precio = 11, StockMinimo = 2 });
    Verificar(tiendaCompras.ConsultarCompra(compraSql.Id).ProveedorNombre == "Proveedor de compras SQL" && tiendaCompras.ConsultarCompra(compraSql.Id).Detalles.Single(d => d.ProductoId == productoA.Id).ProductoNombre == "Producto compra A", "Las compras conservan los nombres historicos de proveedor y productos.");
    int comprasAntes = tiendaCompras.ConsultarCompras().Count;
    string stockAntes = JsonSerializer.Serialize(tiendaCatalogo.BuscarProductos(null, true).Select(p => new { p.Id, p.Stock }));
    foreach (var invalida in new[]
    {
        new CompraSolicitud { ProveedorId = proveedorCompra.Id },
        new CompraSolicitud { ProveedorId = proveedorCompra.Id, Items = new() { new() { ProductoId = productoA.Id, Cantidad = 1, CostoUnitario = .001m } } },
        new CompraSolicitud { ProveedorId = proveedorCompra.Id, Items = new() { new() { ProductoId = productoA.Id, Cantidad = 1, CostoUnitario = decimal.MaxValue } } },
        new CompraSolicitud { ProveedorId = proveedorCompra.Id, Items = new() { new() { ProductoId = productoA.Id, Cantidad = 1, CostoUnitario = 1 }, new() { ProductoId = productoA.Id, Cantidad = 1, CostoUnitario = 1 } } },
        new CompraSolicitud { ProveedorId = proveedorCompra.Id, Items = new() { new() { ProductoId = productoA.Id, Cantidad = 1, CostoUnitario = 1 }, new() { ProductoId = int.MaxValue, Cantidad = 1, CostoUnitario = 1 } } },
        new CompraSolicitud { ProveedorId = proveedorCompra.Id, Items = new() { new() { ProductoId = productoLimite.Id, Cantidad = 1, CostoUnitario = 1 } } }
    }) Rechazar(() => tiendaCompras.RegistrarCompra(invalida, 1), "Compra invalida rechazada sin guardar parcialmente.");
    proveedoresSql.CambiarEstadoProveedor(proveedorCompra.Id, new() { Activo = false });
    Rechazar(() => tiendaCompras.RegistrarCompra(SolicitudCompraSql(), 1), "Proveedor inactivo no admite nuevas compras.");
    Verificar(tiendaCompras.ConsultarCompra(compraSql.Id).Total == 8.72m, "Desactivar proveedor no oculta sus compras historicas.");
    proveedoresSql.CambiarEstadoProveedor(proveedorCompra.Id, new() { Activo = true });
    productosSql.CambiarEstadoProducto(productoB.Id, new() { Activo = false });
    Rechazar(() => tiendaCompras.RegistrarCompra(SolicitudCompraSql(), 1), "Producto inactivo no admite nuevas compras.");
    productosSql.CambiarEstadoProducto(productoB.Id, new() { Activo = true });
    Verificar(comprasAntes == tiendaCompras.ConsultarCompras().Count && stockAntes == JsonSerializer.Serialize(tiendaCatalogo.BuscarProductos(null, true).Select(p => new { p.Id, p.Stock })), "Todas las validaciones conservan compras y stock previos.");
    // Forzar un fallo al insertar el segundo detalle, despues de reponer el primero.
    // El trigger existe solo en la base aislada de pruebas y se elimina en finally.
    SqlCompraPrueba($"CREATE TRIGGER qa_Prueba_Compra_Atomicidad ON DetalleCompras AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted WHERE IdProducto = {productoB.Id}) BEGIN ;THROW 51090, N'Fallo controlado de prueba.', 1; END; END;");
    bool falloForzado = false;
    try { tiendaCompras.RegistrarCompra(SolicitudCompraSql(), 1); }
    catch (SqlException error) when (error.Number == 51090) { falloForzado = true; }
    finally { SqlCompraPrueba("DROP TRIGGER IF EXISTS qa_Prueba_Compra_Atomicidad"); }
    Verificar(falloForzado && comprasAntes == tiendaCompras.ConsultarCompras().Count && stockAntes == JsonSerializer.Serialize(tiendaCatalogo.BuscarProductos(null, true).Select(p => new { p.Id, p.Stock })), "Un fallo durante la escritura revierte cabecera, detalles e incrementos previos de stock.");
    var concurrentes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => new ComprasSqlService(ConexionBD.Instancia).RegistrarCompra(SolicitudCompraSql(), 1))));
    Verificar(concurrentes.Select(c => c.Id).Distinct().Count() == 2 && Ficha(productoA.Id).Stock == 6 && Ficha(productoB.Id).Stock == 19, "Compras simultaneas del mismo stock no pierden incrementos ni mezclan detalles.");
    Verificar(tiendaCompras.ConsultarCompras().Count == 63 && Escalar("SELECT COUNT(*) FROM Compras c WHERE c.Total <> (SELECT SUM(d.Subtotal) FROM DetalleCompras d WHERE d.IdCompra = c.IdCompra)") == 0, "Todas las compras confirmadas coinciden con sus detalles SQL.");
}
finally
{
    SqlCompraPrueba("DROP TRIGGER IF EXISTS qa_Prueba_Compra_Atomicidad");
    SqlCompraPrueba($"DELETE FROM DetalleCompras WHERE IdCompra IN (SELECT IdCompra FROM Compras WHERE IdProveedor = {proveedorCompra.Id}); DELETE FROM Compras WHERE IdProveedor = {proveedorCompra.Id}; DELETE FROM Productos WHERE IdProducto IN ({productoA.Id}, {productoB.Id}, {productoLimite.Id}); DELETE FROM Proveedores WHERE IdProveedor = {proveedorCompra.Id}");
}
Verificar(tiendaCompras.ConsultarCompras().Count == 60 && tiendaCatalogo.BuscarProductos(null, true).Count == 120 && productosSql.ContarStockBajo() == 7, "Pruebas de compras dejan intacta la carga original.");

// Ventas: precios del catalogo, inventario y fiados dentro de una transaccion.
var ventasSql = new VentasSqlService(ConexionBD.Instancia);
var tiendaVentas = new TiendaService(clientesSql, proveedoresSql, productosSql, comprasSql, ventasSql);
Verificar(tiendaVentas.ConsultarVentas(null, null).Count == 931 && tiendaVentas.ConsultarVentas(null, null).All(v => v.UsuarioId > 0 && v.Detalles.Count > 0), "Historial SQL original de 931 ventas con detalles y usuario.");
Verificar(tiendaVentas.ConsultarVentas(new(2026, 9, 17), new(2026, 10, 16)).Count == 931 && tiendaVentas.ConsultarVentas(new(2000, 1, 1), new(2000, 1, 1)).Count == 0, "Filtrado inclusivo de ventas originales por fecha de Lima.");
Rechazar(() => tiendaVentas.ConsultarVentas(new(2026, 10, 17), new(2026, 10, 16)), "Periodo de ventas invertido rechazado.");
Rechazar(() => tiendaVentas.ConsultarVenta(int.MaxValue), "Venta inexistente devuelve error controlado.");
var clienteVenta = clientesSql.RegistrarCliente(new() { Documento = "99000100", Nombre = "Cliente venta SQL" });
var articuloA = productosSql.RegistrarProducto(new() { Codigo = "QA_VENTA_A", Nombre = "Articulo venta A", Precio = 2.50m, StockInicial = 20 });
var articuloB = productosSql.RegistrarProducto(new() { Codigo = "QA_VENTA_B", Nombre = "Articulo venta B", Precio = 1.25m, StockInicial = 20 });
var articuloMax = productosSql.RegistrarProducto(new() { Codigo = "QA_VENTA_MAX", Nombre = "Articulo importe limite", Precio = VentasValidacion.ImporteMaximo, StockInicial = 2 });
VentaSolicitud SolicitudVentaSql(string forma = "Fiado") => new() { FormaPago = forma, ClienteId = clienteVenta.Id, MontoRecibido = forma == "Efectivo" ? 10 : 0,
    ReferenciaPago = "  OPERACION-SQL  ", Items = new() { new() { ProductoId = articuloB.Id, Cantidad = 2 }, new() { ProductoId = articuloA.Id, Cantidad = 1 }, new() { ProductoId = articuloA.Id, Cantidad = 1 } } };
decimal DeudaVenta() => clientesSql.ConsultarClientes(true).Single(c => c.Id == clienteVenta.Id).Deuda;
try
{
    Rechazar(() => tiendaVentas.RegistrarVenta(SolicitudVentaSql()), "Venta SQL requiere usuario autenticado.");
    Rechazar(() => tiendaVentas.RegistrarVenta(SolicitudVentaSql(), int.MaxValue), "Usuario inexistente no registra ventas.");
    Venta efectivo = tiendaVentas.RegistrarVenta(SolicitudVentaSql("Efectivo"), 1);
    Verificar(efectivo.Total == 7.50m && efectivo.MontoRecibido == 10 && efectivo.Vuelto == 2.50m && efectivo.Detalles.Count == 2 && efectivo.Detalles.All(d => d.Cantidad == 2), "Efectivo agrupa productos repetidos, usa precio SQL y calcula vuelto exacto.");
    Verificar(efectivo.ClienteId == null && efectivo.ReferenciaPago == null && efectivo.UsuarioId == 1 && DeudaVenta() == 0, "Efectivo ignora el cliente y referencia que no corresponden, sin generar deuda.");
    Venta digital = tiendaVentas.RegistrarVenta(SolicitudVentaSql("Digital"), 2);
    Verificar(digital.Total == 7.50m && digital.MontoRecibido == 7.50m && digital.Vuelto == 0 && digital.ReferenciaPago == "OPERACION-SQL" && digital.UsuarioId == 2, "Cajero registra pago digital con referencia normalizada y sin vuelto.");
    Venta fiado = tiendaVentas.RegistrarVenta(SolicitudVentaSql(), 2);
    Verificar(fiado.ClienteId == clienteVenta.Id && fiado.ClienteNombre == clienteVenta.Nombre && fiado.MontoRecibido == 0 && fiado.Vuelto == 0 && DeudaVenta() == 7.50m, "Fiado guarda cliente y aumenta su deuda mediante la vista SQL.");
    Verificar(Ficha(articuloA.Id).Stock == 14 && Ficha(articuloB.Id).Stock == 14 && tiendaVentas.ConsultarPagos(clienteVenta.Id).Count == 0, "Tres modalidades descuentan stock una sola vez y no crean abonos.");
    Verificar(JsonSerializer.Serialize(new VentasSqlService(ConexionBD.Instancia).ConsultarVenta(fiado.Id)) == JsonSerializer.Serialize(fiado), "Nueva instancia recupera fecha, usuario y detalles de la venta persistida.");
    productosSql.EditarProducto(articuloA.Id, new() { Codigo = articuloA.Codigo, Nombre = "Articulo renombrado", Precio = 3.25m });
    clientesSql.EditarCliente(clienteVenta.Id, new() { Documento = clienteVenta.Documento, Nombre = "Cliente renombrado" });
    var historica = tiendaVentas.ConsultarVenta(fiado.Id);
    Verificar(historica.ClienteNombre == "Cliente venta SQL" && historica.Detalles.Single(d => d.ProductoId == articuloA.Id).Nombre == "Articulo venta A" && historica.Detalles.Single(d => d.ProductoId == articuloA.Id).PrecioUnitario == 2.50m, "Editar catalogos conserva nombres y precios historicos de ventas.");
    int antesVentas = tiendaVentas.ConsultarVentas(null, null).Count;
    foreach (var invalida in new[]
    {
        new VentaSolicitud { FormaPago = "Fiado", ClienteId = clienteVenta.Id },
        new VentaSolicitud { FormaPago = "Tarjeta", Items = SolicitudVentaSql().Items },
        new VentaSolicitud { FormaPago = "Efectivo", MontoRecibido = 1, Items = SolicitudVentaSql().Items },
        new VentaSolicitud { FormaPago = "Efectivo", MontoRecibido = -1, Items = SolicitudVentaSql().Items },
        new VentaSolicitud { FormaPago = "Efectivo", MontoRecibido = decimal.MaxValue, Items = SolicitudVentaSql().Items },
        new VentaSolicitud { FormaPago = "Digital", Items = SolicitudVentaSql().Items },
        new VentaSolicitud { FormaPago = "Digital", ReferenciaPago = new string('x',101), Items = SolicitudVentaSql().Items },
        new VentaSolicitud { FormaPago = "Fiado", Items = SolicitudVentaSql().Items },
        new VentaSolicitud { FormaPago = "Fiado", ClienteId = int.MaxValue, Items = SolicitudVentaSql().Items },
        new VentaSolicitud { FormaPago = "Fiado", ClienteId = clienteVenta.Id, Items = new() { new() { ProductoId = articuloA.Id, Cantidad = 1 }, new() { ProductoId = int.MaxValue, Cantidad = 1 } } },
        new VentaSolicitud { FormaPago = "Fiado", ClienteId = clienteVenta.Id, Items = new() { new() { ProductoId = articuloA.Id, Cantidad = 0 } } },
        new VentaSolicitud { FormaPago = "Fiado", ClienteId = clienteVenta.Id, Items = new() { new() { ProductoId = articuloA.Id, Cantidad = int.MaxValue }, new() { ProductoId = articuloA.Id, Cantidad = 1 } } },
        new VentaSolicitud { FormaPago = "Fiado", ClienteId = clienteVenta.Id, Items = new() { new() { ProductoId = articuloMax.Id, Cantidad = 2 } } },
        new VentaSolicitud { FormaPago = "Fiado", ClienteId = clienteVenta.Id, Items = new() { new() { ProductoId = articuloA.Id, Cantidad = 15 } } }
    }) Rechazar(() => tiendaVentas.RegistrarVenta(invalida, 1), "Venta invalida rechazada sin persistencia parcial.");
    productosSql.CambiarEstadoProducto(articuloB.Id, new() { Activo = false });
    Rechazar(() => tiendaVentas.RegistrarVenta(SolicitudVentaSql(), 1), "Producto inactivo no se puede vender.");
    productosSql.CambiarEstadoProducto(articuloB.Id, new() { Activo = true });
    clientesSql.CambiarEstadoCliente(clienteVenta.Id, new() { Activo = false });
    Rechazar(() => tiendaVentas.RegistrarVenta(SolicitudVentaSql(), 1), "Cliente inactivo no admite nuevos fiados.");
    Verificar(tiendaVentas.ConsultarVenta(fiado.Id).ClienteNombre == "Cliente venta SQL", "Cliente inactivo conserva su historial de ventas.");
    clientesSql.CambiarEstadoCliente(clienteVenta.Id, new() { Activo = true });
    Verificar(tiendaVentas.ConsultarVentas(null, null).Count == antesVentas && Ficha(articuloA.Id).Stock == 14 && Ficha(articuloB.Id).Stock == 14 && DeudaVenta() == 7.50m, "Rechazos conservan ventas, stock y deuda anteriores.");
    SqlCompraPrueba($"CREATE TRIGGER qa_Prueba_Venta_Atomicidad ON DetalleVentas AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted WHERE IdProducto = {articuloB.Id}) BEGIN ;THROW 51091, N'Fallo controlado de prueba.', 1; END; END;");
    bool falloVenta = false;
    try { tiendaVentas.RegistrarVenta(SolicitudVentaSql(), 2); }
    catch (SqlException error) when (error.Number == 51091) { falloVenta = true; }
    finally { SqlCompraPrueba("DROP TRIGGER IF EXISTS qa_Prueba_Venta_Atomicidad"); }
    Verificar(falloVenta && tiendaVentas.ConsultarVentas(null, null).Count == antesVentas && Ficha(articuloA.Id).Stock == 14 && Ficha(articuloB.Id).Stock == 14 && DeudaVenta() == 7.50m, "Fallo al guardar segundo detalle revierte venta, primer descuento y deuda.");
    var simultaneas = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => new VentasSqlService(ConexionBD.Instancia).RegistrarVenta(SolicitudVentaSql(), 2))));
    Verificar(simultaneas.Select(v => v.Id).Distinct().Count() == 2 && Ficha(articuloA.Id).Stock == 10 && Ficha(articuloB.Id).Stock == 10 && DeudaVenta() == 25.50m, "Fiados concurrentes conservan ambas ventas y calculan stock y deuda exactos.");
    VentaSolicitud ultimaUnidad = new() { FormaPago = "Digital", ReferenciaPago = "ULTIMA", Items = new() { new() { ProductoId = articuloB.Id, Cantidad = 10 } } };
    var escasez = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
    {
        try { ventasSql.RegistrarVenta(ultimaUnidad, 2); return true; } catch (ArgumentException) { return false; }
    })));
    Verificar(escasez.Count(x => x) == 1 && Ficha(articuloB.Id).Stock == 0, "Ventas concurrentes no venden dos veces las ultimas existencias.");
    // Un ingreso y una venta del mismo producto tampoco pierden actualizaciones.
    var ingreso = new CompraSolicitud { ProveedorId = 1, Items = new() { new() { ProductoId = articuloA.Id, Cantidad = 2, CostoUnitario = 1 } } };
    var salida = new VentaSolicitud { FormaPago = "Digital", ReferenciaPago = "CRUCE", Items = new() { new() { ProductoId = articuloA.Id, Cantidad = 1 } } };
    await Task.WhenAll(Task.Run(() => comprasSql.RegistrarCompra(ingreso, 1)), Task.Run(() => ventasSql.RegistrarVenta(salida, 2)));
    Verificar(Ficha(articuloA.Id).Stock == 11, "Compra y venta simultaneas del mismo producto conservan todos los movimientos.");
    DateOnly diaLima = DateOnly.FromDateTime(fiado.Fecha.ToOffset(TimeSpan.FromHours(-5)).Date);
    var reporteVentas = tiendaVentas.Reporte(diaLima, diaLima);
    Verificar(reporteVentas.CantidadVentas == 7 && reporteVentas.TotalVendido == 56.25m && reporteVentas.TotalFiado == 25.50m && reporteVentas.CobradoEnVentas == 30.75m, "Reporte consulta ventas SQL del dia, separa cobrado y fiado y suma exactamente sus detalles.");
    Verificar(tiendaVentas.Reporte(new(2026,9,17),new(2026,10,16)).Pagos.Count == 124, "Reporte recupera tambien el historial existente de abonos SQL.");
    Verificar(Escalar("SELECT COUNT(*) FROM Ventas v WHERE v.Total <> (SELECT SUM(d.Subtotal) FROM DetalleVentas d WHERE d.IdVenta = v.IdVenta)") == 0, "Toda venta confirmada coincide con sus detalles SQL.");
    // 04:59 UTC pertenece aun al dia anterior en Lima; 05:00 inicia el siguiente.
    SqlCompraPrueba($"UPDATE Ventas SET Fecha = '2001-01-02T04:59:59+00:00' WHERE IdVenta = {efectivo.Id}; UPDATE Ventas SET Fecha = '2001-01-02T05:00:00+00:00' WHERE IdVenta = {digital.Id}");
    Verificar(ventasSql.ConsultarVentas(new(2001,1,1),new(2001,1,1)).Single().Id == efectivo.Id && ventasSql.ConsultarVentas(new(2001,1,2),new(2001,1,2)).Single().Id == digital.Id, "Fechas de Lima incluyen correctamente ambos lados de la medianoche.");
}
finally
{
    SqlCompraPrueba("DROP TRIGGER IF EXISTS qa_Prueba_Venta_Atomicidad");
    SqlCompraPrueba($"DECLARE @ventas TABLE (Id int); INSERT INTO @ventas SELECT DISTINCT IdVenta FROM DetalleVentas WHERE IdProducto IN ({articuloA.Id},{articuloB.Id},{articuloMax.Id}); DELETE FROM DetalleVentas WHERE IdVenta IN (SELECT Id FROM @ventas); DELETE FROM Ventas WHERE IdVenta IN (SELECT Id FROM @ventas); DECLARE @compras TABLE (Id int); INSERT INTO @compras SELECT DISTINCT IdCompra FROM DetalleCompras WHERE IdProducto={articuloA.Id}; DELETE FROM DetalleCompras WHERE IdCompra IN (SELECT Id FROM @compras); DELETE FROM Compras WHERE IdCompra IN (SELECT Id FROM @compras); DELETE FROM Productos WHERE IdProducto IN ({articuloA.Id},{articuloB.Id},{articuloMax.Id}); DELETE FROM Clientes WHERE IdCliente={clienteVenta.Id}");
}
Verificar(ventasSql.ConsultarVentas(null,null).Count == 931 && tiendaCompras.ConsultarCompras().Count == 60 && tiendaCatalogo.BuscarProductos(null,true).Count == 120, "Pruebas de ventas dejan intactos los movimientos y catalogos originales.");

// Abonos: la fila del cliente coordina pagos y ventas al fiado concurrentes.
var pagosSql = new PagosSqlService(ConexionBD.Instancia);
var tiendaPagos = new TiendaService(clientesSql, proveedoresSql, productosSql, comprasSql, ventasSql, pagosSql);
Verificar(tiendaPagos.ConsultarPagos(null).Count == 124 && tiendaPagos.ConsultarPagos(null).All(p => p.UsuarioId > 0 && !string.IsNullOrWhiteSpace(p.UsuarioNombre)), "Historial original de 124 abonos con su usuario SQL.");
var clienteAbono = clientesSql.RegistrarCliente(new() { Documento = "99000200", Nombre = "Cliente abonos SQL" });
var clienteOtroAbono = clientesSql.RegistrarCliente(new() { Documento = "99000201", Nombre = "Otro cliente abonos SQL" });
var articuloAbono = productosSql.RegistrarProducto(new() { Codigo = "QA_ABONO", Nombre = "Articulo fiado para abonos", Precio = 10, StockInicial = 20 });
var usuarioAbono = usuarios.RegistrarUsuario(new() { Nombre = "Operador abonos QA", Login = "qa_abonos", Rol = Roles.Cajero, Password = clavePrueba });
Venta NuevoFiadoAbono(int cantidad = 1) => ventasSql.RegistrarVenta(new() { FormaPago = "Fiado", ClienteId = clienteAbono.Id, Items = new() { new() { ProductoId = articuloAbono.Id, Cantidad = cantidad } } }, 2);
PagoSolicitud SolicitudAbono(decimal monto = 10, string metodo = "Efectivo") => new() { ClienteId = clienteAbono.Id, Monto = monto, Metodo = metodo };
decimal DeudaAbono() => clientesSql.ConsultarClientes(true).Single(c => c.Id == clienteAbono.Id).Deuda;
try
{
    NuevoFiadoAbono(3);
    Rechazar(() => tiendaPagos.RegistrarPago(SolicitudAbono()), "Abono SQL requiere usuario autenticado.");
    Rechazar(() => tiendaPagos.RegistrarPago(SolicitudAbono(), int.MaxValue), "Usuario inexistente no registra abonos.");
    usuarios.EditarUsuario(usuarioAbono.Id, new() { Nombre = usuarioAbono.Nombre, Login = usuarioAbono.Login, Rol = Roles.Cajero, Activo = false });
    Rechazar(() => tiendaPagos.RegistrarPago(SolicitudAbono(), usuarioAbono.Id), "Usuario inactivo no registra abonos aunque invoque el servicio directamente.");
    var efectivoAbono = tiendaPagos.RegistrarPago(SolicitudAbono(), 1);
    Verificar(efectivoAbono.Monto == 10 && efectivoAbono.SaldoRestante == 20 && DeudaAbono() == 20 && efectivoAbono.UsuarioId == 1 && efectivoAbono.Metodo == "Efectivo", "Abono efectivo: deuda de 30 menos pago de 10 deja 20 y guarda administrador.");
    var digitalAbono = tiendaPagos.RegistrarPago(SolicitudAbono(1.235m, "Digital"), 2);
    Verificar(digitalAbono.Monto == 1.24m && digitalAbono.SaldoRestante == 18.76m && DeudaAbono() == 18.76m && digitalAbono.UsuarioId == 2, "Cajero registra abono digital con redondeo monetario exacto.");
    Verificar(digitalAbono.Fecha >= DateTimeOffset.UtcNow.AddMinutes(-1) && digitalAbono.Fecha <= DateTimeOffset.UtcNow.AddSeconds(2), "Fecha del abono asignada por SQL al registrar.");
    Verificar(JsonSerializer.Serialize(new PagosSqlService(ConexionBD.Instancia).ConsultarPagos(clienteAbono.Id).First()) == JsonSerializer.Serialize(digitalAbono), "Nueva instancia recupera abono, usuario y saldo restante persistidos.");
    clientesSql.EditarCliente(clienteAbono.Id, new() { Documento = clienteAbono.Documento, Nombre = "Cliente abonos renombrado" });
    Verificar(tiendaPagos.ConsultarPagos(clienteAbono.Id).All(p => p.ClienteNombre == "Cliente abonos SQL"), "Editar el cliente conserva su nombre historico en los abonos.");
    foreach (var invalido in new[]
    {
        SolicitudAbono(0), SolicitudAbono(-1), SolicitudAbono(.001m), SolicitudAbono(18.77m),
        SolicitudAbono(decimal.MaxValue), SolicitudAbono(1, "Fiado"), SolicitudAbono(1, "Tarjeta"),
        new PagoSolicitud { ClienteId = 0, Monto = 1, Metodo = "Efectivo" },
        new PagoSolicitud { ClienteId = int.MaxValue, Monto = 1, Metodo = "Efectivo" }
    }) Rechazar(() => tiendaPagos.RegistrarPago(invalido, 1), "Abono invalido rechazado sin modificar la deuda.");
    Verificar(tiendaPagos.ConsultarPagos(clienteAbono.Id).Count == 2 && DeudaAbono() == 18.76m && Ficha(articuloAbono.Id).Stock == 17, "Pagos rechazados conservan historial, deuda e inventario.");
    SqlCompraPrueba($"CREATE TRIGGER qa_Prueba_Abono_Atomicidad ON Pagos AFTER INSERT AS BEGIN SET NOCOUNT ON; IF EXISTS (SELECT 1 FROM inserted WHERE IdCliente = {clienteAbono.Id}) BEGIN ;THROW 51092, N'Fallo controlado de prueba.', 1; END; END;");
    bool falloAbono = false;
    try { tiendaPagos.RegistrarPago(SolicitudAbono(5), 2); }
    catch (SqlException error) when (error.Number == 51092) { falloAbono = true; }
    finally { SqlCompraPrueba("DROP TRIGGER IF EXISTS qa_Prueba_Abono_Atomicidad"); }
    Verificar(falloAbono && tiendaPagos.ConsultarPagos(clienteAbono.Id).Count == 2 && DeudaAbono() == 18.76m, "Fallo despues de insertar revierte el abono y conserva la deuda anterior.");
    var cancelacion = tiendaPagos.RegistrarPago(SolicitudAbono(18.76m), 1);
    Verificar(cancelacion.SaldoRestante == 0 && DeudaAbono() == 0, "Cancelacion total deja deuda cero sin borrar el historial.");
    Rechazar(() => tiendaPagos.RegistrarPago(SolicitudAbono(.01m), 2), "Cliente sin deuda no admite nuevos abonos.");
    NuevoFiadoAbono(3);
    var cobrosSimultaneos = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
    {
        try { new PagosSqlService(ConexionBD.Instancia).RegistrarPago(SolicitudAbono(20), 2); return true; }
        catch (ArgumentException) { return false; }
    })));
    Verificar(cobrosSimultaneos.Count(x => x) == 1 && DeudaAbono() == 10, "Dos abonos de 20 sobre deuda de 30: solo uno se confirma y el saldo queda 10.");
    await Task.WhenAll(Task.Run(() => NuevoFiadoAbono()), Task.Run(() => pagosSql.RegistrarPago(SolicitudAbono(), 2)));
    Verificar(DeudaAbono() == 10 && Ficha(articuloAbono.Id).Stock == 13, "Fiado y abono simultaneos conservan ambas operaciones y el saldo correcto.");
    var ultimosCobros = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
    {
        try { pagosSql.RegistrarPago(SolicitudAbono(), 2); return true; }
        catch (ArgumentException) { return false; }
    })));
    Verificar(ultimosCobros.Count(x => x) == 1 && DeudaAbono() == 0, "Dos cobros de la ultima deuda no producen un saldo negativo.");
    NuevoFiadoAbono();
    var parciales = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => pagosSql.RegistrarPago(SolicitudAbono(5), 2))));
    Verificar(parciales.Select(p => p.SaldoRestante).OrderBy(x => x).SequenceEqual(new decimal[] { 0, 5 }) && DeudaAbono() == 0, "Dos abonos parciales validos se confirman con saldos historicos de 5 y 0.");
    NuevoFiadoAbono();
    clientesSql.CambiarEstadoCliente(clienteAbono.Id, new() { Activo = false });
    var pagoInactivo = tiendaPagos.RegistrarPago(SolicitudAbono(), 1);
    Verificar(pagoInactivo.SaldoRestante == 0 && DeudaAbono() == 0 && !clientesSql.ConsultarClientes().Any(c => c.Id == clienteAbono.Id), "Cliente inactivo puede cancelar su deuda sin reactivarse.");
    var propios = tiendaPagos.ConsultarPagos(clienteAbono.Id);
    Verificar(propios.Count == 9 && propios.Sum(p => p.Monto) == 90 && propios.All(p => p.ClienteId == clienteAbono.Id && p.SaldoRestante >= 0), "Historial por cliente guarda nueve pagos y 90 soles sin saldos negativos.");
    Verificar(propios.Single(p => p.Id == efectivoAbono.Id).SaldoRestante == 20 && Escalar($"SELECT SaldoInicial FROM Clientes WHERE IdCliente={clienteAbono.Id}") == 0, "Pagos posteriores no reescriben el saldo restante historico ni el saldo inicial.");
    DateOnly diaPago = DateOnly.FromDateTime(pagoInactivo.Fecha.ToOffset(TimeSpan.FromHours(-5)).Date);
    var reporteAbonos = tiendaPagos.Reporte(diaPago, diaPago);
    Verificar(reporteAbonos.Abonos == 90 && reporteAbonos.TotalFiado == 90 && reporteAbonos.CobradoEnVentas == 0 && reporteAbonos.Pagos.Count == 9, "Reporte separa abonos nuevos de fiados sin duplicar el dinero cobrado.");
    SqlCompraPrueba($"UPDATE Pagos SET Fecha='2002-01-02T04:59:59+00:00' WHERE IdPago={efectivoAbono.Id}; UPDATE Pagos SET Fecha='2002-01-02T05:00:00+00:00' WHERE IdPago={digitalAbono.Id}");
    Verificar(tiendaPagos.Reporte(new(2002,1,1),new(2002,1,1)).Abonos == 10 && tiendaPagos.Reporte(new(2002,1,2),new(2002,1,2)).Abonos == 1.24m, "Reporte filtra abonos por los limites de medianoche de Lima.");
    clientesSql.CambiarEstadoCliente(clienteAbono.Id, new() { Activo = true });
    NuevoFiadoAbono();
    ventasSql.RegistrarVenta(new() { FormaPago = "Fiado", ClienteId = clienteOtroAbono.Id, Items = new() { new() { ProductoId = articuloAbono.Id, Cantidad = 1 } } }, 2);
    var clientesDistintos = await Task.WhenAll(Task.Run(() => pagosSql.RegistrarPago(SolicitudAbono(), 1)), Task.Run(() => pagosSql.RegistrarPago(new() { ClienteId = clienteOtroAbono.Id, Monto = 10, Metodo = "Digital" }, 2)));
    Verificar(clientesDistintos.All(p => p.SaldoRestante == 0) && clientesDistintos.Select(p => p.ClienteId).Distinct().Count() == 2, "Abonos simultaneos de clientes distintos guardan sus propios saldos sin mezclar operaciones.");
}
finally
{
    SqlCompraPrueba("DROP TRIGGER IF EXISTS qa_Prueba_Abono_Atomicidad");
    SqlCompraPrueba($"DELETE FROM Pagos WHERE IdCliente IN ({clienteAbono.Id},{clienteOtroAbono.Id}); DELETE FROM DetalleVentas WHERE IdVenta IN (SELECT IdVenta FROM Ventas WHERE IdCliente IN ({clienteAbono.Id},{clienteOtroAbono.Id})); DELETE FROM Ventas WHERE IdCliente IN ({clienteAbono.Id},{clienteOtroAbono.Id}); DELETE FROM Productos WHERE IdProducto={articuloAbono.Id}; DELETE FROM Clientes WHERE IdCliente IN ({clienteAbono.Id},{clienteOtroAbono.Id}); DELETE FROM Usuarios WHERE IdUsuario={usuarioAbono.Id}");
}
Verificar(tiendaPagos.ConsultarPagos(null).Count == 124 && ventasSql.ConsultarVentas(null,null).Count == 931 && productosSql.BuscarProductos(null,true).Count == 120 && clientesSql.ConsultarClientes(true).Count == 60, "Pruebas de abonos conservan los datos originales de la base temporal.");
Console.WriteLine($"{comprobaciones} comprobaciones SQL correctas.");
