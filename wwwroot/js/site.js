// JavaScript presenta los datos; C# valida y registra las operaciones.
const $ = id => document.getElementById(id);
const dinero = monto => new Intl.NumberFormat('es-PE', { style: 'currency', currency: 'PEN' }).format(monto);
const fecha = valor => new Intl.DateTimeFormat('es-PE', { dateStyle: 'short', timeStyle: 'short', timeZone: 'America/Lima' }).format(new Date(valor));
const baseApi = document.body.dataset.apiBase || '/api/tienda/';
const esAdministrador = document.body.dataset.admin === 'true';
let carrito = [];
let catalogo = [];

async function api(ruta, datos, raiz = baseApi) {
    const opciones = { credentials: 'same-origin', cache: 'no-store' };
    if (datos !== undefined) {
        opciones.method = 'POST';
        opciones.headers = { 'Content-Type': 'application/json', 'RequestVerificationToken': document.querySelector('[name="__RequestVerificationToken"]').value };
        opciones.body = JSON.stringify(datos);
    }
    const respuesta = await fetch(raiz + ruta, opciones);
    if (respuesta.status === 401) {
        location.assign(document.body.dataset.loginUrl || '/Cuenta/Login');
        throw new Error('Tu sesión terminó. Inicia sesión nuevamente.');
    }
    const contenido = await respuesta.json().catch(() => null);
    if (!respuesta.ok) {
        const errores = contenido?.errors ? Object.values(contenido.errors).flat().join(' ') : '';
        throw new Error(contenido?.mensaje || errores || (respuesta.status === 403 ? 'Tu rol no tiene permiso para realizar esta operación.' : 'No se pudo completar la operación. Revisa los datos e intenta nuevamente.'));
    }
    return contenido;
}

function elemento(tipo, contenido, clase) {
    const nodo = document.createElement(tipo);
    nodo.textContent = contenido;
    if (clase) nodo.className = clase;
    return nodo;
}
function boton(titulo, accion, deshabilitado = false) {
    const nodo = elemento('button', titulo);
    nodo.type = 'button'; nodo.disabled = deshabilitado;
    nodo.addEventListener('click', accion);
    return nodo;
}
function mensaje(id, contenido, error = false) {
    $(id).textContent = contenido;
    $(id).classList.toggle('error', error);
}
function tabla(id, datos, columnas, transformar) {
    const cuerpo = $(id); cuerpo.replaceChildren();
    if (!datos.length) {
        const fila = document.createElement('tr');
        const celda = elemento('td', 'No hay registros para mostrar.', 'muted');
        celda.colSpan = columnas; fila.append(celda); cuerpo.append(fila); return;
    }
    datos.forEach(registro => {
        const fila = document.createElement('tr');
        transformar(registro).forEach(valor => {
            const celda = document.createElement('td');
            if (valor instanceof Node) celda.append(valor); else celda.textContent = valor;
            fila.append(celda);
        });
        cuerpo.append(fila);
    });
}
function opciones(select, registros, etiqueta, valor, opcionTodos) {
    const anterior = select.value;
    select.replaceChildren();
    if (opcionTodos) select.append(new Option(opcionTodos, ''));
    registros.forEach(r => select.append(new Option(etiqueta(r), valor(r))));
    if ([...select.options].some(o => o.value === anterior)) select.value = anterior;
}
function formulario(id, mensajeId, accion) {
    $(id).addEventListener('submit', async evento => {
        evento.preventDefault();
        const enviar = $(id).querySelector('[type="submit"]');
        if (enviar.disabled) return;
        enviar.disabled = true;
        try { await accion(); }
        catch (error) { mensaje(mensajeId, error.message, true); }
        finally { enviar.disabled = enviar.dataset.bloqueado === 'true'; }
    });
}
function buscar(id, mensajeId, accion) {
    let temporizador; let revision = 0;
    $(id).addEventListener('input', () => {
        const actual = ++revision;
        clearTimeout(temporizador);
        temporizador = setTimeout(async () => {
            try {
                const productos = await api('productos?busqueda=' + encodeURIComponent($(id).value));
                if (actual === revision) accion(productos);
            } catch (error) { if (actual === revision) mensaje(mensajeId, error.message, true); }
        }, 180);
    });
}
function abrirDetalle(titulo, contenido) {
    $('tituloDetalle').textContent = titulo;
    $('contenidoDetalle').replaceChildren(...contenido);
    if (!$('dialogoDetalle').open) $('dialogoDetalle').showModal();
}
$('cerrarDetalle').addEventListener('click', () => $('dialogoDetalle').close());

async function fichaProducto(id) {
    try {
        const { producto, alternativas } = await api('productos/' + id);
        const contenido = [elemento('p', `${producto.codigo} · ${producto.nombre}`),
            elemento('p', `Precio: ${dinero(producto.precio)} · Stock: ${producto.stock} · Mínimo: ${producto.stockMinimo}`),
            elemento('p', !producto.activo ? 'Producto inactivo.' : producto.stockBajo ? 'Requiere reposición.' : 'Stock disponible.')];
        if (producto.stock === 0 && $('listaProductos')) {
            contenido.push(elemento('h3', 'Alternativas disponibles'));
            if (!alternativas.length) contenido.push(elemento('p', 'No hay alternativas con stock registradas.'));
            alternativas.forEach(p => {
                const fila = elemento('div', '', 'product-row');
                fila.append(elemento('span', `${p.nombre} · ${dinero(p.precio)} · Stock: ${p.stock}`),
                    boton('Agregar', () => { agregar(p); $('dialogoDetalle').close(); }));
                contenido.push(fila);
            });
        } else {
            contenido.push(elemento('p', 'Alternativas con stock: ' + (alternativas.map(p => p.nombre).join(', ') || 'Ninguna')));
        }
        abrirDetalle('Información del producto', contenido);
    } catch (error) {
        mensaje($('mensajeVenta') ? 'mensajeVenta' : 'mensajeInventario', error.message, true);
    }
}

function agregar(producto) {
    const item = carrito.find(i => i.productoId === producto.id);
    if ((item?.cantidad || 0) >= producto.stock) { mensaje('mensajeVenta', 'No hay más unidades disponibles.', true); return; }
    if (item) item.cantidad++;
    else carrito.push({ productoId: producto.id, nombre: producto.nombre, precio: producto.precio, cantidad: 1 });
    mensaje('mensajeVenta', ''); renderCarrito();
}
function renderProductos(productos) {
    const zona = $('listaProductos'); zona.replaceChildren();
    if (!productos.length) zona.append(elemento('p', 'No se encontraron productos.', 'muted'));
    productos.forEach(p => {
        const fila = elemento('div', '', 'product-row');
        const info = document.createElement('div');
        info.append(elemento('strong', p.nombre), elemento('small', `${p.codigo} · ${dinero(p.precio)} · Stock: ${p.stock}`));
        if (p.stockBajo) info.append(elemento('small', 'Requiere reposición', 'low-text'));
        const acciones = elemento('div', '', 'button-row');
        acciones.append(boton('Ficha', () => fichaProducto(p.id)));
        acciones.append(p.stock === 0 ? boton('Ver alternativas', () => fichaProducto(p.id)) : boton('Agregar', () => agregar(p)));
        fila.append(info, acciones); zona.append(fila);
    });
}
function totalPreview() { return carrito.reduce((s, i) => s + Math.round(i.precio * i.cantidad * 100), 0) / 100; }
function renderCarrito() {
    const zona = $('detalleVenta'); zona.replaceChildren();
    if (!carrito.length) zona.append(elemento('p', 'Aún no agregas productos.', 'muted'));
    carrito.forEach(item => {
        const fila = elemento('div', '', 'product-row');
        const quitar = boton('Quitar una', () => {
            item.cantidad--;
            carrito = carrito.filter(i => i.cantidad > 0); renderCarrito();
        });
        fila.append(elemento('span', `${item.nombre} × ${item.cantidad} · ${dinero(item.precio * item.cantidad)}`), quitar);
        zona.append(fila);
    });
    $('totalVenta').textContent = dinero(totalPreview()); actualizarPago();
}
function actualizarPago() {
    const tipo = $('formaPago').value;
    $('campoEfectivo').hidden = tipo !== 'Efectivo';
    $('campoDigital').hidden = tipo !== 'Digital';
    $('campoFiado').hidden = tipo !== 'Fiado';
    $('referenciaPago').required = tipo === 'Digital';
    $('montoRecibido').required = tipo === 'Efectivo';
    $('clienteFiado').required = tipo === 'Fiado';
    $('montoRecibido').disabled = tipo !== 'Efectivo';
    $('referenciaPago').disabled = tipo !== 'Digital';
    $('clienteFiado').disabled = tipo !== 'Fiado';
    const diferencia = Math.round((Number($('montoRecibido').value) - totalPreview()) * 100) / 100;
    $('vuelto').textContent = diferencia < 0 ? 'Falta pagar: ' + dinero(-diferencia) : 'Vuelto: ' + dinero(diferencia);
}
async function iniciarVentas() {
    renderProductos(catalogo); renderCarrito();
    const clientes = await api('clientes');
    opciones($('clienteFiado'), clientes, c => c.nombre, c => c.id);
    buscar('buscarProducto', 'mensajeVenta', renderProductos);
    $('formaPago').addEventListener('change', actualizarPago);
    $('montoRecibido').addEventListener('input', actualizarPago);
    formulario('formVenta', 'mensajeVenta', async () => {
        const venta = await api('ventas', { items: carrito.map(i => ({ productoId: i.productoId, cantidad: i.cantidad })),
            formaPago: $('formaPago').value, clienteId: Number($('clienteFiado').value) || null,
            montoRecibido: $('formaPago').value === 'Efectivo' ? Number($('montoRecibido').value) : 0,
            referenciaPago: $('referenciaPago').value });
        carrito = []; $('formVenta').reset(); renderCarrito();
        renderProductos(await api('productos?busqueda=' + encodeURIComponent($('buscarProducto').value)));
        mensaje('mensajeVenta', `Venta N.º ${venta.id} registrada. Total: ${dinero(venta.total)}. Vuelto: ${dinero(venta.vuelto)}.`);
    });
}

function renderInventario(productos) {
    $('cantidadProductos').textContent = `Productos mostrados: ${productos.length} · Con stock bajo: ${productos.filter(p => p.activo && p.stockBajo).length}`;
    tabla('tablaInventario', productos, 7, p => {
        const acciones = elemento('div', '', 'button-row');
        acciones.append(boton('Ficha', () => fichaProducto(p.id)));
        if (esAdministrador) acciones.append(boton('Editar', () => abrirProducto(p)),
            boton(p.activo ? 'Desactivar' : 'Activar', evento => cambiarEstadoProducto(p, evento.currentTarget)));
        return [p.codigo, p.nombre, dinero(p.precio), p.stock, p.stockMinimo,
            elemento('span', !p.activo ? 'Inactivo' : p.stockBajo ? 'Reponer' : 'Disponible',
                !p.activo ? 'status inactive' : p.stockBajo ? 'status low' : 'status'), acciones];
    });
}
function abrirProducto(producto = null) {
    $('formProducto').reset();
    $('productoEditarId').value = producto?.id || '';
    $('tituloProducto').textContent = producto ? 'Editar producto' : 'Nuevo producto';
    $('codigoProducto').value = producto?.codigo || '';
    $('nombreProducto').value = producto?.nombre || '';
    $('precioProducto').value = producto ? producto.precio.toFixed(2) : '';
    $('minimoProducto').value = producto?.stockMinimo ?? 5;
    $('campoStockInicial').hidden = Boolean(producto);
    $('stockInicialProducto').disabled = Boolean(producto);
    $('notaStockProducto').textContent = producto
        ? `Stock actual: ${producto.stock}. Las entradas se registran en Compras. Las ventas anteriores conservan sus precios.`
        : 'Después del registro, las entradas de stock se realizan en Compras.';
    mensaje('mensajeProducto', '');
    $('dialogoProducto').showModal(); $('codigoProducto').focus();
}
let revisionInventario = 0;
async function recargarInventario() {
    const actual = ++revisionInventario;
    const parametros = new URLSearchParams({ busqueda: $('buscarInventario').value });
    if ($('mostrarInactivos').checked) parametros.set('incluirInactivos', 'true');
    const [activos, encontrados] = await Promise.all([api('productos'), api('productos?' + parametros)]);
    if (actual !== revisionInventario) return;
    catalogo = activos; renderInventario(encontrados);
    opciones($('productoMinimo'), catalogo, p => p.nombre, p => p.id);
    opciones($('productoOriginal'), catalogo, p => p.nombre, p => p.id);
    ['formStockMinimo', 'formAlternativas'].forEach(id => {
        $(id).querySelectorAll('input, select, button').forEach(control => { control.disabled = catalogo.length === 0; });
    });
    cargarMinimo(); cargarAlternativas();
}
function cargarMinimo() {
    const producto = catalogo.find(p => p.id === Number($('productoMinimo').value));
    $('stockMinimo').value = producto?.stockMinimo ?? '';
}
function cargarAlternativas() {
    const original = catalogo.find(p => p.id === Number($('productoOriginal').value));
    opciones($('productosAlternativos'), catalogo.filter(p => p.id !== original?.id), p => p.nombre, p => p.id);
    [...$('productosAlternativos').options].forEach(o => { o.selected = original?.alternativosIds.includes(Number(o.value)) || false; });
}
async function cambiarEstadoProducto(producto, control) {
    control.disabled = true;
    try {
        await api(`productos/${producto.id}/estado`, { activo: !producto.activo });
        await recargarInventario();
        mensaje('mensajeInventario', producto.activo
            ? `${producto.nombre} desactivado. Puedes reactivarlo marcando «Mostrar también productos inactivos».`
            : `${producto.nombre} activado.`);
    } catch (error) { mensaje('mensajeInventario', error.message, true); }
    finally { control.disabled = false; }
}
async function iniciarInventario() {
    if (!esAdministrador) {
        renderInventario(catalogo); buscar('buscarInventario', 'mensajeInventario', renderInventario); return;
    }
    await recargarInventario();
    $('nuevoProducto').addEventListener('click', () => abrirProducto());
    $('cancelarProducto').addEventListener('click', () => $('dialogoProducto').close());
    $('productoMinimo').addEventListener('change', cargarMinimo);
    $('productoOriginal').addEventListener('change', cargarAlternativas);
    // Las revisiones impiden que una respuesta antigua reemplace la búsqueda actual.
    const actualizar = () => recargarInventario().catch(e => mensaje('mensajeInventario', e.message, true));
    $('buscarInventario').addEventListener('input', actualizar);
    $('mostrarInactivos').addEventListener('change', actualizar);
    formulario('formProducto', 'mensajeProducto', async () => {
        const id = $('productoEditarId').value;
        const datos = { codigo: $('codigoProducto').value, nombre: $('nombreProducto').value,
            precio: Number($('precioProducto').value), stockMinimo: Number($('minimoProducto').value) };
        if (!id) datos.stockInicial = Number($('stockInicialProducto').value);
        const producto = await api(id ? `productos/${id}/editar` : 'productos', datos);
        await recargarInventario(); $('dialogoProducto').close();
        mensaje('mensajeInventario', `${producto.nombre}: producto ${id ? 'actualizado' : 'registrado'}.`);
    });
    formulario('formStockMinimo', 'mensajeInventario', async () => {
        await api('stock-minimo', { productoId: Number($('productoMinimo').value), stockMinimo: Number($('stockMinimo').value) });
        await recargarInventario();
        mensaje('mensajeInventario', 'Stock mínimo actualizado.');
    });
    formulario('formAlternativas', 'mensajeInventario', async () => {
        await api('alternativas', { productoId: Number($('productoOriginal').value), alternativosIds: [...$('productosAlternativos').selectedOptions].map(o => Number(o.value)) });
        await recargarInventario(); mensaje('mensajeInventario', 'Alternativas registradas.');
    });
}

async function renderCompras() {
    const compras = await api('compras');
    $('cantidadCompras').textContent = `Compras registradas: ${compras.length}`;
    tabla('tablaCompras', compras, 7, c => [c.id, fecha(c.fecha), c.proveedorNombre, c.usuarioNombre || 'Práctica', c.detalles.length, dinero(c.total), boton('Ver detalle', () => detalleCompra(c.id))]);
}
async function iniciarCompras() {
    const seleccionar = productos => {
        opciones($('productoCompra'), productos, p => `${p.codigo} · ${p.nombre}`, p => p.id);
        if (!productos.length) mensaje('mensajeCompra', 'No hay productos que coincidan.'); else mensaje('mensajeCompra', '');
    };
    seleccionar(catalogo); buscar('buscarCompra', 'mensajeCompra', seleccionar);
    const proveedores = await api('proveedores');
    opciones($('proveedorCompra'), proveedores, p => `${p.ruc} · ${p.razonSocial}`, p => p.id);
    $('avisoProveedor').textContent = proveedores.length ? '' : 'Registra o activa un proveedor en el módulo Proveedores antes de comprar.';
    $('proveedorCompra').addEventListener('change', renderLineasCompra);
    renderLineasCompra();
    await renderCompras();
    formulario('formLineaCompra', 'mensajeCompra', async () => {
        const id = Number($('productoCompra').value);
        const p = catalogo.find(p => p.id === id) || (await api('productos/' + id)).producto;
        const cantidad = Number($('cantidadCompra').value), costoUnitario = Number($('costoCompra').value);
        if (!Number.isInteger(cantidad) || cantidad <= 0 || costoUnitario <= 0) throw new Error('Ingresa cantidad y costo mayores que cero.');
        const existente = lineasCompra.find(i => i.productoId === id);
        if (existente) Object.assign(existente, { cantidad, costoUnitario });
        else lineasCompra.push({ productoId: id, nombre: p.nombre, cantidad, costoUnitario });
        renderLineasCompra(); mensaje('mensajeCompra', existente ? 'Cantidad y costo actualizados en el detalle.' : 'Producto agregado al detalle.');
    });
    formulario('formCompra', 'mensajeCompra', async () => {
        const compra = await api('compras', { proveedorId: Number($('proveedorCompra').value), items: lineasCompra.map(({ productoId, cantidad, costoUnitario }) => ({ productoId, cantidad, costoUnitario })) });
        lineasCompra = []; renderLineasCompra();
        await renderCompras();
        const [actuales, encontrados] = await Promise.all([api('productos'), api('productos?busqueda=' + encodeURIComponent($('buscarCompra').value))]);
        catalogo = actuales; seleccionar(encontrados);
        mensaje('mensajeCompra', `Compra N.º ${compra.id} registrada. Total: ${dinero(compra.total)}. El stock fue actualizado.`);
    });
}

async function renderPagos() {
    const id = $('filtrarPagos').value;
    const pagos = await api('pagos' + (id ? '?clienteId=' + id : ''));
    tabla('tablaPagos', pagos, 6, p => [p.id, fecha(p.fecha), p.clienteNombre, p.metodo, dinero(p.monto), dinero(p.saldoRestante)]);
}
async function renderClientes() {
    const clientes = await api('clientes?incluirInactivos=true');
    clientesCatalogo = clientes;
    $('cantidadClientes').textContent = `Clientes registrados: ${clientes.length} · Activos: ${clientes.filter(c => c.activo).length}`;
    const deudores = clientes.filter(c => c.deuda > 0);
    opciones($('clientePago'), deudores, c => `${c.nombre} · Deuda: ${dinero(c.deuda)}`, c => c.id);
    const pagoBloqueado = deudores.length === 0 || $('formPago').dataset.pendiente === 'true';
    $('formPago').querySelector('button[type=submit]').disabled = pagoBloqueado;
    $('formPago').querySelector('button[type=submit]').dataset.bloqueado = String(pagoBloqueado);
    opciones($('filtrarPagos'), clientes, c => c.nombre, c => c.id, 'Todos los clientes');
    renderTablaClientes();
}
async function iniciarClientes() {
    await renderClientes(); await renderPagos();
    $('buscarCliente').addEventListener('input', renderTablaClientes);
    if (esAdministrador) iniciarGestionClientes();
    $('filtrarPagos').addEventListener('change', () => renderPagos().catch(e => mensaje('mensajeClientes', e.message, true)));
    formulario('formPago', 'mensajeClientes', async () => {
        const pago = await api('pagos', { clienteId: Number($('clientePago').value), monto: Number($('montoPago').value), metodo: $('metodoPago').value });
        $('montoPago').value = ''; await renderClientes(); await renderPagos();
        mensaje('mensajeClientes', `Pago N.º ${pago.id} registrado. Saldo restante: ${dinero(pago.saldoRestante)}.`);
    });
}

async function detalleVenta(id) {
    try {
        const venta = await api('ventas/' + id);
        const contenido = [elemento('p', `${fecha(venta.fecha)} · ${venta.formaPago} · ${venta.clienteNombre || 'Venta al contado'}`),
            elemento('p', 'Registrada por: ' + (venta.usuarioNombre || 'Práctica'))];
        const listado = document.createElement('ul');
        venta.detalles.forEach(d => listado.append(elemento('li', `${d.codigo} · ${d.nombre} × ${d.cantidad} · ${dinero(d.precioUnitario)} c/u · Subtotal: ${dinero(d.subtotal)}`)));
        contenido.push(listado, elemento('p', `Total: ${dinero(venta.total)} · Recibido: ${dinero(venta.montoRecibido)} · Vuelto: ${dinero(venta.vuelto)}`));
        if (venta.referenciaPago) contenido.push(elemento('p', 'Referencia digital: ' + venta.referenciaPago));
        abrirDetalle('Detalle de venta N.º ' + venta.id, contenido);
    } catch (e) { mensaje('mensajeReportes', e.message, true); }
}
async function renderReporte() {
    const parametros = new URLSearchParams();
    if ($('fechaDesde').value) parametros.set('desde', $('fechaDesde').value);
    if ($('fechaHasta').value) parametros.set('hasta', $('fechaHasta').value);
    const reporte = await api('reporte?' + parametros);
    $('reporteCantidad').textContent = reporte.cantidadVentas;
    $('reporteTotal').textContent = dinero(reporte.totalVendido);
    $('reporteCobrado').textContent = dinero(reporte.cobradoEnVentas);
    $('reporteFiado').textContent = dinero(reporte.totalFiado);
    $('reporteAbonos').textContent = dinero(reporte.abonos);
    $('reporteStockBajo').textContent = reporte.productosStockBajo;
    tabla('tablaVentas', reporte.ventas, 7, v => [v.id, fecha(v.fecha), v.clienteNombre || 'Contado', v.formaPago, v.usuarioNombre || 'Práctica', dinero(v.total), boton('Ver detalle', () => detalleVenta(v.id))]);
    mensaje('mensajeReportes', reporte.cantidadVentas + ' venta(s) en el periodo consultado.');
}
async function iniciarReportes() {
    await renderReporte(); formulario('formPeriodo', 'mensajeReportes', renderReporte);
    $('limpiarPeriodo').addEventListener('click', () => {
        $('formPeriodo').reset(); renderReporte().catch(e => mensaje('mensajeReportes', e.message, true));
    });
    $('exportarCsv').addEventListener('click', () => descargarReporte('csv'));
    $('exportarPdf').addEventListener('click', () => descargarReporte('pdf'));
}

async function iniciar() {
    document.querySelectorAll('.sidebar nav a').forEach(enlace => {
        if (new URL(enlace.href).pathname.toLowerCase() === location.pathname.toLowerCase()) enlace.classList.add('active');
    });
    if (!['listaProductos', 'tablaInventario', 'productoCompra', 'tablaClientes', 'tablaVentas', 'tablaProveedores', 'tablaUsuarios'].some(id => $(id))) return;
    try {
        catalogo = await api('productos');
        if ($('listaProductos')) await iniciarVentas();
        if ($('tablaInventario')) await iniciarInventario();
        if ($('productoCompra')) await iniciarCompras();
        if ($('tablaClientes')) await iniciarClientes();
        if ($('tablaVentas')) await iniciarReportes();
        if ($('tablaProveedores')) await iniciarProveedores();
        if ($('tablaUsuarios')) await iniciarUsuarios();
    } catch (e) {
        const id = ['mensajeVenta', 'mensajeInventario', 'mensajeCompra', 'mensajeClientes', 'mensajeReportes', 'mensajeProveedores', 'mensajeUsuarios'].find(id => $(id));
        if (id) mensaje(id, e.message, true);
    }
}
