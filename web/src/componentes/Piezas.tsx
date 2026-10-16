import type { ReactNode } from "react";

export function Aviso({ tipo, children }: { tipo: "error" | "exito"; children: ReactNode }) {
  return (
    // `role=alert` para que un lector de pantalla lo anuncie en cuanto aparece.
    <p className={`aviso ${tipo}`} role="alert">
      {children}
    </p>
  );
}

export function Tarjeta({ titulo, children }: { titulo?: string; children: ReactNode }) {
  return (
    <section className="tarjeta">
      {titulo !== undefined && <h2>{titulo}</h2>}
      {children}
    </section>
  );
}

export function Campo({
  etiqueta,
  children,
}: {
  etiqueta: string;
  children: ReactNode;
}) {
  return (
    <label className="campo">
      <span>{etiqueta}</span>
      {children}
    </label>
  );
}

export function Vacio({ children }: { children: ReactNode }) {
  return <p className="vacio">{children}</p>;
}

export function Cargando({ que }: { que: string }) {
  return (
    <p className="vacio" aria-live="polite">
      Cargando {que}…
    </p>
  );
}
