const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

// Ejecutar despues de las pruebas HTTP y de productos, solo contra SQL temporal.
(async () => {
    const browser = await chromium.launch({ headless: true });
    const base = 'http://127.0.0.1:5099';
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errores = [];
    page.on('pageerror', error => errores.push(error.message));
    async function entrar(p, usuario, clave) {
        await p.goto(base + '/Cuenta/Login');
        await p.locator('#login').fill(usuario); await p.locator('#password').fill(clave);
        await p.getByRole('button', { name: 'Ingresar', exact: true }).click(); await p.waitForURL(base + '/');
    }
    async function texto(id, contenido) {
        await page.waitForFunction(({ id, contenido }) => document.getElementById(id).textContent.includes(contenido), { id, contenido });
    }
    try {
        await entrar(page, 'admin', process.env.GIOVANNA_ADMIN_PASSWORD);
        await page.goto(base + '/Home/Compras');
        // Las pruebas HTTP previas agregaron una compra persistida tras reiniciar.
        await texto('cantidadCompras', 'Compras registradas: 61');
        assert.equal(await page.locator('#tablaCompras tr').count(), 61);
        assert(await page.locator('#formCompra button[type=submit]').isDisabled());
        const proveedor = page.locator('#proveedorCompra option').filter({ hasText: '20990000021' });
        await proveedor.waitFor({ state: 'attached' });
        await page.locator('#proveedorCompra').selectOption(await proveedor.getAttribute('value'));
        for (const [codigo, cantidad, costo] of [['QA_UI_01', '3', '2.50'], ['QA_UI_02', '2', '1.25']]) {
            await page.locator('#buscarCompra').fill(codigo);
            const opcion = page.locator('#productoCompra option').filter({ hasText: codigo });
            await opcion.waitFor({ state: 'attached' });
            await page.locator('#productoCompra').selectOption(await opcion.getAttribute('value'));
            await page.locator('#cantidadCompra').fill(cantidad); await page.locator('#costoCompra').fill(costo);
            await page.locator('#formLineaCompra button[type=submit]').click();
        }
        assert.equal(await page.locator('#detalleCompra tr').count(), 2);
        assert.equal((await page.locator('#totalCompra').innerText()).replace(/\s/g, ''), 'S/10.00');
        assert(await page.locator('#formCompra button[type=submit]').isEnabled());
        await page.locator('#formCompra button[type=submit]').click();
        await texto('mensajeCompra', 'El stock fue actualizado.');
        await texto('cantidadCompras', 'Compras registradas: 62');
        const compras = await (await page.request.get(base + '/api/tienda/compras')).json();
        const compra = compras[0];
        assert.equal(compra.total, 10); assert.equal(compra.usuarioId, 1); assert.equal(compra.detalles.length, 2);
        const ultimaFila = page.locator('#tablaCompras tr').first();
        assert((await ultimaFila.innerText()).includes('Proveedor SQL actualizado en pantalla'));
        assert((await ultimaFila.locator('td').nth(3).innerText()).length > 0);
        await ultimaFila.getByRole('button', { name: 'Ver detalle', exact: true }).click();
        await page.locator('#dialogoDetalle').waitFor();
        const detalle = await page.locator('#contenidoDetalle').innerText();
        assert(detalle.includes('QA_UI_01') && detalle.includes('QA_UI_02') && detalle.includes('Usuario:'));
        await page.locator('#cerrarDetalle').click();
        await page.reload(); await texto('cantidadCompras', 'Compras registradas: 62');
        assert((await page.locator('#tablaCompras tr').first().innerText()).includes('Proveedor SQL actualizado en pantalla'));

        await page.goto(base + '/Home/Inventario');
        await texto('cantidadProductos', 'Productos mostrados:');
        for (const [codigo, stock] of [['QA_UI_01', '3'], ['QA_UI_02', '10']]) {
            await page.locator('#buscarInventario').fill(codigo);
            await texto('cantidadProductos', 'Productos mostrados: 1 ·');
            const fila = page.locator('#tablaInventario tr').filter({ hasText: codigo });
            await fila.waitFor();
            assert.equal(await fila.locator('td').nth(3).innerText(), stock);
        }
        await page.goto(base + '/Home/Compras'); await texto('cantidadCompras', 'Compras registradas: 62');
        fs.mkdirSync('/tmp/giovanna-qa', { recursive: true });
        await page.screenshot({ path: '/tmp/giovanna-qa/compras-sql.png', fullPage: true });
        const caja = await browser.newPage(); caja.on('pageerror', error => errores.push(error.message));
        await entrar(caja, 'cajero', process.env.GIOVANNA_CAJERO_PASSWORD);
        assert.equal(await caja.getByRole('link', { name: 'Compras', exact: true }).count(), 0);
        await caja.goto(base + '/Home/Compras'); assert(caja.url().includes('/Cuenta/AccesoDenegado'));
        assert.deepEqual(errores, []);
        console.log('OK: compras SQL en Chromium: historial, compra multiple, usuario, detalle, recarga, stock actualizado y permisos.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
