using System.Data;
using Microsoft.Data.SqlClient;
using Tienda_Giovanna.Datos;
using Tienda_Giovanna.Models;

namespace Tienda_Giovanna.Services;

// Comparte el Singleton existente; no mantiene conexiones abiertas ni listas en cache.
public sealed class ProveedoresSqlService
{
    private readonly ConexionBD conexion;
    public ProveedoresSqlService(ConexionBD conexion) { this.conexion = conexion; }
    private const string Columnas = "IdProveedor, Ruc, RazonSocial, Contacto, Telefono, Correo, Direccion, Activo";

    private static Proveedor Leer(SqlDataReader lector) => new()
    {
        Id = lector.GetInt32(0), Ruc = lector.GetString(1), RazonSocial = lector.GetString(2),
        Contacto = lector.IsDBNull(3) ? "" : lector.GetString(3),
        Telefono = lector.IsDBNull(4) ? "" : lector.GetString(4),
        Correo = lector.IsDBNull(5) ? "" : lector.GetString(5),
        Direccion = lector.IsDBNull(6) ? "" : lector.GetString(6), Activo = lector.GetBoolean(7)
    };

    public List<Proveedor> ConsultarProveedores(bool incluirInactivos = false)
    {
        using var c = conexion.CrearConexion(); c.Open();
        using var cmd = new SqlCommand($"SELECT {Columnas} FROM Proveedores WHERE @todos = 1 OR Activo = 1 ORDER BY RazonSocial, IdProveedor", c);
        cmd.Parameters.Add("@todos", SqlDbType.Bit).Value = incluirInactivos;
        using var lector = cmd.ExecuteReader();
        var proveedores = new List<Proveedor>();
        while (lector.Read()) proveedores.Add(Leer(lector));
        return proveedores;
    }

    public Proveedor ConsultarProveedorActivo(int id)
    {
        using var c = conexion.CrearConexion(); c.Open();
        using var cmd = new SqlCommand($"SELECT {Columnas} FROM Proveedores WHERE IdProveedor = @id AND Activo = 1", c);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
        using var lector = cmd.ExecuteReader();
        if (!lector.Read()) throw new ArgumentException("Selecciona un proveedor activo registrado.");
        return Leer(lector);
    }

    private Proveedor Guardar(string sql, Proveedor? datos = null, int? id = null, bool? activo = null)
    {
        using var c = conexion.CrearConexion(); c.Open();
        using var tx = c.BeginTransaction();
        try
        {
            using var cmd = new SqlCommand(sql, c, tx);
            if (datos != null)
            {
                cmd.Parameters.Add("@ruc", SqlDbType.VarChar, 11).Value = datos.Ruc;
                cmd.Parameters.Add("@razon", SqlDbType.NVarChar, 100).Value = datos.RazonSocial;
                cmd.Parameters.Add("@contacto", SqlDbType.NVarChar, 100).Value = datos.Contacto;
                cmd.Parameters.Add("@telefono", SqlDbType.NVarChar, 20).Value = datos.Telefono;
                cmd.Parameters.Add("@correo", SqlDbType.NVarChar, 100).Value = datos.Correo;
                cmd.Parameters.Add("@direccion", SqlDbType.NVarChar, 150).Value = datos.Direccion;
            }
            if (id.HasValue) cmd.Parameters.Add("@id", SqlDbType.Int).Value = id.Value;
            if (activo.HasValue) cmd.Parameters.Add("@activo", SqlDbType.Bit).Value = activo.Value;
            object? resultado = cmd.ExecuteScalar();
            if (resultado == null) throw new ArgumentException("El proveedor no existe.");
            using var consulta = new SqlCommand($"SELECT {Columnas} FROM Proveedores WHERE IdProveedor = @id", c, tx);
            consulta.Parameters.Add("@id", SqlDbType.Int).Value = Convert.ToInt32(resultado);
            Proveedor proveedor;
            using (var lector = consulta.ExecuteReader())
            {
                if (!lector.Read()) throw new ArgumentException("El proveedor no existe.");
                proveedor = Leer(lector);
            }
            tx.Commit();
            return proveedor;
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            throw new ArgumentException("Ya existe un proveedor con ese RUC, incluso si está inactivo.");
        }
    }

    public Proveedor RegistrarProveedor(ProveedorSolicitud solicitud) => Guardar(
        "INSERT INTO Proveedores (Ruc, RazonSocial, Contacto, Telefono, Correo, Direccion) OUTPUT INSERTED.IdProveedor VALUES (@ruc, @razon, @contacto, @telefono, @correo, @direccion)",
        ProveedoresValidacion.Validar(solicitud));

    public Proveedor EditarProveedor(int id, ProveedorSolicitud solicitud) => Guardar(
        "UPDATE Proveedores SET Ruc = @ruc, RazonSocial = @razon, Contacto = @contacto, Telefono = @telefono, Correo = @correo, Direccion = @direccion OUTPUT INSERTED.IdProveedor WHERE IdProveedor = @id",
        ProveedoresValidacion.Validar(solicitud), id);

    public Proveedor CambiarEstadoProveedor(int id, EstadoSolicitud solicitud) => Guardar(
        "UPDATE Proveedores SET Activo = @activo OUTPUT INSERTED.IdProveedor WHERE IdProveedor = @id",
        id: id, activo: solicitud.Activo ?? throw new ArgumentException("Indica el estado del proveedor."));
}
