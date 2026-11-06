const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

// Recorrido final, despues de catalogos, compras y ventas de la base temporal.
(async () => {
    const browser = await chromium.launch({ headless: true });
    const base = 'http://127.0.0.1:5099';
    const admin = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errores = [];
    admin.on('pageerror', e => errores.push(e.message));
    const dinero = n => new Intl.NumberFormat('es-PE', { style: 'currency', currency: 'PEN' }).format(n).replace(/\s/g, '');
    async function entrar(page, login, password) {
        await page.goto(base + '/Cuenta/Login');
        await page.locator('#login').fill(login); await page.locator('#password').fill(password);
        await page.getByRole('button', { name: 'Ingresar', exact: true }).click(); await page.waitForURL(base + '/');
    }
    async function texto(page, id, contenido) {
        await page.waitForFunction(({ id, contenido }) => id === 'cantidadPagos' ? document.getElementById(id).textContent === contenido : document.getElementById(id).textContent.includes(contenido), { id, contenido });
    }
    async function abonar(page, monto, metodo) {
        await page.locator('#montoPago').fill(monto); await page.locator('#metodoPago').selectOption(metodo);
        const [respuesta] = await Promise.all([
            page.waitForResponse(r => r.url() === base + '/api/tienda/pagos' && r.request().method() === 'POST'),
            page.locator('#formPago button[type=submit]').click()
        ]);
        assert.equal(respuesta.status(), 200);
        const pago = await respuesta.json();
        await texto(page, 'mensajeClientes', `Pago N.º ${pago.id} registrado.`);
        return pago;
    }
    try {
        await entrar(admin, 'admin', process.env.GIOVANNA_ADMIN_PASSWORD);
        const reporteAntes = await (await admin.request.get(base + '/api/tienda/reporte')).json();
        const clientes = await (await admin.request.get(base + '/api/tienda/clientes?incluirInactivos=true')).json();
        const cliente = clientes.find(c => c.documento === '99000021');
        assert.equal(cliente.deuda, 0);
        // Un fiado nuevo de 13 soles permite comprobar abono parcial y total.
        await admin.goto(base + '/Home/Ventas');
        await admin.locator(`#clienteFiado option[value="${cliente.id}"]`).waitFor({ state: 'attached' });
        await admin.locator('#buscarProducto').fill('QA_UI_02');
        const articulo = admin.locator('#listaProductos .product-row').filter({ hasText: 'QA_UI_02' });
        await articulo.getByRole('button', { name: 'Agregar', exact: true }).click();
        await articulo.getByRole('button', { name: 'Agregar', exact: true }).click();
        await admin.locator('#formaPago').selectOption('Fiado');
        await admin.locator('#clienteFiado').selectOption(String(cliente.id));
        await admin.locator('#formVenta button[type=submit]').click(); await texto(admin, 'mensajeVenta', 'registrada.');
        await admin.goto(base + '/Home/Clientes'); await texto(admin, 'cantidadClientes', 'Clientes registrados: 62');
        await admin.locator('#buscarCliente').fill(cliente.documento);
        const fila = admin.locator('#tablaClientes tr').filter({ hasText: cliente.documento });
        await fila.getByRole('button', { name: 'Abonar', exact: true }).click();
        assert.equal(await admin.locator('#montoPago').getAttribute('max'), '13');
        assert.equal(await admin.locator('#montoPago').inputValue(), '13.00');
        const parcial = await abonar(admin, '5', 'Efectivo');
        assert.equal(parcial.usuarioId, 1); assert.equal(parcial.saldoRestante, 8);
        await texto(admin, 'cantidadPagos', 'Pagos mostrados: 1');
        assert.equal(await admin.locator('#filtrarPagos').inputValue(), String(cliente.id));
        assert.equal((await fila.locator('td').nth(4).innerText()).replace(/\s/g, ''), dinero(8));
        assert.equal(await admin.locator('#montoPago').getAttribute('max'), '8');
        await admin.locator('#montoPago').fill('9');
        assert(await admin.locator('#montoPago').evaluate(el => el.validity.rangeOverflow));
        // El cliente inactivo conserva la posibilidad de pagar su deuda.
        await fila.getByRole('button', { name: 'Desactivar', exact: true }).click(); await texto(admin, 'tablaClientes', 'Inactivo');
        assert(await fila.getByRole('button', { name: 'Abonar', exact: true }).isEnabled());
        await admin.reload(); await texto(admin, 'cantidadPagos', 'Pagos mostrados: 127');
        await admin.locator('#filtrarPagos').selectOption(String(cliente.id)); await texto(admin, 'cantidadPagos', 'Pagos mostrados: 1');
        assert((await admin.locator('#tablaPagos tr').first().innerText()).includes(parcial.usuarioNombre));

        const caja = await browser.newPage({ viewport: { width: 1280, height: 900 } });
        caja.on('pageerror', e => errores.push(e.message));
        await entrar(caja, 'cajero', process.env.GIOVANNA_CAJERO_PASSWORD);
        await caja.goto(base + '/Home/Clientes'); await texto(caja, 'cantidadClientes', 'Clientes registrados: 62');
        await caja.locator('#buscarCliente').fill(cliente.documento);
        const filaCaja = caja.locator('#tablaClientes tr').filter({ hasText: cliente.documento });
        await filaCaja.getByRole('button', { name: 'Abonar', exact: true }).click();
        const cancelacion = await abonar(caja, '8', 'Digital');
        assert.equal(cancelacion.usuarioId, 2); assert.equal(cancelacion.saldoRestante, 0);
        await texto(caja, 'cantidadPagos', 'Pagos mostrados: 2');
        assert.equal((await filaCaja.locator('td').nth(4).innerText()).replace(/\s/g, ''), dinero(0));
        assert(await filaCaja.getByRole('button', { name: 'Abonar', exact: true }).isDisabled());
        assert.equal(await caja.locator(`#clientePago option[value="${cliente.id}"]`).count(), 0);
        await caja.reload(); await texto(caja, 'cantidadPagos', 'Pagos mostrados: 128');
        await caja.locator('#filtrarPagos').selectOption(String(cliente.id)); await texto(caja, 'cantidadPagos', 'Pagos mostrados: 2');
        assert((await caja.locator('#tablaPagos tr').first().innerText()).includes(cancelacion.usuarioNombre));
        const reporteDespues = await (await caja.request.get(base + '/api/tienda/reporte')).json();
        assert.equal(reporteDespues.abonos, Math.round((reporteAntes.abonos + 13) * 100) / 100);
        assert.equal(reporteDespues.totalFiado, Math.round((reporteAntes.totalFiado + 13) * 100) / 100);
        assert.equal(reporteDespues.cobradoEnVentas, reporteAntes.cobradoEnVentas);
        fs.mkdirSync('/tmp/giovanna-qa', { recursive: true });
        await caja.screenshot({ path: '/tmp/giovanna-qa/abonos-sql.png', fullPage: false });
        assert.deepEqual(errores, []);
        console.log('OK: abonos SQL en Chromium: fiado, efectivo parcial, limite de deuda, cliente inactivo, cajero digital, cancelacion, historial, usuarios, recarga y reporte.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exit(1); });
