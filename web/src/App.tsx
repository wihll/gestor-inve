import { NavLink, Navigate, Route, Routes } from "react-router-dom";
import { useSesion } from "./sesion/SesionContexto";
import { Entrar } from "./paginas/Entrar";
import { AltaEmpresa } from "./paginas/AltaEmpresa";
import { Catalogo } from "./paginas/Catalogo";
import { Inventario } from "./paginas/Inventario";
import { Kardex } from "./paginas/Kardex";
import { Cargando } from "./componentes/Piezas";

export function App() {
  const { usuario, cargando, salir } = useSesion();

  if (cargando) {
    return <Cargando que="la sesión" />;
  }

  return (
    <>
      {/* Salto al contenido: lo primero que necesita quien navega con teclado. */}
      <a className="salto-al-contenido" href="#contenido">
        Saltar al contenido
      </a>

      <header className="cabecera">
        <h1>gestor-inve</h1>

        {usuario !== null && (
          <>
            <nav aria-label="Secciones">
              <NavLink to="/">Inventario</NavLink>
              <NavLink to="/catalogo">Catálogo</NavLink>
              <NavLink to="/kardex">Kardex</NavLink>
            </nav>
            <button type="button" className="secundario" onClick={() => void salir()}>
              Salir ({usuario.correo})
            </button>
          </>
        )}
      </header>

      <main id="contenido">
        <Routes>
          {usuario === null ? (
            <>
              <Route path="/entrar" element={<Entrar />} />
              <Route path="/alta" element={<AltaEmpresa />} />
              <Route path="*" element={<Navigate to="/entrar" replace />} />
            </>
          ) : (
            <>
              <Route path="/" element={<Inventario />} />
              <Route path="/catalogo" element={<Catalogo />} />
              <Route path="/kardex" element={<Kardex />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </>
          )}
        </Routes>
      </main>

      <p className="pie">Software libre bajo AGPL-3.0</p>
    </>
  );
}
