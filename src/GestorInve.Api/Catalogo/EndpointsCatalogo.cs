using GestorInve.Catalogo;
using GestorInve.Persistencia;
using GestorInve.Seguridad;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GestorInve.Api.Catalogo;

public sealed record PeticionUnidad(string Codigo, string Nombre, int Decimales);

public sealed record RespuestaUnidad(Guid Id, string Codigo, string Nombre, int Decimales, bool Activa);

public sealed record PeticionCategoria(string Nombre, Guid? PadreId);

public sealed record RespuestaCategoria(Guid Id, string Nombre, Guid? PadreId, bool Activa);

public sealed record PeticionProducto(
    string Sku,
    string Nombre,
    Guid UnidadMedidaId,
    string? Descripcion,
    Guid? CategoriaId,
    IReadOnlyList<string>? Atributos,
    bool ExigeLote,
    bool ExigeSerial);

public sealed record PeticionVariante(string Sku, Dictionary<string, string> Atributos);

public sealed record PeticionCodigo(string Codigo);

public sealed record PeticionLimites(decimal? StockMinimo, decimal? StockMaximo);

public sealed record RespuestaVariante(
    Guid Id,
    string Sku,
    IReadOnlyDictionary<string, string> Atributos,
    decimal? StockMinimo,
    decimal? StockMaximo,
    bool Activa,
    IReadOnlyList<string> Codigos);

/// <summary>Código de barras recién añadido.</summary>
public sealed record RespuestaCodigo(string Codigo);

/// <summary>Variante a la que corresponde un código escaneado.</summary>
public sealed record RespuestaVarianteDeCodigo(Guid VarianteId);

public sealed record RespuestaProducto(
    Guid Id,
    string Sku,
    string Nombre,
    string? Descripcion,
    Guid UnidadMedidaId,
    Guid? CategoriaId,
    IReadOnlyList<string> Atributos,
    bool ExigeLote,
    bool ExigeSerial,
    bool Activo,
    IReadOnlyList<RespuestaVariante> Variantes);

/// <summary>
/// Endpoints del catálogo.
///
/// Todos usan el <see cref="GestorInveDbContext"/> del ámbito de la petición, cuya empresa sale
/// del token validado. Nada aquí recibe ni acepta un identificador de empresa: por eso una
/// consulta por id que pertenezca a otro cliente simplemente no encuentra nada, y se responde
/// **404 y no 403**, para no confirmar que ese registro existe en otra empresa.
/// </summary>
public static class EndpointsCatalogo
{
    public static IEndpointRouteBuilder MapEndpointsCatalogo(this IEndpointRouteBuilder rutas)
    {
        ArgumentNullException.ThrowIfNull(rutas);

        var grupo = rutas.MapGroup("/catalogo").WithTags("Catálogo");

        MapUnidades(grupo);
        MapCategorias(grupo);
        MapProductos(grupo);

        return rutas;
    }

    private static void MapUnidades(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/unidades", async (GestorInveDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Set<UnidadMedida>()
                .OrderBy(u => u.Codigo)
                .Select(u => new RespuestaUnidad(u.Id, u.Codigo, u.Nombre, u.Decimales, u.Activa))
                .ToListAsync(ct)))
            .RequireAuthorization(Permiso.CatalogoLeer)
            .Produces<List<RespuestaUnidad>>(StatusCodes.Status200OK)
            .WithSummary("Unidades de medida de la empresa.");

        grupo.MapPost("/unidades", async (
            PeticionUnidad peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            var unidad = new UnidadMedida(peticion.Codigo, peticion.Nombre, peticion.Decimales);
            db.Add(unidad);

            return await GuardarAsync(
                db,
                () => Results.Created(
                    $"/catalogo/unidades/{unidad.Id}",
                    new RespuestaUnidad(unidad.Id, unidad.Codigo, unidad.Nombre, unidad.Decimales, unidad.Activa)),
                ct);
        })
            .RequireAuthorization(Permiso.CatalogoEscribir)
            .Produces<RespuestaUnidad>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Crea una unidad de medida.");
    }

    private static void MapCategorias(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/categorias", async (GestorInveDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Set<Categoria>()
                .OrderBy(c => c.Nombre)
                .Select(c => new RespuestaCategoria(c.Id, c.Nombre, c.PadreId, c.Activa))
                .ToListAsync(ct)))
            .RequireAuthorization(Permiso.CatalogoLeer)
            .Produces<List<RespuestaCategoria>>(StatusCodes.Status200OK)
            .WithSummary("Categorías de la empresa.");

        grupo.MapPost("/categorias", async (
            PeticionCategoria peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            Categoria? padre = null;

            if (peticion.PadreId is { } padreId)
            {
                padre = await db.Set<Categoria>().SingleOrDefaultAsync(c => c.Id == padreId, ct);

                if (padre is null)
                {
                    return Results.Problem(
                        detail: "La categoría padre no existe.",
                        statusCode: StatusCodes.Status404NotFound);
                }
            }

            var categoria = new Categoria(peticion.Nombre, padre);
            db.Add(categoria);
            await db.SaveChangesAsync(ct);

            return Results.Created(
                $"/api/catalogo/categorias/{categoria.Id}",
                new RespuestaCategoria(categoria.Id, categoria.Nombre, categoria.PadreId, categoria.Activa));
        })
            .RequireAuthorization(Permiso.CatalogoEscribir)
            .Produces<RespuestaCategoria>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Crea una categoría, opcionalmente bajo otra.");

        grupo.MapPut("/categorias/{id:guid}/padre", async (
            Guid id,
            PeticionCategoria peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            var categoria = await db.Set<Categoria>().SingleOrDefaultAsync(c => c.Id == id, ct);

            if (categoria is null)
            {
                return Results.NotFound();
            }

            if (peticion.PadreId is not { } padreId)
            {
                categoria.MoverARaiz();
                await db.SaveChangesAsync(ct);

                return Results.NoContent();
            }

            var padre = await db.Set<Categoria>().SingleOrDefaultAsync(c => c.Id == padreId, ct);

            if (padre is null)
            {
                return Results.NotFound();
            }

            try
            {
                categoria.MoverBajo(padre, await AntecesorasAsync(db, padre, ct));
                await db.SaveChangesAsync(ct);

                return Results.NoContent();
            }
            catch (InvalidOperationException error)
            {
                return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
            .RequireAuthorization(Permiso.CatalogoEscribir)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Recoloca una categoría en el árbol, rechazando ciclos.");
    }

    private static void MapProductos(RouteGroupBuilder grupo)
    {
        grupo.MapGet("/productos", async (GestorInveDbContext db, CancellationToken ct) =>
            Results.Ok((await ConsultaProductos(db).OrderBy(p => p.Sku).ToListAsync(ct))
                .Select(Proyectar)
                .ToList()))
            .RequireAuthorization(Permiso.CatalogoLeer)
            .Produces<List<RespuestaProducto>>(StatusCodes.Status200OK)
            .WithSummary("Productos de la empresa con sus variantes.");

        grupo.MapGet("/productos/{id:guid}", async (Guid id, GestorInveDbContext db, CancellationToken ct) =>
        {
            var producto = await ConsultaProductos(db).SingleOrDefaultAsync(p => p.Id == id, ct);

            return producto is null ? Results.NotFound() : Results.Ok(Proyectar(producto));
        })
            .RequireAuthorization(Permiso.CatalogoLeer)
            .Produces<RespuestaProducto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Un producto por identificador.");

        grupo.MapPost("/productos", async (
            PeticionProducto peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.Set<UnidadMedida>().AnyAsync(u => u.Id == peticion.UnidadMedidaId, ct))
            {
                return Results.Problem(
                    detail: "La unidad de medida no existe.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            if (peticion.CategoriaId is { } categoriaId
                && !await db.Set<Categoria>().AnyAsync(c => c.Id == categoriaId, ct))
            {
                return Results.Problem(
                    detail: "La categoría no existe.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var producto = Producto.Crear(
                peticion.Sku,
                peticion.Nombre,
                peticion.UnidadMedidaId,
                peticion.Atributos);

            producto.CambiarDescripcion(peticion.Descripcion);
            producto.CambiarCategoria(peticion.CategoriaId);
            producto.ExigirTrazabilidad(peticion.ExigeLote, peticion.ExigeSerial);

            db.Add(producto);

            return await GuardarAsync(
                db,
                () => Results.Created($"/api/catalogo/productos/{producto.Id}", Proyectar(producto)),
                ct);
        })
            .RequireAuthorization(Permiso.CatalogoEscribir)
            .Produces<RespuestaProducto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Crea un producto; sin atributos declarados nace con su variante única.");

        grupo.MapPost("/productos/{id:guid}/variantes", async (
            Guid id,
            PeticionVariante peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            var producto = await db.Set<Producto>()
                .Include(p => p.Variantes)
                .SingleOrDefaultAsync(p => p.Id == id, ct);

            if (producto is null)
            {
                return Results.NotFound();
            }

            try
            {
                var variante = producto.AgregarVariante(peticion.Sku, peticion.Atributos);

                // `Add` explícito: estas entidades nacen con su Id ya puesto (GUID v7), así que
                // al aparecer en la colección de un agregado ya cargado EF las toma por
                // existentes y emitiría un UPDATE que no afecta a ninguna fila.
                db.Add(variante);

                return await GuardarAsync(
                    db,
                    () => Results.Created($"/api/catalogo/variantes/{variante.Id}", Proyectar(variante)),
                    ct);
            }
            catch (ArgumentException error)
            {
                return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (InvalidOperationException error)
            {
                return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
            .RequireAuthorization(Permiso.CatalogoEscribir)
            .Produces<RespuestaVariante>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Añade una variante con sus valores de atributo.");

        grupo.MapPost("/variantes/{id:guid}/codigos", async (
            Guid id,
            PeticionCodigo peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            var variante = await db.Set<Variante>()
                .Include(v => v.Codigos)
                .SingleOrDefaultAsync(v => v.Id == id, ct);

            if (variante is null)
            {
                return Results.NotFound();
            }

            try
            {
                var codigo = variante.AgregarCodigo(peticion.Codigo);

                // Mismo motivo que en las variantes: Id asignado en el dominio, `Add` explícito.
                db.Add(codigo);

                return await GuardarAsync(
                    db,
                    () => Results.Created(
                        $"/catalogo/variantes/{id}/codigos/{codigo.Id}",
                        new RespuestaCodigo(codigo.Codigo)),
                    ct);
            }
            catch (InvalidOperationException error)
            {
                return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
            .RequireAuthorization(Permiso.CatalogoEscribir)
            .Produces<RespuestaCodigo>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithSummary("Añade un código de barras a una variante.");

        grupo.MapPut("/variantes/{id:guid}/limites", async (
            Guid id,
            PeticionLimites peticion,
            GestorInveDbContext db,
            CancellationToken ct) =>
        {
            var variante = await db.Set<Variante>().SingleOrDefaultAsync(v => v.Id == id, ct);

            if (variante is null)
            {
                return Results.NotFound();
            }

            try
            {
                variante.FijarLimitesDeStock(peticion.StockMinimo, peticion.StockMaximo);
                await db.SaveChangesAsync(ct);

                return Results.NoContent();
            }
            catch (ArgumentException error)
            {
                return Results.Problem(detail: error.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        })
            .RequireAuthorization(Permiso.CatalogoEscribir)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithSummary("Fija las existencias mínimas y máximas de una variante.");

        grupo.MapGet("/codigos/{codigo}", async (string codigo, GestorInveDbContext db, CancellationToken ct) =>
        {
            var normalizado = CodigoBarras.Normalizar(codigo);

            var encontrado = await db.Set<CodigoBarras>()
                .Where(c => c.Codigo == normalizado)
                .Select(c => c.VarianteId)
                .SingleOrDefaultAsync(ct);

            return encontrado == Guid.Empty
                ? Results.NotFound()
                : Results.Ok(new RespuestaVarianteDeCodigo(encontrado));
        })
            .RequireAuthorization(Permiso.CatalogoLeer)
            .Produces<RespuestaVarianteDeCodigo>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithSummary("Resuelve un código escaneado a su variante.");
    }

    /// <summary>
    /// Guarda traduciendo una violación de unicidad (SKU o código repetido dentro de la empresa)
    /// a 409. Sin esto la respuesta sería un 500, que para el cliente es indistinguible de un
    /// fallo del servidor cuando en realidad el dato ya existía.
    /// </summary>
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
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: "23505" } duplicado)
        {
            return Results.Problem(
                detail: $"Ya existe un registro con esos datos ({duplicado.ConstraintName}).",
                statusCode: StatusCodes.Status409Conflict);
        }
    }

    /// <summary>Antecesoras de una categoría, de la más cercana a la más lejana.</summary>
    private static async Task<List<Categoria>> AntecesorasAsync(
        GestorInveDbContext db,
        Categoria desde,
        CancellationToken ct)
    {
        var cadena = new List<Categoria> { desde };
        var actual = desde;

        // Tope defensivo: si la base ya tuviera un ciclo, esto no debe girar para siempre.
        for (var salto = 0; salto < 100 && actual.PadreId is { } padreId; salto++)
        {
            var padre = await db.Set<Categoria>().SingleOrDefaultAsync(c => c.Id == padreId, ct);

            if (padre is null)
            {
                break;
            }

            cadena.Add(padre);
            actual = padre;
        }

        return cadena;
    }

    /// <summary>
    /// Carga los productos con sus variantes y códigos. La proyección se hace **en memoria**
    /// (<see cref="Proyectar(Producto)"/>) porque los atributos no son columnas que EF pueda
    /// traducir: en el producto son un campo privado excluido del modelo y en la variante un
    /// documento JSON que hay que deserializar. `AsSplitQuery` evita multiplicar las filas del
    /// producto por sus variantes y códigos.
    /// </summary>
    private static IQueryable<Producto> ConsultaProductos(GestorInveDbContext db) =>
        db.Set<Producto>()
            .Include(p => p.Variantes)
            .ThenInclude(v => v.Codigos)
            .AsSplitQuery();

    private static RespuestaProducto Proyectar(Producto p) => new(
        p.Id,
        p.Sku,
        p.Nombre,
        p.Descripcion,
        p.UnidadMedidaId,
        p.CategoriaId,
        [.. p.Atributos],
        p.ExigeLote,
        p.ExigeSerial,
        p.Activo,
        [.. p.Variantes.Select(Proyectar)]);

    private static RespuestaVariante Proyectar(Variante v) => new(
        v.Id,
        v.Sku,
        v.Atributos(),
        v.StockMinimo,
        v.StockMaximo,
        v.Activa,
        [.. v.Codigos.Select(c => c.Codigo)]);
}
