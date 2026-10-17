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

Los modos `Persistencia:Usuarios=Memoria`, `Persistencia:Clientes=Memoria`, `Persistencia:Proveedores=Memoria`, `Persistencia:Productos=Memoria`, `Persistencia:Compras=Memoria` y `Persistencia:Ventas=Memoria` se conservan **solo como selección explícita para pruebas sin SQL**. Los seis valores predeterminados son `SqlServer`: un fallo de conexión no activa registros alternativos ni cambia automáticamente a memoria. Para ejecutar todas las pruebas HTTP sin SQL, configura las seis variables `Persistencia__Usuarios=Memoria`, `Persistencia__Clientes=Memoria`, `Persistencia__Proveedores=Memoria`, `Persistencia__Productos=Memoria`, `Persistencia__Compras=Memoria` y `Persistencia__Ventas=Memoria`.

## Clientes conectados a SQL Server

`Services/ClientesSqlService.cs` utiliza el mismo Singleton `ConexionBD` para consultar, registrar, editar y activar o desactivar clientes con consultas parametrizadas. SQL Server asigna el identificador y garantiza el DNI único, incluso si el cliente está inactivo o hay registros simultáneos. Cada operación libera su conexión; los cambios se confirman en una transacción.

La deuda se obtiene de `vw_SaldosClientes` (saldo inicial + ventas al fiado - abonos), y el historial se consulta desde `Pagos`. El formulario no puede modificar la deuda ni el saldo inicial; editar o desactivar conserva los movimientos históricos.

1. Actualiza `avance-rf-csharp`, ejecuta con F5 e ingresa como administrador.
2. Abre **Clientes y fiados**: con la carga original verás **60 clientes registrados**, sus estados, sus deudas y los **124 pagos** del script, con filtro por cliente. También puedes buscar por nombre o DNI.
3. Registra un cliente de prueba y edita sus datos. Detén la aplicación y vuelve a ejecutarla: los cambios permanecen. Puedes confirmarlos con `SELECT * FROM Clientes` en SSMS.
4. Desactiva y reactiva ese cliente. El cajero puede consultar los datos; solo el administrador puede modificarlos.

**Alcance de esta etapa:** las ventas al fiado ya están habilitadas y actualizan la deuda calculada del cliente. El registro de nuevos abonos sigue pendiente; se puede consultar su historial. Las compras y ventas guardan sus transacciones y actualizan el stock. Reportes consulta ventas y abonos existentes de SQL. No ejecutes nuevamente los scripts ni vacíes las tablas.

## Proveedores conectados a SQL Server

`Services/ProveedoresSqlService.cs` usa el mismo Singleton `ConexionBD` para consultar, registrar, editar y activar o desactivar proveedores. Las consultas son parametrizadas, cada operación libera su conexión y las escrituras se confirman en una transacción. SQL Server asigna el identificador y garantiza el RUC único incluso para proveedores inactivos y registros simultáneos. Se validan los 11 dígitos del RUC, el correo opcional y los tamaños de los campos; no se consulta SUNAT.

1. Actualiza `avance-rf-csharp`, ejecuta con F5 y abre **Proveedores** como administrador: con la carga original verás **12 proveedores registrados**, sus contactos y estados.
2. Busca por razón social o RUC, registra un proveedor de prueba y edita sus datos. Detén y vuelve a ejecutar: los cambios permanecen. Confírmalos con `SELECT * FROM Proveedores` en SSMS.
3. Desactiva y reactiva el proveedor. El listado de Compras solo ofrece proveedores activos y el servidor comprueba su estado al registrar la operación.

Los datos históricos en `Compras` y `DetalleCompras` no se modifican al editar o desactivar un proveedor. El acceso a Proveedores y Compras sigue reservado al administrador. La selección de Compras reconoce los proveedores activos reales, incluidos los recién creados. Cada compra guarda sus detalles y repone las existencias de forma transaccional. No es necesario ejecutar scripts nuevos.

## Productos y alternativas conectados a SQL Server

`Services/ProductosSqlService.cs` utiliza el mismo Singleton `ConexionBD`. Consulta `Productos`, `ProductosAlternativos`, `vw_StockBajo` y `vw_AlternativasDisponibles` con parámetros; cada operación libera su conexión. El código de producto se normaliza a mayúsculas y es único incluso si está inactivo. El precio se redondea a dos decimales y debe caber en `DECIMAL(18,2)`.

1. Actualiza `avance-rf-csharp`, ejecuta con F5 y abre **Inventario**. Con la carga original verás **120 productos** y **7 alertas de stock bajo**. Busca por código o nombre y abre las fichas.
2. Registra un producto de prueba con stock inicial, edita su nombre y precio, y cambia su stock mínimo. Reinicia: los cambios permanecen. Editar conserva existencias, categoría, vencimiento e historial; el stock inicial solo se admite en el alta.
3. Desactiva y reactiva desde **Mostrar también productos inactivos**. El cajero consulta, y el administrador modifica el catálogo.
4. Configura **Productos alternativos**. El reemplazo de relaciones es una transacción: se valida que todos existan y estén activos antes de cambiar la selección. No admite el mismo producto como sustituto y elimina duplicados. Una lista vacía quita todas las alternativas. La ficha solo ofrece sustitutos activos con stock y un producto original activo.

**Ventas habilitadas:** las nuevas ventas guardan la transacción completa y descuentan stock. Si un producto no tiene existencias suficientes, no se guarda ninguna parte de la venta. Las compras reponen existencias. No necesitas scripts nuevos ni volver a cargar los datos.

## Compras conectadas a SQL Server

`Services/ComprasSqlService.cs` consulta `Compras` y `DetalleCompras` mediante el mismo Singleton. La pantalla muestra las **60 compras originales**, sus detalles, totales y usuarios. Los nombres del proveedor y de los productos se conservan como se registraron; editar los catálogos no reescribe el historial.

Al guardar una compra, el controlador toma el identificador de la sesión, y SQL comprueba que corresponda a un administrador activo. No se acepta el usuario ni el total enviados como campos adicionales del navegador. SQL asigna el identificador y la fecha. El servidor valida proveedor y productos activos, cantidades enteras positivas, líneas sin productos repetidos, costos redondeados a dos decimales, límites monetarios y capacidad del stock.

La cabecera, todos los detalles y los incrementos de stock usan una sola transacción y una sola conexión de operación. Los productos se bloquean en orden de identificador para coordinar compras concurrentes. Si falla una línea o una escritura, se revierte toda la compra y el stock. El precio de venta no cambia al registrar un costo de compra.

1. Actualiza `avance-rf-csharp`, ejecuta con F5 e ingresa como administrador. Abre **Compras** y revisa el historial y sus detalles.
2. Selecciona un proveedor activo, agrega uno o varios productos con cantidades y costos, y pulsa **Registrar compra**.
3. Comprueba el nuevo registro, el usuario de la operación y las existencias en **Inventario**. Reinicia y confirma que la compra y su reposición permanecen.

`Persistencia:Compras=SqlServer` requiere también Usuarios, Proveedores y Productos en SqlServer, para usar las mismas identidades y existencias. El modo explícito de memoria se conserva para las pruebas. La aplicación no modifica la estructura ni vuelve a cargar datos al arrancar; no necesitas scripts nuevos.

## Ventas conectadas a SQL Server

`Services/VentasSqlService.cs` consulta `Ventas` y `DetalleVentas` mediante el mismo Singleton. Reportes muestra las **931 ventas originales** más las nuevas, sus detalles y el usuario. Los filtros usan fechas inclusivas de Lima; los totales separan ventas cobradas, fiados y abonos existentes. Las descargas CSV y PDF usan esos datos.

Administrador y cajero pueden registrar ventas. El controlador toma el usuario de la sesión y SQL confirma que esté activo y tenga un rol autorizado. Los precios, totales, subtotales, fecha y vuelto se calculan en el servidor; no se aceptan importes o usuarios adicionales del navegador. Los productos repetidos se agrupan y las cantidades deben ser enteras positivas. Se conservan nombres y precios históricos al editar el catálogo.

Una sola transacción guarda cabecera, detalles y descuentos de stock. Los productos se bloquean por identificador, igual que en Compras, para coordinar operaciones simultáneas e impedir vender dos veces las últimas unidades. Un fallo revierte toda la venta y sus descuentos.

- **Efectivo:** requiere un monto recibido suficiente y calcula el vuelto.
- **Digital (Yape / Plin):** requiere confirmar externamente el pago e ingresar su referencia. Registra el total cobrado, sin vuelto; no hay integración automática con las billeteras.
- **Fiado:** exige un cliente activo, registra recibido y vuelto en cero y aumenta su deuda a través de `vw_SaldosClientes`. Conserva `SaldoInicial` y no crea pagos ficticios. El cliente se bloquea mientras se confirma la operación.

1. Actualiza `avance-rf-csharp`, ejecuta con F5 y abre **Ventas**.
2. Agrega uno o varios productos con stock, selecciona la forma de pago y pulsa **Finalizar venta**.
3. Revisa el stock en **Inventario** y la venta en **Reportes → Ver detalle**. Si fue fiado, comprueba la nueva deuda en **Clientes y fiados**.
4. Reinicia y confirma que venta, detalles, usuario, stock y deuda permanecen.

`Persistencia:Ventas=SqlServer` requiere Usuarios, Clientes y Productos en SqlServer. El registro de nuevos abonos será el siguiente módulo. No vuelvas a ejecutar los scripts de creación o llenado.

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
- `Services/ProductosSqlService.cs`: persistencia de productos, stock mínimo y alternativas; consulta de fichas y alertas.
- `Services/ProductosValidacion.cs`: normalización y validación compartida de códigos, precios y mínimos.
- `Services/ComprasSqlService.cs`: consulta y guardado transaccional de compras, detalles y existencias.
- `Services/ComprasValidacion.cs`: validación compartida de líneas, costos, cantidades y límites.
- `Services/VentasSqlService.cs`: historial y guardado transaccional de ventas, detalles, descuento de stock y fiados.
- `Services/VentasValidacion.cs`: modalidades de pago, referencias, cantidades agrupadas y límites monetarios.
- `Datos/ConexionBD.cs`: Singleton que configura y crea conexiones a SQL Server.
- `Services/IUsuariosService.cs`: contrato común de cuentas y sesiones.
- `Services/UsuariosSqlService.cs`: cuentas, contraseñas y validación de sesiones persistidas.
- `Services/UsuariosService.cs`: implementación en memoria para las pruebas explícitas.
- `Services/ExportacionService.cs`: genera CSV y PDF desde el mismo reporte consultado.
- `Controllers/TiendaController.cs`: recibe solicitudes de la interfaz y llama al servicio.
- `Views/Home`: pantallas MVC.
- `wwwroot/js/site.js`: presenta las respuestas de C# y envía las operaciones.

**Etapa actual:** usuarios, clientes, proveedores, productos, alternativas, compras y ventas usan SQL Server. Las compras reponen stock y las ventas lo descuentan; ambas conservan detalles y usuario. Los fiados actualizan la deuda del cliente. Reportes consulta ventas, abonos existentes y stock bajo reales, con exportación CSV/PDF. El registro de nuevos abonos sigue pendiente. Reiniciar conserva los datos. No hay integración automática con Yape o Plin.

La exportación PDF usa QuestPDF 2026.9.1 con licencia Community para este proyecto académico. Visual Studio restaura el paquete NuGet al compilar por primera vez; después la generación se realiza localmente.

## Probar los RF

```sh
dotnet run --project tests/RfChecks/RfChecks.csproj
```

GitHub Actions compila el proyecto y ejecuta las comprobaciones en cada actualización.

## Probar la gestión de productos

1. En Inventario, pulsa **Nuevo producto** y registra `P121`, `Atún en lata`, precio `6.50`, stock inicial `20` y mínimo `5`.
2. Busca el producto en Ventas y abre su ficha. Edita su nombre y precio desde Inventario y confirma los cambios al reiniciar. Registra una venta y comprueba que el detalle conserva el precio usado aunque lo edites después.
3. Pulsa **Desactivar**: dejará de aparecer en Ventas y Compras. En Inventario marca **Mostrar también productos inactivos** para consultarlo o activarlo otra vez.
4. Intenta registrar un código repetido: el servidor debe rechazarlo sin modificar el catálogo.

## Pruebas con SQL Server

El flujo `verificar-sql.yml` crea un SQL Server 2022 temporal, ejecuta los dos scripts completos, comprueba los hashes de las cuentas con PasswordHasher de .NET y prueba el Singleton, persistencia, bloqueos, permisos y operaciones HTTP. Para clientes comprueba DNI único ante concurrencia, conservación de deuda e historial, filtros, permisos y protección antifalsificación. Para proveedores comprueba RUC único, campos opcionales, validación de correo, conservación de compras históricas y selección de proveedores activos en Compras. Además reinicia el servidor web para comprobar la persistencia de ediciones y bajas, y prueba las reactivaciones. Para productos comprueba códigos únicos, precios, stock, metadatos e historial conservados, alternativas transaccionales, concurrencia, filtros de activos y disponibles, alertas y bloqueo de movimientos pendientes. Para Compras verifica la reposición concurrente, el usuario de sesión, importes, límites, historial persistente y reversión completa ante un fallo provocado durante la escritura del segundo detalle. El trigger de esa prueba solo existe en la base temporal y se elimina al terminar. Para Ventas prueba efectivo, digital y fiado, precios del servidor, deuda calculada, historia, fechas de Lima, stock insuficiente, fallo al escribir el segundo detalle, ventas simultáneas y cruce de compra/venta. Reinicia la aplicación para comprobar las ventas persistidas. Chromium verifica catálogos, compras y ventas, las tres formas de pago, agotamiento, deuda visible, detalle, recarga y permisos. Las claves del contenedor y de las cuentas se generan al ejecutar CI; no se almacenan en el repositorio. La autenticación SQL del contenedor solo se usa en CI; la configuración local sigue usando Windows. La conexión a localhost debe comprobarse desde ese equipo.
