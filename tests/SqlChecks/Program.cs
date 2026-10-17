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
Console.WriteLine($"{comprobaciones} comprobaciones SQL correctas.");
