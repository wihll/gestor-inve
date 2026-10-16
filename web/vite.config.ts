import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // En desarrollo la API va por el mismo origen, así que no hace falta CORS.
    proxy: {
      "/auth": "http://127.0.0.1:5080",
      "/empresas": "http://127.0.0.1:5080",
      "/catalogo": "http://127.0.0.1:5080",
      "/inventario": "http://127.0.0.1:5080",
      "/usuarios": "http://127.0.0.1:5080",
    },
  },
});
