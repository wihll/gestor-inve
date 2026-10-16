import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSesion } from "../sesion/SesionContexto";
import type {
  RespuestaVariante,
  RespuestaBodega,
  RespuestaExistencia,
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
    queryFn: () => cliente.get<RespuestaBodega[]>("/inventario/bodegas"),
  });

  const productos = useQuery({
    queryKey: ["productos"],
    queryFn: () => cliente.get<RespuestaProducto[]>("/catalogo/productos"),
  });

  const existencias = useQuery({
    queryKey: ["existencias"],
    queryFn: () => cliente.get<RespuestaExistencia[]>("/inventario/existencias"),
  });

  const crearBodega = useMutation({
    mutationFn: (datos: { codigo: string; nombre: string }) =>
      cliente.post<RespuestaBodega>("/inventario/bodegas", datos),
    onSuccess: () => void cola.invalidateQueries({ queryKey: ["bodegas"] }),
    onError: (fallo: Error) => setError(fallo.message),
  });

  const mover = useMutation({
    mutationFn: (datos: Record<string, unknown>) =>
      cliente.post<RespuestaMovimiento>("/inventario/movimientos", datos),
    onSuccess: (movimiento) => {
      setAviso(
        movimiento.forzadoNegativo === true
          ? "Movimiento registrado, pero el saldo quedó negativo."
          : "Movimiento registrado.",
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
                  <th scope="col" className="numero">Cantidad</th>
                  <th scope="col" className="numero">Costo promedio</th>
                  <th scope="col" className="numero">Valorizado</th>
                </tr>
              </thead>
              <tbody>
                {existencias.data?.map((fila) => (
                  <tr key={`${fila.varianteId}-${fila.ubicacionId}`}>
                    <td>{etiquetaDe(variantes, fila.varianteId ?? "")}</td>
                    <td>{nombreBodega(bodegas.data ?? [], fila.bodegaId ?? "")}</td>
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
  puedeAjustar,
  enviando,
  onMover,
}: {
  bodegas: RespuestaBodega[];
  variantes: { id: string; etiqueta: string }[];
  puedeAjustar: boolean;
  enviando: boolean;
  onMover: (datos: Record<string, unknown>) => void;
}) {
  const [tipo, setTipo] = useState("Entrada");
  const [varianteId, setVariante] = useState("");
  const [bodegaId, setBodega] = useState("");
  const [cantidad, setCantidad] = useState("1");
  const [costoUnitario, setCosto] = useState("");
  const [referencia, setReferencia] = useState("");

  const esEntrada = tipo === "Entrada" || tipo === "AjustePositivo";
  const faltanDatos = bodegas.length === 0 || variantes.length === 0;

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
