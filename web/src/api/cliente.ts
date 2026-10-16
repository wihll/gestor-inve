import type { components, paths } from "./esquema";

/// Tipos tomados del OpenAPI: si la API cambia su contrato, esto deja de compilar.
/// Es la razón de generar el esquema en vez de escribir los tipos a mano: el fallo
/// aparece en el build y no en una pantalla del cliente.
export type PeticionLogin = components["schemas"]["PeticionLogin"];
export type RespuestaSesion = components["schemas"]["RespuestaSesion"];
export type RespuestaYo = components["schemas"]["RespuestaYo"];
export type PeticionAlta = components["schemas"]["PeticionAlta"];
export type RespuestaProducto = components["schemas"]["RespuestaProducto"];
export type RespuestaVariante = components["schemas"]["RespuestaVariante"];
export type RespuestaUnidad = components["schemas"]["RespuestaUnidad"];
export type PeticionProducto = components["schemas"]["PeticionProducto"];
export type PeticionUnidad = components["schemas"]["PeticionUnidad"];
export type RespuestaBodega = components["schemas"]["RespuestaBodega"];
export type PeticionBodega = components["schemas"]["PeticionBodega"];
export type RespuestaMovimiento = components["schemas"]["RespuestaMovimiento"];
export type PeticionMovimiento = components["schemas"]["PeticionMovimiento"];
export type RespuestaExistencia = components["schemas"]["RespuestaExistencia"];
export type TipoMovimiento = components["schemas"]["TipoMovimiento"];

/// Comprobación en tiempo de compilación de que las rutas que usamos existen en el contrato.
type RutasUsadas =
  | "/api/auth/login"
  | "/api/auth/refresh"
  | "/api/auth/logout"
  | "/api/auth/yo"
  | "/api/empresas"
  | "/api/catalogo/unidades"
  | "/api/catalogo/productos"
  | "/api/inventario/bodegas"
  | "/api/inventario/movimientos"
  | "/api/inventario/existencias"
  | "/api/inventario/kardex";

type Verifica<T extends keyof paths> = T;
export type RutasVerificadas = Verifica<RutasUsadas>;

export class ErrorApi extends Error {
  constructor(
    readonly estado: number,
    readonly detalle: string,
  ) {
    super(detalle);
    this.name = "ErrorApi";
  }
}

export interface Tokens {
  tokenAcceso: string;
  tokenRefresco: string;
  expiraEn: string;
}

/// Dónde viven los tokens.
///
/// El de **acceso** vive solo en memoria: así no sobrevive a un cierre de pestaña ni queda
/// al alcance de un script. El de **refresco** va a `localStorage`, porque sin él la sesión
/// se perdería en cada F5.
///
/// El riesgo está asumido y es conocido: un XSS puede leer `localStorage`. La defensa correcta
/// es una cookie `httpOnly`, que exige cambiar la API (y añadir protección CSRF) y queda
/// pendiente. Mientras tanto lo que acota el daño es la rotación con detección de reutilización
/// que ya hace el servidor: en cuanto el atacante usa el token robado, la familia completa se
/// revoca y la víctima tiene que volver a entrar. Ver ADR 0007.
const CLAVE_REFRESCO = "gestorinve.refresco";

export function guardarRefresco(token: string | null): void {
  try {
    if (token === null) {
      localStorage.removeItem(CLAVE_REFRESCO);
    } else {
      localStorage.setItem(CLAVE_REFRESCO, token);
    }
  } catch {
    // Navegación privada o almacenamiento bloqueado: la sesión durará lo que dure la pestaña.
  }
}

export function leerRefresco(): string | null {
  try {
    return localStorage.getItem(CLAVE_REFRESCO);
  } catch {
    return null;
  }
}

export const BASE_API: string = import.meta.env.VITE_API ?? "";

/// Cliente con **un solo refresco en vuelo**: si tres peticiones reciben 401 a la vez y cada
/// una refrescara por su cuenta, dos de los tres tokens rotarían en paralelo y el servidor
/// (que detecta reutilización) revocaría la familia y echaría al usuario. Por eso se comparte
/// la misma promesa.
export class ClienteApi {
  private acceso: string | null = null;
  private refrescoEnVuelo: Promise<boolean> | null = null;

  constructor(private readonly alCerrarSesion: () => void = () => {}) {}

  fijarTokens(tokens: Tokens | null): void {
    this.acceso = tokens?.tokenAcceso ?? null;
    guardarRefresco(tokens?.tokenRefresco ?? null);
  }

  get tieneSesion(): boolean {
    return this.acceso !== null || leerRefresco() !== null;
  }

  async entrar(credenciales: PeticionLogin): Promise<RespuestaSesion> {
    const sesion = await this.pedir<RespuestaSesion>("POST", "/api/auth/login", credenciales, false);
    this.fijarTokens(sesion as Tokens);

    return sesion;
  }

  async salir(): Promise<void> {
    const refresco = leerRefresco();

    if (refresco !== null) {
      // El cierre revoca la cadena en el servidor; si falla, igual se limpia aquí.
      await this.pedir("POST", "/api/auth/logout", { tokenRefresco: refresco }, false).catch(() => undefined);
    }

    this.fijarTokens(null);
    this.alCerrarSesion();
  }

  /// Recupera la sesión al abrir la aplicación usando el token de refresco guardado.
  async restaurar(): Promise<boolean> {
    return this.acceso !== null ? true : this.refrescar();
  }

  get<T>(ruta: string): Promise<T> {
    return this.pedir<T>("GET", ruta);
  }

  post<T>(ruta: string, cuerpo?: unknown): Promise<T> {
    return this.pedir<T>("POST", ruta, cuerpo);
  }

  put<T>(ruta: string, cuerpo?: unknown): Promise<T> {
    return this.pedir<T>("PUT", ruta, cuerpo);
  }

  private async refrescar(): Promise<boolean> {
    this.refrescoEnVuelo ??= this.refrescarUnaVez().finally(() => {
      this.refrescoEnVuelo = null;
    });

    return this.refrescoEnVuelo;
  }

  private async refrescarUnaVez(): Promise<boolean> {
    const refresco = leerRefresco();

    if (refresco === null) {
      return false;
    }

    try {
      const sesion = await this.pedir<RespuestaSesion>(
        "POST",
        "/api/auth/refresh",
        { tokenRefresco: refresco },
        false,
      );

      this.fijarTokens(sesion as Tokens);

      return true;
    } catch {
      // Refresco inválido, caducado o ya usado: la sesión se termina aquí.
      this.fijarTokens(null);
      this.alCerrarSesion();

      return false;
    }
  }

  private async pedir<T>(
    metodo: string,
    ruta: string,
    cuerpo?: unknown,
    reintentar = true,
  ): Promise<T> {
    const respuesta = await this.enviar(metodo, ruta, cuerpo);

    if (respuesta.status === 401 && reintentar && (await this.refrescar())) {
      return this.pedir<T>(metodo, ruta, cuerpo, false);
    }

    if (!respuesta.ok) {
      throw new ErrorApi(respuesta.status, await this.detalleDe(respuesta));
    }

    if (respuesta.status === 204) {
      return undefined as T;
    }

    return (await respuesta.json()) as T;
  }

  private enviar(metodo: string, ruta: string, cuerpo?: unknown): Promise<Response> {
    const cabeceras: Record<string, string> = { Accept: "application/json" };

    if (cuerpo !== undefined) {
      cabeceras["Content-Type"] = "application/json";
    }

    if (this.acceso !== null) {
      cabeceras.Authorization = `Bearer ${this.acceso}`;
    }

    return fetch(`${BASE_API}${ruta}`, {
      method: metodo,
      headers: cabeceras,
      body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo),
    });
  }

  /// La API responde `ProblemDetails`; se muestra su `detail`, que está redactado para el usuario.
  private async detalleDe(respuesta: Response): Promise<string> {
    try {
      const problema = (await respuesta.json()) as { detail?: string; title?: string };

      return problema.detail ?? problema.title ?? `Error ${respuesta.status}`;
    } catch {
      return `Error ${respuesta.status}`;
    }
  }
}
