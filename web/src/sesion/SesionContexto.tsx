import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import type { ReactNode } from "react";
import { ClienteApi, type PeticionLogin, type RespuestaYo } from "../api/cliente";

interface Sesion {
  cliente: ClienteApi;
  usuario: RespuestaYo | null;
  cargando: boolean;
  entrar: (credenciales: PeticionLogin) => Promise<void>;
  salir: () => Promise<void>;
  puede: (permiso: string) => boolean;
}

const Contexto = createContext<Sesion | null>(null);

export function ProveedorSesion({ children }: { children: ReactNode }) {
  const [usuario, setUsuario] = useState<RespuestaYo | null>(null);
  const [cargando, setCargando] = useState(true);

  // El cliente se crea una sola vez: lleva dentro el token de acceso en memoria y el
  // control de «un solo refresco en vuelo», que se perderían al recrearlo.
  const cliente = useMemo(() => new ClienteApi(() => setUsuario(null)), []);

  const cargarUsuario = useCallback(async () => {
    try {
      setUsuario(await cliente.get<RespuestaYo>("/auth/yo"));
    } catch {
      setUsuario(null);
    }
  }, [cliente]);

  useEffect(() => {
    // Al abrir la aplicación se intenta recuperar la sesión con el token de refresco guardado.
    let cancelado = false;

    void (async () => {
      if (await cliente.restaurar()) {
        await cargarUsuario();
      }

      if (!cancelado) {
        setCargando(false);
      }
    })();

    return () => {
      cancelado = true;
    };
  }, [cliente, cargarUsuario]);

  const valor = useMemo<Sesion>(
    () => ({
      cliente,
      usuario,
      cargando,
      entrar: async (credenciales) => {
        await cliente.entrar(credenciales);
        await cargarUsuario();
      },
      salir: async () => {
        await cliente.salir();
        setUsuario(null);
      },
      // Los permisos vienen en el token; la interfaz solo esconde lo que el servidor
      // ya rechazaría. Nunca es la única defensa.
      puede: (permiso) => usuario?.permisos?.includes(permiso) ?? false,
    }),
    [cliente, usuario, cargando, cargarUsuario],
  );

  return <Contexto.Provider value={valor}>{children}</Contexto.Provider>;
}

export function useSesion(): Sesion {
  const sesion = useContext(Contexto);

  if (sesion === null) {
    throw new Error("useSesion debe usarse dentro de ProveedorSesion.");
  }

  return sesion;
}
