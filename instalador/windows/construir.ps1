# Construye el instalador de Windows:  dist\gestor-inve-<versión>-setup.exe
#
#   instalador\windows\construir.ps1 -Version 0.1.0 [-PgRoot "C:\Program Files\PostgreSQL\16"]
#
# Necesita .NET SDK, Node, Inno Setup 6 (iscc en el PATH o en su carpeta habitual) y una
# instalación de PostgreSQL de la que copiar los binarios que se empaquetan.
param(
    [string]$Version = '0.1.0',
    [string]$PgRoot = $env:PGROOT
)

$ErrorActionPreference = 'Stop'
$Raiz   = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$Armado = Join-Path $Raiz 'dist\windows'

function Comprobar([string]$que) { if ($LASTEXITCODE -ne 0) { throw "$que falló con código $LASTEXITCODE" } }

if (-not $PgRoot -or -not (Test-Path (Join-Path $PgRoot 'bin\initdb.exe'))) {
    throw "No se encontró PostgreSQL en '$PgRoot'. Indique -PgRoot."
}

Remove-Item $Armado -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path (Join-Path $Armado 'app'), (Join-Path $Armado 'pgsql') | Out-Null

Write-Host '== Web'
Push-Location (Join-Path $Raiz 'web')
npm ci --no-audit --no-fund; Comprobar 'npm ci'
npm run build; Comprobar 'npm run build'
Pop-Location

Write-Host '== Api y lanzador (win-x64, self-contained)'
# Los dos en la misma carpeta: comparten el runtime de .NET en vez de llevar dos copias.
dotnet publish (Join-Path $Raiz 'src\GestorInve.Api') -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:DebugType=none -o (Join-Path $Armado 'app'); Comprobar 'publish Api'
dotnet publish (Join-Path $Raiz 'src\GestorInve.Escritorio') -c Release -r win-x64 --self-contained true "-p:Version=$Version" -p:DebugType=none -o (Join-Path $Armado 'app'); Comprobar 'publish Escritorio'
Copy-Item (Join-Path $Raiz 'web\dist') (Join-Path $Armado 'app\wwwroot') -Recurse

Write-Host "== PostgreSQL desde $PgRoot"
# Solo lo que hace falta para ejecutar el servidor: sin pgAdmin, documentación ni cabeceras.
foreach ($carpeta in 'bin', 'lib', 'share') {
    Copy-Item (Join-Path $PgRoot $carpeta) (Join-Path $Armado "pgsql\$carpeta") -Recurse
}
Get-ChildItem (Join-Path $Armado 'pgsql') -Recurse -Include '*.pdb', '*.lib', '*.a' | Remove-Item -Force
Remove-Item (Join-Path $Armado 'pgsql\share\doc'), (Join-Path $Armado 'pgsql\share\man') -Recurse -Force -ErrorAction SilentlyContinue

Write-Host '== Instalador'
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) { $iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
if (-not (Test-Path $iscc)) { throw 'No se encontró Inno Setup 6 (ISCC.exe).' }

& $iscc "/DVersion=$Version" "/DOrigen=$Armado" (Join-Path $PSScriptRoot 'gestor-inve.iss'); Comprobar 'ISCC'

$setup = Join-Path $Raiz "dist\gestor-inve-$Version-setup.exe"
Write-Host "== Listo: $setup ($([math]::Round((Get-Item $setup).Length / 1MB)) MB)"
