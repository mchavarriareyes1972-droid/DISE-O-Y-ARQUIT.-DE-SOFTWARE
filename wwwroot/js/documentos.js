// Documentos construidos con los datos confirmados por el servidor.
function mostrarDocumento(titulo, contenido, descargar) {
    let dialogo = $('dialogoDocumento');
    if (!dialogo) {
        dialogo = document.createElement('dialog'); dialogo.id = 'dialogoDocumento';
        dialogo.setAttribute('aria-labelledby', 'tituloDocumento'); document.body.append(dialogo);
        dialogo.addEventListener('close', () => document.body.classList.remove('imprimiendo-documento'));
    }
    dialogo.replaceChildren();
    const encabezado = elemento('h2', titulo); encabezado.id = 'tituloDocumento';
    const acciones = elemento('div', '', 'button-row acciones-documento');
    acciones.append(boton('Imprimir / Guardar PDF', () => {
        document.body.classList.add('imprimiendo-documento');
        window.print();
        document.body.classList.remove('imprimiendo-documento');
    }));
    if (descargar) acciones.append(boton('Descargar CSV', descargar));
    acciones.append(boton('Cerrar', () => dialogo.close()));
    dialogo.append(encabezado, contenido, acciones);
    if (!dialogo.open) dialogo.showModal();
}
function mostrarTicket(venta) {
    const ticket = elemento('div', '', 'ticket');
    ticket.append(elemento('h2', 'BODEGA GIOVANNA'), elemento('h3', 'Ticket de venta N.º ' + venta.id),
        elemento('p', fecha(venta.fecha)), elemento('p', 'Atendido por: ' + (venta.usuarioNombre || 'Usuario')),
        elemento('p', 'Cliente: ' + (venta.clienteNombre || 'Público general')));
    for (const d of venta.detalles || []) {
        const linea = elemento('div', '', 'linea-ticket');
        linea.append(elemento('strong', d.nombre), elemento('div', `${d.cantidad} × ${dinero(d.precioUnitario)} = ${dinero(d.subtotal)}`));
        ticket.append(linea);
    }
    ticket.append(elemento('h3', 'TOTAL: ' + dinero(venta.total)), elemento('p', 'Forma de pago: ' + venta.formaPago));
    if (venta.formaPago === 'Efectivo') ticket.append(elemento('p', 'Recibido: ' + dinero(venta.montoRecibido)), elemento('p', 'Vuelto: ' + dinero(venta.vuelto)));
    if (venta.formaPago === 'Fiado') ticket.append(elemento('p', 'Importe registrado a la deuda del cliente.'));
    if (venta.referenciaPago) ticket.append(elemento('p', 'Referencia: ' + venta.referenciaPago));
    ticket.append(elemento('p', '¡Gracias por su compra!'), elemento('small', 'Resumen de venta · No es un comprobante tributario.'));
    mostrarDocumento('Resumen de venta', ticket);
}
function descargarInventarioCsv(productos) {
    const celda = valor => '"' + String(valor ?? '').replace(/^[=+@\-\t\r]/, "'$&").replaceAll('"', '""') + '"';
    const filas = [['Código','Producto','Precio de venta','Stock','Stock mínimo','Estado','Alerta'],
        ...productos.map(p => [p.codigo,p.nombre,p.precio,p.stock,p.stockMinimo,p.activo ? 'Activo' : 'Inactivo', p.stock <= p.stockMinimo ? 'Stock bajo' : 'Normal'])];
    const url = URL.createObjectURL(new Blob(['\uFEFF' + filas.map(f => f.map(celda).join(';')).join('\r\n')], {type:'text/csv;charset=utf-8;'}));
    const a = document.createElement('a'); a.href = url; a.download = 'inventario.csv'; a.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
}
$('verReporteInventario')?.addEventListener('click', async () => {
    const botonReporte = $('verReporteInventario'); botonReporte.disabled = true;
    try {
        const busqueda = $('buscarInventario').value;
        const incluirInactivos = $('mostrarInactivos')?.checked === true;
        const productos = await api('productos?' + new URLSearchParams({busqueda, incluirInactivos}));
        const contenido = elemento('div');
        contenido.append(elemento('p', 'Bodega Giovanna · Generado: ' + fecha(new Date().toISOString())),
            elemento('p', `Búsqueda: ${busqueda || 'Todos'} · ${incluirInactivos ? 'Incluye inactivos' : 'Solo activos'}`),
            elemento('p', `${productos.length} productos · ${productos.reduce((n,p) => n + p.stock, 0)} unidades · ${productos.filter(p => p.activo && p.stock <= p.stockMinimo).length} productos activos con stock bajo`));
        const envoltura = elemento('div', '', 'table-wrap'); const tablaReporte = document.createElement('table');
        const cabecera = document.createElement('thead'); const fila = document.createElement('tr');
        ['Código','Producto','Precio','Stock','Mínimo','Estado','Alerta'].forEach(t => fila.append(elemento('th',t)));
        cabecera.append(fila); const cuerpo = document.createElement('tbody');
        productos.forEach(p => { const f = document.createElement('tr');
            [p.codigo,p.nombre,dinero(p.precio),p.stock,p.stockMinimo,p.activo ? 'Activo' : 'Inactivo',p.stock <= p.stockMinimo ? 'Stock bajo' : 'Normal'].forEach(v => f.append(elemento('td',v))); cuerpo.append(f); });
        tablaReporte.append(cabecera,cuerpo); envoltura.append(tablaReporte); contenido.append(envoltura);
        if (!productos.length) contenido.append(elemento('p','No hay productos para estos filtros.'));
        mostrarDocumento('Reporte de inventario',contenido,() => descargarInventarioCsv(productos));
    } catch(e) { mensaje('mensajeInventario',e.message,true); }
    finally { botonReporte.disabled = false; }
});
