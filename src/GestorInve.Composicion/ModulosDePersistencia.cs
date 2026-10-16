using GestorInve.Catalogo;
using GestorInve.Persistencia;
using GestorInve.Seguridad;

namespace GestorInve.Composicion;

/// <summary>
/// Raíz de composición: la **única** lista de módulos que aportan modelo de datos.
///
/// Existe porque antes la Api y las pruebas de persistencia mantenían listas separadas, y al
/// añadir un módulo nuevo las pruebas seguían ejercitando un modelo que ya no era el de la
/// aplicación. Con una sola lista, añadir un módulo aquí lo pone en producción, en las
/// migraciones y en las pruebas a la vez.
///
/// Este proyecto es el único que puede referenciar a todos los módulos: `Persistencia` no los
/// conoce y los módulos no se conocen entre sí (hay pruebas de arquitectura que lo vigilan).
/// </summary>
public static class ModulosDePersistencia
{
    public static IReadOnlyList<IModuloPersistencia> Todos { get; } =
        [new ModuloSeguridadPersistencia(), new ModuloCatalogoPersistencia()];
}
