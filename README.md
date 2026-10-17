# Bodega Giovanna

Proyecto académico de ASP.NET Core MVC con C#, HTML5, CSS3 y JavaScript. Requiere el SDK de .NET 8 y puede abrirse con `Tienda_Giovanna.sln` en Visual Studio.

## Ejecutar

Desde la carpeta del proyecto:

```sh
dotnet run --project Tienda_Giovanna.csproj
```

Abre en el navegador la dirección que indique la consola. En Visual Studio también puedes ejecutar con F5.

## Inicio de sesión (datos de práctica)

| Usuario | Rol |
|---|---|
| `admin` | Administrador |
| `cajero` | Cajero |

El administrador tiene acceso total. El cajero registra ventas y abonos y consulta productos, clientes y reportes. No puede administrar productos, clientes, proveedores, compras ni usuarios. Los permisos se comprueban en C#, además de adaptar las pantallas.

Las contraseñas se guardan como hashes mediante `PasswordHasher` de ASP.NET Core. Al modificar un usuario se invalidan sus sesiones. No se puede quitar el último administrador activo. Tras cinco contraseñas incorrectas para una cuenta, el acceso se bloquea cinco minutos.

Estas credenciales son públicas para la demostración local. En el modo predeterminado SqlServer, las cuentas, cambios de contraseña, permisos y bloqueos se guardan en la tabla Usuarios y se conservan al reiniciar. La aplicación no crea ni reemplaza estas cuentas al arrancar: deben estar en la base.

## Conexión a SQL Server y Singleton

La configuración de `appsettings.json` está preparada para `localhost`, base `BodegaGiovanna`, con Autenticación de Windows:

```text
Server=localhost;Database=BodegaGiovanna;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=10
```

1. En SQL Server Management Studio, comprueba que puedes entrar al servidor con tu usuario de Windows y que existen las tablas y sus datos.
2. Actualiza la rama `avance-rf-csharp` del repositorio y abre `Tienda_Giovanna.sln` en Visual Studio.
3. Compila para restaurar Microsoft.Data.SqlClient y ejecuta con F5 desde la misma cuenta de Windows que utilizas en SSMS.
4. Inicia sesión con `admin` o `cajero` y la contraseña que configuraste al llenar la base.
5. Para comprobar persistencia, cambia el nombre del cajero en Usuarios, reinicia la aplicación e ingresa otra vez: el nombre se conserva. Puedes devolverle su nombre original desde la misma pantalla.

**Si ya creaste y llenaste la base, no ejecutes otra vez los scripts.** Para una instalación nueva, ejecuta `database/01_Crear_Tablas.sql` y después tu script completo de datos en SSMS. El archivo de llenado conserva las plantillas de hash para CI: para tu instalación utiliza el script completo de datos que ya recibiste y ejecutaste. No vuelvas a cargar la base.

`Datos/ConexionBD.cs` implementa Singleton con constructor privado e instancia estática mediante `Lazy`. Program configura la cadena una vez y registra esa misma instancia en el contenedor. Cada operación crea una nueva `SqlConnection` y la libera con `using`. La instancia Singleton es la clase administradora, no una conexión abierta compartida.

`Services/IUsuariosService.cs` define las operaciones; `UsuariosSqlService.cs` las implementa con consultas parametrizadas y transacciones. El bloqueo por intentos, la baja y los cambios de versión de sesión se guardan en SQL. Una transacción protege al último administrador activo durante las ediciones.

`TrustServerCertificate=True` permite el certificado local de esta instalación de prácticas. Si otro compañero ejecuta el proyecto, debe cambiar el nombre del servidor en su configuración; no necesita la contraseña de Windows en la cadena. También se puede sustituir la conexión con la variable `ConnectionStrings__BodegaGiovanna`.

El modo `Persistencia:Usuarios=Memoria` se conserva **solo como selección explícita para pruebas sin SQL**. El valor predeterminado es `SqlServer`: un fallo de conexión no activa cuentas alternativas ni cambia automáticamente a memoria.

## Funciones disponibles

- Gestión de productos en Inventario: registrar, editar, desactivar y reactivar. Los códigos son únicos, incluso para productos inactivos. Las ediciones conservan las existencias y el historial de ventas; el stock inicial solo se ingresa al registrar y las entradas posteriores se hacen en Compras.
- Gestión de clientes: registrar y editar DNI, nombre, teléfono y dirección; activar y desactivar. El DNI es único, la deuda solo cambia con fiados y abonos y un cliente inactivo puede cancelar sus pendientes.
- Gestión de proveedores: registrar y editar RUC, razón social y contacto; activar y desactivar. El RUC es único y debe tener 11 dígitos (no se consulta SUNAT).
- Compras con varios productos: seleccionar un proveedor activo, agregar artículos al detalle con cantidades y costos, registrar una sola compra y consultar su detalle. Si una línea falla, no se modifica ningún stock.
- Exportación del reporte de ventas y abonos en CSV y PDF con las fechas seleccionadas. No se calculan ganancias netas en este avance.
- Gestión de usuarios y roles desde el módulo Usuarios, disponible para el administrador.
- RF01: buscar productos por código o nombre en Ventas y Compras.
- RF02: consultar la ficha del producto desde Ventas o Inventario.
- RF03: consultar el historial y detalle de ventas por periodo, con fechas de Lima.
- RF04: registrar abonos parciales o totales e historial de pagos.
- RF05: avisos de reposición según el stock mínimo configurado para cada producto.
- RF06: reportes por periodo con ventas al contado, fiados y abonos separados.
- RF07: registrar alternativas en Inventario y ofrecer las que tengan stock al buscar un producto agotado en Ventas.
- RF08: C# calcula el vuelto y valida el monto recibido y las cantidades disponibles.

## Organización

- `Models/TiendaModels.cs`: productos, clientes, ventas, detalles, pagos, compras y solicitudes.
- `Services/TiendaService.cs`: reglas de negocio y almacenamiento temporal compartido por la aplicación.
- `Services/GestionService.cs`: segunda parte del mismo servicio para clientes y proveedores.
- `Datos/ConexionBD.cs`: Singleton que configura y crea conexiones a SQL Server.
- `Services/IUsuariosService.cs`: contrato común de cuentas y sesiones.
- `Services/UsuariosSqlService.cs`: cuentas, contraseñas y validación de sesiones persistidas.
- `Services/UsuariosService.cs`: implementación en memoria para las pruebas explícitas.
- `Services/ExportacionService.cs`: genera CSV y PDF desde el mismo reporte consultado.
- `Controllers/TiendaController.cs`: recibe solicitudes de la interfaz y llama al servicio.
- `Views/Home`: pantallas MVC.
- `wwwroot/js/site.js`: presenta las respuestas de C# y envía las operaciones.

**Etapa actual:** el inicio de sesión, la validación de sesiones y la gestión de usuarios usan SQL Server. Productos, clientes, proveedores, ventas, compras, abonos y reportes todavía usan TiendaService en memoria; sus pantallas aún no muestran los 120 productos ni los movimientos cargados en SQL. Esos módulos se conectarán en la siguiente etapa. Reiniciar conserva las cuentas de SQL, cierra las sesiones y restablece los movimientos de práctica en memoria. La referencia de pago digital se confirma manualmente; no existe integración con Yape o Plin.

La exportación PDF usa QuestPDF 2026.9.1 con licencia Community para este proyecto académico. Visual Studio restaura el paquete NuGet al compilar por primera vez; después la generación se realiza localmente.

## Probar los RF

```sh
dotnet run --project tests/RfChecks/RfChecks.csproj
```

GitHub Actions compila el proyecto y ejecuta las comprobaciones en cada actualización.

## Probar la gestión de productos

1. En Inventario, pulsa **Nuevo producto** y registra `P008`, `Atún en lata`, precio `6.50`, stock inicial `20` y mínimo `5`.
2. Busca el producto en Ventas y registra una venta. Luego edita su nombre y precio desde Inventario: las ventas anteriores deben conservar sus datos originales.
3. Pulsa **Desactivar**: dejará de aparecer en Ventas y Compras. En Inventario marca **Mostrar también productos inactivos** para consultarlo o activarlo otra vez.
4. Intenta registrar un código repetido: el servidor debe rechazarlo sin modificar el catálogo.

## Pruebas con SQL Server

El flujo `verificar-sql.yml` crea un SQL Server 2022 temporal, ejecuta los dos scripts completos, comprueba los hashes de las cuentas con PasswordHasher de .NET y prueba el Singleton, persistencia, bloqueos, permisos y operaciones HTTP. Las claves del contenedor y de las cuentas se generan al ejecutar CI; no se almacenan en el repositorio. La autenticación SQL del contenedor solo se usa en CI; la configuración local sigue usando Windows. La conexión a localhost debe comprobarse desde ese equipo.
