# Quita los servicios de gestor-inve. **No borra los datos**: la base de datos y los secretos
# se quedan en C:\ProgramData\GestorInve. Borrar el inventario de una empresa por quitar un
# programa sería imperdonable; quien de verdad quiera borrarlo, elimina esa carpeta a mano.
param([Parameter(Mandatory = $true)][string]$Raiz)

$ErrorActionPreference = 'Continue'
$PgBin = Join-Path $Raiz 'pgsql\bin'

foreach ($servicio in 'GestorInve', 'GestorInvePostgres') {
    if (Get-Service $servicio -ErrorAction SilentlyContinue) {
        Stop-Service $servicio -Force -ErrorAction SilentlyContinue
    }
}

if (Get-Service 'GestorInve' -ErrorAction SilentlyContinue) { & sc.exe delete GestorInve | Out-Null }

if (Get-Service 'GestorInvePostgres' -ErrorAction SilentlyContinue) {
    & (Join-Path $PgBin 'pg_ctl.exe') unregister -N GestorInvePostgres | Out-Null
}

exit 0
