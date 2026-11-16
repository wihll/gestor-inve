# Imagen de la Api. Dos etapas: el SDK solo existe durante la construcción, así que la
# imagen que se despliega no lleva compilador, ni fuentes, ni caché de NuGet.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS construccion
WORKDIR /origen

# Primero solo los manifiestos: mientras no cambien, Docker reutiliza la capa de `restore`.
# Con Central Package Management las versiones viven en Directory.Packages.props, así que
# ese archivo es parte de la clave de caché.
COPY global.json Directory.Build.props Directory.Packages.props GestorInve.slnx ./
COPY src/GestorInve.Core/*.csproj src/GestorInve.Core/
COPY src/GestorInve.Persistencia/*.csproj src/GestorInve.Persistencia/
COPY src/GestorInve.Composicion/*.csproj src/GestorInve.Composicion/
COPY src/GestorInve.Api/*.csproj src/GestorInve.Api/
COPY src/GestorInve.Worker/*.csproj src/GestorInve.Worker/
COPY src/Modules/ src/Modules/
# El `restore` es del proyecto, no de la solución: no arrastra los proyectos de prueba, que no
# tienen nada que hacer en la imagen que se despliega.
RUN dotnet restore src/GestorInve.Api

COPY . .
# `-warnaserror` también aquí: la imagen no debe poder construirse con un aviso que el CI
# rechazaría. Y `--no-restore` para que un paquete no declarado falle en vez de colarse.
RUN dotnet publish src/GestorInve.Api \
    --configuration Release \
    --no-restore \
    -warnaserror \
    --output /aplicacion

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /aplicacion

# Usuario sin privilegios, creado explícitamente. Un proceso que no necesita root no debe
# tenerlo: si alguien consigue ejecución de código, el radio de daño no incluye el sistema.
RUN useradd --system --create-home --uid 64200 gestorinve
COPY --from=construccion --chown=gestorinve:gestorinve /aplicacion ./
USER gestorinve

ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_NOLOGO=true \
    DOTNET_CLI_TELEMETRY_OPTOUT=true
EXPOSE 8080

# `/ready` toca la base de datos; `/health` solo responde que el proceso vive. Aquí interesa
# el primero: un contenedor que arranca pero no alcanza PostgreSQL no está listo para tráfico.
HEALTHCHECK --interval=10s --timeout=3s --start-period=40s --retries=12 \
    CMD ["dotnet", "GestorInve.Api.dll", "--comprobar-salud"]

ENTRYPOINT ["dotnet", "GestorInve.Api.dll"]
