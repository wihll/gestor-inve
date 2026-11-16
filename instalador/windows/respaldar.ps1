# Copia de seguridad de la base de datos de gestor-inve, en Documentos\gestor-inve-copias.
#
# En un solo equipo, sin copia, un disco dañado es perder el inventario. La copia es un volcado
# completo (`pg_dump` en formato propio): se restaura con `pg_restore`.
param(
    [Parameter(Mandatory = $true)][string]$Raiz,
    [string]$Destino = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'gestor-inve-copias')
)

$ErrorActionPreference = 'Stop'

# El archivo de secretos solo lo leen los administradores: si no lo somos, se pide elevación.
$esAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $esAdmin) {
    Start-Process powershell.exe -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-Raiz', "`"$Raiz`"", '-Destino', "`"$Destino`"")
    exit 0
}

try {
    $secretos = Get-Content (Join-Path $env:ProgramData 'GestorInve\secretos.json') -Raw | ConvertFrom-Json
    New-Item -ItemType Directory -Force -Path $Destino | Out-Null
    $archivo = Join-Path $Destino "gestor-inve-$(Get-Date -Format 'yyyyMMdd-HHmmss').dump"

    $env:PGPASSWORD = $secretos.ClaveSuper
    & (Join-Path $Raiz 'pgsql\bin\pg_dump.exe') -h 127.0.0.1 -p 48621 -U postgres --format=custom --file=$archivo gestorinve
    if ($LASTEXITCODE -ne 0) { throw "pg_dump terminó con código $LASTEXITCODE" }

    Write-Host "Copia guardada en $archivo"
    Write-Host 'Guárdela también fuera de este equipo (USB o nube): una copia en el mismo disco no protege de un disco dañado.'
    $codigo = 0
}
catch {
    Write-Host "No se pudo hacer la copia: $($_.Exception.Message)" -ForegroundColor Red
    $codigo = 1
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
}

# Sin esto la ventana se cerraría antes de que el usuario leyera el resultado.
if (-not $env:GESTORINVE_SIN_PAUSA) { Read-Host 'Pulse Intro para cerrar' | Out-Null }
exit $codigo
