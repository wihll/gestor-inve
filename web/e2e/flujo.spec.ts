import { expect, test, type Locator, type Page } from "@playwright/test";

/// Localiza una tarjeta por su título y devuelve **solo** su interior.
///
/// No es cosmética. Catálogo e Inventario tienen campos con el mismo rótulo («Código»,
/// «Nombre»), así que un `page.getByLabel("Código")` puede resolverse contra el formulario de
/// la pantalla **anterior** mientras el enrutador aún no la ha desmontado: se rellena el
/// campo equivocado, el formulario correcto queda vacío, `required` impide el envío y la
/// prueba falla más adelante con un «no existe la opción», sin ninguna petición de por medio.
/// Era exactamente el fallo del runner, que aquí no se reproducía porque la transición es
/// instantánea. Acotar por tarjeta hace imposible ese cruce.
function tarjeta(page: Page, titulo: string): Locator {
  return page
    .locator("section.tarjeta")
    .filter({ has: page.getByRole("heading", { name: titulo, exact: true }) });
}

/// Espera a que la pantalla pedida esté montada antes de tocar sus campos.
async function irA(page: Page, nombre: string) {
  await page.getByRole("link", { name: nombre }).click();
  await expect(page.getByRole("heading", { level: 1, name: nombre })).toBeVisible();
}

/// `selectOption` solo acepta texto exacto, y las opciones llevan el nombre del producto
/// detrás del SKU; se busca la opción que lo contiene y se elige por su valor.
async function seleccionarPorTexto(ambito: Locator, etiqueta: string, contiene: string) {
  const select = ambito.getByLabel(etiqueta);
  const opcion = select.locator("option", { hasText: contiene }).first();

  // La opción puede llegar con los datos de una consulta en vuelo: esperarla en vez de leer
  // un `value` que todavía no existe, que daría `selectOption(null)`.
  await expect(opcion).toBeAttached();
  await select.selectOption(await opcion.getAttribute("value"));
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

async function registrarYEntrar(page: Page) {
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
  await irA(page, "Catálogo");

  const nuevaUnidad = tarjeta(page, "Nueva unidad de medida");
  const nuevoProducto = tarjeta(page, "Nuevo producto");

  await nuevaUnidad.getByRole("button", { name: "Crear unidad" }).click();
  await expect(nuevoProducto.getByRole("option", { name: /UND/ })).toBeAttached();

  const sku = `MART-${Math.random().toString(36).slice(2, 7).toUpperCase()}`;
  await nuevoProducto.getByLabel("SKU").fill(sku);
  await nuevoProducto.getByLabel("Nombre").fill("Martillo de uña");
  await seleccionarPorTexto(nuevoProducto, "Unidad de medida", "UND");
  await nuevoProducto.getByRole("button", { name: "Crear producto" }).click();

  // El producto aparece con su variante única, que hereda el SKU.
  const fila = tarjeta(page, "Productos").getByRole("row").filter({ hasText: sku });
  await expect(fila).toBeVisible();
  await expect(fila.locator('[data-prueba="variantes"]')).toHaveText(sku);

  // --- Inventario: bodega y entrada
  await irA(page, "Inventario");

  const nuevaBodega = tarjeta(page, "Nueva bodega");
  const movimiento = tarjeta(page, "Registrar movimiento");

  await nuevaBodega.getByLabel("Código").fill("PRI");
  await nuevaBodega.getByLabel("Nombre").fill("Principal");
  await nuevaBodega.getByRole("button", { name: "Crear bodega" }).click();
  await expect(movimiento.getByRole("option", { name: /PRI/ })).toBeAttached();

  await seleccionarPorTexto(movimiento, "Variante", sku);
  await seleccionarPorTexto(movimiento, "Bodega", "PRI");
  await movimiento.getByLabel("Cantidad").fill("10");
  await movimiento.getByLabel("Costo unitario").fill("1000");
  await movimiento.getByRole("button", { name: "Registrar" }).click();

  await expect(page.getByRole("alert")).toContainText("Movimiento registrado");

  // Las existencias muestran 10 unidades con su valorización.
  const existencia = tarjeta(page, "Existencias").getByRole("row").filter({ hasText: sku });
  await expect(existencia.locator('[data-prueba="cantidad"]')).toHaveText("10 UND");

  // --- Una salida deja 7
  await movimiento.getByLabel("Tipo").selectOption("Salida");
  await seleccionarPorTexto(movimiento, "Variante", sku);
  await seleccionarPorTexto(movimiento, "Bodega", "PRI");
  await movimiento.getByLabel("Cantidad").fill("3");
  await movimiento.getByRole("button", { name: "Registrar" }).click();

  await expect(existencia.locator('[data-prueba="cantidad"]')).toHaveText("7 UND");

  // --- Kardex: el acumulado del libro tiene que cuadrar con el saldo
  await irA(page, "Kardex");
  await seleccionarPorTexto(page.locator("main"), "Variante", sku);

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
