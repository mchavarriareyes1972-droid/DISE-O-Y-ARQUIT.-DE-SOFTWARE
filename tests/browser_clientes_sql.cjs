const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

// Ejecutar solo contra la base temporal del flujo verificar-sql.
(async () => {
    const browser = await chromium.launch({ headless: true });
    const base = 'http://127.0.0.1:5099';
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    const errores = [];
    page.on('pageerror', error => errores.push(error.message));
    async function entrar(p, usuario, clave) {
        await p.goto(base + '/Cuenta/Login');
        await p.locator('#login').fill(usuario);
        await p.locator('#password').fill(clave);
        await p.getByRole('button', { name: 'Ingresar', exact: true }).click();
        await p.waitForURL(base + '/');
        await p.goto(base + '/Home/Clientes');
        await p.locator('#tablaClientes tr').first().waitFor();
    }
    async function esperarTexto(id, texto) {
        await page.waitForFunction(({ id, texto }) => document.getElementById(id).textContent.includes(texto), { id, texto });
    }
    try {
        await entrar(page, 'admin', process.env.GIOVANNA_ADMIN_PASSWORD);
        // Las pruebas HTTP previas crearon un cliente y lo reactivaron tras reiniciar.
        await esperarTexto('cantidadClientes', 'Clientes registrados: 61');
        assert.equal(await page.locator('#tablaClientes tr').count(), 61);
        await page.waitForFunction(() => document.querySelectorAll('#tablaPagos tr').length === 124);
        assert(await page.locator('#formPago button[type=submit]').isDisabled());
        assert(await page.locator('#tablaClientes button').filter({ hasText: /^Abonar$/ }).first().isDisabled());

        await page.locator('#nuevoCliente').click();
        await page.locator('#documentoCliente').fill('99000021');
        await page.locator('#nombreCliente').fill("Cliente O'Connor SQL");
        await page.locator('#telefonoCliente').fill('999123123');
        await page.locator('#formCliente button[type=submit]').click();
        await page.locator('#dialogoCliente').waitFor({ state: 'hidden' });
        await esperarTexto('cantidadClientes', 'Clientes registrados: 62');
        await page.locator('#buscarCliente').fill('99000021');
        const fila = page.locator('#tablaClientes tr').filter({ hasText: '99000021' });
        assert((await fila.innerText()).includes("Cliente O'Connor SQL"));
        await fila.getByRole('button', { name: 'Editar', exact: true }).click();
        await page.locator('#nombreCliente').fill('Cliente SQL actualizado en pantalla');
        await page.locator('#formCliente button[type=submit]').click();
        await page.locator('#dialogoCliente').waitFor({ state: 'hidden' });
        await esperarTexto('tablaClientes', 'Cliente SQL actualizado en pantalla');
        await fila.getByRole('button', { name: 'Desactivar', exact: true }).click();
        await esperarTexto('tablaClientes', 'Inactivo');
        await page.reload();
        await esperarTexto('cantidadClientes', 'Clientes registrados: 62');
        await page.locator('#buscarCliente').fill('99000021');
        assert((await fila.innerText()).includes('Cliente SQL actualizado en pantalla'));
        assert((await fila.innerText()).includes('Inactivo'));
        await fila.getByRole('button', { name: 'Activar', exact: true }).click();
        await fila.getByRole('button', { name: 'Desactivar', exact: true }).waitFor();

        await page.locator('#nuevoCliente').click();
        await page.locator('#documentoCliente').fill('99000021');
        await page.locator('#nombreCliente').fill('Duplicado');
        await page.locator('#formCliente button[type=submit]').click();
        await esperarTexto('mensajeClienteFormulario', 'Ya existe un cliente');
        await page.locator('#cancelarCliente').click();
        await page.goto(base + '/Home/Ventas');
        assert(await page.locator('#formaPago option').filter({ hasText: /^Fiado$/ }).isEnabled());

        const caja = await browser.newPage();
        caja.on('pageerror', error => errores.push(error.message));
        await entrar(caja, 'cajero', process.env.GIOVANNA_CAJERO_PASSWORD);
        assert.equal(await caja.locator('#nuevoCliente').count(), 0);
        assert.equal(await caja.getByRole('button', { name: 'Editar', exact: true }).count(), 0);
        await caja.locator('#buscarCliente').fill('99000021');
        assert((await caja.locator('#tablaClientes').innerText()).includes('Cliente SQL actualizado en pantalla'));
        assert(await caja.locator('#formPago button[type=submit]').isDisabled());
        fs.mkdirSync('/tmp/giovanna-qa', { recursive: true });
        await caja.screenshot({ path: '/tmp/giovanna-qa/clientes-sql.png', fullPage: true });
        assert.deepEqual(errores, []);
        console.log('OK: clientes SQL en Chromium: listado, contador, historial, alta, edicion, baja, reactivacion, recarga, duplicados y permisos.');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exit(1); });
