---
estado: vigente
actualizado: 2026-11-17
---
# Instalación de escritorio (un solo equipo)

Para usar gestor-inve en un computador, sin Docker. Los instaladores están en la página **Releases** del repositorio. Decisiones de fondo: [[../decisiones/0011-aplicacion-instalable]].

## Ubuntu / Debian
```bash
sudo apt install ./gestor-inve_0.1.0_amd64.deb
```
`apt` instala PostgreSQL si hace falta. Después, ábralo desde el menú de aplicaciones («gestor-inve») o con `gestor-inve`.

**Ventana propia:** necesita Chromium, Chrome o Edge. Ubuntu trae Firefox, así que sin más abrirá una pestaña normal. Para la ventana propia: `sudo snap install chromium`.

La primera pantalla es el **alta de empresa**: no hay usuario de fábrica.

| Qué | Cómo |
|---|---|
| Estado del servicio | `systemctl status gestor-inve` |
| Registro | `journalctl -u gestor-inve -n 50` |
| Copia de seguridad | `sudo gestor-inve-respaldo` → `~/gestor-inve-copias/` |
| Restaurar una copia | `sudo systemctl stop gestor-inve && sudo -u postgres pg_restore --clean --if-exists -d gestorinve copia.dump && sudo systemctl start gestor-inve` |
| Actualizar | instalar el `.deb` nuevo encima: conserva datos y secretos |
| Reparar (el servicio no arranca tras restaurar o tocar la base) | `sudo dpkg-reconfigure gestor-inve` o reinstalar el mismo `.deb` |
| Desinstalar | `sudo apt remove gestor-inve` — **no borra los datos** |
| Borrar los datos definitivamente | `sudo -u postgres dropdb gestorinve && sudo -u postgres dropuser gestorinve` |

## Windows 10 / 11
Ejecute `gestor-inve-0.1.0-setup.exe`. No está firmado: SmartScreen dirá «editor desconocido»; pulse **Más información → Ejecutar de todas formas**. El asistente instala todo (incluido PostgreSQL, en un clúster propio que no interfiere con otro que ya tenga) y deja un icono en el escritorio.

| Qué | Cómo |
|---|---|
| Servicios | `GestorInve` y `GestorInvePostgres` en «Servicios» |
| Registro de la instalación | `C:\ProgramData\GestorInve\instalacion.log` |
| Copia de seguridad | menú Inicio → gestor-inve → **Copia de seguridad** → `Documentos\gestor-inve-copias` |
| Actualizar | ejecutar el instalador nuevo: conserva datos y secretos |
| Desinstalar | «Agregar o quitar programas» — **no borra los datos** |
| Borrar los datos definitivamente | eliminar `C:\ProgramData\GestorInve` tras desinstalar |

## Lo que conviene saber
- Escucha **solo en este equipo** (`127.0.0.1:48620`). Para varios equipos, el camino es el despliegue con Docker ([instalacion.md](instalacion.md)).
- **Haga copias y guárdelas fuera del equipo.** No hay copia automática, y una copia en el mismo disco no protege de un disco dañado.
- Los secretos de la instalación (`/etc/gestor-inve/` o `C:\ProgramData\GestorInve\`) son de ese equipo: no los copie a otro.
