// Pantallas de gestión. C# realiza las validaciones y comprueba los permisos.
let lineasCompra = [], clientesCatalogo = [], proveedoresCatalogo = [];

function renderLineasCompra() {
    tabla('detalleCompra', lineasCompra, 5, i => [i.nombre, i.cantidad, dinero(i.costoUnitario), dinero(i.cantidad * i.costoUnitario),
        boton('Quitar', () => { lineasCompra = lineasCompra.filter(p => p.productoId !== i.productoId); renderLineasCompra(); })]);
    $('totalCompra').textContent = dinero(lineasCompra.reduce((s, i) => s + Math.round(i.cantidad * i.costoUnitario * 100), 0) / 100);
    const enviar = $('formCompra').querySelector('button[type=submit]');
    enviar.dataset.bloqueado = String(lineasCompra.length === 0 || !$('proveedorCompra').value);
    enviar.disabled = enviar.dataset.bloqueado === 'true';
}
async function detalleCompra(id) {
    try {
        const compra = await api('compras/' + id);
        const lista = document.createElement('ul');
        compra.detalles.forEach(d => lista.append(elemento('li', `${d.codigo} · ${d.productoNombre} × ${d.cantidad} · ${dinero(d.costoUnitario)} c/u · Subtotal: ${dinero(d.subtotal)}`)));
        abrirDetalle('Compra N.º ' + compra.id, [elemento('p', `${fecha(compra.fecha)} · ${compra.proveedorNombre}`), lista, elemento('p', 'Total: ' + dinero(compra.total))]);
    } catch (e) { mensaje('mensajeCompra', e.message, true); }
}
function renderTablaClientes() {
    const texto = $('buscarCliente').value.trim().toLowerCase();
    tabla('tablaClientes', clientesCatalogo.filter(c => c.nombre.toLowerCase().includes(texto) || c.documento.includes(texto)), 7, c => {
        const acciones = elemento('div', '', 'button-row');
        acciones.append(boton('Abonar', () => { $('clientePago').value = c.id; $('montoPago').value = c.deuda.toFixed(2); $('montoPago').focus(); }, c.deuda === 0));
        if (esAdministrador) acciones.append(boton('Editar', () => abrirCliente(c)), boton(c.activo ? 'Desactivar' : 'Activar', evento => estadoRegistro('clientes', c, evento.currentTarget, 'mensajeClientes', renderClientes)));
        return [c.documento, c.nombre, c.telefono, c.direccion, dinero(c.deuda), c.activo ? 'Activo' : 'Inactivo', acciones];
    });
}
async function estadoRegistro(ruta, registro, control, mensajeId, recargar) {
    control.disabled = true;
    try { await api(`${ruta}/${registro.id}/estado`, { activo: !registro.activo }); await recargar(); mensaje(mensajeId, 'Estado actualizado.'); }
    catch (e) { mensaje(mensajeId, e.message, true); }
    finally { control.disabled = false; }
}
function abrirCliente(cliente = null) {
    $('formCliente').reset(); $('clienteEditarId').value = cliente?.id || '';
    $('tituloCliente').textContent = cliente ? 'Editar cliente' : 'Nuevo cliente';
    $('documentoCliente').value = cliente?.documento || ''; $('nombreCliente').value = cliente?.nombre || '';
    $('telefonoCliente').value = cliente?.telefono || ''; $('direccionCliente').value = cliente?.direccion || '';
    mensaje('mensajeClienteFormulario', ''); $('dialogoCliente').showModal(); $('documentoCliente').focus();
}
function iniciarGestionClientes() {
    $('nuevoCliente').addEventListener('click', () => abrirCliente());
    $('cancelarCliente').addEventListener('click', () => $('dialogoCliente').close());
    formulario('formCliente', 'mensajeClienteFormulario', async () => {
        const id = $('clienteEditarId').value;
        await api(id ? `clientes/${id}/editar` : 'clientes', { documento: $('documentoCliente').value, nombre: $('nombreCliente').value, telefono: $('telefonoCliente').value, direccion: $('direccionCliente').value });
        await renderClientes(); $('dialogoCliente').close(); mensaje('mensajeClientes', 'Cliente guardado.');
    });
}
function renderTablaProveedores() {
    const texto = $('buscarProveedor').value.trim().toLowerCase();
    tabla('tablaProveedores', proveedoresCatalogo.filter(p => p.razonSocial.toLowerCase().includes(texto) || p.ruc.includes(texto)), 7, p => {
        const acciones = elemento('div', '', 'button-row');
        acciones.append(boton('Ficha', () => abrirDetalle('Proveedor', [elemento('p', p.razonSocial), elemento('p', 'RUC: ' + p.ruc), elemento('p', p.direccion || 'Sin dirección registrada')])),
            boton('Editar', () => abrirProveedor(p)), boton(p.activo ? 'Desactivar' : 'Activar', evento => estadoRegistro('proveedores', p, evento.currentTarget, 'mensajeProveedores', renderProveedores)));
        return [p.ruc, p.razonSocial, p.contacto, p.telefono, p.correo, p.activo ? 'Activo' : 'Inactivo', acciones];
    });
}
async function renderProveedores() { proveedoresCatalogo = await api('proveedores?incluirInactivos=true'); renderTablaProveedores(); }
function abrirProveedor(p = null) {
    $('formProveedor').reset(); $('proveedorEditarId').value = p?.id || '';
    $('tituloProveedor').textContent = p ? 'Editar proveedor' : 'Nuevo proveedor';
    $('rucProveedor').value = p?.ruc || ''; $('razonProveedor').value = p?.razonSocial || '';
    $('contactoProveedor').value = p?.contacto || ''; $('telefonoProveedor').value = p?.telefono || '';
    $('correoProveedor').value = p?.correo || ''; $('direccionProveedor').value = p?.direccion || '';
    mensaje('mensajeProveedorFormulario', ''); $('dialogoProveedor').showModal(); $('rucProveedor').focus();
}
async function iniciarProveedores() {
    await renderProveedores(); $('buscarProveedor').addEventListener('input', renderTablaProveedores);
    $('nuevoProveedor').addEventListener('click', () => abrirProveedor()); $('cancelarProveedor').addEventListener('click', () => $('dialogoProveedor').close());
    formulario('formProveedor', 'mensajeProveedorFormulario', async () => {
        const id = $('proveedorEditarId').value;
        await api(id ? `proveedores/${id}/editar` : 'proveedores', { ruc: $('rucProveedor').value, razonSocial: $('razonProveedor').value, contacto: $('contactoProveedor').value,
            telefono: $('telefonoProveedor').value, correo: $('correoProveedor').value || null, direccion: $('direccionProveedor').value });
        await renderProveedores(); $('dialogoProveedor').close(); mensaje('mensajeProveedores', 'Proveedor guardado.');
    });
}
const usuariosApi = (ruta = '', datos) => api(ruta, datos, document.body.dataset.usersBase || '/api/usuarios');
async function renderUsuarios() {
    tabla('tablaUsuarios', await usuariosApi(), 5, u => [u.login, u.nombre, u.rol, u.activo ? 'Activo' : 'Inactivo', boton('Editar', () => abrirUsuario(u))]);
}
function abrirUsuario(u = null) {
    $('formUsuario').reset(); $('usuarioEditarId').value = u?.id || '';
    $('tituloUsuario').textContent = u ? 'Editar usuario' : 'Nuevo usuario';
    $('loginUsuario').value = u?.login || ''; $('nombreUsuario').value = u?.nombre || '';
    $('rolUsuario').value = u?.rol || 'Cajero'; $('estadoUsuario').value = String(u?.activo ?? true);
    $('campoEstadoUsuario').hidden = !u; $('passwordUsuario').required = !u;
    $('notaPassword').textContent = u ? 'Deja vacío para conservar la contraseña. Si la cambias, usa al menos 8 caracteres.' : 'Mínimo 8 caracteres.';
    mensaje('mensajeUsuarioFormulario', ''); $('dialogoUsuario').showModal(); $('loginUsuario').focus();
}
async function iniciarUsuarios() {
    await renderUsuarios(); $('nuevoUsuario').addEventListener('click', () => abrirUsuario()); $('cancelarUsuario').addEventListener('click', () => $('dialogoUsuario').close());
    formulario('formUsuario', 'mensajeUsuarioFormulario', async () => {
        const id = $('usuarioEditarId').value;
        const datos = { nombre: $('nombreUsuario').value, login: $('loginUsuario').value, rol: $('rolUsuario').value, password: $('passwordUsuario').value || null };
        if (id) datos.activo = $('estadoUsuario').value === 'true';
        await usuariosApi(id ? `/${id}/editar` : '', datos);
        await renderUsuarios(); $('dialogoUsuario').close(); mensaje('mensajeUsuarios', 'Usuario guardado.');
    });
}
async function descargarReporte(formato) {
    const control = $(formato === 'csv' ? 'exportarCsv' : 'exportarPdf'); control.disabled = true;
    try {
        const parametros = new URLSearchParams();
        if ($('fechaDesde').value) parametros.set('desde', $('fechaDesde').value);
        if ($('fechaHasta').value) parametros.set('hasta', $('fechaHasta').value);
        const respuesta = await fetch(baseApi + `reporte/exportar/${formato}?` + parametros, { credentials: 'same-origin', cache: 'no-store' });
        if (!respuesta.ok) {
            if (respuesta.status === 401) location.assign(document.body.dataset.loginUrl);
            const error = await respuesta.json().catch(() => null);
            throw new Error(error?.mensaje || 'No se pudo descargar el reporte. Revisa las fechas.');
        }
        const url = URL.createObjectURL(await respuesta.blob());
        const enlace = document.createElement('a'); enlace.href = url; enlace.download = `Ventas_Giovanna.${formato}`;
        document.body.append(enlace); enlace.click(); enlace.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
        mensaje('mensajeReportes', 'Reporte descargado con las fechas seleccionadas.');
    } catch (e) { mensaje('mensajeReportes', e.message, true); }
    finally { control.disabled = false; }
}
iniciar();
