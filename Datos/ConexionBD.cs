using Microsoft.Data.SqlClient;

namespace Tienda_Giovanna.Datos;

// Singleton: una sola instancia administra la configuracion de la base de datos.
public sealed class ConexionBD
{
    private static readonly Lazy<ConexionBD> instancia = new(() => new ConexionBD());
    private readonly object bloqueo = new();
    private string? cadenaConexion;
    private ConexionBD() { }
    public static ConexionBD Instancia => instancia.Value;

    public void Configurar(string cadena)
    {
        if (string.IsNullOrWhiteSpace(cadena)) throw new InvalidOperationException("Falta la conexion BodegaGiovanna en appsettings.json.");
        string normalizada = new SqlConnectionStringBuilder(cadena).ConnectionString;
        lock (bloqueo)
        {
            if (cadenaConexion != null && cadenaConexion != normalizada)
                throw new InvalidOperationException("La conexion ya fue configurada. Reinicia la aplicacion para cambiarla.");
            cadenaConexion = normalizada;
        }
    }

    // Cada operacion usa using para cerrar y liberar su propia conexion.
    public SqlConnection CrearConexion()
    {
        lock (bloqueo)
            return new SqlConnection(cadenaConexion ?? throw new InvalidOperationException("La conexion todavia no esta configurada."));
    }
}
