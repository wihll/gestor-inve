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
        var metodo = await MetodoAsync(ct);

        // Una salida sin lote de un producto con lotes significa «elige tú»: FEFO. Un serial no
        // entra por aquí, porque elegir un serial automáticamente no tiene sentido: quien
        // despacha una unidad identificada sabe cuál es.
        if (contexto.ExigeLote && !contexto.ExigeSerial && solicitud.LoteId is null && !solicitud.Tipo.Suma())
        {
            var fefo = await SalidaFefoAsync(contexto, solicitud, fechaMovimiento, metodo, ct);

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
        var costeo = await CargarCosteoAsync(contexto, existencia, metodo, ct);
        var saldoAntes = existencia.Saldo();

        SaldoValorizado saldo;
        Dinero costoUnitario;
        Dinero? costoTotal = null;

        if (solicitud.Tipo.Suma())
        {
            // Una entrada necesita costo; un ajuste positivo puede heredar el costo vigente.
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
            else if (solicitud.Tipo is TipoMovimiento.AjustePositivo && saldoAntes.Cantidad.Valor > 0m)
            {
                costoUnitario = costeo.CostoDeSalida();
            }
            else
            {
                return ResultadoMovimiento.Error(
                    FalloMovimiento.CostoRequerido,
                    "Una entrada sin existencias previas necesita su costo unitario.");
            }

            saldo = costeo.Entrada(cantidad, costoUnitario);
        }
        else
        {
            var excede = cantidad.Valor > saldoAntes.Cantidad.Valor;

            if (excede && !permitirNegativo)
            {
                return ResultadoMovimiento.Error(
                    FalloMovimiento.SinExistencias,
                    $"No hay existencias suficientes: hay {saldoAntes.Cantidad.Valor} y se piden {cantidad.Valor}.");
            }

            var resultado = costeo.Salida(cantidad, forzar: excede);
            (saldo, costoUnitario, costoTotal) = (resultado.Saldo, resultado.CostoUnitario, resultado.CostoTotal);
        }

        var forzado = !solicitud.Tipo.Suma() && saldoAntes.Cantidad.Valor < cantidad.Valor;

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
            forzadoNegativo: forzado,
            costoTotal: costoTotal);

        db.Add(movimiento);
        existencia.Aplicar(saldo, reloj.Ahora);

        foreach (var capa in costeo.CapasDe(movimiento, contexto))
        {
            db.Add(capa);
        }

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
        MetodoCosteo metodo,
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

            // Cada celda es un lote distinto, así que tiene sus propias capas: el costeo se carga
            // por celda y no una vez para todo el reparto.
            var costeo = await CargarCosteoAsync(contexto.ConLote(existencia.LoteId), existencia, metodo, ct);
            var resultado = costeo.Salida(cantidad, forzar: false);
            var (despues, costo, costoTotal) = (resultado.Saldo, resultado.CostoUnitario, resultado.CostoTotal);

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
                operacionId: operacionId,
                costoTotal: costoTotal);

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

        var metodo = await MetodoAsync(ct);
        var costeoOrigen = await CargarCosteoAsync(origen, existenciaOrigen, metodo, ct);
        var costeoDestino = await CargarCosteoAsync(destino, existenciaDestino, metodo, ct);

        var trasladoId = Guid.CreateVersion7();
        var fecha = solicitud.Fecha ?? reloj.Ahora;

        var salidaOrigen = costeoOrigen.Salida(cantidad, forzar: false);
        var costo = salidaOrigen.CostoUnitario;
        var saldoOrigenDespues = salidaOrigen.Saldo;

        // El destino recibe los **tramos** que salieron, cada uno con su costo y su antigüedad,
        // en vez de una sola entrada al costo ponderado. Mover mercancía de bodega no es un hecho
        // económico: no debe cambiar lo que costará cada unidad al salir de verdad.
        var saldoDestinoDespues = salidaOrigen.Consumido.Count > 0
            ? costeoDestino.RecibirTramos(salidaOrigen.Consumido, costo)
            : costeoDestino.Entrada(cantidad, costo);

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
            trasladoId,
            costoTotal: salidaOrigen.CostoTotal);

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
            trasladoId,
            costoTotal: salidaOrigen.CostoTotal);

        db.Add(salida);
        db.Add(entrada);
        existenciaOrigen.Aplicar(saldoOrigenDespues, reloj.Ahora);
        existenciaDestino.Aplicar(saldoDestinoDespues, reloj.Ahora);

        foreach (var capa in costeoDestino.CapasDe(entrada, destino))
        {
            db.Add(capa);
        }

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
        var metodo = await MetodoAsync(ct);
        var costeo = await CargarCosteoAsync(contexto, existencia, metodo, ct);

        var saldoAntes = existencia.Saldo();
        var cantidad = original.ComoCantidad();
        var tipo = original.Tipo.Opuesto();

        // La reversa usa el costo **del asiento original**, no el vigente: deshacer una salida
        // devuelve la mercancía al costo con el que salió. Eso vale para los dos métodos.
        var costo = original.ComoCostoUnitario();

        SaldoValorizado saldo;
        Dinero? costoTotal = null;
        var forzado = false;

        if (tipo.Suma())
        {
            // Bajo PEPS la capa que repone la reversa lleva la **fecha del asiento original**, no
            // la de hoy: si llevara la de hoy se iría al final de la cola y la mercancía devuelta
            // saldría después de lo que entró entretanto, que es justo lo contrario de PEPS.
            saldo = costeo.RecibirTramos([new TramoConsumido(cantidad, costo, original.Fecha)], costo);
        }
        else
        {
            var excede = cantidad.Valor > saldoAntes.Cantidad.Valor;

            if (excede && !permitirNegativo)
            {
                return ResultadoMovimiento.Error(
                    FalloMovimiento.SinExistencias,
                    $"Reversar dejaría el saldo negativo: hay {saldoAntes.Cantidad.Valor} y habría que sacar {cantidad.Valor}.");
            }

            // Reversar una **entrada** bajo PEPS consume por antigüedad, como cualquier salida, y
            // no deshace exactamente la capa que creó aquella entrada: si ya se consumió en parte,
            // esas salidas se asentaron con ese costo y el libro es inmutable. El valor total se
            // conserva; la composición de las capas no se puede reconstruir, y se asume.
            var resultado = costeo.Salida(cantidad, forzar: excede);
            saldo = resultado.Saldo;
            costoTotal = resultado.CostoTotal;
            forzado = excede;
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
            forzado,
            costoTotal: costoTotal);

        db.Add(reversa);
        existencia.Aplicar(saldo, reloj.Ahora);

        foreach (var capa in costeo.CapasDe(reversa, contexto))
        {
            db.Add(capa);
        }

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
    /// Fija el método de costeo de la empresa. **Falla si ya hay movimientos registrados.**
    ///
    /// La regla vive aquí y no en el endpoint a propósito: un segundo llamador —una importación,
    /// un script de migración de datos— la saltaría sin enterarse. PEPS y promedio dan costos de
    /// salida distintos para la misma historia, así que cambiarlo recalcularía costos ya
    /// asentados, y el kardex es inmutable. Ver ADR 0010.
    /// </summary>
    public async Task<(bool Exitoso, string? Error, MetodoCosteo Metodo)> FijarMetodoAsync(
        MetodoCosteo metodo,
        CancellationToken ct = default)
    {
        if (await db.Set<Movimiento>().AnyAsync(ct))
        {
            var actual = await MetodoAsync(ct);

            return (false,
                "Ya hay movimientos registrados: el método de costeo no se puede cambiar sin "
                + "reescribir costos ya asentados.",
                actual);
        }

        var configuracion = await db.Set<ConfiguracionInventario>().FirstOrDefaultAsync(ct);

        if (configuracion is null)
        {
            configuracion = ConfiguracionInventario.PorOmision(reloj.Ahora);
            db.Add(configuracion);
        }

        configuracion.Cambiar(metodo, reloj.Ahora);
        await db.SaveChangesAsync(ct);

        return (true, null, metodo);
    }

    /// <summary>Método vigente y si todavía se puede cambiar, para que la pantalla no mienta.</summary>
    public async Task<(MetodoCosteo Metodo, bool SePuedeCambiar, DateTimeOffset ActualizadoEn)> LeerMetodoAsync(
        CancellationToken ct = default)
    {
        var configuracion = await db.Set<ConfiguracionInventario>().FirstOrDefaultAsync(ct);
        var hayMovimientos = await db.Set<Movimiento>().AnyAsync(ct);

        return (
            configuracion?.Metodo ?? MetodoCosteo.PromedioPonderado,
            !hayMovimientos,
            configuracion?.ActualizadoEn ?? default);
    }

    /// <summary>
    /// Método de costeo de la empresa. Si no hay fila de configuración —empresas creadas antes de
    /// H8— se asume promedio ponderado, que es el comportamiento que ya tenían.
    /// </summary>
    private async Task<MetodoCosteo> MetodoAsync(CancellationToken ct)
    {
        var configuracion = await db.Set<ConfiguracionInventario>().FirstOrDefaultAsync(ct);

        return configuracion?.Metodo ?? MetodoCosteo.PromedioPonderado;
    }

    /// <summary>
    /// Estado de costeo de **una celda**: el método de la empresa y, solo en PEPS, sus capas
    /// vivas bloqueadas y en orden de antigüedad.
    ///
    /// Existe para que la bifurcación entre los dos métodos ocurra en **un solo sitio**. Todo lo
    /// demás —bloqueo pesimista, inmutabilidad del libro, lotes, FEFO— es común, y repartir el
    /// `if` del método por el servicio sería la forma más segura de que un camino quedara
    /// costeando distinto que otro sin que nadie lo notara.
    /// </summary>
    private sealed class Costeo(MetodoCosteo metodo, List<CapaDeCosto> capas, Existencia existencia, string moneda)
    {
        /// <summary>Capa pendiente de persistir, que necesita el id del movimiento que la origina.</summary>
        private (Cantidad Cantidad, Dinero Costo)? pendiente;

        /// <summary>Tramos recibidos en un traslado, pendientes de persistir como capas propias.</summary>
        private IReadOnlyList<TramoConsumido> tramosPendientes = [];

        public bool EsPeps => metodo is MetodoCosteo.Peps;

        private SaldoPeps SaldoPeps() =>
            Core.SaldoPeps.De(capas.Select(c => c.ComoCapa()), existencia.Saldo().Cantidad, moneda);

        /// <summary>Costo al que debe salir la mercancía, para quien solo necesita consultarlo.</summary>
        public Dinero CostoDeSalida() =>
            EsPeps ? SaldoPeps().CostoImplicito() : existencia.Saldo().CostoDeSalida();

        public SaldoValorizado Entrada(Cantidad cantidad, Dinero costoUnitario)
        {
            var saldo = existencia.Saldo();

            if (!EsPeps)
            {
                return saldo.AplicarEntrada(cantidad, costoUnitario);
            }

            // Reinicio con saldo ≤ 0: las capas anteriores se agotan en la base igual que
            // `SaldoPeps` las descarta en memoria. Si solo se descartaran en memoria, la próxima
            // transacción las volvería a leer y el saldo dejaría de cuadrar con sus capas.
            if (saldo.Cantidad.Valor <= 0m)
            {
                foreach (var capa in capas)
                {
                    capa.Agotar();
                }

                capas.Clear();
            }

            pendiente = (cantidad, costoUnitario);

            var despues = SaldoPeps().AplicarEntrada(cantidad, costoUnitario);

            return SaldoValorizado.De(despues.Cantidad, despues.CostoImplicito());
        }

        /// <summary>
        /// Resuelve una salida. Devuelve el saldo resultante, el costo **unitario** del asiento y
        /// su costo **total exacto**: bajo PEPS el total es la suma de las capas consumidas y el
        /// unitario se deriva de él, así que multiplicar el unitario no lo reproduce.
        /// </summary>
        public ResultadoSalida Salida(Cantidad cantidad, bool forzar)
        {
            var saldo = existencia.Saldo();

            if (!EsPeps)
            {
                var promedio = forzar ? saldo.AplicarSalidaForzada(cantidad) : saldo.AplicarSalida(cantidad);

                return new ResultadoSalida(promedio, saldo.CostoDeSalida(), null, []);
            }

            var antes = SaldoPeps();
            var (restante, costoTotal) = antes.ConsumirSalida(cantidad);

            // Las entidades de capa se consumen en el mismo orden que la aritmética, que es el
            // que impone el índice: por fecha y, a igual fecha, por identificador.
            var porConsumir = cantidad.Valor;
            var consumido = new List<TramoConsumido>();

            foreach (var capa in capas)
            {
                if (porConsumir <= 0m)
                {
                    break;
                }

                var tomar = Math.Min(porConsumir, capa.CantidadRestante);
                consumido.Add(new TramoConsumido(
                    new Cantidad(tomar, cantidad.Unidad),
                    new Dinero(capa.CostoUnitario, capa.Moneda),
                    capa.Fecha));

                capa.Consumir(tomar);
                porConsumir -= tomar;
            }

            capas.RemoveAll(c => c.CantidadRestante == 0m);

            var unitario = new Dinero(costoTotal.Monto / cantidad.Valor, moneda)
                .Redondear(SaldoValorizado.DecimalesCosto);

            return new ResultadoSalida(
                SaldoValorizado.De(restante.Cantidad, restante.CostoImplicito()),
                unitario,
                costoTotal,
                consumido);
        }

        /// <summary>
        /// Recibe en el destino los tramos que salieron del origen **conservando su costo y su
        /// antigüedad**, para un traslado bajo PEPS.
        ///
        /// Fundirlos en una sola capa al costo ponderado daría el mismo valor total pero
        /// reiniciaría la antigüedad: las unidades que queden después en el destino costarían el
        /// promedio de lo trasladado y no lo que de verdad costó cada una. Mover mercancía de
        /// bodega no es un hecho económico, así que no debe cambiar su costeo futuro.
        /// </summary>
        public SaldoValorizado RecibirTramos(IReadOnlyList<TramoConsumido> tramos, Dinero costoPonderado)
        {
            var total = tramos.Aggregate(
                new Cantidad(0m, tramos[0].Cantidad.Unidad),
                (suma, tramo) => suma + tramo.Cantidad);

            if (!EsPeps)
            {
                return existencia.Saldo().AplicarEntrada(total, costoPonderado);
            }

            if (existencia.Saldo().Cantidad.Valor <= 0m)
            {
                foreach (var capa in capas)
                {
                    capa.Agotar();
                }

                capas.Clear();
            }

            tramosPendientes = tramos;

            var saldo = SaldoPeps();

            foreach (var tramo in tramos)
            {
                saldo = saldo.AplicarEntrada(tramo.Cantidad, tramo.CostoUnitario);
            }

            return SaldoValorizado.De(saldo.Cantidad, saldo.CostoImplicito());
        }

        /// <summary>
        /// Crea la capa de la entrada ya con el id de su movimiento. Se llama **después** de
        /// construir el asiento porque la capa apunta a él: es lo que permite auditar de qué
        /// entrada salió cada peso del costo de ventas.
        /// </summary>
        public List<CapaDeCosto> CapasDe(Movimiento movimiento, ContextoCelda celda)
        {
            if (!EsPeps)
            {
                return [];
            }

            CapaDeCosto Crear(Cantidad cantidad, Dinero costo, DateTimeOffset fecha) =>
                CapaDeCosto.Nueva(
                    celda.VarianteId,
                    celda.BodegaId,
                    celda.UbicacionId,
                    celda.LoteId,
                    movimiento.Id,
                    cantidad,
                    costo,
                    fecha);

            if (tramosPendientes.Count > 0)
            {
                // Un traslado conserva la fecha original de cada tramo: es su antigüedad para
                // PEPS, y perderla cambiaría el costo de las salidas futuras del destino.
                var capasDeTraslado = tramosPendientes
                    .Select(tr => Crear(tr.Cantidad, tr.CostoUnitario, tr.Fecha))
                    .ToList();

                tramosPendientes = [];

                return capasDeTraslado;
            }

            if (pendiente is not { } nueva)
            {
                return [];
            }

            pendiente = null;

            return [Crear(nueva.Cantidad, nueva.Costo, movimiento.Fecha)];
        }
    }

    /// <summary>Un tramo de mercancía que salió de una capa concreta, con su costo y antigüedad.</summary>
    private sealed record TramoConsumido(Cantidad Cantidad, Dinero CostoUnitario, DateTimeOffset Fecha);

    /// <summary>
    /// Resultado de resolver una salida. <c>CostoTotal</c> es nulo en promedio ponderado, donde el
    /// total se deduce del unitario; bajo PEPS es la suma exacta de las capas consumidas y manda.
    /// </summary>
    private sealed record ResultadoSalida(
        SaldoValorizado Saldo,
        Dinero CostoUnitario,
        Dinero? CostoTotal,
        IReadOnlyList<TramoConsumido> Consumido);

    /// <summary>
    /// Carga el costeo de una celda. En PEPS bloquea sus capas vivas con <c>FOR UPDATE</c> y en
    /// orden de antigüedad: sin el bloqueo, dos salidas simultáneas consumirían la misma capa.
    /// </summary>
    private async Task<Costeo> CargarCosteoAsync(
        ContextoCelda celda,
        Existencia existencia,
        MetodoCosteo metodo,
        CancellationToken ct)
    {
        if (metodo is not MetodoCosteo.Peps)
        {
            return new Costeo(metodo, [], existencia, MonedaBase);
        }

        var capas = await db.Set<CapaDeCosto>()
            .FromSql($"""
                select * from inventario.capas_costo
                where variante_id = {celda.VarianteId}
                  and bodega_id = {celda.BodegaId}
                  and ubicacion_id = {celda.UbicacionId}
                  and lote_id is not distinct from {celda.LoteId}
                  and cantidad_restante > 0
                order by fecha, id
                for update
                """)
            .ToListAsync(ct);

        return new Costeo(metodo, capas, existencia, MonedaBase);
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
