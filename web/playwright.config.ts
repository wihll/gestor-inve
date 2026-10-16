import { defineConfig } from "@playwright/test";

/// Pruebas de punta a punta en un navegador real contra la **API real** y PostgreSQL.
/// Con una API simulada no se comprobaría lo que de verdad falla: el contrato, el refresco
/// de la sesión y que el kardex de la pantalla cuadre con el del servidor.
export default defineConfig({
  testDir: "./e2e",
  timeout: 60_000,
  expect: { timeout: 10_000 },
  // Sin reintentos: una prueba de interfaz que pasa al segundo intento esconde una carrera.
  retries: 0,
  reporter: [["list"]],
  use: {
    baseURL: process.env.WEB_URL ?? "http://127.0.0.1:4173",
    trace: "retain-on-failure",
    // Chromium ya instalado en la imagen. Se apunta al binario en vez de descargar otro:
    // `playwright install` no está disponible aquí, y en CI se instala con el comando oficial.
    launchOptions: process.env.CHROMIUM_PATH
      ? { executablePath: process.env.CHROMIUM_PATH }
      : {},
  },
});
