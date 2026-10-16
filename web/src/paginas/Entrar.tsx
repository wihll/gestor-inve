import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useSesion } from "../sesion/SesionContexto";
import { Aviso, Campo, Tarjeta } from "../componentes/Piezas";

export function Entrar() {
  const { entrar } = useSesion();
  const navegar = useNavigate();
  const [nit, setNit] = useState("");
  const [correo, setCorreo] = useState("");
  const [contrasena, setContrasena] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [enviando, setEnviando] = useState(false);

  async function enviar(evento: React.FormEvent) {
    evento.preventDefault();
    setError(null);
    setEnviando(true);

    try {
      await entrar({ nit, correo, contrasena });
      navegar("/");
    } catch (fallo) {
      // Se muestra lo que dice el servidor: está redactado para no revelar si el
      // correo existe, así que repetirlo tal cual es lo correcto.
      setError(fallo instanceof Error ? fallo.message : "No se pudo entrar.");
    } finally {
      setEnviando(false);
    }
  }

  return (
    <Tarjeta titulo="Entrar">
      <form onSubmit={enviar}>
        {error !== null && <Aviso tipo="error">{error}</Aviso>}

        <Campo etiqueta="NIT de la empresa">
          <input
            value={nit}
            onChange={(e) => setNit(e.target.value)}
            required
            autoComplete="organization"
            inputMode="numeric"
          />
        </Campo>

        <Campo etiqueta="Correo">
          <input
            type="email"
            value={correo}
            onChange={(e) => setCorreo(e.target.value)}
            required
            autoComplete="username"
          />
        </Campo>

        <Campo etiqueta="Contraseña">
          <input
            type="password"
            value={contrasena}
            onChange={(e) => setContrasena(e.target.value)}
            required
            autoComplete="current-password"
          />
        </Campo>

        <button type="submit" disabled={enviando}>
          {enviando ? "Entrando…" : "Entrar"}
        </button>
      </form>

      <p>
        ¿No tiene empresa registrada? <a href="/alta">Registre una</a>.
      </p>
    </Tarjeta>
  );
}
