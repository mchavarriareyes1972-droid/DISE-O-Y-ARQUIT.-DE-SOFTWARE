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
Console.WriteLine($"{comprobaciones} comprobaciones SQL correctas.");
