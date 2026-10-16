using GestorInve.Api.Autenticacion;

namespace GestorInve.Api.Tests;

/// <summary>
/// Un despliegue con la clave de firma vacía o débil acepta tokens que cualquiera puede
/// fabricar. Tiene que impedir el arranque, no degradarse en silencio.
/// </summary>
public class OpcionesJwtTests
{
    [Fact]
    public void Sin_clave_la_validacion_falla()
    {
        var opciones = new OpcionesJwt { Clave = string.Empty };

        var error = Assert.Throws<InvalidOperationException>(opciones.Validar);

        Assert.Contains("Jwt:Clave", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_clave_corta_falla_y_dice_cuanto_falta()
    {
        var opciones = new OpcionesJwt { Clave = new string('k', OpcionesJwt.BytesMinimosClave - 1) };

        var error = Assert.Throws<InvalidOperationException>(opciones.Validar);

        Assert.Contains(OpcionesJwt.BytesMinimosClave.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_clave_suficiente_pasa()
    {
        var opciones = new OpcionesJwt { Clave = new string('k', OpcionesJwt.BytesMinimosClave) };

        opciones.Validar();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_duracion_no_positiva_falla(int minutos)
    {
        var opciones = new OpcionesJwt
        {
            Clave = new string('k', OpcionesJwt.BytesMinimosClave),
            DuracionAcceso = TimeSpan.FromMinutes(minutos),
        };

        Assert.Throws<InvalidOperationException>(opciones.Validar);
    }

    [Fact]
    public void El_token_de_acceso_dura_poco_por_defecto()
    {
        // No se puede revocar un token de acceso: solo esperar a que caduque.
        Assert.True(new OpcionesJwt().DuracionAcceso <= TimeSpan.FromMinutes(15));
    }
}
