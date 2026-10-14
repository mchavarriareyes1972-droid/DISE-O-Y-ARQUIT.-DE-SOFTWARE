// Prueba del flujo de productos en las pantallas reales, usando Chromium en CI.
const { chromium } = require('playwright');
const assert = require('node:assert/strict');

(async () => {
    const browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errores = [];
    page.on('pageerror', error => errores.push(error.message));
    const base = 'http://127.0.0.1:5099';
    const fila = () => page.locator('#tablaInventario tr').filter({ hasText: 'Q901' });
    const abrirInventario = async () => {
        await page.goto(base + '/Home/Inventario');
        await page.locator('#productoMinimo option').first().waitFor();
    };
    try {
        await abrirInventario();
        await page.locator('#nuevoProducto').click();
        await page.locator('#codigoProducto').fill('P001');
        await page.locator('#nombreProducto').fill('Atún de prueba');
        await page.locator('#precioProducto').fill('6.50');
        await page.locator('#stockInicialProducto').fill('20');
        await page.locator('#formProducto button[type=submit]').click();
        await page.waitForFunction(() => document.getElementById('mensajeProducto').textContent.includes('código'));
        assert(await page.locator('#dialogoProducto').isVisible(), 'Un duplicado mantiene abierto el formulario.');
        await page.locator('#codigoProducto').fill('Q901');
        await page.locator('#formProducto button[type=submit]').click();
        await page.locator('#dialogoProducto').waitFor({ state: 'hidden' });
        await fila().waitFor();
        assert((await fila().innerText()).includes('Atún de prueba'));

        await page.goto(base + '/Home/Ventas');
        await page.locator('#listaProductos .product-row').first().waitFor();
        await page.locator('#buscarProducto').fill('Q901');
        const producto = page.locator('#listaProductos .product-row').filter({ hasText: 'Q901' });
        await producto.getByRole('button', { name: 'Agregar', exact: true }).click();
        await producto.getByRole('button', { name: 'Agregar', exact: true }).click();
        await page.locator('#montoRecibido').fill('20');
        await page.getByRole('button', { name: 'Finalizar venta' }).click();
        await page.waitForFunction(() => document.getElementById('mensajeVenta').textContent.includes('registrada'));

        await abrirInventario();
        await fila().getByRole('button', { name: 'Editar', exact: true }).click();
        assert(await page.locator('#stockInicialProducto').isDisabled());
        assert(!(await page.locator('#campoStockInicial').isVisible()));
        await page.locator('#nombreProducto').fill('Atún actualizado');
        await page.locator('#precioProducto').fill('7.00');
        await page.locator('#formProducto button[type=submit]').click();
        await page.locator('#dialogoProducto').waitFor({ state: 'hidden' });
        assert.equal(await fila().locator('td').nth(3).innerText(), '18');
        assert((await fila().innerText()).includes('Atún actualizado'));

        await page.goto(base + '/Home/Reportes');
        await page.locator('#tablaVentas tr').first().getByRole('button', { name: 'Ver detalle' }).click();
        await page.locator('#dialogoDetalle').waitFor();
        const detalle = await page.locator('#contenidoDetalle').innerText();
        assert(detalle.includes('Atún de prueba') && detalle.includes('6.50'), 'El detalle conserva nombre y precio anteriores.');

        await abrirInventario();
        await fila().getByRole('button', { name: 'Desactivar', exact: true }).click();
        await fila().waitFor({ state: 'hidden' });
        await page.locator('#mostrarInactivos').check();
        await fila().getByRole('button', { name: 'Activar', exact: true }).waitFor();
        assert((await fila().innerText()).includes('Inactivo'));
        await page.goto(base + '/Home/Compras');
        await page.locator('#productoCompra option').first().waitFor();
        assert(!(await page.locator('#productoCompra').innerText()).includes('Q901'));
        await abrirInventario();
        await page.locator('#mostrarInactivos').check();
        await fila().getByRole('button', { name: 'Activar', exact: true }).click();
        await fila().getByRole('button', { name: 'Desactivar', exact: true }).waitFor();

        // El inventario debe seguir funcionando aunque todos los productos estén inactivos.
        const token = await page.locator('[name=__RequestVerificationToken]').inputValue();
        const activos = await (await page.request.get(base + '/api/tienda/productos')).json();
        for (const p of activos) {
            const respuesta = await page.request.post(base + `/api/tienda/productos/${p.id}/estado`,
                { data: { activo: false }, headers: { RequestVerificationToken: token } });
            assert(respuesta.ok());
        }
        await page.reload();
        await page.waitForFunction(() => document.querySelector('#formStockMinimo button').disabled);
        assert(await page.locator('#formAlternativas button').isDisabled());
        await page.locator('#nuevoProducto').click();
        await page.locator('#codigoProducto').fill('Q902');
        await page.locator('#nombreProducto').fill('Producto para reabrir catálogo');
        await page.locator('#precioProducto').fill('1.00');
        await page.locator('#formProducto button[type=submit]').click();
        await page.locator('#dialogoProducto').waitFor({ state: 'hidden' });
        assert(!(await page.locator('#formStockMinimo button').isDisabled()));
        assert.deepEqual(errores, [], 'No deben ocurrir errores de JavaScript.');
        console.log('OK: flujo de productos verificado en Chromium (registro, duplicados, venta, edición, historial, estado y catálogo vacío).');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
