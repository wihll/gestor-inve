using GestorInve.Catalogo;
using GestorInve.Core;
using GestorInve.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Inventario;

/// <summary>
/// Datos de un movimiento simple (entrada, salida o ajuste).
/// <paramref name="LoteId"/> es obligatorio si el producto exige lote o serial, y tiene que ser
/// nulo si no lo exige. La excepción es una **salida sin lote** de un producto con lotes: se
/// interpreta como «elige tú», y el servicio aplica FEFO (ADR 0009).
/// </summary>
public sealed record SolicitudMovimiento(
    TipoMovimiento Tipo,
    Guid VarianteId,
    Guid BodegaId,
    Guid? UbicacionId,
    decimal Cantidad,
    decimal? CostoUnitario,
    DateTimeOffset? Fecha,
    string? Referencia,
    string? Observacion,
    Guid? LoteId = null);

public sealed record SolicitudTraslado(
    Guid VarianteId,
    Guid BodegaOrigenId,
    Guid? UbicacionOrigenId,
    Guid BodegaDestinoId,
    Guid? UbicacionDestinoId,
    decimal Cantidad,
    DateTimeOffset? Fecha,
    string? Referencia,
    string? Observacion,
    Guid? LoteId = null);

public enum FalloMovimiento
{
    Ninguno,
    VarianteInexistente,
    BodegaInexistente,
    UbicacionInexistente,
    SinExistencias,
    CostoRequerido,
    DatosInvalidos,

    /// <summary>
    /// Ya no se usa: la trazabilidad por lotes existe desde H7b. El valor se conserva para no
    /// cambiar el contrato de la Api sin necesidad.
    /// </summary>
    TrazabilidadNoSoportada,
    MovimientoInexistente,
    YaReversado,
    MismaUbicacion,

    /// <summary>El producto exige lote o serial y el movimiento no lo indica.</summary>
    LoteRequerido,

    /// <summary>El producto no exige trazabilidad y el movimiento indica un lote.</summary>
    LoteNoAplica,

    /// <summary>El lote no existe, o no es de esa variante.</summary>
    LoteInexistente,

    /// <summary>La mercancía de ese lote ya venció y no puede salir como despacho normal.</summary>
    LoteVencido,

    /// <summary>Regla propia de los seriales: cantidad distinta de 1, o serial ya existente.</summary>
    SerialInvalido,
}

public sealed record ResultadoMovimiento(FalloMovimiento Fallo, string? Mensaje, IReadOnlyList<Movimiento> Movimientos)
{
    public bool Exitoso => Fallo is FalloMovimiento.Ninguno;

    public static ResultadoMovimiento Ok(params Movimiento[] movimientos) =>
        new(FalloMovimiento.Ninguno, null, movimientos);

    public static ResultadoMovimiento Error(FalloMovimiento fallo, string mensaje) => new(fallo, mensaje, []);
}

/// <summary>
/// Escribe el kardex. Cada operación corre en una transacción que **bloquea la fila de
/// existencias** (<c>SELECT … FOR UPDATE</c>) antes de calcular: sin ese bloqueo, dos salidas
/// simultáneas de la misma celda leen el mismo saldo y el libro deja de cuadrar. Se eligió
/// bloqueo pesimista y no optimista porque reintentar en silencio un asiento contable es peor
/// que serializar las operaciones sobre una misma celda.
///
/// La moneda del costeo es fija por ahora (<see cref="MonedaBase"/>); el multimoneda llega con
/// la fase de ventas y no cambia la forma del libro, porque cada movimiento ya guarda su moneda.
/// </summary>
public sealed class ServicioMovimientos(GestorInveDbContext db, IReloj reloj, IContextoTenant tenant)
{
    public const string MonedaBase = "COP";

    /// <summary>
    /// Registra un movimiento simple. <paramref name="permitirNegativo"/> solo debe venir en
    /// cierto si quien lo pide tiene el permiso de ajuste: la autorización la resuelve el
    /// endpoint, y el dominio no conoce HTTP.
    /// </summary>
    public async Task<ResultadoMovimiento> RegistrarAsync(
        SolicitudMovimiento solicitud,
        bool permitirNegativo = false,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (solicitud.Cantidad <= 0m)
        {
            return ResultadoMovimiento.Error(
                FalloMovimiento.DatosInvalidos,
                "La cantidad del movimiento debe ser positiva.");
        }

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        var contexto = await ResolverAsync(solicitud.VarianteId, solicitud.BodegaId, solicitud.UbicacionId, ct);

        if (contexto.Fallo is not FalloMovimiento.Ninguno)
        {
            return ResultadoMovimiento.Error(contexto.Fallo, contexto.Mensaje!);
        }

        var fechaMovimiento = solicitud.Fecha ?? reloj.Ahora;

        // Una salida sin lote de un producto con lotes significa «elige tú»: FEFO. Un serial no
        // entra por aquí, porque elegir un serial automáticamente no tiene sentido: quien
        // despacha una unidad identificada sabe cuál es.
        if (contexto.ExigeLote && !contexto.ExigeSerial && solicitud.LoteId is null && !solicitud.Tipo.Suma())
        {
            var fefo = await SalidaFefoAsync(contexto, solicitud, fechaMovimiento, ct);

            if (fefo.Exitoso)
            {
                await transaccion.CommitAsync(ct);
            }

            return fefo;
        }

        var (celda, lote) = await ResolverLoteAsync(
            contexto, solicitud.LoteId, solicitud.Tipo, fechaMovimiento, ct);

        if (celda.Fallo is not FalloMovimiento.Ninguno)
        {
            return ResultadoMovimiento.Error(celda.Fallo, celda.Mensaje!);
        }

        if (await ValidarSerialAsync(celda, lote, solicitud.Tipo, solicitud.Cantidad, ct) is { } falloSerial)
        {
            return falloSerial;
        }

        contexto = celda;

        var cantidad = CrearCantidad(solicitud.Cantidad, contexto.Unidad!.Value, out var errorCantidad);

        if (errorCantidad is not null)
        {
            return ResultadoMovimiento.Error(FalloMovimiento.DatosInvalidos, errorCantidad);
        }

        var existencia = await BloquearExistenciaAsync(contexto, ct);
        var saldo = existencia.Saldo();

        Dinero costoUnitario;

        if (solicitud.Tipo.Suma())
        {
            // Una entrada necesita costo; un ajuste positivo puede heredar el promedio vigente.
            if (solicitud.CostoUnitario is { } costo)
            {
                if (costo < 0m)
                {
                    return ResultadoMovimiento.Error(
                        FalloMovimiento.DatosInvalidos,
                        "El costo unitario no puede ser negativo.");
                }

                costoUnitario = new Dinero(costo, MonedaBase);
            }
            else if (solicitud.Tipo is TipoMovimiento.AjustePositivo && saldo.Cantidad.Valor > 0m)
            {
                costoUnitario = saldo.CostoPromedio;
            }
            else
            {
                return ResultadoMovimiento.Error(
                    FalloMovimiento.CostoRequerido,
                    "Una entrada sin existencias previas necesita su costo unitario.");
            }

            saldo = saldo.AplicarEntrada(cantidad, costoUnitario);
        }
        else
        {
            costoUnitario = saldo.CostoDeSalida();

            if (cantidad.Valor > saldo.Cantidad.Valor)
            {
                if (!permitirNegativo)
                {
                    return ResultadoMovimiento.Error(
                        FalloMovimiento.SinExistencias,
                        $"No hay existencias suficientes: hay {saldo.Cantidad.Valor} y se piden {cantidad.Valor}.");
                }

                saldo = saldo.AplicarSalidaForzada(cantidad);
            }
            else
            {
                saldo = saldo.AplicarSalida(cantidad);
            }
        }

        var forzado = !solicitud.Tipo.Suma() && existencia.Saldo().Cantidad.Valor < cantidad.Valor;

        var movimiento = Movimiento.Registrar(
            solicitud.Tipo,
            solicitud.VarianteId,
            solicitud.BodegaId,
            contexto.UbicacionId,
            contexto.LoteId,
            cantidad,
            costoUnitario,
            saldo,
            solicitud.Fecha ?? reloj.Ahora,
            tenant.Usuario,
            solicitud.Referencia,
            solicitud.Observacion,
            forzadoNegativo: forzado);

        db.Add(movimiento);
        existencia.Aplicar(saldo, reloj.Ahora);

        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        return ResultadoMovimiento.Ok(movimiento);
    }

    /// <summary>
    /// Salida de un producto con lotes sin lote indicado: se consume por **vencimiento más
    /// próximo primero** (FEFO), y se registra **un asiento por lote**, todos agrupados por
    /// <c>OperacionId</c>.
    ///
    /// Partir el movimiento es lo único correcto: un asiento del kardex dice «salieron 30 de
    /// este lote y quedó este saldo»; uno que dijera «salieron 30 de varios lotes» no permitiría
    /// reconstruir el saldo de ninguno, que es justo para lo que existe la trazabilidad.
    ///
    /// **No fuerza negativos.** Si no hay suficiente, falla: forzar un saldo negativo requiere
    /// decidir sobre qué lote cargarlo, y esa decisión es de quien ajusta, no del algoritmo. Para
    /// eso la salida tiene que nombrar su lote.
    /// </summary>
    private async Task<ResultadoMovimiento> SalidaFefoAsync(
        ContextoCelda contexto,
        SolicitudMovimiento solicitud,
        DateTimeOffset fecha,
        CancellationToken ct)
    {
        var dia = DateOnly.FromDateTime(fecha.UtcDateTime);

        // Se bloquean **todas** las celdas candidatas en una sola sentencia y ordenadas por `id`.
        // Hacerlo fila a fila en orden FEFO expondría a un abrazo mortal contra una salida que
        // nombra su lote y las bloquea en otro orden; una sola sentencia con orden fijo, no.
        var celdas = await db.Set<Existencia>()
            .FromSql($"""
                select * from inventario.existencias
                where variante_id = {contexto.VarianteId}
                  and bodega_id = {contexto.BodegaId}
                  and ubicacion_id = {contexto.UbicacionId}
                  and lote_id is not null
                  and cantidad > 0
                order by id
                for update
                """)
            .ToListAsync(ct);

        var lotesPorId = await db.Set<Lote>()
            .Where(l => l.VarianteId == contexto.VarianteId)
            .ToDictionaryAsync(l => l.Id, ct);

        // Un lote vencido no se elige nunca: si FEFO lo tomara, la salida fallaría siempre en
        // cuanto hubiera mercancía caducada en la bodega. Los sin vencimiento van al final.
        var candidatas = celdas
            .Where(x => x.LoteId is { } id
                        && lotesPorId.TryGetValue(id, out var lote)
                        && !lote.EstaVencido(dia))
            .OrderBy(x => lotesPorId[x.LoteId!.Value].Vencimiento ?? DateOnly.MaxValue)
            .ThenBy(x => lotesPorId[x.LoteId!.Value].Codigo, StringComparer.Ordinal)
            .ToList();

        var disponible = candidatas.Sum(x => x.Cantidad);

        if (solicitud.Cantidad > disponible)
        {
            return ResultadoMovimiento.Error(
                FalloMovimiento.SinExistencias,
                $"No hay existencias suficientes en lotes vigentes: hay {disponible} y se piden {solicitud.Cantidad}. " +
                "Para sacar de un lote vencido, indique el lote y use un ajuste negativo.");
        }

        var operacionId = Guid.CreateVersion7();
        var restante = solicitud.Cantidad;
        var asientos = new List<Movimiento>();

        foreach (var existencia in candidatas)
        {
            if (restante <= 0m)
            {
                break;
            }

            var tomar = Math.Min(restante, existencia.Cantidad);
            var cantidad = CrearCantidad(tomar, contexto.Unidad!.Value, out var error);

            if (error is not null)
            {
                return ResultadoMovimiento.Error(FalloMovimiento.DatosInvalidos, error);
            }

            var saldo = existencia.Saldo();
            var costo = saldo.CostoDeSalida();
            var despues = saldo.AplicarSalida(cantidad);

            var asiento = Movimiento.Registrar(
                solicitud.Tipo,
                contexto.VarianteId,
                contexto.BodegaId,
                contexto.UbicacionId,
                existencia.LoteId,
                cantidad,
                costo,
                despues,
                fecha,
                tenant.Usuario,
                solicitud.Referencia,
                solicitud.Observacion,
                operacionId: operacionId);

            db.Add(asiento);
            existencia.Aplicar(despues, reloj.Ahora);
            asientos.Add(asiento);

            restante -= tomar;
        }

        await db.SaveChangesAsync(ct);

        return ResultadoMovimiento.Ok([.. asientos]);
    }

    /// <summary>
    /// Traslado: dos movimientos en una sola transacción, agrupados por <c>TrasladoId</c>. La
    /// entrada hereda el costo de la salida, porque mover mercancía no cambia lo que costó.
    /// </summary>
    public async Task<ResultadoMovimiento> TrasladarAsync(
        SolicitudTraslado solicitud,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (solicitud.Cantidad <= 0m)
        {
            return ResultadoMovimiento.Error(
                FalloMovimiento.DatosInvalidos,
                "La cantidad del traslado debe ser positiva.");
        }

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        var origen = await ResolverAsync(solicitud.VarianteId, solicitud.BodegaOrigenId, solicitud.UbicacionOrigenId, ct);

        if (origen.Fallo is not FalloMovimiento.Ninguno)
        {
            return ResultadoMovimiento.Error(origen.Fallo, origen.Mensaje!);
        }

        var destino = await ResolverAsync(solicitud.VarianteId, solicitud.BodegaDestinoId, solicitud.UbicacionDestinoId, ct);

        if (destino.Fallo is not FalloMovimiento.Ninguno)
        {
            return ResultadoMovimiento.Error(destino.Fallo, destino.Mensaje!);
        }

        if (origen.UbicacionId == destino.UbicacionId)
        {
            return ResultadoMovimiento.Error(
                FalloMovimiento.MismaUbicacion,
                "El origen y el destino del traslado son la misma ubicación.");
        }

        // Mover mercancía no cambia su lote ni su vencimiento, así que el lote es **el mismo** en
        // los dos extremos y no se elige por FEFO: un traslado mueve un lote concreto. Tampoco
        // se juzga el vencimiento: mover mercancía caducada dentro de la bodega (por ejemplo a
        // una ubicación de cuarentena) es exactamente lo que hay que poder hacer con ella.
        var (celdaOrigen, lote) = await ResolverLoteAsync(
            origen, solicitud.LoteId, TipoMovimiento.TrasladoEntrada, solicitud.Fecha ?? reloj.Ahora, ct);

        if (celdaOrigen.Fallo is not FalloMovimiento.Ninguno)
        {
            return ResultadoMovimiento.Error(celdaOrigen.Fallo, celdaOrigen.Mensaje!);
        }

        if (await ValidarSerialAsync(celdaOrigen, lote, TipoMovimiento.TrasladoSalida, solicitud.Cantidad, ct)
            is { } falloSerial)
        {
            return falloSerial;
        }

        origen = celdaOrigen;
        destino = destino.ConLote(celdaOrigen.LoteId);

        var cantidad = CrearCantidad(solicitud.Cantidad, origen.Unidad!.Value, out var errorCantidad);

        if (errorCantidad is not null)
        {
            return ResultadoMovimiento.Error(FalloMovimiento.DatosInvalidos, errorCantidad);
        }

        // Se bloquean las dos celdas en orden de identificador: el mismo orden en todas las
        // operaciones evita un abrazo mortal entre dos traslados cruzados.
        var (primera, segunda) = origen.UbicacionId.CompareTo(destino.UbicacionId) < 0
            ? (origen, destino)
            : (destino, origen);

        var bloqueada1 = await BloquearExistenciaAsync(primera, ct);
        var bloqueada2 = await BloquearExistenciaAsync(segunda, ct);

        var existenciaOrigen = primera.UbicacionId == origen.UbicacionId ? bloqueada1 : bloqueada2;
        var existenciaDestino = primera.UbicacionId == origen.UbicacionId ? bloqueada2 : bloqueada1;

        var saldoOrigen = existenciaOrigen.Saldo();

        if (cantidad.Valor > saldoOrigen.Cantidad.Valor)
        {
            return ResultadoMovimiento.Error(
                FalloMovimiento.SinExistencias,
                $"No hay existencias suficientes en el origen: hay {saldoOrigen.Cantidad.Valor} y se piden {cantidad.Valor}.");
        }

        var costo = saldoOrigen.CostoDeSalida();
        var trasladoId = Guid.CreateVersion7();
        var fecha = solicitud.Fecha ?? reloj.Ahora;

        var saldoOrigenDespues = saldoOrigen.AplicarSalida(cantidad);
        var saldoDestinoDespues = existenciaDestino.Saldo().AplicarEntrada(cantidad, costo);

        var salida = Movimiento.Registrar(
            TipoMovimiento.TrasladoSalida,
            solicitud.VarianteId,
            solicitud.BodegaOrigenId,
            origen.UbicacionId,
            origen.LoteId,
            cantidad,
            costo,
            saldoOrigenDespues,
            fecha,
            tenant.Usuario,
            solicitud.Referencia,
            solicitud.Observacion,
            trasladoId);

        var entrada = Movimiento.Registrar(
            TipoMovimiento.TrasladoEntrada,
            solicitud.VarianteId,
            solicitud.BodegaDestinoId,
            destino.UbicacionId,
            destino.LoteId,
            cantidad,
            costo,
            saldoDestinoDespues,
            fecha,
            tenant.Usuario,
            solicitud.Referencia,
            solicitud.Observacion,
            trasladoId);

        db.Add(salida);
        db.Add(entrada);
        existenciaOrigen.Aplicar(saldoOrigenDespues, reloj.Ahora);
        existenciaDestino.Aplicar(saldoDestinoDespues, reloj.Ahora);

        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        return ResultadoMovimiento.Ok(salida, entrada);
    }

    /// <summary>
    /// Reversa un movimiento creando su opuesto. **No modifica el original**: el kardex es
    /// inmutable. Usa el costo del movimiento reversado y no el promedio actual, porque deshacer
    /// una salida devuelve la mercancía al costo con el que salió.
    /// </summary>
    public async Task<ResultadoMovimiento> ReversarAsync(
        Guid movimientoId,
        string? observacion,
        bool permitirNegativo = false,
        CancellationToken ct = default)
    {
        await using var transaccion = await db.Database.BeginTransactionAsync(ct);

        var original = await db.Set<Movimiento>().SingleOrDefaultAsync(m => m.Id == movimientoId, ct);

        if (original is null)
        {
            return ResultadoMovimiento.Error(FalloMovimiento.MovimientoInexistente, "El movimiento no existe.");
        }

        if (await db.Set<Movimiento>().AnyAsync(m => m.MovimientoReversadoId == movimientoId, ct))
        {
            return ResultadoMovimiento.Error(FalloMovimiento.YaReversado, "El movimiento ya fue reversado.");
        }

        var contexto = await ResolverAsync(original.VarianteId, original.BodegaId, original.UbicacionId, ct);

        if (contexto.Fallo is not FalloMovimiento.Ninguno)
        {
            return ResultadoMovimiento.Error(contexto.Fallo, contexto.Mensaje!);
        }

        // La reversa toma el lote **del asiento original**, sin volver a pasar por las reglas de
        // trazabilidad: deshacer un movimiento que ya está en el libro no puede quedar bloqueado
        // porque entretanto el lote venciera o el producto dejara de exigir lote. Si el libro
        // registró esa salida de ese lote, la corrección tiene que poder entrar por ahí.
        contexto = contexto.ConLote(original.LoteId);

        var existencia = await BloquearExistenciaAsync(contexto, ct);
        var saldo = existencia.Saldo();
        var cantidad = original.ComoCantidad();
        var tipo = original.Tipo.Opuesto();
        var costo = original.ComoCostoUnitario();

        var forzado = false;

        if (tipo.Suma())
        {
            saldo = saldo.AplicarEntrada(cantidad, costo);
        }
        else if (cantidad.Valor > saldo.Cantidad.Valor)
        {
            if (!permitirNegativo)
            {
                return ResultadoMovimiento.Error(
                    FalloMovimiento.SinExistencias,
                    $"Reversar dejaría el saldo negativo: hay {saldo.Cantidad.Valor} y habría que sacar {cantidad.Valor}.");
            }

            saldo = saldo.AplicarSalidaForzada(cantidad);
            forzado = true;
        }
        else
        {
            saldo = saldo.AplicarSalida(cantidad);
        }

        var reversa = Movimiento.Registrar(
            tipo,
            original.VarianteId,
            original.BodegaId,
            original.UbicacionId,
            original.LoteId,
            cantidad,
            costo,
            saldo,
            reloj.Ahora,
            tenant.Usuario,
            original.Referencia,
            observacion ?? $"Reversa de {original.Id}",
            original.TrasladoId,
            movimientoId,
            forzado);

        db.Add(reversa);
        existencia.Aplicar(saldo, reloj.Ahora);

        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);

        return ResultadoMovimiento.Ok(reversa);
    }

    private sealed record ContextoCelda(
        FalloMovimiento Fallo,
        string? Mensaje,
        Guid VarianteId,
        Guid BodegaId,
        Guid UbicacionId,
        Unidad? Unidad,
        bool ExigeLote = false,
        bool ExigeSerial = false,
        Guid? LoteId = null)
    {
        public bool ExigeTrazabilidad => ExigeLote || ExigeSerial;

        public ContextoCelda ConLote(Guid? loteId) => this with { LoteId = loteId };
    }

    private static Cantidad CrearCantidad(decimal valor, Unidad unidad, out string? error)
    {
        try
        {
            error = null;
            return new Cantidad(valor, unidad);
        }
        catch (ArgumentException excepcion)
        {
            error = excepcion.Message;
            return default;
        }
    }

    /// <summary>
    /// Comprueba que la variante, la bodega y la ubicación existan **en esta empresa** (el filtro
    /// y el RLS se encargan) y devuelve la unidad del producto. Si no se indica ubicación, usa la
    /// general de la bodega.
    /// </summary>
    private async Task<ContextoCelda> ResolverAsync(
        Guid varianteId,
        Guid bodegaId,
        Guid? ubicacionId,
        CancellationToken ct)
    {
        var variante = await db.Set<Variante>().SingleOrDefaultAsync(v => v.Id == varianteId, ct);

        if (variante is null)
        {
            return new ContextoCelda(FalloMovimiento.VarianteInexistente, "La variante no existe.", varianteId, bodegaId, Guid.Empty, null);
        }

        var producto = await db.Set<Producto>().SingleOrDefaultAsync(p => p.Id == variante.ProductoId, ct);

        if (producto is null)
        {
            return new ContextoCelda(FalloMovimiento.VarianteInexistente, "El producto de la variante no existe.", varianteId, bodegaId, Guid.Empty, null);
        }

        var unidadMedida = await db.Set<UnidadMedida>()
            .SingleOrDefaultAsync(u => u.Id == producto.UnidadMedidaId, ct);

        if (unidadMedida is null)
        {
            return new ContextoCelda(FalloMovimiento.VarianteInexistente, "La unidad de medida del producto no existe.", varianteId, bodegaId, Guid.Empty, null);
        }

        var bodega = await db.Set<Bodega>().SingleOrDefaultAsync(b => b.Id == bodegaId, ct);

        if (bodega is null)
        {
            return new ContextoCelda(FalloMovimiento.BodegaInexistente, "La bodega no existe.", varianteId, bodegaId, Guid.Empty, null);
        }

        var ubicacion = ubicacionId is { } pedida
            ? await db.Set<Ubicacion>().SingleOrDefaultAsync(u => u.Id == pedida && u.BodegaId == bodegaId, ct)
            : await db.Set<Ubicacion>().SingleOrDefaultAsync(
                u => u.BodegaId == bodegaId && u.Codigo == Ubicacion.CodigoGeneral,
                ct);

        if (ubicacion is null)
        {
            return new ContextoCelda(FalloMovimiento.UbicacionInexistente, "La ubicación no existe en esa bodega.", varianteId, bodegaId, Guid.Empty, null);
        }

        return new ContextoCelda(
            FalloMovimiento.Ninguno,
            null,
            varianteId,
            bodegaId,
            ubicacion.Id,
            unidadMedida.ComoUnidad(),
            producto.ExigeLote,
            producto.ExigeSerial);
    }

    /// <summary>
    /// Aplica las reglas de trazabilidad del ADR 0009 y devuelve la celda con su lote resuelto.
    /// Devuelve <c>null</c> en <paramref name="lote"/> cuando el producto no exige trazabilidad.
    ///
    /// La fecha con la que se juzga el vencimiento es la **del movimiento**, no la de hoy: un
    /// asiento con fecha de negocio anterior tiene que juzgarse con la fecha que le corresponde,
    /// o registrar ayer sería imposible en cuanto el lote venciera hoy.
    /// </summary>
    private async Task<(ContextoCelda Celda, Lote? Lote)> ResolverLoteAsync(
        ContextoCelda celda,
        Guid? loteId,
        TipoMovimiento tipo,
        DateTimeOffset fecha,
        CancellationToken ct)
    {
        if (!celda.ExigeTrazabilidad)
        {
            // No se acepta una dimensión que el producto no declara: crearía existencias que
            // ninguna pantalla sabría mostrar y que el saldo sin lote no contaría.
            return loteId is null
                ? (celda.ConLote(null), null)
                : (celda with
                {
                    Fallo = FalloMovimiento.LoteNoAplica,
                    Mensaje = "El producto no exige lote ni serial: el movimiento no debe indicar lote.",
                }, null);
        }

        if (loteId is not { } pedido)
        {
            return (celda with
            {
                Fallo = FalloMovimiento.LoteRequerido,
                Mensaje = "El producto exige lote o serial y el movimiento no lo indica.",
            }, null);
        }

        var lote = await db.Set<Lote>().SingleOrDefaultAsync(l => l.Id == pedido, ct);

        if (lote is null || lote.VarianteId != celda.VarianteId)
        {
            // El mismo mensaje para «no existe» y «es de otra variante»: distinguirlos le diría
            // a quien prueba identificadores ajenos que ese lote existe en otra empresa.
            return (celda with
            {
                Fallo = FalloMovimiento.LoteInexistente,
                Mensaje = "El lote no existe para esa variante.",
            }, null);
        }

        // Un lote vencido **no sale** como despacho. La baja existe y es explícita: un
        // AjusteNegativo, que exige el permiso de ajuste y queda en el libro como lo que es.
        if (!tipo.Suma() && !tipo.EsAjuste() && lote.EstaVencido(DateOnly.FromDateTime(fecha.UtcDateTime)))
        {
            return (celda with
            {
                Fallo = FalloMovimiento.LoteVencido,
                Mensaje =
                    $"El lote {lote.Codigo} venció el {lote.Vencimiento:yyyy-MM-dd}: " +
                    "para darlo de baja use un ajuste negativo.",
            }, null);
        }

        return (celda.ConLote(lote.Id), lote);
    }

    /// <summary>
    /// Reglas propias de un serial: cada unidad es única, así que la cantidad solo puede ser 1 y
    /// un serial no puede estar en dos sitios a la vez. Si pudiera, dejaría de identificar nada.
    /// </summary>
    private async Task<ResultadoMovimiento?> ValidarSerialAsync(
        ContextoCelda celda,
        Lote? lote,
        TipoMovimiento tipo,
        decimal cantidad,
        CancellationToken ct)
    {
        if (lote is null || !celda.ExigeSerial)
        {
            return null;
        }

        if (cantidad != 1m)
        {
            return ResultadoMovimiento.Error(
                FalloMovimiento.SerialInvalido,
                $"El producto se identifica por serial: la cantidad debe ser 1 y se pidieron {cantidad}.");
        }

        if (!tipo.Suma())
        {
            return null;
        }

        var yaTieneExistencia = await db.Set<Existencia>()
            .AnyAsync(x => x.LoteId == lote.Id && x.Cantidad > 0m, ct);

        return yaTieneExistencia
            ? ResultadoMovimiento.Error(
                FalloMovimiento.SerialInvalido,
                $"El serial {lote.Codigo} ya tiene existencia: un serial no puede estar en dos sitios.")
            : null;
    }

    /// <summary>
    /// Trae la fila de existencias **bloqueada para escritura**. Si no existe, la crea; una
    /// colisión con otra transacción que la cree a la vez se resuelve volviendo a leerla.
    /// </summary>
    private async Task<Existencia> BloquearExistenciaAsync(ContextoCelda celda, CancellationToken ct)
    {
        // `is not distinct from` y no `=`: con un lote nulo, `lote_id = null` nunca es cierto y
        // la consulta no encontraría la fila que sí existe, de modo que cada movimiento crearía
        // una existencia nueva. Es la contraparte del `nulls not distinct` del índice.
        var existente = await db.Set<Existencia>()
            .FromSql($"""
                select * from inventario.existencias
                where variante_id = {celda.VarianteId}
                  and bodega_id = {celda.BodegaId}
                  and ubicacion_id = {celda.UbicacionId}
                  and lote_id is not distinct from {celda.LoteId}
                for update
                """)
            .FirstOrDefaultAsync(ct);

        if (existente is not null)
        {
            return existente;
        }

        var nueva = Existencia.Nueva(
            celda.VarianteId,
            celda.BodegaId,
            celda.UbicacionId,
            celda.LoteId,
            celda.Unidad!.Value,
            MonedaBase);

        db.Add(nueva);
        await db.SaveChangesAsync(ct);

        return nueva;
    }
}
