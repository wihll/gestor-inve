import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ClienteApi, ErrorApi } from "../src/api/cliente";

/// Respuesta mínima con la forma que espera el cliente.
function respuesta(estado: number, cuerpo: unknown = {}): Response {
  return {
    ok: estado >= 200 && estado < 300,
    status: estado,
    json: async () => cuerpo,
  } as Response;
}

const sesion = {
  tokenAcceso: "acceso-nuevo",
  tokenRefresco: "empresa.refresco-nuevo",
  expiraEn: new Date().toISOString(),
};

describe("ClienteApi", () => {
  beforeEach(() => {
    localStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("entra y guarda el refresco para sobrevivir a un F5", async () => {
    const fetchMock = vi.fn().mockResolvedValue(respuesta(200, sesion));
    vi.stubGlobal("fetch", fetchMock);

    const cliente = new ClienteApi();
    await cliente.entrar({ nit: "900", correo: "a@b.co", contrasena: "Clave-Muy-Larga-2026" });

    expect(localStorage.getItem("gestorinve.refresco")).toBe("empresa.refresco-nuevo");
    expect(cliente.tieneSesion).toBe(true);
  });

  it("manda el token de acceso en la cabecera", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(respuesta(200, sesion))
      .mockResolvedValueOnce(respuesta(200, { dato: 1 }));
    vi.stubGlobal("fetch", fetchMock);

    const cliente = new ClienteApi();
    await cliente.entrar({ nit: "900", correo: "a@b.co", contrasena: "Clave-Muy-Larga-2026" });
    await cliente.get("/catalogo/productos");

    const cabeceras = fetchMock.mock.calls[1]?.[1].headers as Record<string, string>;
    expect(cabeceras.Authorization).toBe("Bearer acceso-nuevo");
  });

  it("al recibir 401 refresca y reintenta una sola vez", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(respuesta(401))
      .mockResolvedValueOnce(respuesta(200, sesion))
      .mockResolvedValueOnce(respuesta(200, { dato: 42 }));
    vi.stubGlobal("fetch", fetchMock);
    localStorage.setItem("gestorinve.refresco", "empresa.refresco-viejo");

    const cliente = new ClienteApi();
    const resultado = await cliente.get<{ dato: number }>("/catalogo/productos");

    expect(resultado.dato).toBe(42);
    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls[1]?.[0]).toContain("/auth/refresh");
  });

  /// **La prueba que importa.** El servidor revoca la familia completa cuando un token de
  /// refresco se reutiliza. Si tres peticiones con 401 refrescaran cada una por su cuenta, dos
  /// de los tres intentos reutilizarían el mismo token y el usuario quedaría fuera sin motivo.
  it("tres peticiones con 401 a la vez provocan un solo refresco", async () => {
    let refrescos = 0;

    const fetchMock = vi.fn(async (url: string) => {
      if (String(url).includes("/auth/refresh")) {
        refrescos += 1;
        return respuesta(200, sesion);
      }

      // Antes de refrescar, todo da 401; después, 200.
      return refrescos === 0 ? respuesta(401) : respuesta(200, { dato: 1 });
    });
    vi.stubGlobal("fetch", fetchMock);
    localStorage.setItem("gestorinve.refresco", "empresa.refresco-viejo");

    const cliente = new ClienteApi();
    await Promise.all([
      cliente.get("/catalogo/productos"),
      cliente.get("/inventario/existencias"),
      cliente.get("/inventario/bodegas"),
    ]);

    expect(refrescos).toBe(1);
  });

  it("si el refresco falla cierra la sesión y avisa", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(respuesta(401))
      .mockResolvedValueOnce(respuesta(401, { detail: "Credenciales inválidas." }));
    vi.stubGlobal("fetch", fetchMock);
    localStorage.setItem("gestorinve.refresco", "empresa.refresco-robado");

    let cerrada = false;
    const cliente = new ClienteApi(() => {
      cerrada = true;
    });

    await expect(cliente.get("/catalogo/productos")).rejects.toBeInstanceOf(ErrorApi);
    expect(cerrada).toBe(true);
    expect(localStorage.getItem("gestorinve.refresco")).toBeNull();
  });

  it("sin token de refresco no intenta refrescar", async () => {
    const fetchMock = vi.fn().mockResolvedValue(respuesta(401));
    vi.stubGlobal("fetch", fetchMock);

    const cliente = new ClienteApi();

    await expect(cliente.get("/catalogo/productos")).rejects.toBeInstanceOf(ErrorApi);
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it("traslada el detalle del ProblemDetails al mensaje de error", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(respuesta(409, { detail: "No hay existencias suficientes." })),
    );

    const cliente = new ClienteApi();

    await expect(cliente.post("/inventario/movimientos", {})).rejects.toThrow(
      "No hay existencias suficientes.",
    );
  });

  it("al salir revoca en el servidor y limpia el almacenamiento", async () => {
    const fetchMock = vi.fn().mockResolvedValue(respuesta(204));
    vi.stubGlobal("fetch", fetchMock);
    localStorage.setItem("gestorinve.refresco", "empresa.refresco");

    const cliente = new ClienteApi();
    await cliente.salir();

    expect(fetchMock.mock.calls[0]?.[0]).toContain("/auth/logout");
    expect(localStorage.getItem("gestorinve.refresco")).toBeNull();
    expect(cliente.tieneSesion).toBe(false);
  });

  it("una respuesta 204 no intenta leer JSON", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        status: 204,
        json: async () => {
          throw new Error("no debería leerse");
        },
      } as unknown as Response),
    );

    const cliente = new ClienteApi();

    await expect(cliente.put("/catalogo/variantes/x/limites", {})).resolves.toBeUndefined();
  });
});
