import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { ClienteApi } from "../api/cliente";
import { Aviso, Campo, Tarjeta } from "../componentes/Piezas";

const LONGITUD_MINIMA = 12;

export function AltaEmpresa() {
  const navegar = useNavigate();
  const [datos, setDatos] = useState({
    nit: "",
    razonSocial: "",
    correoAdmin: "",
    nombreAdmin: "",
    contrasena: "",
  });
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  function cambiar(campo: keyof typeof datos) {
    return (evento: React.ChangeEvent<HTMLInputElement>) =>
      setDatos((previos) => ({ ...previos, [campo]: evento.target.value }));
  }

  async function enviar(evento: React.FormEvent) {
    evento.preventDefault();
    setError(null);
    setEnviando(true);

    try {
      // El alta no necesita sesión, así que usa un cliente suelto.
      await new ClienteApi().post("/api/empresas", datos);
      navegar("/entrar?alta=1");
    } catch (fallo) {
      setError(fallo instanceof Error ? fallo.message : "No se pudo registrar la empresa.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Tarjeta titulo="Registrar empresa">
      <form onSubmit={enviar}>
        {error !== null && <Aviso tipo="error">{error}</Aviso>}

        <div className="rejilla dos">
          <Campo etiqueta="NIT">
            <input value={datos.nit} onChange={cambiar("nit")} required />
          </Campo>

          <Campo etiqueta="Razón social">
            <input value={datos.razonSocial} onChange={cambiar("razonSocial")} required />
          </Campo>

          <Campo etiqueta="Nombre del administrador">
            <input value={datos.nombreAdmin} onChange={cambiar("nombreAdmin")} required />
          </Campo>

          <Campo etiqueta="Correo del administrador">
            <input
              type="email"
              value={datos.correoAdmin}
              onChange={cambiar("correoAdmin")}
              required
              autoComplete="username"
            />
          </Campo>
        </div>

        <Campo etiqueta={`Contraseña (mínimo ${LONGITUD_MINIMA} caracteres)`}>
          <input
            type="password"
            value={datos.contrasena}
            onChange={cambiar("contrasena")}
            required
            minLength={LONGITUD_MINIMA}
            autoComplete="new-password"
          />
        </Campo>

        <button type="submit" disabled={enviando}>
          {enviando ? "Registrando…" : "Registrar"}
        </button>
      </form>
    </Tarjeta>
  );
}
