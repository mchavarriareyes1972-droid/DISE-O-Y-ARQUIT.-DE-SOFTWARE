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

Los modos `Persistencia:Usuarios=Memoria`, `Persistencia:Clientes=Memoria` y `Persistencia:Proveedores=Memoria` se conservan **solo como selección explícita para pruebas sin SQL**. Los tres valores predeterminados son `SqlServer`: un fallo de conexión no activa registros alternativos ni cambia automáticamente a memoria. Para ejecutar todas las pruebas HTTP sin SQL, configura las tres variables `Persistencia__Usuarios=Memoria`, `Persistencia__Clientes=Memoria` y `Persistencia__Proveedores=Memoria`.

## Clientes conectados a SQL Server

`Services/ClientesSqlService.cs` utiliza el mismo Singleton `ConexionBD` para consultar, registrar, editar y activar o desactivar clientes con consultas parametrizadas. SQL Server asigna el identificador y garantiza el DNI único, incluso si el cliente está inactivo o hay registros simultáneos. Cada operación libera su conexión; los cambios se confirman en una transacción.

La deuda se obtiene de `vw_SaldosClientes` (saldo inicial + ventas al fiado - abonos), y el historial se consulta desde `Pagos`. El formulario no puede modificar la deuda ni el saldo inicial; editar o desactivar conserva los movimientos históricos.

1. Actualiza `avance-rf-csharp`, ejecuta con F5 e ingresa como administrador.
2. Abre **Clientes y fiados**: con la carga original verás **60 clientes registrados**, sus estados, sus deudas y los **124 pagos** del script, con filtro por cliente. También puedes buscar por nombre o DNI.
3. Registra un cliente de prueba y edita sus datos. Detén la aplicación y vuelve a ejecutarla: los cambios permanecen. Puedes confirmarlos con `SELECT * FROM Clientes` en SSMS.
4. Desactiva y reactiva ese cliente. El cajero puede consultar los datos; solo el administrador puede modificarlos.

**Alcance de esta etapa:** el registro de nuevos fiados y abonos queda temporalmente deshabilitado en modo SQL hasta migrar ventas y pagos. Las pantallas lo indican y la API también rechaza esas operaciones antes de cambiar stock o saldos. Así se evita registrar movimientos temporales sobre clientes reales de la base. La consulta de saldos e historial sí funciona. Las ventas en efectivo o digitales siguen siendo prácticas en memoria; los reportes aún corresponden a esos movimientos temporales. No ejecutes nuevamente los scripts ni vacíes las tablas.

## Proveedores conectados a SQL Server

`Services/ProveedoresSqlService.cs` usa el mismo Singleton `ConexionBD` para consultar, registrar, editar y activar o desactivar proveedores. Las consultas son parametrizadas, cada operación libera su conexión y las escrituras se confirman en una transacción. SQL Server asigna el identificador y garantiza el RUC único incluso para proveedores inactivos y registros simultáneos. Se validan los 11 dígitos del RUC, el correo opcional y los tamaños de los campos; no se consulta SUNAT.

1. Actualiza `avance-rf-csharp`, ejecuta con F5 y abre **Proveedores** como administrador: con la carga original verás **12 proveedores registrados**, sus contactos y estados.
2. Busca por razón social o RUC, registra un proveedor de prueba y edita sus datos. Detén y vuelve a ejecutar: los cambios permanecen. Confírmalos con `SELECT * FROM Proveedores` en SSMS.
3. Desactiva y reactiva el proveedor. El listado de Compras solo ofrece proveedores activos y comprueba su estado nuevamente antes de registrar la compra de práctica.

Los datos históricos en `Compras` y `DetalleCompras` no se modifican al editar o desactivar un proveedor. El acceso a Proveedores y Compras sigue reservado al administrador. **Las compras aún se registran en memoria**: la pantalla lo indica. Reconocen los proveedores reales de SQL, incluidos los recién creados, pero sus importes y existencias no se escriben todavía en las tablas de movimientos. No es necesario ejecutar scripts nuevos.

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
- `Services/GestionService.cs`: segunda parte del mismo servicio para clientes y proveedores; delega clientes a SQL cuando está configurado.
- `Services/ClientesSqlService.cs`: persistencia de clientes y consulta de sus saldos e historial.
- `Services/ClientesValidacion.cs`: validación de DNI y campos de clientes, compartida por SQL y las pruebas en memoria.
- `Services/ProveedoresSqlService.cs`: persistencia de proveedores y consulta del proveedor activo para las compras de práctica.
- `Services/ProveedoresValidacion.cs`: validación compartida de RUC, correo y campos del proveedor.
- `Datos/ConexionBD.cs`: Singleton que configura y crea conexiones a SQL Server.
- `Services/IUsuariosService.cs`: contrato común de cuentas y sesiones.
- `Services/UsuariosSqlService.cs`: cuentas, contraseñas y validación de sesiones persistidas.
- `Services/UsuariosService.cs`: implementación en memoria para las pruebas explícitas.
- `Services/ExportacionService.cs`: genera CSV y PDF desde el mismo reporte consultado.
- `Controllers/TiendaController.cs`: recibe solicitudes de la interfaz y llama al servicio.
- `Views/Home`: pantallas MVC.
- `wwwroot/js/site.js`: presenta las respuestas de C# y envía las operaciones.

**Etapa actual:** el inicio de sesión, la validación de sesiones y la gestión de usuarios, clientes y proveedores usan SQL Server. Clientes muestra las personas, deudas e historial de pagos cargados en SQL; Proveedores muestra los 12 distribuidores originales y los que registres. Productos, ventas, compras y reportes todavía usan TiendaService en memoria; sus pantallas aún no muestran los 120 productos ni el historial de ventas o compras cargado en SQL. Los nuevos fiados y abonos se habilitarán al completar esa migración. Reiniciar conserva las cuentas, clientes y proveedores de SQL, cierra las sesiones y restablece los movimientos de práctica en memoria. La referencia de pago digital se confirma manualmente; no existe integración con Yape o Plin.

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

El flujo `verificar-sql.yml` crea un SQL Server 2022 temporal, ejecuta los dos scripts completos, comprueba los hashes de las cuentas con PasswordHasher de .NET y prueba el Singleton, persistencia, bloqueos, permisos y operaciones HTTP. Para clientes comprueba DNI único ante concurrencia, conservación de deuda e historial, filtros, permisos y protección antifalsificación. Para proveedores comprueba RUC único, campos opcionales, validación de correo, conservación de compras históricas y selección de proveedores activos en Compras. Además reinicia el servidor web para comprobar la persistencia de ediciones y bajas, y prueba las reactivaciones. Chromium verifica los recorridos de ambas pantallas y sus permisos. Las claves del contenedor y de las cuentas se generan al ejecutar CI; no se almacenan en el repositorio. La autenticación SQL del contenedor solo se usa en CI; la configuración local sigue usando Windows. La conexión a localhost debe comprobarse desde ese equipo.
