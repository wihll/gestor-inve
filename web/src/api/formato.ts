/// Los decimales del contrato llegan como `number | string`: así los declara el OpenAPI de
/// .NET, porque `System.Text.Json` admite ambas formas al leer. Convertirlos en un solo sitio
/// evita que cada pantalla haga su propia suposición (y que un `"12"` se concatene en vez de
/// sumarse, que es el fallo clásico).
export function aNumero(valor: number | string | null | undefined): number {
  if (typeof valor === "number") {
    return valor;
  }

  if (typeof valor === "string") {
    const numero = Number(valor);

    return Number.isNaN(numero) ? 0 : numero;
  }

  return 0;
}

/// Formateo para Colombia. El idioma de la interfaz es es-CO, así que los miles se separan con
/// punto y los decimales con coma; usar el formato inglés en una factura colombiana se lee como
/// un error de mil veces el valor.
const LOCALE = "es-CO";

const moneda = new Intl.NumberFormat(LOCALE, {
  style: "currency",
  currency: "COP",
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

const fechaHora = new Intl.DateTimeFormat(LOCALE, {
  dateStyle: "short",
  timeStyle: "short",
});

export function formatearDinero(monto: number, codigoMoneda = "COP"): string {
  if (codigoMoneda === "COP") {
    return moneda.format(monto);
  }

  return new Intl.NumberFormat(LOCALE, {
    style: "currency",
    currency: codigoMoneda,
  }).format(monto);
}

/// Las cantidades se muestran con los decimales de su unidad: «3 UND», nunca «3,000 UND»,
/// y «2,5 KG» con los suyos. Mostrar decimales que la unidad no admite confunde al contar.
export function formatearCantidad(valor: number, unidad: string, decimales = 0): string {
  const numero = new Intl.NumberFormat(LOCALE, {
    minimumFractionDigits: decimales,
    maximumFractionDigits: decimales,
  }).format(valor);

  return `${numero} ${unidad}`;
}

export function formatearFecha(iso: string): string {
  const fecha = new Date(iso);

  return Number.isNaN(fecha.getTime()) ? iso : fechaHora.format(fecha);
}

const nombresDeTipo: Record<string, string> = {
  Entrada: "Entrada",
  Salida: "Salida",
  TrasladoSalida: "Traslado (salida)",
  TrasladoEntrada: "Traslado (entrada)",
  AjustePositivo: "Ajuste (+)",
  AjusteNegativo: "Ajuste (−)",
};

export function nombreDeTipo(tipo: string): string {
  return nombresDeTipo[tipo] ?? tipo;
}

/// Signo del movimiento sobre las existencias. Debe coincidir con `TipoMovimientoExtensiones`
/// del servidor: si divergen, el kardex de la pantalla no cuadraría con el del backend.
export function signoDeTipo(tipo: string): number {
  switch (tipo) {
    case "Entrada":
    case "TrasladoEntrada":
    case "AjustePositivo":
      return 1;
    case "Salida":
    case "TrasladoSalida":
    case "AjusteNegativo":
      return -1;
    default:
      return 0;
  }
}
