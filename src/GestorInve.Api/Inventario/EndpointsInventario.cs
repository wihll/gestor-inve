using System.Security.Claims;
using GestorInve.Api.Autenticacion;
using GestorInve.Core;
using GestorInve.Inventario;
using GestorInve.Persistencia;
using GestorInve.Seguridad;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GestorInve.Api.Inventario;

public sealed record PeticionBodega(string Codigo, string Nombre);

public sealed record RespuestaUbicacion(Guid Id, string Codigo, string Nombre, bool Activa);

public sealed record RespuestaBodega(Guid Id, string Codigo, string Nombre, bool Activa, IReadOnlyList<RespuestaUbicacion> Ubicaciones);

public sealed record PeticionUbicacion(string Codigo, string Nombre);

/// <summary>
/// <see cref="LoteId"/> es obligatorio si el producto exige lote o serial, y debe ser nulo si no
/// lo exige. Una **salida sin lote** de un producto con lotes se interpreta como «elige tú»: el
/// servidor aplica FEFO y puede devolver **varios** asientos (ADR 0009).
/// </summary>
public sealed record PeticionMovimiento(
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

public sealed record PeticionTraslado(
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

public sealed record PeticionLote(string Codigo, DateOnly? Vencimiento, bool EsSerial);

public sealed record PeticionConfiguracion(MetodoCosteo Metodo);

public sealed record RespuestaConfiguracion(MetodoCosteo Metodo, bool SePuedeCambiar, DateTimeOffset ActualizadoEn);

public sealed record RespuestaLote(
    Guid Id,
    Guid VarianteId,
    string Codigo,
    DateOnly? Vencimiento,
    bool EsSerial,
    bool Vencido);

public sealed record PeticionReversa(string? Observacion);

public sealed record RespuestaMovimiento(
    Guid Id,
    TipoMovimiento Tipo,
    Guid VarianteId,
    Guid BodegaId,
    Guid UbicacionId,
    decimal Cantidad,
    string Unidad,
    decimal CostoUnitario,
    decimal CostoTotal,
    string Moneda,
    decimal SaldoCantidadDespues,
    decimal SaldoCostoPromedioDespues,
    DateTimeOffset Fecha,
    string Usuario,
    string? Referencia,
    string? Observacion,
    Guid? TrasladoId,
    Guid? MovimientoReversadoId,
    bool ForzadoNegativo,
    Guid? LoteId,
    Guid? OperacionId);

public sealed record RespuestaExistencia(
    Guid VarianteId,
    Guid BodegaId,
    Guid UbicacionId,
    decimal Cantidad,
    string Unidad,
    decimal CostoPromedio,
    decimal Valorizado,
    string Moneda,
    Guid? LoteId);

/// <summary>
/// Endpoints del inventario.
///
/// Dos cosas que no se ven en las firmas:
/// - **Forzar existencias negativas exige `inventario:ajustar`.** El endpoint resuelve el
///   permiso y se lo pasa al servicio como un dato; el dominio no conoce HTTP ni reclamaciones.
/// - Lo que pertenece a otra empresa responde **404 y no 403**, para no confirmar que existe.
/// </summary>
public static class EndpointsInventario
{
    public static IEndpointRouteBuilder MapEndpointsInventario(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/inventario").WithTags("Inventario");

        MapBodegas(grupo);
        MapConfiguracion(grupo);
        MapLotes(grupo);
        MapMovimientos(grupo);
        MapConsultas(grupo);

        return rutas;
    }

    private static void MapBodegas(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/bodegas", async (GestorInveDbContext db, CancellationToken ct) =>
        {
            var bodegas = await db.Set<Bodega>()
                .Include(b => b.Ubicaciones)
                .OrderBy(b => b.Codigo)
                .ToListAsync(ct);

            return Results.Ok(bodegas.Select(Proyectar).ToList());
        })
            .RequireAuthorization(Permiso.InventarioLeer)
            .Produces<List<RespuestaBodega>>(StatusCodes.Status200OK)
            .WithSummary("Bodegas con sus ubicaciones.");

        grupo.MapPost("/bodegas", async (
            PeticionBodega peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            var bodega = Bodega.Crear(peticion.Codigo, peticion.Nombre);
            db.Add(bodega);

            return await GuardarAsync(db, () => Results.Created($"/api/inventario/bodegas/{bodega.Id}", Proyectar(bodega)), ct);
        })
            .RequireAuthorization(Permiso.InventarioAjustar)
            .Produces<RespuestaBodega>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Crea una bodega con su ubicación general.");

        grupo.MapPost("/bodegas/{id:guid}/ubicaciones", async (
            Guid id,
            PeticionUbicacion peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            var bodega = await db.Set<Bodega>()
                .Include(b => b.Ubicaciones)
                .SingleOrDefaultAsync(b => b.Id == id, ct);

            if (bodega is null)
            {
                return Results.NotFound();
            }

            try
            {
                var ubicacion = bodega.AgregarUbicacion(peticion.Codigo, peticion.Nombre);

                // `Add` explícito: la entidad nace con su Id puesto, así que al aparecer en la
                // colección de un agregado ya cargado EF la tomaría por existente (ADR 0005).
                db.Add(ubicacion);

                return await GuardarAsync(
                    db,
                    () => Results.Created($"/api/inventario/bodegas/{id}/ubicaciones/{ubicacion.Id}", Proyectar(ubicacion)),
                    ct);
            }
            catch (InvalidOperationException error)
            {
                return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
            .RequireAuthorization(Permiso.InventarioAjustar)
            .Produces<RespuestaUbicacion>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Añade una ubicación a una bodega.");
    }

    private static void MapConfiguracion(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/configuracion", async (ServicioMovimientos servicio, CancellationToken ct) =>
        {
            var (metodo, sePuedeCambiar, actualizadoEn) = await servicio.LeerMetodoAsync(ct);

            return Results.Ok(new RespuestaConfiguracion(metodo, sePuedeCambiar, actualizadoEn));
        })
            .RequireAuthorization(Permiso.InventarioLeer)
            .Produces<RespuestaConfiguracion>(StatusCodes.Status200OK)
            .WithSummary("Método de costeo de la empresa y si todavía se puede cambiar.");

        grupo.MapPut("/configuracion", async (
            PeticionConfiguracion peticion,
            ServicioMovimientos servicio,
            CancellationToken ct) =>
        {
            // La regla «no se cambia con movimientos» vive en el servicio, no aquí: así una
            // importación o un script tampoco la pueden saltar. Ver ADR 0010.
            var (exitoso, error, metodo) = await servicio.FijarMetodoAsync(peticion.Metodo, ct);

            if (!exitoso)
            {
                return Results.Problem(detail: error, statusCode: StatusCodes.Status409Conflict);
            }

            var (_, sePuedeCambiar, actualizadoEn) = await servicio.LeerMetodoAsync(ct);

            return Results.Ok(new RespuestaConfiguracion(metodo, sePuedeCambiar, actualizadoEn));
        })
            .RequireAuthorization(Permiso.InventarioAjustar)
            .Produces<RespuestaConfiguracion>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Fija el método de costeo. Solo antes del primer movimiento.");
    }

    private static void MapLotes(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/lotes", async (
            GestorInveDbContext db,
            IReloj reloj,
            Guid? varianteId,
            CancellationToken ct) =>
        {
            var consulta = db.Set<Lote>().AsQueryable();

            if (varianteId is { } variante)
            {
                consulta = consulta.Where(l => l.VarianteId == variante);
            }

            // Orden FEFO también en la consulta: es el orden en el que se van a consumir, así que
            // es el que tiene sentido ver. Los sin vencimiento al final.
            var lotes = await consulta
                .OrderBy(l => l.Vencimiento ?? DateOnly.MaxValue)
                .ThenBy(l => l.Codigo)
                .ToListAsync(ct);

            var hoy = DateOnly.FromDateTime(reloj.Ahora.UtcDateTime);

            return Results.Ok(lotes.Select(l => Proyectar(l, hoy)).ToList());
        })
            .RequireAuthorization(Permiso.InventarioLeer)
            .Produces<List<RespuestaLote>>(StatusCodes.Status200OK)
            .WithSummary("Lotes y seriales, en orden de vencimiento más próximo primero.");

        grupo.MapPost("/variantes/{varianteId:guid}/lotes", async (
            Guid varianteId,
            PeticionLote peticion,
            GestorInveDbContext db,
            IReloj reloj,
            CancellationToken ct) =>
        {
            // La variante se comprueba contra la empresa de la sesión (filtro global + RLS): un
            // identificador de otra empresa responde 404, no 403.
            if (!await db.Set<GestorInve.Catalogo.Variante>().AnyAsync(v => v.Id == varianteId, ct))
            {
                return Results.Problem(
                    detail: "La variante no existe.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (string.IsNullOrWhiteSpace(peticion.Codigo))
            {
                return Results.Problem(
                    detail: "El lote necesita código.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var lote = Lote.Nuevo(varianteId, peticion.Codigo, peticion.Vencimiento, peticion.EsSerial);
            db.Add(lote);

            var hoy = DateOnly.FromDateTime(reloj.Ahora.UtcDateTime);

            return await GuardarAsync(
                db,
                () => Results.Created($"/api/inventario/lotes/{lote.Id}", Proyectar(lote, hoy)),
                ct);
        })
            .RequireAuthorization(Permiso.InventarioAjustar)
            .Produces<RespuestaLote>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Crea un lote o un serial para una variante.");
    }

    private static void MapMovimientos(RouteGroupBuilder grupo)
    {
        grupo.MapPost("/movimientos", async (
            PeticionMovimiento peticion,
            ServicioMovimientos servicio,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            // Un ajuste es una corrección contable: exige el permiso más fuerte.
            if (!peticion.Tipo.EsSimple())
            {
                return Results.Problem(
                    detail: "Los traslados se registran en /inventario/traslados.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (peticion.Tipo.EsAjuste() && !PuedeAjustar(principal))
            {
                return Results.Problem(
                    detail: "Un ajuste de inventario requiere el permiso inventario:ajustar.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var resultado = await servicio.RegistrarAsync(
                new SolicitudMovimiento(
                    peticion.Tipo,
                    peticion.VarianteId,
                    peticion.BodegaId,
                    peticion.UbicacionId,
                    peticion.Cantidad,
                    peticion.CostoUnitario,
                    peticion.Fecha,
                    peticion.Referencia,
                    peticion.Observacion,
                    peticion.LoteId),
                permitirNegativo: PuedeAjustar(principal),
                ct);

            // Una salida FEFO puede generar varios asientos, así que la respuesta es siempre una
            // **lista**: devolver el primero escondería que el saldo se tomó de tres lotes.
            return Respuesta(resultado, m => Results.Created(
                $"/api/inventario/movimientos/{m[0].Id}",
                m.Select(Proyectar).ToList()));
        })
            .RequireAuthorization(Permiso.InventarioMover)
            .Produces<List<RespuestaMovimiento>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Registra una entrada, una salida o un ajuste. Una salida sin lote aplica FEFO.");

        grupo.MapPost("/traslados", async (
            PeticionTraslado peticion,
            ServicioMovimientos servicio,
            CancellationToken ct) =>
        {
            var resultado = await servicio.TrasladarAsync(
                new SolicitudTraslado(
                    peticion.VarianteId,
                    peticion.BodegaOrigenId,
                    peticion.UbicacionOrigenId,
                    peticion.BodegaDestinoId,
                    peticion.UbicacionDestinoId,
                    peticion.Cantidad,
                    peticion.Fecha,
                    peticion.Referencia,
                    peticion.Observacion,
                    peticion.LoteId),
                ct);

            return Respuesta(resultado, m => Results.Created(
                $"/api/inventario/traslados/{m[0].TrasladoId}",
                m.Select(Proyectar).ToList()));
        })
            .RequireAuthorization(Permiso.InventarioMover)
            .Produces<List<RespuestaMovimiento>>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Traslada existencias entre ubicaciones, conservando el costo.");

        grupo.MapPost("/movimientos/{id:guid}/reversa", async (
            Guid id,
            PeticionReversa peticion,
            ServicioMovimientos servicio,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var resultado = await servicio.ReversarAsync(
                id,
                peticion.Observacion,
                permitirNegativo: PuedeAjustar(principal),
                ct);

            return Respuesta(resultado, m => Results.Created($"/api/inventario/movimientos/{m[0].Id}", Proyectar(m[0])));
        })
            .RequireAuthorization(Permiso.InventarioAjustar)
            .Produces<RespuestaMovimiento>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Reversa un movimiento creando su opuesto; el original no se toca.");
    }

    private static void MapConsultas(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/existencias", async (
            GestorInveDbContext db,
            Guid? varianteId,
            Guid? bodegaId,
            CancellationToken ct) =>
        {
            var consulta = db.Set<Existencia>().AsQueryable();

            if (varianteId is { } variante)
            {
                consulta = consulta.Where(e => e.VarianteId == variante);
            }

            if (bodegaId is { } bodega)
            {
                consulta = consulta.Where(e => e.BodegaId == bodega);
            }

            var existencias = await consulta.ToListAsync(ct);

            return Results.Ok(existencias.Select(Proyectar).ToList());
        })
            .RequireAuthorization(Permiso.InventarioLeer)
            .Produces<List<RespuestaExistencia>>(StatusCodes.Status200OK)
            .WithSummary("Existencias por variante, bodega y ubicación.");

        grupo.MapGet("/kardex", async (
            GestorInveDbContext db,
            Guid varianteId,
            Guid? bodegaId,
            CancellationToken ct) =>
        {
            var consulta = db.Set<Movimiento>().Where(m => m.VarianteId == varianteId);

            if (bodegaId is { } bodega)
            {
                consulta = consulta.Where(m => m.BodegaId == bodega);
            }

            var movimientos = await consulta
                .OrderBy(m => m.Fecha)
                .ThenBy(m => m.Id)
                .ToListAsync(ct);

            return Results.Ok(movimientos.Select(Proyectar).ToList());
        })
            .RequireAuthorization(Permiso.InventarioLeer)
            .Produces<List<RespuestaMovimiento>>(StatusCodes.Status200OK)
            .WithSummary("Kardex de una variante en orden cronológico.");
    }

    /// <summary>Forzar negativos y ajustar son la misma potestad: corregir el inventario.</summary>
    private static bool PuedeAjustar(ClaimsPrincipal principal) =>
        principal.HasClaim(ClaimsGestorInve.Permiso, Permiso.InventarioAjustar);

    private static IResult Respuesta(
        ResultadoMovimiento resultado,
        Func<IReadOnlyList<Movimiento>, IResult> alTenerExito) => resultado.Fallo switch
        {
            FalloMovimiento.Ninguno => alTenerExito(resultado.Movimientos),

            // 404 para lo que no existe **en esta empresa**: no se confirma que exista en otra.
            FalloMovimiento.VarianteInexistente
                or FalloMovimiento.BodegaInexistente
                or FalloMovimiento.UbicacionInexistente
                or FalloMovimiento.MovimientoInexistente =>
                Results.Problem(detail: resultado.Mensaje, statusCode: StatusCodes.Status404NotFound),

            FalloMovimiento.LoteInexistente =>
                Results.Problem(detail: resultado.Mensaje, statusCode: StatusCodes.Status404NotFound),

            FalloMovimiento.SinExistencias
                or FalloMovimiento.YaReversado
                or FalloMovimiento.MismaUbicacion
                // 409 y no 400: el lote vencido y el serial duplicado no son datos mal formados,
                // son un conflicto con el estado del inventario. Quien integra necesita poder
                // distinguir «corrige la petición» de «la mercancía no está como creías».
                or FalloMovimiento.LoteVencido
                or FalloMovimiento.SerialInvalido =>
                Results.Problem(detail: resultado.Mensaje, statusCode: StatusCodes.Status409Conflict),

            FalloMovimiento.TrazabilidadNoSoportada =>
                Results.Problem(detail: resultado.Mensaje, statusCode: StatusCodes.Status501NotImplemented),

            _ => Results.Problem(detail: resultado.Mensaje, statusCode: StatusCodes.Status400BadRequest),
        };

    private static async Task<IResult> GuardarAsync(
        GestorInveDbContext db,
        Func<IResult> alGuardar,
        CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);

            return alGuardar();
        }
        catch (DbUpdateException error)
            when (error.InnerException is Npgsql.PostgresException { SqlState: "23505" } duplicado)
        {
            return Results.Problem(
                detail: $"Ya existe un registro con esos datos ({duplicado.ConstraintName}).",
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static RespuestaBodega Proyectar(Bodega b) => new(
        b.Id,
        b.Codigo,
        b.Nombre,
        b.Activa,
        [.. b.Ubicaciones.Select(Proyectar)]);

    private static RespuestaUbicacion Proyectar(Ubicacion u) => new(u.Id, u.Codigo, u.Nombre, u.Activa);

    /// <summary>
    /// `Vencido` se calcula aquí y no en el cliente: si cada pantalla comparara la fecha por su
    /// cuenta, el navegador usaría su zona horaria y el mismo lote estaría vencido o no según el
    /// equipo desde el que se consulte.
    /// </summary>
    private static RespuestaLote Proyectar(Lote l, DateOnly hoy) => new(
        l.Id,
        l.VarianteId,
        l.Codigo,
        l.Vencimiento,
        l.EsSerial,
        l.EstaVencido(hoy));

    private static RespuestaMovimiento Proyectar(Movimiento m) => new(
        m.Id,
        m.Tipo,
        m.VarianteId,
        m.BodegaId,
        m.UbicacionId,
        m.Cantidad,
        m.UnidadCodigo,
        m.CostoUnitario,
        m.CostoTotal,
        m.Moneda,
        m.SaldoCantidadDespues,
        m.SaldoCostoPromedioDespues,
        m.Fecha,
        m.Usuario,
        m.Referencia,
        m.Observacion,
        m.TrasladoId,
        m.MovimientoReversadoId,
        m.ForzadoNegativo,
        m.LoteId,
        m.OperacionId);

    private static RespuestaExistencia Proyectar(Existencia e) => new(
        e.VarianteId,
        e.BodegaId,
        e.UbicacionId,
        e.Cantidad,
        e.UnidadCodigo,
        e.CostoPromedio,
        e.Saldo().Valorizado().Monto,
        e.Moneda,
        e.LoteId);
}
