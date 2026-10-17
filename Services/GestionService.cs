using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

// Parte del mismo servicio: comparte el almacenamiento y el bloqueo de las operaciones.
public partial class TiendaService
{
    private readonly List<Proveedor> proveedores = new()
    {
        new() { Id = 1, Ruc = "20123456789", RazonSocial = "Distribuidora de práctica", Contacto = "Ana", Telefono = "999222333" }
    };
    private Cliente ValidarCliente(ClienteSolicitud solicitud, int? id = null)
    {
        Cliente datos = ClientesValidacion.Validar(solicitud);
        if (clientes.Any(c => c.Id != id && c.Documento == datos.Documento)) throw new ArgumentException("Ya existe un cliente con ese DNI, incluso si está inactivo.");
        return datos;
    }
    public Cliente RegistrarCliente(ClienteSolicitud solicitud)
    {
        if (clientesSql != null) return clientesSql.RegistrarCliente(solicitud);
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
        if (clientesSql != null) return clientesSql.EditarCliente(id, solicitud);
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
        if (clientesSql != null) return clientesSql.CambiarEstadoCliente(id, solicitud);
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
        if (proveedoresSql != null) return proveedoresSql.ConsultarProveedores(incluirInactivos);
        lock (bloqueo) { return Copia(proveedores.Where(p => incluirInactivos || p.Activo).ToList()); }
    }
    private Proveedor ValidarProveedor(ProveedorSolicitud solicitud, int? id = null)
    {
        Proveedor datos = ProveedoresValidacion.Validar(solicitud);
        if (proveedores.Any(p => p.Id != id && p.Ruc == datos.Ruc)) throw new ArgumentException("Ya existe un proveedor con ese RUC, incluso si está inactivo.");
        return datos;
    }
    public Proveedor RegistrarProveedor(ProveedorSolicitud solicitud)
    {
        if (proveedoresSql != null) return proveedoresSql.RegistrarProveedor(solicitud);
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
        if (proveedoresSql != null) return proveedoresSql.EditarProveedor(id, solicitud);
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
        if (proveedoresSql != null) return proveedoresSql.CambiarEstadoProveedor(id, solicitud);
        lock (bloqueo)
        {
            Proveedor proveedor = proveedores.FirstOrDefault(p => p.Id == id) ?? throw new ArgumentException("El proveedor no existe.");
            proveedor.Activo = solicitud.Activo ?? throw new ArgumentException("Indica el estado del proveedor.");
            return Copia(proveedor);
        }
    }
    public Compra ConsultarCompra(int id)
    {
        if (comprasSql != null) return comprasSql.ConsultarCompra(id);
        lock (bloqueo) { return Copia(compras.FirstOrDefault(c => c.Id == id) ?? throw new ArgumentException("La compra no existe.")); }
    }
}
