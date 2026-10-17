using System.Data;
using Microsoft.Data.SqlClient;
using Tienda_Giovanna.Datos;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

// Comparte el Singleton de usuarios; cada operacion libera su propia conexion.
public sealed class ClientesSqlService
{
    private readonly ConexionBD conexion;
    public ClientesSqlService(ConexionBD conexion) { this.conexion = conexion; }
    private const string Columnas = "IdCliente, Documento, Nombre, Telefono, Direccion, Activo, Deuda";

    private static Cliente LeerCliente(SqlDataReader lector) => new()
    {
        Id = lector.GetInt32(0), Documento = lector.GetString(1), Nombre = lector.GetString(2),
        Telefono = lector.IsDBNull(3) ? "" : lector.GetString(3),
        Direccion = lector.IsDBNull(4) ? "" : lector.GetString(4),
        Activo = lector.GetBoolean(5), Deuda = lector.GetDecimal(6)
    };

    public List<Cliente> ConsultarClientes(bool incluirInactivos = false)
    {
        using var c = conexion.CrearConexion(); c.Open();
        using var cmd = new SqlCommand($"SELECT {Columnas} FROM vw_SaldosClientes WHERE @todos = 1 OR Activo = 1 ORDER BY Nombre, IdCliente", c);
        cmd.Parameters.Add("@todos", SqlDbType.Bit).Value = incluirInactivos;
        using var lector = cmd.ExecuteReader();
        var clientes = new List<Cliente>();
        while (lector.Read()) clientes.Add(LeerCliente(lector));
        return clientes;
    }

    private Cliente Guardar(string sql, Cliente? datos = null, int? id = null, bool? activo = null)
    {
        using var c = conexion.CrearConexion(); c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            using var cmd = new SqlCommand(sql, c, tx);
            if (datos != null)
            {
                cmd.Parameters.Add("@documento", SqlDbType.VarChar, 8).Value = datos.Documento;
                cmd.Parameters.Add("@nombre", SqlDbType.NVarChar, 100).Value = datos.Nombre;
                cmd.Parameters.Add("@telefono", SqlDbType.NVarChar, 20).Value = datos.Telefono;
                cmd.Parameters.Add("@direccion", SqlDbType.NVarChar, 150).Value = datos.Direccion;
            }
            if (id.HasValue) cmd.Parameters.Add("@id", SqlDbType.Int).Value = id.Value;
            if (activo.HasValue) cmd.Parameters.Add("@activo", SqlDbType.Bit).Value = activo.Value;
            object? resultado = cmd.ExecuteScalar();
            if (resultado == null) throw new ArgumentException("El cliente no existe.");
            using var consulta = new SqlCommand($"SELECT {Columnas} FROM vw_SaldosClientes WHERE IdCliente = @id", c, tx);
            consulta.Parameters.Add("@id", SqlDbType.Int).Value = Convert.ToInt32(resultado);
            Cliente cliente;
            using (var lector = consulta.ExecuteReader())
            {
                if (!lector.Read()) throw new ArgumentException("El cliente no existe.");
                cliente = LeerCliente(lector);
            }
            tx.Commit();
            return cliente;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            // La restriccion UNIQUE protege el DNI tambien entre solicitudes simultaneas.
            throw new ArgumentException("Ya existe un cliente con ese DNI, incluso si está inactivo.");
        }
    }

    public Cliente RegistrarCliente(ClienteSolicitud solicitud) => Guardar(
        "INSERT INTO Clientes (Documento, Nombre, Telefono, Direccion) OUTPUT INSERTED.IdCliente VALUES (@documento, @nombre, @telefono, @direccion)",
        ClientesValidacion.Validar(solicitud));

    public Cliente EditarCliente(int id, ClienteSolicitud solicitud) => Guardar(
        "UPDATE Clientes SET Documento = @documento, Nombre = @nombre, Telefono = @telefono, Direccion = @direccion OUTPUT INSERTED.IdCliente WHERE IdCliente = @id",
        ClientesValidacion.Validar(solicitud), id);

    public Cliente CambiarEstadoCliente(int id, EstadoSolicitud solicitud) => Guardar(
        "UPDATE Clientes SET Activo = @activo OUTPUT INSERTED.IdCliente WHERE IdCliente = @id",
        id: id, activo: solicitud.Activo ?? throw new ArgumentException("Indica el estado del cliente."));

    // Mantiene la consulta historica disponible aunque solo Clientes use SQL.
    public List<Pago> ConsultarPagos(int? clienteId)
    {
        using var c = conexion.CrearConexion(); c.Open();
        return PagosSqlService.Consultar(c, null, clienteId);
    }
}
