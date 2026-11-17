---
estado: aceptado
fecha: 2026-11-17
---
# 0011 Aplicación instalable de escritorio

## Contexto
[[0008-despliegue-con-contenedores]] dejó el sistema en `docker compose up`, que exige instalar Docker y usar la terminal. El usuario al que va dirigido —una pyme sin personal de sistemas— espera un instalador: doble clic, asistente e icono. H10 lo entrega para **Windows y Linux**, pensado para **un solo equipo** (aplicación y base de datos en la misma máquina) y con **ventana propia**.

## Decisiones

### La Api sirve también la web
En la instalación local no hay nginx. La Api sirve el `wwwroot` **solo si existe**, así que Docker y las pruebas no cambian. Las rutas del enrutador del cliente devuelven el SPA, pero una ruta inexistente bajo `/api` **sigue siendo 404**: quien integra contra la Api espera un error, no una página con 200. Verificado por sabotaje: sin esa frontera fallan tres pruebas.

### Ventana propia sin empaquetar un navegador
El lanzador (`GestorInve.Escritorio`) espera a que `/ready` responda y abre la aplicación en el **modo aplicación** de Edge, Chrome o Chromium (`--app=`): una ventana sin barra de direcciones ni pestañas, con su icono. No se empaqueta Electron ni un WebView: Edge viene en todo Windows 10 y 11, y un motor propio serían cientos de megas y un componente más que actualizar por seguridad. Sin navegador Chromium, abre el navegador por omisión: la aplicación abre igual.

Tres detalles que no son cosméticos:
- **Perfil propio**, no el del usuario: la sesión guarda su token de refresco en el almacenamiento del navegador ([[0007-interfaz-web]]), y en el perfil de uso diario quedaría al alcance de las extensiones instaladas.
- **Solo abre direcciones locales.** El lanzador recibe la URL por argumento; sin esa comprobación, un acceso directo manipulado lo convertiría en una forma de abrir cualquier página en una ventana donde no se ve a dónde se entró.
- El Chromium de Ubuntu es un **snap** y no puede escribir en carpetas ocultas del home: su perfil va en `~/snap/<nombre>/common/`.

### Solo el bucle local
El servicio y PostgreSQL escuchan en `127.0.0.1`. Es una instalación de un solo equipo y nada debe quedar expuesto a la red; el CI lo comprueba en los dos sistemas. El uso desde varios equipos es el despliegue con Docker, detrás de un proxy con TLS.

### Los secretos se generan en cada instalación
La clave de firma y las contraseñas de la base **no vienen en el instalador**: una clave repartida con el instalador sería la misma en todos los equipos, y un usuario de una empresa podría fabricar sesiones válidas para cualquier otra instalación. Una actualización los conserva.
- **Linux**: `/etc/gestor-inve/gestor-inve.env`, modo 600 y de root. systemd lo lee antes de bajar privilegios, así que ni el usuario del servicio puede leerlo del disco.
- **Windows**: archivos en `ProgramData` con ACL restringida, **no** el entorno del servicio: ese entorno vive en el registro, en una clave que cualquier usuario del equipo puede leer. Al servicio solo le llega la ruta.

### PostgreSQL
- **Linux**: dependencia del paquete (`postgresql >= 15`). El `postinst` crea el rol y la base.
- **Windows**: el instalador lleva los binarios y crea un **clúster propio** en `ProgramData`, en un puerto no estándar, para no tocar ni chocar con un PostgreSQL que el equipo ya tenga.

En los dos, el rol de la aplicación se crea sin `SUPERUSER` ni `BYPASSRLS` y la instalación **aborta si quedó privilegiado**: es la comprobación de [[0003-persistencia-y-aislamiento]] llevada al equipo del usuario. Además, en Linux cada configuración vuelve a sincronizar el rol con el archivo de secretos, para que reinstalar repare una base restaurada o un rol borrado.

### Servicios sin privilegios
Linux: usuario de sistema propio y unidad endurecida (`ProtectSystem=strict`, `NoNewPrivileges`, etc.). Windows: la Api como `LocalService` y PostgreSQL como `NetworkService`. La ruta del servicio se escribe entre comillas en el registro: una ruta con espacios y sin comillas es una vulnerabilidad conocida (Windows probaría `C:\Program.exe`).

### Desinstalar no borra los datos
Ni `remove`, ni `purge`, ni el desinstalador de Windows tocan la base. Borrar el inventario de una empresa por quitar un programa sería imperdonable; el borrado definitivo es un paso manual y documentado. Y hay un acceso «Copia de seguridad» (`pg_dump`): en un solo equipo, sin copia, un disco dañado es perder el inventario.

### Los instaladores se instalan en el CI
Un instalador que solo se compila es código no ejecutado. El workflow `Instaladores` construye el `.deb` y el `setup.exe` y **los instala en el runner**: sistema arriba, SPA, alta de empresa 201, sin privilegios, solo bucle local, secretos no legibles, actualización que conserva datos y secretos, copia restaurable, rol privilegiado que aborta y desinstalación que conserva los datos. Con una etiqueta `v*` los publica en Releases.

## Pendientes y límites
- **Sin firma digital**: Windows SmartScreen avisa de «editor desconocido». Firmar exige comprar un certificado.
- El lanzador no se puede probar con pantalla en el CI; se probó a mano en Linux con un Chromium real bajo un servidor gráfico virtual. **La ventana en Windows y el perfil con Chromium snap no se han visto ejecutar.**
- Los binarios de PostgreSQL de Windows se copian de la instalación del runner; si un equipo careciera del runtime de Visual C++ que necesitan, no se detectaría en el CI.
- No hay actualización automática ni copia de seguridad programada: las dos son manuales.
