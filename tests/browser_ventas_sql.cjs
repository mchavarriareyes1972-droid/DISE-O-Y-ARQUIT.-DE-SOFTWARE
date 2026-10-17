const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

// Despues de Compras: QA_UI_01 tiene 3 unidades y QA_UI_02 tiene 10.
(async () => {
    const browser = await chromium.launch({ headless: true });
    const base = 'http://127.0.0.1:5099';
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errores = [];
    page.on('pageerror', e => errores.push(e.message));
    async function texto(id, contenido) {
        await page.waitForFunction(({ id, contenido }) => document.getElementById(id).textContent.includes(contenido), { id, contenido });
    }
    async function agregar(codigo) {
        await page.locator('#buscarProducto').fill(codigo);
        const fila = page.locator('#listaProductos .product-row').filter({ hasText: codigo });
        await fila.getByRole('button', { name: 'Agregar', exact: true }).click();
    }
    async function saldo(id) {
        return (await (await page.request.get(base + '/api/tienda/clientes')).json()).find(c => c.id === id).deuda;
    }
    try {
        await page.goto(base + '/Cuenta/Login');
        await page.locator('#login').fill('cajero'); await page.locator('#password').fill(process.env.GIOVANNA_CAJERO_PASSWORD);
        await page.getByRole('button', { name: 'Ingresar', exact: true }).click(); await page.waitForURL(base + '/');
        await page.goto(base + '/Home/Ventas');
        await page.locator('#clienteFiado option').first().waitFor({ state: 'attached' });
        assert(await page.locator('#formVenta button[type=submit]').isEnabled());
        assert(await page.locator('#formaPago option').filter({ hasText: /^Fiado$/ }).isEnabled());
        // Efectivo multiple: 7.25 + 6.50 = 13.75, vuelto 6.25.
        await agregar('QA_UI_01'); await agregar('QA_UI_02');
        await page.locator('#montoRecibido').fill('20');
        await page.locator('#formVenta button[type=submit]').click(); await texto('mensajeVenta', 'registrada.');
        assert((await page.locator('#mensajeVenta').innerText()).replace(/\s/g, '').includes('Vuelto:S/6.25'));
        const efectivo = (await (await page.request.get(base + '/api/tienda/ventas')).json())[0];
        assert.equal(efectivo.total, 13.75); assert.equal(efectivo.usuarioId, 2); assert.equal(efectivo.detalles.length, 2);
        await agregar('QA_UI_01'); await page.locator('#formaPago').selectOption('Digital');
        await page.locator('#referenciaPago').fill('UI-YAPE-123');
        await page.locator('#formVenta button[type=submit]').click();
        await page.waitForFunction(id => document.getElementById('mensajeVenta').textContent.includes('registrada.') && !document.getElementById('mensajeVenta').textContent.includes(`N.º ${id} `), efectivo.id);
        const digital = (await (await page.request.get(base + '/api/tienda/ventas')).json())[0];
        assert.equal(digital.referenciaPago, 'UI-YAPE-123'); assert.equal(digital.vuelto, 0);
        await agregar('QA_UI_01'); await page.locator('#formaPago').selectOption('Fiado');
        const clienteId = Number(await page.locator('#clienteFiado').inputValue());
        const antes = await saldo(clienteId);
        await page.locator('#formVenta button[type=submit]').click();
        await page.waitForFunction(id => document.getElementById('mensajeVenta').textContent.includes('registrada.') && !document.getElementById('mensajeVenta').textContent.includes(`N.º ${id} `), digital.id);
        assert.equal(await saldo(clienteId), Math.round((antes + 7.25) * 100) / 100);
        const fiado = (await (await page.request.get(base + '/api/tienda/ventas')).json())[0];
        assert.equal(fiado.formaPago, 'Fiado'); assert.equal(fiado.usuarioId, 2); assert.equal(fiado.total, 7.25);
        await page.locator('#listaProductos .product-row').filter({ hasText: 'QA_UI_01' }).getByRole('button', { name: 'Ver alternativas', exact: true }).waitFor();
        await page.goto(base + '/Home/Clientes');
        const cliente = page.locator('#tablaClientes tr').filter({ hasText: fiado.clienteNombre });
        await cliente.waitFor(); assert((await cliente.innerText()).replace(/\s/g, '').includes(new Intl.NumberFormat('es-PE', { style: 'currency', currency: 'PEN' }).format(await saldo(clienteId)).replace(/\s/g, '')));
        assert(await page.locator('#formPago button[type=submit]').isDisabled());
        await page.goto(base + '/Home/Reportes'); await texto('reporteCantidad', '937');
        const ultima = page.locator('#tablaVentas tr').first();
        await ultima.getByRole('button', { name: 'Ver detalle', exact: true }).click();
        await page.locator('#dialogoDetalle').waitFor();
        const detalle = await page.locator('#contenidoDetalle').innerText();
        assert(detalle.includes('QA_UI_01') && detalle.includes(fiado.clienteNombre) && detalle.includes('Registrada por:'));
        await page.locator('#cerrarDetalle').click(); await page.reload(); await texto('reporteCantidad', '937');
        assert((await page.locator('#tablaVentas tr').first().innerText()).includes(fiado.clienteNombre));
        fs.mkdirSync('/tmp/giovanna-qa', { recursive: true });
        await page.screenshot({ path: '/tmp/giovanna-qa/ventas-sql.png', fullPage: false });
        assert.deepEqual(errores, []);
        console.log('OK: ventas SQL en Chromium: cajero, efectivo multiple, digital, fiado, agotamiento, deuda, historial, detalle y recarga.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
