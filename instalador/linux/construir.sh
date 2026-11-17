#!/usr/bin/env bash
# Construye el paquete .deb de gestor-inve.
#
#   instalador/linux/construir.sh [versión]      →  dist/gestor-inve_<versión>_amd64.deb
#
# La publicación es *self-contained*: el paquete lleva su propio .NET y el equipo donde se
# instala no necesita tenerlo. Lo único que se pide al sistema es PostgreSQL.
set -euo pipefail

VERSION="${1:-0.1.0}"
RAIZ="$(cd "$(dirname "$0")/../.." && pwd)"
AQUI="$RAIZ/instalador/linux"
ARMADO="$RAIZ/dist/deb"
PAQUETE="$RAIZ/dist/gestor-inve_${VERSION}_amd64.deb"

rm -rf "$ARMADO"
mkdir -p "$ARMADO"/{DEBIAN,opt/gestor-inve,usr/bin,lib/systemd/system,usr/share/applications,usr/share/icons/hicolor/scalable/apps}

echo "== Web"
(cd "$RAIZ/web" && npm ci --no-audit --no-fund && npm run build)

echo "== Api y lanzador (linux-x64, self-contained)"
# Los dos se publican en la **misma** carpeta: comparten el runtime de .NET en vez de llevar
# dos copias, que duplicaría el tamaño del paquete.
dotnet publish "$RAIZ/src/GestorInve.Api" -c Release -r linux-x64 --self-contained true \
    -p:Version="$VERSION" -p:DebugType=none -o "$ARMADO/opt/gestor-inve"
dotnet publish "$RAIZ/src/GestorInve.Escritorio" -c Release -r linux-x64 --self-contained true \
    -p:Version="$VERSION" -p:DebugType=none -o "$ARMADO/opt/gestor-inve"

# La Api sirve la web desde `wwwroot` cuando existe (ADR 0011).
rm -rf "$ARMADO/opt/gestor-inve/wwwroot"
cp -r "$RAIZ/web/dist" "$ARMADO/opt/gestor-inve/wwwroot"

echo "== Paquete"
install -m 755 "$AQUI/gestor-inve" "$AQUI/gestor-inve-respaldo" "$ARMADO/usr/bin/"
install -m 644 "$AQUI/gestor-inve.service" "$ARMADO/lib/systemd/system/"
install -m 644 "$AQUI/gestor-inve.desktop" "$AQUI/gestor-inve-respaldo.desktop" "$ARMADO/usr/share/applications/"
install -m 644 "$RAIZ/instalador/comun/gestor-inve.svg" "$ARMADO/usr/share/icons/hicolor/scalable/apps/"

install -m 755 "$AQUI/DEBIAN/postinst" "$AQUI/DEBIAN/prerm" "$AQUI/DEBIAN/postrm" "$ARMADO/DEBIAN/"
sed "s/@VERSION@/$VERSION/" "$AQUI/DEBIAN/control" > "$ARMADO/DEBIAN/control"
echo "Installed-Size: $(du -sk "$ARMADO" --exclude=DEBIAN | cut -f1)" >> "$ARMADO/DEBIAN/control"

# `--root-owner-group`: los archivos quedan de root aunque quien construye no lo sea.
dpkg-deb --build --root-owner-group "$ARMADO" "$PAQUETE" > /dev/null

echo "== Listo: $PAQUETE ($(du -h "$PAQUETE" | cut -f1))"
