import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSesion } from "../sesion/SesionContexto";
import type {
  RespuestaVariante,
  RespuestaBodega,
  RespuestaExistencia,
  RespuestaLote,
  RespuestaMovimiento,
  RespuestaProducto,
} from "../api/cliente";
import { Aviso, Campo, Cargando, Tarjeta, Vacio } from "../componentes/Piezas";
import { aNumero, formatearCantidad, formatearDinero } from "../api/formato";

export function Inventario() {
  const { cliente, puede } = useSesion();
  const cola = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [aviso, setAviso] = useState<string | null>(null);

  const bodegas = useQuery({
    queryKey: ["bodegas"],
    queryFn: () => cliente.get<RespuestaBodega[]>("/api/inventario/bodegas"),
  });

  const productos = useQuery({
    queryKey: ["productos"],
    queryFn: () => cliente.get<RespuestaProducto[]>("/api/catalogo/productos"),
  });

  const existencias = useQuery({
    queryKey: ["existencias"],
    queryFn: () => cliente.get<RespuestaExistencia[]>("/api/inventario/existencias"),
  });

  const lotes = useQuery({
    queryKey: ["lotes"],
    queryFn: () => cliente.get<RespuestaLote[]>("/api/inventario/lotes"),
  });

  const crearBodega = useMutation({
    mutationFn: (datos: { codigo: string; nombre: string }) =>
      cliente.post<RespuestaBodega>("/api/inventario/bodegas", datos),
    onSuccess: () => void cola.invalidateQueries({ queryKey: ["bodegas"] }),
    onError: (fallo: Error) => setError(fallo.message),
  });

  const crearLote = useMutation({
    mutationFn: ({ varianteId, ...datos }: { varianteId: string } & Record<string, unknown>) =>
      cliente.post<RespuestaLote>(`/api/inventario/variantes/${varianteId}/lotes`, datos),
    onSuccess: (lote) => {
      setAviso(`Lote ${lote.codigo} creado.`);
      void cola.invalidateQueries({ queryKey: ["lotes"] });
    },
    onError: (fallo: Error) => setError(fallo.message),
  });

  const mover = useMutation({
    // La respuesta es una **lista**: una salida sin lote de un producto con lotes se reparte
    // por vencimiento (FEFO) y genera un asiento por lote. Tratarla como un objeto escondería
    // que el saldo se tomó de tres sitios distintos.
    mutationFn: (datos: Record<string, unknown>) =>
      cliente.post<RespuestaMovimiento[]>("/api/inventario/movimientos", datos),
    onSuccess: (asientos) => {
      const negativo = asientos.some((m) => m.forzadoNegativo === true);
      const reparto =
        asientos.length > 1 ? ` Se tomó de ${asientos.length} lotes por vencimiento.` : "";

      setAviso(
        (negativo ? "Movimiento registrado, pero el saldo quedó negativo." : "Movimiento registrado.") +
          reparto,
      );
      void cola.invalidateQueries({ queryKey: ["existencias"] });
      void cola.invalidateQueries({ queryKey: ["kardex"] });
    },
    onError: (fallo: Error) => setError(fallo.message),
  });

  // Todas las variantes con el nombre de su producto, que es como las reconoce una persona.
  const variantes = (productos.data ?? []).flatMap((producto) =>
    (producto.variantes ?? []).map((variante: RespuestaVariante) => ({
      id: variante.id ?? "",
      etiqueta: `${variante.sku} — ${producto.nombre}`,
    })),
  );

  return (
    <>
      <h1>Inventario</h1>
      {error !== null && <Aviso tipo="error">{error}</Aviso>}
      {aviso !== null && <Aviso tipo="exito">{aviso}</Aviso>}

      <div className="rejilla dos">
        {puede("inventario:ajustar") && (
          <Tarjeta titulo="Nueva bodega">
            <FormularioBodega
              enviando={crearBodega.isPending}
              onCrear={(datos) => {
                setError(null);
                crearBodega.mutate(datos);
              }}
            />
          </Tarjeta>
        )}

        {puede("inventario:mover") && (
          <Tarjeta titulo="Registrar movimiento">
            <FormularioMovimiento
              bodegas={bodegas.data ?? []}
              variantes={variantes}
              lotes={lotes.data ?? []}
              puedeAjustar={puede("inventario:ajustar")}
              enviando={mover.isPending}
              onMover={(datos) => {
                setError(null);
                setAviso(null);
                mover.mutate(datos);
              }}
            />
          </Tarjeta>
        )}
      </div>

      {puede("inventario:ajustar") && (
        <Tarjeta titulo="Nuevo lote o serial">
          <FormularioLote
            variantes={variantes}
            enviando={crearLote.isPending}
            onCrear={(datos) => {
              setError(null);
              setAviso(null);
              crearLote.mutate(datos);
            }}
          />
        </Tarjeta>
      )}

      <Tarjeta titulo="Existencias">
        {existencias.isPending ? (
          <Cargando que="las existencias" />
        ) : (existencias.data?.length ?? 0) === 0 ? (
          <Vacio>No hay existencias. Registre una entrada para empezar.</Vacio>
        ) : (
          <div className="tabla-contenedor">
            <table>
              <thead>
                <tr>
                  <th scope="col">Variante</th>
                  <th scope="col">Bodega</th>
                  <th scope="col">Lote</th>
                  <th scope="col" className="numero">Cantidad</th>
                  <th scope="col" className="numero">Costo promedio</th>
                  <th scope="col" className="numero">Valorizado</th>
                </tr>
              </thead>
              <tbody>
                {existencias.data?.map((fila) => (
                  <tr key={`${fila.varianteId}-${fila.ubicacionId}-${fila.loteId ?? "sin"}`}>
                    <td>{etiquetaDe(variantes, fila.varianteId ?? "")}</td>
                    <td>{nombreBodega(bodegas.data ?? [], fila.bodegaId ?? "")}</td>
                    <td data-prueba="lote">{etiquetaLote(lotes.data ?? [], fila.loteId)}</td>
                    <td
                      className={`numero ${aNumero(fila.cantidad) < 0 ? "negativo" : ""}`}
                      data-prueba="cantidad"
                    >
                      {formatearCantidad(aNumero(fila.cantidad), fila.unidad ?? "")}
                    </td>
                    <td className="numero">
                      {formatearDinero(aNumero(fila.costoPromedio), fila.moneda ?? "COP")}
                    </td>
                    <td className="numero">
                      {formatearDinero(aNumero(fila.valorizado), fila.moneda ?? "COP")}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Tarjeta>
    </>
  );
}

function etiquetaDe(variantes: { id: string; etiqueta: string }[], id: string): string {
  return variantes.find((v) => v.id === id)?.etiqueta ?? id;
}

function nombreBodega(bodegas: RespuestaBodega[], id: string): string {
  return bodegas.find((b) => b.id === id)?.nombre ?? id;
}

/// Un guion y no una celda vacía: «sin lote» es un estado legítimo —el producto no exige
/// trazabilidad— y no un dato que falte por cargar.
function etiquetaLote(lotes: RespuestaLote[], id: string | null | undefined): string {
  if (id === null || id === undefined) {
    return "—";
  }

  const lote = lotes.find((l) => l.id === id);

  if (lote === undefined) {
    return id;
  }

  return lote.vencido === true ? `${lote.codigo} (vencido)` : lote.codigo;
}

function FormularioBodega({
  enviando,
  onCrear,
}: {
  enviando: boolean;
  onCrear: (datos: { codigo: string; nombre: string }) => void;
}) {
  const [codigo, setCodigo] = useState("");
  const [nombre, setNombre] = useState("");

  return (
    <form
      onSubmit={(evento) => {
        evento.preventDefault();
        onCrear({ codigo, nombre });
        setCodigo("");
        setNombre("");
      }}
    >
      <Campo etiqueta="Código">
        <input value={codigo} onChange={(e) => setCodigo(e.target.value)} required maxLength={20} />
      </Campo>

      <Campo etiqueta="Nombre">
        <input value={nombre} onChange={(e) => setNombre(e.target.value)} required />
      </Campo>

      <p className="vacio">Se creará con su ubicación «GENERAL».</p>

      <button type="submit" disabled={enviando}>
        Crear bodega
      </button>
    </form>
  );
}

function FormularioMovimiento({
  bodegas,
  variantes,
  lotes,
  puedeAjustar,
  enviando,
  onMover,
}: {
  bodegas: RespuestaBodega[];
  variantes: { id: string; etiqueta: string }[];
  lotes: RespuestaLote[];
  puedeAjustar: boolean;
  enviando: boolean;
  onMover: (datos: Record<string, unknown>) => void;
}) {
  const [tipo, setTipo] = useState("Entrada");
  const [varianteId, setVariante] = useState("");
  const [bodegaId, setBodega] = useState("");
  const [loteId, setLote] = useState("");
  const [cantidad, setCantidad] = useState("1");
  const [costoUnitario, setCosto] = useState("");
  const [referencia, setReferencia] = useState("");

  const esEntrada = tipo === "Entrada" || tipo === "AjustePositivo";
  const faltanDatos = bodegas.length === 0 || variantes.length === 0;

  // Solo los lotes de la variante elegida: un lote es del artículo, y ofrecer los de otro sería
  // ofrecer un error que el servidor va a rechazar.
  const lotesDeLaVariante = lotes.filter((l) => l.varianteId === varianteId);

  return (
    <form
      onSubmit={(evento) => {
        evento.preventDefault();
        onMover({
          tipo,
          varianteId,
          bodegaId,
          ubicacionId: null,
          cantidad: Number(cantidad),
          costoUnitario: esEntrada && costoUnitario !== "" ? Number(costoUnitario) : null,
          fecha: null,
          referencia: referencia === "" ? null : referencia,
          observacion: null,
          loteId: loteId === "" ? null : loteId,
        });
      }}
    >
      {faltanDatos && <Aviso tipo="error">Hacen falta una bodega y un producto.</Aviso>}

      <Campo etiqueta="Tipo">
        <select value={tipo} onChange={(e) => setTipo(e.target.value)}>
          <option value="Entrada">Entrada</option>
          <option value="Salida">Salida</option>
          {/* Los ajustes exigen `inventario:ajustar`: si no, el servidor responde 403. */}
          {puedeAjustar && <option value="AjustePositivo">Ajuste (+)</option>}
          {puedeAjustar && <option value="AjusteNegativo">Ajuste (−)</option>}
        </select>
      </Campo>

      <Campo etiqueta="Variante">
        <select
          value={varianteId}
          onChange={(e) => setVariante(e.target.value)}
          required
          disabled={faltanDatos}
        >
          <option value="">Seleccione…</option>
          {variantes.map((variante) => (
            <option key={variante.id} value={variante.id}>
              {variante.etiqueta}
            </option>
          ))}
        </select>
      </Campo>

      <Campo etiqueta="Bodega">
        <select
          value={bodegaId}
          onChange={(e) => setBodega(e.target.value)}
          required
          disabled={faltanDatos}
        >
          <option value="">Seleccione…</option>
          {bodegas.map((bodega) => (
            <option key={bodega.id} value={bodega.id ?? ""}>
              {bodega.codigo} — {bodega.nombre}
            </option>
          ))}
        </select>
      </Campo>

      {lotesDeLaVariante.length > 0 && (
        <Campo etiqueta="Lote">
          <select value={loteId} onChange={(e) => setLote(e.target.value)}>
            {/* En una salida, dejarlo sin elegir es una opción real y la recomendada: el
                servidor consume por vencimiento más próximo. En una entrada hay que elegir. */}
            <option value="">
              {esEntrada ? "Seleccione…" : "El más próximo a vencer (automático)"}
            </option>
            {lotesDeLaVariante.map((lote) => (
              <option key={lote.id} value={lote.id ?? ""}>
                {lote.codigo}
                {lote.vencimiento === null || lote.vencimiento === undefined
                  ? ""
                  : ` — vence ${lote.vencimiento}`}
                {lote.vencido === true ? " (vencido)" : ""}
              </option>
            ))}
          </select>
        </Campo>
      )}

      <Campo etiqueta="Cantidad">
        <input
          type="number"
          step="any"
          min="0"
          value={cantidad}
          onChange={(e) => setCantidad(e.target.value)}
          required
        />
      </Campo>

      {esEntrada && (
        <Campo etiqueta="Costo unitario">
          <input
            type="number"
            step="any"
            min="0"
            value={costoUnitario}
            onChange={(e) => setCosto(e.target.value)}
            required={tipo === "Entrada"}
          />
        </Campo>
      )}

      <Campo etiqueta="Documento de referencia (opcional)">
        <input value={referencia} onChange={(e) => setReferencia(e.target.value)} />
      </Campo>

      <button type="submit" disabled={enviando || faltanDatos}>
        Registrar
      </button>
    </form>
  );
}

function FormularioLote({
  variantes,
  enviando,
  onCrear,
}: {
  variantes: { id: string; etiqueta: string }[];
  enviando: boolean;
  onCrear: (datos: { varianteId: string; codigo: string; vencimiento: string | null; esSerial: boolean }) => void;
}) {
  const [varianteId, setVariante] = useState("");
  const [codigo, setCodigo] = useState("");
  const [vencimiento, setVencimiento] = useState("");
  const [esSerial, setSerial] = useState(false);

  return (
    <form
      onSubmit={(evento) => {
        evento.preventDefault();
        onCrear({
          varianteId,
          codigo,
          vencimiento: vencimiento === "" ? null : vencimiento,
          esSerial,
        });
        setCodigo("");
        setVencimiento("");
      }}
    >
      <Campo etiqueta="Variante del lote">
        <select value={varianteId} onChange={(e) => setVariante(e.target.value)} required>
          <option value="">Seleccione…</option>
          {variantes.map((variante) => (
            <option key={variante.id} value={variante.id}>
              {variante.etiqueta}
            </option>
          ))}
        </select>
      </Campo>

      <Campo etiqueta="Código del lote o número de serie">
        <input value={codigo} onChange={(e) => setCodigo(e.target.value)} required maxLength={60} />
      </Campo>

      <Campo etiqueta="Vencimiento (opcional)">
        {/* `date` y no fecha con hora: un lote vence un día, y con hora el mismo lote estaría
            vencido o no según la zona desde la que se consulte. */}
        <input type="date" value={vencimiento} onChange={(e) => setVencimiento(e.target.value)} />
      </Campo>

      <label className="campo-casilla">
        <input type="checkbox" checked={esSerial} onChange={(e) => setSerial(e.target.checked)} />
        <span>Es un número de serie (una sola unidad)</span>
      </label>

      <button type="submit" disabled={enviando}>
        Crear lote
      </button>
    </form>
  );
}
