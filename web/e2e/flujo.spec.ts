import { expect, test } from "@playwright/test";

/// `selectOption` solo acepta texto exacto, y las opciones llevan el nombre del producto
/// detrás del SKU; se busca la opción que lo contiene y se elige por su valor.
async function seleccionarPorTexto(
  page: import("@playwright/test").Page,
  etiqueta: string,
  contiene: string,
) {
  const select = page.getByLabel(etiqueta);
  const valor = await select
    .locator("option", { hasText: contiene })
    .first()
    .getAttribute("value");

  await select.selectOption(valor);
}

/// Datos únicos por ejecución: la base es compartida y el NIT es único por empresa.
function datosNuevos() {
  const sufijo = Math.random().toString(36).slice(2, 10);

  return {
    nit: `NITE2E${sufijo}`.slice(0, 15),
    razonSocial: "Pyme de Pruebas S.A.S.",
    nombreAdmin: "Administradora",
    correo: `admin-${sufijo}@empresa.co`,
    contrasena: "Clave-Muy-Larga-2026",
  };
}

async function registrarYEntrar(page: import("@playwright/test").Page) {
  const datos = datosNuevos();

  await page.goto("/alta");
  await page.getByLabel("NIT").fill(datos.nit);
  await page.getByLabel("Razón social").fill(datos.razonSocial);
  await page.getByLabel("Nombre del administrador").fill(datos.nombreAdmin);
  await page.getByLabel("Correo del administrador").fill(datos.correo);
  await page.getByLabel(/Contraseña/).fill(datos.contrasena);
  await page.getByRole("button", { name: "Registrar" }).click();

  await expect(page).toHaveURL(/\/entrar/);

  await page.getByLabel("NIT de la empresa").fill(datos.nit);
  await page.getByLabel("Correo").fill(datos.correo);
  await page.getByLabel("Contraseña").fill(datos.contrasena);
  await page.getByRole("button", { name: "Entrar" }).click();

  await expect(page.getByRole("button", { name: new RegExp(datos.correo) })).toBeVisible();

  return datos;
}

test("una pyme se registra, entra, carga su catálogo y mueve inventario", async ({ page }) => {
  await registrarYEntrar(page);

  // --- Catálogo: unidad y producto
  await page.getByRole("link", { name: "Catálogo" }).click();

  await page.getByRole("button", { name: "Crear unidad" }).click();
  await expect(page.getByRole("option", { name: /UND/ })).toBeAttached();

  const sku = `MART-${Math.random().toString(36).slice(2, 7).toUpperCase()}`;
  const nuevoProducto = page.getByRole("form").filter({ hasText: "Nuevo producto" });
  await page.getByLabel("SKU").fill(sku);
  await page.getByLabel("Nombre").last().fill("Martillo de uña");
  await seleccionarPorTexto(page, "Unidad de medida", "UND");
  await page.getByRole("button", { name: "Crear producto" }).click();

  // El producto aparece con su variante única, que hereda el SKU.
  const fila = page.getByRole("row").filter({ hasText: sku });
  await expect(fila).toBeVisible();
  await expect(fila.locator('[data-prueba="variantes"]')).toHaveText(sku);
  expect(nuevoProducto).toBeTruthy();

  // --- Inventario: bodega y entrada
  await page.getByRole("link", { name: "Inventario" }).click();

  await page.getByLabel("Código").fill("PRI");
  await page.getByLabel("Nombre").first().fill("Principal");
  await page.getByRole("button", { name: "Crear bodega" }).click();
  await expect(page.getByRole("option", { name: /PRI/ })).toBeAttached();

  await seleccionarPorTexto(page, "Variante", sku);
  await seleccionarPorTexto(page, "Bodega", "PRI");
  await page.getByLabel("Cantidad").fill("10");
  await page.getByLabel("Costo unitario").fill("1000");
  await page.getByRole("button", { name: "Registrar" }).click();

  await expect(page.getByRole("alert")).toContainText("Movimiento registrado");

  // Las existencias muestran 10 unidades con su valorización.
  const existencia = page.getByRole("row").filter({ hasText: sku });
  await expect(existencia.locator('[data-prueba="cantidad"]')).toHaveText("10 UND");

  // --- Una salida deja 7
  await page.getByLabel("Tipo").selectOption("Salida");
  await seleccionarPorTexto(page, "Variante", sku);
  await seleccionarPorTexto(page, "Bodega", "PRI");
  await page.getByLabel("Cantidad").fill("3");
  await page.getByRole("button", { name: "Registrar" }).click();

  await expect(existencia.locator('[data-prueba="cantidad"]')).toHaveText("7 UND");

  // --- Kardex: el acumulado del libro tiene que cuadrar con el saldo
  await page.getByRole("link", { name: "Kardex" }).click();
  await seleccionarPorTexto(page, "Variante", sku);

  await expect(page.locator('[data-prueba="acumulado"]')).toContainText("7 UND");
  await expect(page.getByRole("row").filter({ hasText: "Entrada" })).toBeVisible();
  await expect(page.getByRole("row").filter({ hasText: "Salida" })).toBeVisible();

  // Si el acumulado no cuadrara, la pantalla lo grita; aquí no debe haber aviso.
  await expect(page.getByRole("alert")).toHaveCount(0);
});

test("sin sesión cualquier ruta lleva a entrar", async ({ page }) => {
  await page.goto("/kardex");

  await expect(page).toHaveURL(/\/entrar/);
  await expect(page.getByRole("button", { name: "Entrar" })).toBeVisible();
});

test("una contraseña equivocada muestra el mensaje del servidor y no entra", async ({ page }) => {
  const datos = await registrarYEntrar(page);

  await page.getByRole("button", { name: new RegExp(datos.correo) }).click();
  await expect(page.getByRole("button", { name: "Entrar" })).toBeVisible();

  await page.getByLabel("NIT de la empresa").fill(datos.nit);
  await page.getByLabel("Correo").fill(datos.correo);
  await page.getByLabel("Contraseña").fill("Clave-Equivocada-2026");
  await page.getByRole("button", { name: "Entrar" }).click();

  await expect(page.getByRole("alert")).toContainText("Credenciales inválidas");
  await expect(page).toHaveURL(/\/entrar/);
});

test("la sesión sobrevive a recargar la página", async ({ page }) => {
  const datos = await registrarYEntrar(page);

  await page.reload();

  // El token de acceso vive en memoria y se pierde al recargar; el de refresco lo recupera.
  await expect(page.getByRole("button", { name: new RegExp(datos.correo) })).toBeVisible();
});
