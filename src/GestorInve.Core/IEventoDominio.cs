namespace GestorInve.Core;

/// <summary>Hecho inmutable ocurrido en el dominio; los módulos se comunican con estos eventos.</summary>
public interface IEventoDominio
{
    Guid Id { get; }

    DateTimeOffset OcurridoEn { get; }
}
