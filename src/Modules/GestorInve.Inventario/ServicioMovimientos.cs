using GestorInve.Catalogo;
using GestorInve.Core;
using GestorInve.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Inventario;

/// <summary>Datos de un movimiento simple (entrada, salida o ajuste).</summary>
public sealed record SolicitudMovimiento(
    TipoMovimiento Tipo,
    Guid VarianteId,
    Guid BodegaId,
    Guid? UbicacionId,
    decimal Cantidad,
    decimal? CostoUnitario,
    DateTimeOffset? Fecha,
    string? Referencia,
    string? Observacion);

public sealed record SolicitudTraslado(
    Guid VarianteId,
    Guid BodegaOrigenId,
    Guid? UbicacionOrigenId,
    Guid BodegaDestinoId,
    Guid? UbicacionDestinoId,
    decimal Cantidad,
    DateTimeOffset? Fecha,
    string? Referencia,
    string? Observacion);

public enum FalloMovimiento
{
    Ninguno,
    VarianteInexistente,
    BodegaInexistente,
    UbicacionInexistente,
    SinExistencias,
    CostoRequerido,
    DatosInvalidos,
    TrazabilidadNoSoportada,
    MovimientoInexistente,
    YaReversado,
    MismaUbicacion,
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
        Unidad? Unidad);

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

        // El producto declara que exige trazabilidad y todavía no existe (H7b): se rechaza en
        // lugar de aceptar el movimiento perdiendo la trazabilidad que el producto pide.
        if (producto.ExigeLote || producto.ExigeSerial)
        {
            return new ContextoCelda(
                FalloMovimiento.TrazabilidadNoSoportada,
                "El producto exige lote o serial, y la trazabilidad por lotes aún no está implementada.",
                varianteId,
                bodegaId,
                Guid.Empty,
                null);
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
            unidadMedida.ComoUnidad());
    }

    /// <summary>
    /// Trae la fila de existencias **bloqueada para escritura**. Si no existe, la crea; una
    /// colisión con otra transacción que la cree a la vez se resuelve volviendo a leerla.
    /// </summary>
    private async Task<Existencia> BloquearExistenciaAsync(ContextoCelda celda, CancellationToken ct)
    {
        var existente = await db.Set<Existencia>()
            .FromSql($"""
                select * from inventario.existencias
                where variante_id = {celda.VarianteId}
                  and bodega_id = {celda.BodegaId}
                  and ubicacion_id = {celda.UbicacionId}
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
            celda.Unidad!.Value,
            MonedaBase);

        db.Add(nueva);
        await db.SaveChangesAsync(ct);

        return nueva;
    }
}
