# Configura gestor-inve tras copiar los archivos. Lo ejecuta el instalador, con privilegios de
# administrador. Es **idempotente**: en una actualización conserva la base de datos y los
# secretos, y solo reinicia los servicios.
#
# Todo queda registrado en C:\ProgramData\GestorInve\instalacion.log: si algo falla en el equipo
# de un usuario, ese archivo es lo único que hay para saber qué pasó.
param(
    [Parameter(Mandatory = $true)][string]$Raiz
)

$ErrorActionPreference = 'Stop'

$Datos          = Join-Path $env:ProgramData 'GestorInve'
$Cluster        = Join-Path $Datos 'datos'
$Configuracion  = Join-Path $Datos 'configuracion.json'
$Secretos       = Join-Path $Datos 'secretos.json'
$Registro       = Join-Path $Datos 'instalacion.log'
$PgBin          = Join-Path $Raiz 'pgsql\bin'
$Api            = Join-Path $Raiz 'app\GestorInve.Api.exe'

# Puertos fijos y poco comunes: el clúster es propio de gestor-inve y no debe chocar con un
# PostgreSQL que el equipo ya tenga en el 5432.
$PuertoBd       = 48621
$PuertoWeb      = 48620
$ServicioBd     = 'GestorInvePostgres'
$ServicioApi    = 'GestorInve'

New-Item -ItemType Directory -Force -Path $Datos | Out-Null

function Anotar([string]$mensaje) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  $mensaje" | Add-Content -Path $Registro -Encoding UTF8
}

# Ejecuta un programa y **falla si falla**: PowerShell no trata un código de salida distinto de
# cero como error, y una instalación que sigue adelante tras un `initdb` roto es peor que una
# que se detiene.
function Ejecutar([string]$programa, [string[]]$argumentos, [switch]$Silencioso) {
    # `Continue` solo aquí dentro: con `Stop`, Windows PowerShell convierte cualquier línea que
    # el programa escriba en su salida de error en una excepción, e `initdb` escribe ahí avisos
    # normales. Lo que decide si falló es el código de salida.
    $ErrorActionPreference = 'Continue'
    $salida = & $programa @argumentos 2>&1
    $codigo = $LASTEXITCODE
    if (-not $Silencioso) { Anotar "$([IO.Path]::GetFileName($programa)) -> $codigo" }
    if ($codigo -ne 0) {
        Anotar ($salida | Out-String)
        throw "$([IO.Path]::GetFileName($programa)) terminó con código $codigo. Vea $Registro"
    }
    return $salida
}

function Aleatorio([int]$bytes) {
    $b = New-Object byte[] $bytes
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    return -join ($b | ForEach-Object { $_.ToString('x2') })
}

# Solo SYSTEM, Administradores y la cuenta indicada. Los secretos de la instalación no deben
# poder leerlos los demás usuarios del equipo.
function Restringir([string]$ruta, [string]$cuenta) {
    Ejecutar 'icacls.exe' @($ruta, '/inheritance:r', '/grant:r', '*S-1-5-18:F', '/grant:r', '*S-1-5-32-544:F', '/grant:r', "${cuenta}:R") -Silencioso | Out-Null
}

function Sql([string]$sentencia, [string]$base = 'postgres') {
    $env:PGPASSWORD = $script:ClaveSuper
    try {
        return (Ejecutar (Join-Path $PgBin 'psql.exe') @('-h', '127.0.0.1', '-p', "$PuertoBd", '-U', 'postgres', '-d', $base, '-v', 'ON_ERROR_STOP=1', '-tA', '-c', $sentencia) -Silencioso | Out-String).Trim()
    } finally {
        Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    }
}

try {
    Anotar "=== Instalación desde $Raiz"

    # --- Secretos: se generan aquí, **en cada instalación**; no vienen en el instalador. Una
    # clave de firma repartida con el instalador sería la misma en todos los equipos.
    if (Test-Path $Secretos) {
        $s = Get-Content $Secretos -Raw | ConvertFrom-Json
        Anotar 'Secretos existentes: se conservan'
    } else {
        $s = [pscustomobject]@{
            ClaveSuper = Aleatorio 24
            ClaveBd    = Aleatorio 24
            ClaveJwt   = Aleatorio 48
        }
        $s | ConvertTo-Json | Set-Content -Path $Secretos -Encoding UTF8
        Anotar 'Secretos generados'
    }
    Restringir $Secretos '*S-1-5-32-544'
    $script:ClaveSuper = $s.ClaveSuper

    # --- Clúster de PostgreSQL propio
    if (-not (Test-Path (Join-Path $Cluster 'PG_VERSION'))) {
        $pw = Join-Path $Datos 'pw.tmp'
        # Sin BOM: `initdb` leería los bytes del BOM como parte de la contraseña.
        [IO.File]::WriteAllText($pw, $s.ClaveSuper, (New-Object Text.UTF8Encoding $false))
        try {
            Ejecutar (Join-Path $PgBin 'initdb.exe') @('-D', $Cluster, '-U', 'postgres', '-A', 'scram-sha-256', "--pwfile=$pw", '-E', 'UTF8', '--locale-provider=icu', '--icu-locale=es', '--locale=C') | Out-Null
        } finally {
            Remove-Item $pw -Force -ErrorAction SilentlyContinue
        }
        # Solo el bucle local: es una instalación de un solo equipo.
        Add-Content -Path (Join-Path $Cluster 'postgresql.conf') -Encoding ASCII -Value "`nport = $PuertoBd`nlisten_addresses = '127.0.0.1'`n"
        Anotar 'Clúster creado'
    } else {
        Anotar 'Clúster existente: se conserva'
    }

    # El servicio de PostgreSQL corre como NetworkService, no como SYSTEM: necesita poder
    # escribir en su carpeta de datos y nada más.
    Ejecutar 'icacls.exe' @($Cluster, '/grant', '*S-1-5-20:(OI)(CI)F', '/T', '/Q') -Silencioso | Out-Null

    if (-not (Get-Service $ServicioBd -ErrorAction SilentlyContinue)) {
        Ejecutar (Join-Path $PgBin 'pg_ctl.exe') @('register', '-N', $ServicioBd, '-D', $Cluster, '-S', 'auto', '-U', 'NT AUTHORITY\NetworkService', '-w') | Out-Null
    }
    Start-Service $ServicioBd

    $lista = $false
    foreach ($i in 1..60) {
        & (Join-Path $PgBin 'pg_isready.exe') -h 127.0.0.1 -p $PuertoBd -q
        if ($LASTEXITCODE -eq 0) { $lista = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $lista) { throw 'PostgreSQL no respondió tras iniciarse.' }
    Anotar 'PostgreSQL responde'

    # --- Rol y base de la aplicación. Lo que importa es lo que **no** se concede: ni SUPERUSER
    # ni BYPASSRLS. Cualquiera de los dos anularía el aislamiento entre empresas (ADR 0003).
    if ((Sql "select count(*) from pg_roles where rolname = 'gestorinve'") -eq '0') {
        Sql "create role gestorinve login password '$($s.ClaveBd)' nosuperuser nocreatedb nocreaterole noinherit nobypassrls" | Out-Null
    } else {
        Sql "alter role gestorinve login password '$($s.ClaveBd)' nosuperuser nocreatedb nocreaterole nobypassrls" | Out-Null
    }
    if ((Sql "select count(*) from pg_database where datname = 'gestorinve'") -eq '0') {
        Sql 'create database gestorinve owner gestorinve' | Out-Null
    }

    # Comprobación en la propia instalación, no solo en el CI.
    if ((Sql "select rolsuper or rolbypassrls from pg_roles where rolname = 'gestorinve'") -ne 'f') {
        throw 'El rol de la base tiene privilegios que anularían el aislamiento entre empresas.'
    }
    Anotar 'Rol y base comprobados'

    # --- Configuración de la Api. En un archivo con permisos restringidos y no en el entorno del
    # servicio: ese entorno vive en el registro, donde cualquier usuario del equipo puede leerlo.
    [ordered]@{
        ConnectionStrings = @{ GestorInve = "Host=127.0.0.1;Port=$PuertoBd;Database=gestorinve;Username=gestorinve;Password=$($s.ClaveBd)" }
        Jwt               = @{ Clave = $s.ClaveJwt }
    } | ConvertTo-Json -Depth 4 | ForEach-Object { [IO.File]::WriteAllText($Configuracion, $_, (New-Object Text.UTF8Encoding $false)) }
    Restringir $Configuracion '*S-1-5-19'

    # --- Servicio de la Api, como LocalService: una cuenta sin privilegios sobre el equipo.
    if (-not (Get-Service $ServicioApi -ErrorAction SilentlyContinue)) {
        Ejecutar 'sc.exe' @('create', $ServicioApi, 'binPath=', $Api, 'start=', 'delayed-auto', 'obj=', 'NT AUTHORITY\LocalService', 'depend=', $ServicioBd, 'DisplayName=', 'gestor-inve') | Out-Null
        Ejecutar 'sc.exe' @('description', $ServicioApi, 'Gestor de inventarios. Escucha solo en este equipo.') -Silencioso | Out-Null
        Ejecutar 'sc.exe' @('failure', $ServicioApi, 'reset=', '86400', 'actions=', 'restart/5000/restart/5000/restart/60000') -Silencioso | Out-Null
    }

    # La ruta del ejecutable se escribe **entre comillas** directamente en el registro. Pasar
    # comillas a `sc.exe` desde PowerShell no es fiable, y una ruta de servicio con espacios y
    # sin comillas (`C:\Program Files\...`) es una vulnerabilidad conocida: Windows probaría a
    # ejecutar `C:\Program.exe`. También cubre la actualización a otra carpeta.
    Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServicioApi" -Name 'ImagePath' -Value "`"$Api`""

    # Entorno del servicio: **solo** datos no secretos.
    $entorno = @(
        "GESTORINVE_CONFIG=$Configuracion",
        "ASPNETCORE_URLS=http://127.0.0.1:$PuertoWeb",
        'ASPNETCORE_ENVIRONMENT=Production',
        'GESTORINVE_MIGRAR=true',
        'DOTNET_CLI_TELEMETRY_OPTOUT=1'
    )
    New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServicioApi" -Name 'Environment' -PropertyType MultiString -Value $entorno -Force | Out-Null

    Restart-Service $ServicioApi

    $arriba = $false
    foreach ($i in 1..90) {
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:$PuertoWeb/ready" -UseBasicParsing -TimeoutSec 3
            if ($r.StatusCode -eq 200) { $arriba = $true; break }
        } catch { }
        Start-Sleep -Seconds 1
    }
    if (-not $arriba) { throw 'gestor-inve no respondió tras iniciarse el servicio.' }

    Anotar '=== Instalación correcta'
    exit 0
}
catch {
    Anotar "ERROR: $($_.Exception.Message)"
    Anotar ($_.ScriptStackTrace | Out-String)
    Write-Error $_
    exit 1
}
