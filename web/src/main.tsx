import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App } from "./App";
import { ProveedorSesion } from "./sesion/SesionContexto";
import "./estilos.css";

const cola = new QueryClient({
  defaultOptions: {
    queries: {
      // Un 401 lo resuelve el cliente refrescando; reintentar aquí lo duplicaría.
      retry: false,
      refetchOnWindowFocus: false,
    },
  },
});

const raiz = document.getElementById("raiz");

if (raiz === null) {
  throw new Error("No se encontró el elemento raíz.");
}

createRoot(raiz).render(
  <StrictMode>
    <QueryClientProvider client={cola}>
      <BrowserRouter>
        <ProveedorSesion>
          <App />
        </ProveedorSesion>
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
