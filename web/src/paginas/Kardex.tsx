import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useSesion } from "../sesion/SesionContexto";
import type {
  RespuestaVariante, RespuestaMovimiento, RespuestaProducto, RespuestaLote } from "../api/cliente";
import { Campo, Cargando, Tarjeta, Vacio } from "../componentes/Piezas";
import {
  aNumero,
  formatearCantidad,
  formatearDinero,
  formatearFecha,
  nombreDeTipo,
  signoDeTipo,
} from "../api/formato";

export function Kardex() {
  const { cliente } = useSesion();
  const [varianteId, setVariante] = useState("");

  const productos = useQuery({
    queryKey: ["productos"],
    queryFn: () => cliente.get<RespuestaProducto[]>("/api/catalogo/productos"),
  });

  const lotes = useQuery({
    queryKey: ["lotes"],
    queryFn: () => cliente.get<RespuestaLote[]>("/api/inventario/lotes"),
  });

  const movimientos = useQuery({
    queryKey: ["kardex", varianteId],
    queryFn: () => cliente.get<RespuestaMovimiento[]>(`/api/inventario/kardex?varianteId=${varianteId}`),
    enabled: varianteId !== "",
  });

  const variantes = (productos.data ?? []).flatMap((producto) =>
    (producto.variantes ?? []).map((variante: RespuestaVariante) => ({
      id: variante.id ?? "",
      etiqueta: `${variante.sku} — ${producto.nombre}`,
    })),
  );

  const filas = movimientos.data ?? [];

  // El acumulado se recalcula en la pantalla y se compara con el saldo que guardó el
  // servidor: si no coinciden, el libro está descuadrado y hay que verlo, no esconderlo.
  //
  // La comparación es **por celda** —bodega, ubicación y lote—, no sobre el total. El saldo que
  // guarda cada asiento es el de su celda, así que con dos lotes el total de la variante nunca
  // coincidiría con el saldo de la última fila y la pantalla gritaría un descuadre inexistente.
  // Peor: en sentido contrario, dos celdas con errores que se compensan darían un total
  // correcto y el descuadre real pasaría desapercibido.
  const celdaDe = (fila: RespuestaMovimiento) =>
    `${fila.bodegaId ?? ""}|${fila.ubicacionId ?? ""}|${fila.loteId ?? ""}`;

  const porCelda = new Map<string, RespuestaMovimiento[]>();

  for (const fila of filas) {
    const clave = celdaDe(fila);
    porCelda.set(clave, [...(porCelda.get(clave) ?? []), fila]);
  }

  const firmada = (lista: RespuestaMovimiento[]) =>
    lista.reduce((suma, fila) => suma + aNumero(fila.cantidad) * signoDeTipo(fila.tipo ?? ""), 0);

  const acumulado = firmada(filas);

  const celdasDescuadradas = [...porCelda.values()].filter((lista) => {
    const ultimo = aNumero(lista.at(-1)?.saldoCantidadDespues);

    return Math.abs(firmada(lista) - ultimo) > 0.000001;
  });

  const descuadrado = celdasDescuadradas.length > 0;

  return (
    <>
      <h1>Kardex</h1>

      <Tarjeta>
        <Campo etiqueta="Variante">
          <select value={varianteId} onChange={(e) => setVariante(e.target.value)}>
            <option value="">Seleccione una variante…</option>
            {variantes.map((variante) => (
              <option key={variante.id} value={variante.id}>
                {variante.etiqueta}
              </option>
            ))}
          </select>
        </Campo>
      </Tarjeta>

      {varianteId === "" ? (
        <Vacio>Elija una variante para ver su kardex.</Vacio>
      ) : movimientos.isPending ? (
        <Cargando que="el kardex" />
      ) : filas.length === 0 ? (
        <Vacio>Esta variante todavía no tiene movimientos.</Vacio>
      ) : (
        <Tarjeta>
          <p data-prueba="acumulado">
            Acumulado del libro:{" "}
            <strong>{formatearCantidad(acumulado, filas[0]?.unidad ?? "")}</strong>
            {descuadrado && (
              <span className="aviso error" role="alert">
                El libro no cuadra en {celdasDescuadradas.length}{" "}
                {celdasDescuadradas.length === 1 ? "celda" : "celdas"} (bodega, ubicación y lote):
                la suma de los movimientos no coincide con el saldo que registró el servidor.
              </span>
            )}
          </p>

          <div className="tabla-contenedor">
            <table>
              <caption className="vacio">Movimientos en orden cronológico</caption>
              <thead>
                <tr>
                  <th scope="col">Fecha</th>
                  <th scope="col">Tipo</th>
                  <th scope="col">Lote</th>
                  <th scope="col" className="numero">Cantidad</th>
                  <th scope="col" className="numero">Costo unitario</th>
                  <th scope="col" className="numero">Saldo</th>
                  <th scope="col" className="numero">Costo promedio</th>
                  <th scope="col">Referencia</th>
                </tr>
              </thead>
              <tbody>
                {filas.map((fila) => (
                  <tr key={fila.id} className={fila.forzadoNegativo === true ? "fila-forzada" : ""}>
                    <td>{formatearFecha(fila.fecha ?? "")}</td>
                    <td>
                      {nombreDeTipo(fila.tipo ?? "")}
                      {fila.movimientoReversadoId != null && (
                        <> <span className="etiqueta">reversa</span></>
                      )}
                      {fila.forzadoNegativo === true && (
                        <> <span className="etiqueta">forzado</span></>
                      )}
                      {/* Los asientos de una misma operación FEFO comparten `operacionId`: se
                          marca para que no parezcan movimientos independientes. */}
                      {fila.operacionId != null && (
                        <> <span className="etiqueta">reparto</span></>
                      )}
                    </td>
                    <td data-prueba="lote">{nombreLote(lotes.data ?? [], fila.loteId)}</td>
                    <td className="numero">
                      {signoDeTipo(fila.tipo ?? "") < 0 ? "−" : "+"}
                      {formatearCantidad(aNumero(fila.cantidad), fila.unidad ?? "")}
                    </td>
                    <td className="numero">
                      {formatearDinero(aNumero(fila.costoUnitario), fila.moneda ?? "COP")}
                    </td>
                    <td
                      className={`numero ${aNumero(fila.saldoCantidadDespues) < 0 ? "negativo" : ""}`}
                    >
                      {formatearCantidad(aNumero(fila.saldoCantidadDespues), fila.unidad ?? "")}
                    </td>
                    <td className="numero">
                      {formatearDinero(aNumero(fila.saldoCostoPromedioDespues), fila.moneda ?? "COP")}
                    </td>
                    <td>{fila.referencia ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Tarjeta>
      )}
    </>
  );
}

/// Un guion y no una celda vacía: «sin lote» es un estado legítimo del producto sin
/// trazabilidad, no un dato que falte por cargar.
function nombreLote(lotes: RespuestaLote[], id: string | null | undefined): string {
  if (id === null || id === undefined) {
    return "—";
  }

  return lotes.find((l) => l.id === id)?.codigo ?? id;
}
