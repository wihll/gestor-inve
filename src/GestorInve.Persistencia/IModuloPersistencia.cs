using Microsoft.EntityFrameworkCore;

namespace GestorInve.Persistencia;

/// <summary>
/// Punto de extensión de cada módulo sobre el modelo de datos. Invierte la dependencia:
/// <c>Persistencia</c> no conoce los módulos; cada módulo aporta su configuración y el
/// arranque (Api/Worker) los registra. Así los módulos siguen aislados entre sí.
/// </summary>
public interface IModuloPersistencia
{
    /// <summary>Nombre del módulo, solo para diagnóstico.</summary>
    string Nombre { get; }

    void Configurar(ModelBuilder modelo);
}
