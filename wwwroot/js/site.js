const datosIniciales = {
    productos: [
        { id: 1, nombre: 'Arroz 1 kg', precio: 4.50, stock: 18 },
        { id: 2, nombre: 'Leche en lata', precio: 4.20, stock: 3 },
        { id: 3, nombre: 'Aceite 900 ml', precio: 9.20, stock: 12 },
        { id: 4, nombre: 'Azúcar 1 kg', precio: 4.00, stock: 16 },
        { id: 5, nombre: 'Galletas', precio: 2.50, stock: 24 },
        { id: 6, nombre: 'Agua 625 ml', precio: 1.50, stock: 20 }
    ],
    clientes: [
        { nombre: 'María López', telefono: '999 111 222', deuda: 12.50 },
        { nombre: 'José Pérez', telefono: '988 333 444', deuda: 8.00 }
    ],
    ventas: [],
    ultimaCompra: ''
};

let datos;

try {
    datos = JSON.parse(sessionStorage.getItem('giovannaDemo')) || structuredClone(datosIniciales);
} catch {
    datos = structuredClone(datosIniciales);
}

let carrito = [];

const $ = id => document.getElementById(id);
const dinero = cantidad => 'S/ ' + cantidad.toFixed(2);

function guardar() {
    sessionStorage.setItem('giovannaDemo', JSON.stringify(datos));
}

function limpiar(elemento) {
    while (elemento.firstChild) {
        elemento.removeChild(elemento.firstChild);
    }
}

function texto(etiqueta, valor, clase) {
    const elemento = document.createElement(etiqueta);
    elemento.textContent = valor;

    if (clase) {
        elemento.className = clase;
    }

    return elemento;
}

function boton(etiqueta, accion, deshabilitado) {
    const elemento = texto('button', etiqueta);
    elemento.type = 'button';
    elemento.disabled = !!deshabilitado;
    elemento.addEventListener('click', accion);

    return elemento;
}

document.querySelectorAll('.sidebar nav a').forEach(enlace => {
    const rutaEnlace = new URL(enlace.href).pathname.toLowerCase();
    const rutaActual = location.pathname.toLowerCase();

    if (rutaEnlace === rutaActual) {
        enlace.classList.add('active');
    }
});

function total() {
    return carrito.reduce((suma, item) => suma + item.precio * item.cantidad, 0);
}

function renderProductos() {
    const zona = $('listaProductos');

    if (!zona) {
        return;
    }

    limpiar(zona);

    const busqueda = $('buscarProducto').value.trim().toLowerCase();
    const productos = datos.productos.filter(producto =>
        producto.nombre.toLowerCase().includes(busqueda)
    );

    productos.forEach(producto => {
        const fila = texto('div', '', 'product-row');
        const informacion = document.createElement('div');
        const descripcion = `${dinero(producto.precio)} · Stock: ${producto.stock}`;

        informacion.append(
            texto('strong', producto.nombre),
            texto('small', descripcion)
        );

        const botonAgregar = boton('Agregar', () => {
            const item = carrito.find(elemento => elemento.id === producto.id);

            if ((item?.cantidad || 0) >= producto.stock) {
                $('mensajeVenta').textContent = 'No hay más unidades disponibles.';
                return;
            }

            if (item) {
                item.cantidad++;
            } else {
                carrito.push({
                    id: producto.id,
                    nombre: producto.nombre,
                    precio: producto.precio,
                    cantidad: 1
                });
            }

            $('mensajeVenta').textContent = '';
            renderCarrito();
        }, producto.stock === 0);

        fila.append(informacion, botonAgregar);
        zona.append(fila);
    });

    if (!zona.children.length) {
        zona.append(texto('p', 'No se encontraron productos.', 'muted'));
    }
}

function renderCarrito() {
    const zona = $('detalleVenta');

    if (!zona) {
        return;
    }

    limpiar(zona);

    if (!carrito.length) {
        zona.append(texto('p', 'Aún no agregas productos.', 'muted'));
    }

    carrito.forEach(item => {
        const fila = texto('div', '', 'product-row');
        const descripcion = `${item.nombre} × ${item.cantidad} · ${dinero(item.precio * item.cantidad)}`;
        const botonQuitar = boton('Quitar', () => {
            carrito = carrito.filter(elemento => elemento.id !== item.id);
            renderCarrito();
        });

        fila.append(texto('span', descripcion), botonQuitar);
        zona.append(fila);
    });

    $('totalVenta').textContent = dinero(total());
    calcularVuelto();
}

function calcularVuelto() {
    if ($('vuelto')) {
        const recibido = Number($('montoRecibido').value) || 0;
        const vuelto = Math.max(0, recibido - total());

        $('vuelto').textContent = 'Vuelto: ' + dinero(vuelto);
    }
}

if ($('listaProductos')) {
    $('buscarProducto').addEventListener('input', renderProductos);
    $('montoRecibido').addEventListener('input', calcularVuelto);

    datos.clientes.forEach((cliente, indice) => {
        const opcion = document.createElement('option');
        opcion.value = indice;
        opcion.textContent = cliente.nombre;
        $('clienteFiado').append(opcion);
    });

    $('formaPago').addEventListener('change', () => {
        const formaPago = $('formaPago').value;
        $('campoEfectivo').hidden = formaPago !== 'Efectivo';
        $('campoFiado').hidden = formaPago !== 'Fiado';
    });

    $('finalizarVenta').addEventListener('click', () => {
        const mensaje = $('mensajeVenta');
        const formaPago = $('formaPago').value;
        const importe = total();
        const montoRecibido = Number($('montoRecibido').value) || 0;

        if (!carrito.length) {
            mensaje.textContent = 'Agrega un producto antes de finalizar.';
            return;
        }

        if (formaPago === 'Efectivo' && montoRecibido < importe) {
            mensaje.textContent = 'El monto recibido no cubre el total.';
            return;
        }

        if (formaPago === 'Fiado') {
            const indiceCliente = Number($('clienteFiado').value);
            datos.clientes[indiceCliente].deuda += importe;
        }

        carrito.forEach(item => {
            const producto = datos.productos.find(elemento => elemento.id === item.id);
            producto.stock -= item.cantidad;
        });

        datos.ventas.push(importe);
        guardar();
        carrito = [];
        $('montoRecibido').value = '';

        renderProductos();
        renderCarrito();
        mensaje.textContent = 'Venta registrada en esta demostración.';
    });

    renderProductos();
    renderCarrito();
}

function renderInventario() {
    const tabla = $('tablaInventario');

    if (!tabla) {
        return;
    }

    limpiar(tabla);

    const busqueda = $('buscarInventario').value.trim().toLowerCase();
    const productos = datos.productos.filter(producto =>
        producto.nombre.toLowerCase().includes(busqueda)
    );

    productos.forEach(producto => {
        const fila = document.createElement('tr');
        const valores = [
            producto.nombre,
            dinero(producto.precio),
            String(producto.stock)
        ];

        valores.forEach(valor => fila.append(texto('td', valor)));

        const celdaEstado = document.createElement('td');
        const stockBajo = producto.stock <= 5;
        const estado = stockBajo ? 'Stock bajo' : 'Disponible';
        const claseEstado = stockBajo ? 'status low' : 'status';

        celdaEstado.append(texto('span', estado, claseEstado));
        fila.append(celdaEstado);
        tabla.append(fila);
    });

    $('cantidadProductos').textContent = datos.productos.length + ' productos';
}

if ($('tablaInventario')) {
    $('buscarInventario').addEventListener('input', renderInventario);
    renderInventario();
}

if ($('productoCompra')) {
    datos.productos.forEach(producto => {
        const opcion = document.createElement('option');
        opcion.value = producto.id;
        opcion.textContent = producto.nombre;
        $('productoCompra').append(opcion);
    });

    if (datos.ultimaCompra) {
        $('ultimaCompra').textContent = datos.ultimaCompra;
    }

    $('guardarCompra').addEventListener('click', () => {
        const proveedor = $('proveedor').value.trim();
        const cantidad = Number($('cantidadCompra').value);

        if (!proveedor || !Number.isInteger(cantidad) || cantidad < 1) {
            $('mensajeCompra').textContent = 'Ingresa proveedor y cantidad entera mayor que cero.';
            return;
        }

        const producto = datos.productos.find(elemento =>
            elemento.id === Number($('productoCompra').value)
        );

        producto.stock += cantidad;
        datos.ultimaCompra = `${proveedor}: ${cantidad} unidad(es) de ${producto.nombre}.`;
        $('ultimaCompra').textContent = datos.ultimaCompra;
        $('mensajeCompra').textContent = 'Compra registrada en esta demostración.';
        guardar();
    });
}

function renderClientes() {
    const tabla = $('tablaClientes');

    if (!tabla) {
        return;
    }

    limpiar(tabla);

    datos.clientes.forEach(cliente => {
        const fila = document.createElement('tr');
        const valores = [
            cliente.nombre,
            cliente.telefono,
            dinero(cliente.deuda)
        ];

        valores.forEach(valor => fila.append(texto('td', valor)));

        const celdaAccion = document.createElement('td');
        const botonAbono = boton('Registrar abono', () => {
            const entrada = prompt(`Abono de ${cliente.nombre} (S/):`);

            if (entrada === null) {
                return;
            }

            const monto = Number(entrada);

            if (!Number.isFinite(monto) || monto <= 0 || monto > cliente.deuda) {
                $('mensajeClientes').textContent = 'Ingresa un abono mayor que cero y no superior al saldo.';
                return;
            }

            cliente.deuda = Math.round((cliente.deuda - monto) * 100) / 100;
            guardar();
            renderClientes();
            $('mensajeClientes').textContent = 'Abono registrado en esta demostración.';
        }, cliente.deuda === 0);

        celdaAccion.append(botonAbono);
        fila.append(celdaAccion);
        tabla.append(fila);
    });
}

if ($('tablaClientes')) {
    renderClientes();
}

if ($('reporteCantidad')) {
    $('reporteCantidad').textContent = datos.ventas.length;
    $('reporteTotal').textContent = dinero(
        datos.ventas.reduce((suma, venta) => suma + venta, 0)
    );
    $('reporteProductos').textContent = datos.productos.length;
    $('reporteStockBajo').textContent = datos.productos.filter(producto =>
        producto.stock <= 5
    ).length;
}
