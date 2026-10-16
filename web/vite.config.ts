import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // En desarrollo la API va por el mismo origen, así que no hace falta CORS.
    // Un único prefijo, `/api`: antes se reenviaba `/catalogo` e `/inventario`, que son
    // también rutas del enrutador del cliente, y recargar la página en una de ellas acababa
    // en la API, que no las tiene. Ver ADR 0008.
    proxy: {
      "/api": "http://127.0.0.1:5080",
    },
  },
});
