import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useSesion } from "../sesion/SesionContexto";
import type {
  PeticionProducto,
  RespuestaProducto,
  RespuestaUnidad,
  RespuestaVariante,
} from "../api/cliente";
import { Aviso, Campo, Cargando, Tarjeta, Vacio } from "../componentes/Piezas";

export function Catalogo() {
  const { cliente, puede } = useSesion();
  const cola = useQueryClient();
  const puedeEscribir = puede("catalogo:escribir");

  const unidades = useQuery({
    queryKey: ["unidades"],
    queryFn: () => cliente.get<RespuestaUnidad[]>("/catalogo/unidades"),
  });

  const productos = useQuery({
    queryKey: ["productos"],
    queryFn: () => cliente.get<RespuestaProducto[]>("/catalogo/productos"),
  });

  const [error, setError] = useState<string | null>(null);

  const crearUnidad = useMutation({
    mutationFn: (datos: { codigo: string; nombre: string; decimales: number }) =>
      cliente.post<RespuestaUnidad>("/catalogo/unidades", datos),
    onSuccess: () => void cola.invalidateQueries({ queryKey: ["unidades"] }),
    onError: (fallo: Error) => setError(fallo.message),
  });

  const crearProducto = useMutation({
    mutationFn: (datos: PeticionProducto) =>
      cliente.post<RespuestaProducto>("/catalogo/productos", datos),
    onSuccess: () => void cola.invalidateQueries({ queryKey: ["productos"] }),
    onError: (fallo: Error) => setError(fallo.message),
  });

  return (
    <>
      <h1>Catálogo</h1>
      {error !== null && <Aviso tipo="error">{error}</Aviso>}

      {puedeEscribir && (
        <div className="rejilla dos">
          <FormularioUnidad
            enviando={crearUnidad.isPending}
            onCrear={(datos) => {
              setError(null);
              crearUnidad.mutate(datos);
            }}
          />
          <FormularioProducto
            unidades={unidades.data ?? []}
            enviando={crearProducto.isPending}
            onCrear={(datos) => {
              setError(null);
              crearProducto.mutate(datos);
            }}
          />
        </div>
      )}

      <Tarjeta titulo="Productos">
        {productos.isPending ? (
          <Cargando que="los productos" />
        ) : (productos.data?.length ?? 0) === 0 ? (
          <Vacio>Todavía no hay productos. Cree una unidad y luego un producto.</Vacio>
        ) : (
          <div className="tabla-contenedor">
            <table>
              <caption className="vacio">Productos con sus variantes</caption>
              <thead>
                <tr>
                  <th scope="col">SKU</th>
                  <th scope="col">Nombre</th>
                  <th scope="col">Variantes</th>
                  <th scope="col">Trazabilidad</th>
                </tr>
              </thead>
              <tbody>
                {productos.data?.map((producto) => (
                  <tr key={producto.id}>
                    <td>{producto.sku}</td>
                    <td>{producto.nombre}</td>
                    <td data-prueba="variantes">
                      {producto.variantes?.map((v: RespuestaVariante) => v.sku).join(", ")}
                    </td>
                    <td>
                      {producto.exigeLote === true && <span className="etiqueta">lote</span>}{" "}
                      {producto.exigeSerial === true && <span className="etiqueta">serial</span>}
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

function FormularioUnidad({
  enviando,
  onCrear,
}: {
  enviando: boolean;
  onCrear: (datos: { codigo: string; nombre: string; decimales: number }) => void;
}) {
  const [codigo, setCodigo] = useState("UND");
  const [nombre, setNombre] = useState("Unidad");
  const [decimales, setDecimales] = useState(0);

  return (
    <Tarjeta titulo="Nueva unidad de medida">
      <form
        onSubmit={(evento) => {
          evento.preventDefault();
          onCrear({ codigo, nombre, decimales });
        }}
      >
        <Campo etiqueta="Código">
          <input value={codigo} onChange={(e) => setCodigo(e.target.value)} required maxLength={10} />
        </Campo>

        <Campo etiqueta="Nombre">
          <input value={nombre} onChange={(e) => setNombre(e.target.value)} required />
        </Campo>

        <Campo etiqueta="Decimales que admite (0 para piezas enteras)">
          <input
            type="number"
            min={0}
            max={6}
            value={decimales}
            onChange={(e) => setDecimales(Number(e.target.value))}
            required
          />
        </Campo>

        <button type="submit" disabled={enviando}>
          Crear unidad
        </button>
      </form>
    </Tarjeta>
  );
}

function FormularioProducto({
  unidades,
  enviando,
  onCrear,
}: {
  unidades: RespuestaUnidad[];
  enviando: boolean;
  onCrear: (datos: PeticionProducto) => void;
}) {
  const [sku, setSku] = useState("");
  const [nombre, setNombre] = useState("");
  const [unidadMedidaId, setUnidad] = useState("");
  const [atributos, setAtributos] = useState("");

  const sinUnidades = unidades.length === 0;

  return (
    <Tarjeta titulo="Nuevo producto">
      <form
        onSubmit={(evento) => {
          evento.preventDefault();
          onCrear({
            sku,
            nombre,
            unidadMedidaId,
            descripcion: null,
            categoriaId: null,
            // Sin atributos el producto nace con su variante única; con atributos
            // hay que añadir después cada combinación.
            atributos:
              atributos.trim() === ""
                ? null
                : atributos.split(",").map((a) => a.trim()).filter((a) => a !== ""),
            exigeLote: false,
            exigeSerial: false,
          });
          setSku("");
          setNombre("");
        }}
      >
        {sinUnidades && <Aviso tipo="error">Cree primero una unidad de medida.</Aviso>}

        <Campo etiqueta="SKU">
          <input value={sku} onChange={(e) => setSku(e.target.value)} required />
        </Campo>

        <Campo etiqueta="Nombre">
          <input value={nombre} onChange={(e) => setNombre(e.target.value)} required />
        </Campo>

        <Campo etiqueta="Unidad de medida">
          <select
            value={unidadMedidaId}
            onChange={(e) => setUnidad(e.target.value)}
            required
            disabled={sinUnidades}
          >
            <option value="">Seleccione…</option>
            {unidades.map((unidad) => (
              <option key={unidad.id} value={unidad.id ?? ""}>
                {unidad.codigo} — {unidad.nombre}
              </option>
            ))}
          </select>
        </Campo>

        <Campo etiqueta="Atributos de variante, separados por comas (opcional)">
          <input
            value={atributos}
            onChange={(e) => setAtributos(e.target.value)}
            placeholder="talla, color"
          />
        </Campo>

        <button type="submit" disabled={enviando || sinUnidades}>
          Crear producto
        </button>
      </form>
    </Tarjeta>
  );
}
