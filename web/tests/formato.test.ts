import { describe, expect, it } from "vitest";
import {
  formatearCantidad,
  formatearDinero,
  formatearFecha,
  nombreDeTipo,
  signoDeTipo,
} from "../src/api/formato";

describe("formato es-CO", () => {
  it("separa los miles con punto y los decimales con coma", () => {
    const texto = formatearDinero(1234567.89);

    // El separador que usa Intl para es-CO es un espacio estrecho; se comprueban los dígitos.
    expect(texto).toContain("1.234.567,89");
  });

  it("admite otra moneda sin romperse", () => {
    expect(formatearDinero(10, "USD")).toContain("10");
  });

  it("muestra las cantidades con los decimales de su unidad", () => {
    expect(formatearCantidad(3, "UND", 0)).toBe("3 UND");
    expect(formatearCantidad(2.5, "KG", 3)).toBe("2,500 KG");
  });

  it("no inventa decimales en una unidad de piezas", () => {
    expect(formatearCantidad(3, "UND", 0)).not.toContain(",");
  });

  it("devuelve la fecha original si no se puede interpretar", () => {
    expect(formatearFecha("no es una fecha")).toBe("no es una fecha");
  });

  it("formatea una fecha válida", () => {
    expect(formatearFecha("2026-03-01T10:30:00Z")).toMatch(/2026|26/);
  });

  it("traduce los tipos de movimiento", () => {
    expect(nombreDeTipo("TrasladoSalida")).toBe("Traslado (salida)");
    expect(nombreDeTipo("Desconocido")).toBe("Desconocido");
  });

  /// Si estos signos no coinciden con los del servidor, el kardex de la pantalla no cuadraría
  /// con el del backend y el usuario vería un saldo que no existe.
  it("los signos coinciden con los del servidor", () => {
    expect(signoDeTipo("Entrada")).toBe(1);
    expect(signoDeTipo("TrasladoEntrada")).toBe(1);
    expect(signoDeTipo("AjustePositivo")).toBe(1);
    expect(signoDeTipo("Salida")).toBe(-1);
    expect(signoDeTipo("TrasladoSalida")).toBe(-1);
    expect(signoDeTipo("AjusteNegativo")).toBe(-1);
    expect(signoDeTipo("Inventado")).toBe(0);
  });
});
