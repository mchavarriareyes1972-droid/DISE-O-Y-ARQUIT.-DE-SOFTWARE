const { chromium } = require('playwright');
const assert = require('node:assert/strict');

(async () => {
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errores = []; page.on('pageerror', e => errores.push(e.message));
    const base = 'http://127.0.0.1:5099';
    async function login(p, usuario, password) {
        await p.goto(base + '/Cuenta/Login'); await p.locator('#login').fill(usuario); await p.locator('#password').fill(password);
        await p.getByRole('button', { name: 'Ingresar', exact: true }).click(); await p.waitForURL(base + '/');
    }
    const esperarMensaje = (id, texto) => page.waitForFunction(({ id, texto }) => document.getElementById(id).textContent.includes(texto), { id, texto });
    try {
        await login(page, 'admin', 'Giovanna2026!');
        await page.goto(base + '/Home/Clientes');
        await page.locator('#tablaClientes tr').first().waitFor();
        await page.locator('#nuevoCliente').click(); await page.locator('#documentoCliente').fill('44444444');
        await page.locator('#nombreCliente').fill('Cliente de pantalla'); await page.locator('#telefonoCliente').fill('999555666');
        await page.locator('#formCliente button[type=submit]').click(); await page.locator('#dialogoCliente').waitFor({ state: 'hidden' });
        assert((await page.locator('#tablaClientes').innerText()).includes('Cliente de pantalla'));

        await page.goto(base + '/Home/Proveedores'); await page.locator('#tablaProveedores tr').first().waitFor();
        await page.locator('#nuevoProveedor').click(); await page.locator('#rucProveedor').fill('20777777777');
        await page.locator('#razonProveedor').fill('Distribuidor de pantalla');
        await page.locator('#formProveedor button[type=submit]').click(); await page.locator('#dialogoProveedor').waitFor({ state: 'hidden' });
        assert((await page.locator('#tablaProveedores').innerText()).includes('Distribuidor de pantalla'));

        await page.goto(base + '/Home/Inventario'); await page.locator('#productoMinimo option').first().waitFor({ state: 'attached' });
        for (const [codigo, nombre, precio] of [['R901', 'Producto compra A', '4.50'], ['R902', 'Producto compra B', '3.00']]) {
            await page.locator('#nuevoProducto').click(); await page.locator('#codigoProducto').fill(codigo);
            await page.locator('#nombreProducto').fill(nombre); await page.locator('#precioProducto').fill(precio);
            await page.locator('#formProducto button[type=submit]').click(); await page.locator('#dialogoProducto').waitFor({ state: 'hidden' });
        }
        await page.goto(base + '/Home/Compras');
        const proveedor = page.locator('#proveedorCompra option').filter({ hasText: 'Distribuidor de pantalla' });
        await proveedor.waitFor({ state: 'attached' }); await page.locator('#proveedorCompra').selectOption(await proveedor.getAttribute('value'));
        for (const [codigo, costo] of [['R901', '2.00'], ['R902', '1.00']]) {
            await page.locator('#buscarCompra').fill(codigo);
            const opcion = page.locator('#productoCompra option').filter({ hasText: codigo });
            await opcion.waitFor({ state: 'attached' }); await page.locator('#productoCompra').selectOption(await opcion.getAttribute('value'));
            await page.locator('#cantidadCompra').fill('2'); await page.locator('#costoCompra').fill(costo);
            await page.locator('#formLineaCompra button[type=submit]').click();
        }
        assert.equal(await page.locator('#detalleCompra tr').count(), 2);
        await page.locator('#formCompra button[type=submit]').click(); await esperarMensaje('mensajeCompra', 'registrada');
        await page.locator('#tablaCompras tr').first().getByRole('button', { name: 'Ver detalle' }).click();
        await page.locator('#dialogoDetalle').waitFor();
        assert((await page.locator('#contenidoDetalle').innerText()).includes('Producto compra A'));
        assert((await page.locator('#contenidoDetalle').innerText()).includes('Producto compra B'));

        await page.goto(base + '/Home/Ventas'); await page.locator('#listaProductos .product-row').first().waitFor();
        await page.locator('#buscarProducto').fill('R901');
        await page.locator('#listaProductos .product-row').filter({ hasText: 'R901' }).getByRole('button', { name: 'Agregar', exact: true }).click();
        await page.locator('#formaPago').selectOption('Fiado');
        const cliente = page.locator('#clienteFiado option').filter({ hasText: 'Cliente de pantalla' });
        await cliente.waitFor({ state: 'attached' }); await page.locator('#clienteFiado').selectOption(await cliente.getAttribute('value'));
        await page.getByRole('button', { name: 'Finalizar venta' }).click(); await esperarMensaje('mensajeVenta', 'registrada');
        await page.goto(base + '/Home/Clientes');
        const fila = page.locator('#tablaClientes tr').filter({ hasText: '44444444' });
        await fila.getByRole('button', { name: 'Abonar' }).click(); await page.locator('#montoPago').fill('1.50');
        await page.locator('#formPago button[type=submit]').click(); await esperarMensaje('mensajeClientes', 'registrado');
        assert.equal((await fila.locator('td').nth(4).innerText()).replace(/\s/g, ''), 'S/3.00');
        await fila.getByRole('button', { name: 'Editar', exact: true }).click(); await page.locator('#nombreCliente').fill('Cliente actualizado en pantalla');
        await page.locator('#formCliente button[type=submit]').click(); await page.locator('#dialogoCliente').waitFor({ state: 'hidden' });
        assert((await fila.innerText()).includes('Cliente actualizado'));

        await page.goto(base + '/Home/Reportes'); await page.locator('#tablaVentas tr').first().waitFor();
        for (const formato of ['Csv', 'Pdf']) {
            const pendiente = page.waitForEvent('download'); await page.locator('#exportar' + formato).click();
            const descarga = await pendiente; assert(descarga.suggestedFilename().toLowerCase().endsWith('.' + formato.toLowerCase()));
        }
        await page.goto(base + '/Home/Usuarios'); await page.locator('#tablaUsuarios tr').first().waitFor();
        await page.locator('#nuevoUsuario').click(); await page.locator('#loginUsuario').fill('caja_pantalla');
        await page.locator('#nombreUsuario').fill('Caja de pantalla'); await page.locator('#passwordUsuario').fill('Pantalla2026!');
        await page.locator('#formUsuario button[type=submit]').click(); await page.locator('#dialogoUsuario').waitFor({ state: 'hidden' });
        assert((await page.locator('#tablaUsuarios').innerText()).includes('caja_pantalla'));

        const cajero = await browser.newPage(); cajero.on('pageerror', e => errores.push(e.message));
        await login(cajero, 'caja_pantalla', 'Pantalla2026!');
        assert.equal(await cajero.locator('.sidebar a').filter({ hasText: /^Compras$/ }).count(), 0);
        await cajero.goto(base + '/Home/Inventario'); await cajero.locator('#tablaInventario tr').first().waitFor();
        assert.equal(await cajero.locator('#nuevoProducto').count(), 0);
        assert.equal(await cajero.getByRole('button', { name: 'Editar', exact: true }).count(), 0);
        const prohibido = await cajero.request.post(base + '/api/tienda/productos', { data: {} }); assert.equal(prohibido.status(), 403);
        await cajero.goto(base + '/Home/Compras'); assert((await cajero.locator('main').innerText()).includes('Acceso restringido'));
        await cajero.getByRole('button', { name: 'Cerrar sesión' }).click(); await cajero.waitForURL(/Cuenta\/Login/);
        assert.deepEqual(errores, []);
        console.log('OK: clientes, proveedores, compra múltiple, fiados, abonos, descargas, usuarios y permisos verificados en Chromium.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
