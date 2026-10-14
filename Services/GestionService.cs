using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

// Parte del mismo servicio: comparte el almacenamiento y el bloqueo de las operaciones.
public partial class TiendaService
{
    private readonly List<Proveedor> proveedores = new()
    {
        new() { Id = 1, Ruc = "20123456789", RazonSocial = "Distribuidora de práctica", Contacto = "Ana", Telefono = "999222333" }
    };
    private static string Texto(string? valor, int maximo, string campo, bool obligatorio = false)
    {
        string texto = (valor ?? "").Trim();
        if ((obligatorio && texto.Length == 0) || texto.Length > maximo)
            throw new ArgumentException($"Revisa el campo {campo} (máximo {maximo} caracteres).");
        return texto;
    }
    private Cliente ValidarCliente(ClienteSolicitud solicitud, int? id = null)
    {
        string dni = Texto(solicitud.Documento, 8, "DNI", true);
        if (!Regex.IsMatch(dni, @"^[0-9]{8}$")) throw new ArgumentException("El DNI debe tener 8 dígitos.");
        if (clientes.Any(c => c.Id != id && c.Documento == dni)) throw new ArgumentException("Ya existe un cliente con ese DNI, incluso si está inactivo.");
        return new() { Documento = dni, Nombre = Texto(solicitud.Nombre, 100, "nombre", true),
            Telefono = Texto(solicitud.Telefono, 20, "teléfono"), Direccion = Texto(solicitud.Direccion, 150, "dirección") };
    }
    public Cliente RegistrarCliente(ClienteSolicitud solicitud)
    {
        lock (bloqueo)
        {
            Cliente cliente = ValidarCliente(solicitud);
            cliente.Id = clientes.Max(c => c.Id) + 1;
            clientes.Add(cliente);
            return Copia(cliente);
        }
    }
    public Cliente EditarCliente(int id, ClienteSolicitud solicitud)
    {
        lock (bloqueo)
        {
            Cliente cliente = clientes.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentException("El cliente no existe.");
            Cliente datos = ValidarCliente(solicitud, id);
            cliente.Documento = datos.Documento; cliente.Nombre = datos.Nombre;
            cliente.Telefono = datos.Telefono; cliente.Direccion = datos.Direccion;
            // La deuda solo cambia mediante ventas al fiado y abonos.
            return Copia(cliente);
        }
    }
    public Cliente CambiarEstadoCliente(int id, EstadoSolicitud solicitud)
    {
        lock (bloqueo)
        {
            Cliente cliente = clientes.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentException("El cliente no existe.");
            cliente.Activo = solicitud.Activo ?? throw new ArgumentException("Indica el estado del cliente.");
            // Puede pagar su deuda aunque esté inactivo; no puede recibir nuevos fiados.
            return Copia(cliente);
        }
    }
    public List<Proveedor> ConsultarProveedores(bool incluirInactivos = false)
    {
        lock (bloqueo) { return Copia(proveedores.Where(p => incluirInactivos || p.Activo).ToList()); }
    }
    private Proveedor ValidarProveedor(ProveedorSolicitud solicitud, int? id = null)
    {
        string ruc = Texto(solicitud.Ruc, 11, "RUC", true);
        if (!Regex.IsMatch(ruc, @"^[0-9]{11}$")) throw new ArgumentException("El RUC debe tener 11 dígitos.");
        if (proveedores.Any(p => p.Id != id && p.Ruc == ruc)) throw new ArgumentException("Ya existe un proveedor con ese RUC, incluso si está inactivo.");
        string correo = Texto(solicitud.Correo, 100, "correo");
        if (correo.Length > 0 && !new EmailAddressAttribute().IsValid(correo)) throw new ArgumentException("El correo no es válido.");
        return new() { Ruc = ruc, RazonSocial = Texto(solicitud.RazonSocial, 100, "razón social", true),
            Contacto = Texto(solicitud.Contacto, 100, "contacto"), Telefono = Texto(solicitud.Telefono, 20, "teléfono"),
            Correo = correo, Direccion = Texto(solicitud.Direccion, 150, "dirección") };
    }
    public Proveedor RegistrarProveedor(ProveedorSolicitud solicitud)
    {
        lock (bloqueo)
        {
            Proveedor proveedor = ValidarProveedor(solicitud);
            proveedor.Id = proveedores.Max(p => p.Id) + 1;
            proveedores.Add(proveedor);
            return Copia(proveedor);
        }
    }
    public Proveedor EditarProveedor(int id, ProveedorSolicitud solicitud)
    {
        lock (bloqueo)
        {
            Proveedor proveedor = proveedores.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException("El proveedor no existe.");
            Proveedor datos = ValidarProveedor(solicitud, id);
            proveedor.Ruc = datos.Ruc; proveedor.RazonSocial = datos.RazonSocial; proveedor.Contacto = datos.Contacto;
            proveedor.Telefono = datos.Telefono; proveedor.Correo = datos.Correo; proveedor.Direccion = datos.Direccion;
            return Copia(proveedor);
        }
    }
    public Proveedor CambiarEstadoProveedor(int id, EstadoSolicitud solicitud)
    {
        lock (bloqueo)
        {
            Proveedor proveedor = proveedores.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException("El proveedor no existe.");
            proveedor.Activo = solicitud.Activo ?? throw new ArgumentException("Indica el estado del proveedor.");
            return Copia(proveedor);
        }
    }
    public Compra ConsultarCompra(int id)
    {
        lock (bloqueo) { return Copia(compras.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentException("La compra no existe.")); }
    }
}
