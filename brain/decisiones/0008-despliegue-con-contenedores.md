---
estado: aceptado
fecha: 2026-10-16
---
# 0008 Despliegue con contenedores y actualización del esquema

## Contexto
H3 dejó el sistema completo pero sin forma de instalarlo: compilar la solución, levantar PostgreSQL, crear un rol sin privilegios, aplicar nueve migraciones y servir el SPA detrás de un proxy son seis pasos que una pyme sin personal de sistemas no va a ejecutar bien. H4 empaqueta eso en `docker compose up -d`.

## Decisiones

### Las migraciones se aplican al arrancar, pero solo con bandera explícita
La Api aplica las migraciones pendientes al arrancar **únicamente** si `GESTORINVE_MIGRAR=true`. Nunca por omisión.

El origen fue práctico: el job de punta a punta fallaba instalando la herramienta global `dotnet-ef` en el runner. Se podía pelear con la instalación, pero el problema de fondo era otro: había **tres caminos distintos** para llevar una base al día (la herramienta en el CI, un script a mano en la instalación, y nada en el contenedor), y solo uno se ejecutaba de verdad. Ahora hay uno, y lo ejerce el CI en cada push.

Que no sea el comportamiento por omisión no es timidez. En una instalación real, migrar sin que nadie lo haya pedido puede tomar un `ACCESS EXCLUSIVE` sobre tablas en uso o aplicar un cambio de esquema a destiempo, por ejemplo cuando se arranca una segunda instancia para repartir carga: dos procesos migrando a la vez es exactamente el escenario que hay que poder evitar. Con la bandera, quien actualiza decide cuándo.

Qué migraciones se aplicaron queda en la traza (`GestorInve.Migraciones`, evento 2001). Si una actualización sale mal, lo primero que se necesita saber es dónde quedó el esquema.

### El rol de la base de datos no es superusuario, y la instalación lo comprueba
`scripts/init-db.sh` crea el rol `gestorinve` con `nosuperuser nobypassrls` y **verifica el resultado**, abortando el arranque si quedó privilegiado. `postgres` sigue siendo el superusuario y la aplicación no lo usa.

Es la misma comprobación que ya hacía el CI, llevada a la instalación. Un superusuario (o cualquier rol con `BYPASSRLS`) ignora las políticas de Row Level Security de [[0003-persistencia-y-aislamiento]]: una consulta mal filtrada devolvería los datos de **todas** las empresas, y ninguna prueba lo notaría porque las políticas seguirían ahí, simplemente sin aplicarse. Es el fallo más grave que este sistema puede tener y el más silencioso.

### Mismo origen: la Api no publica puerto
Solo el contenedor de la web publica puerto. nginx sirve el SPA y reenvía los prefijos `/auth`, `/empresas`, `/catalogo`, `/inventario` y `/usuarios` a la Api por la red interna del compose.

Así no hace falta CORS, y queda abierta la vía de la cookie `httpOnly` que [[0007-interfaz-web]] dejó pendiente: con dos orígenes distintos, esa cookie exigiría `SameSite=None` y perdería buena parte de su valor. De paso, la Api no es alcanzable desde fuera del anfitrión.

### Los secretos no tienen valor por omisión
El compose los lee con `${VAR:?}`, que **falla si faltan**. Un valor por omisión en el repositorio sería una clave publicada: cualquiera que clonara el proyecto conocería la clave de firma de los tokens de todas las instalaciones que no la cambiaron. `.env.ejemplo` dice cómo generarlas; el CI comprueba que sin `.env` el compose no arranca.

### La sonda de salud vive en el propio ejecutable
La imagen de runtime de ASP.NET no trae `curl` ni `wget`, así que el `HEALTHCHECK` no tiene con qué consultar `/ready`. Antes que añadir un paquete entero a la imagen final, el ejecutable acepta `--comprobar-salud`: hace una petición a la instancia que ya escucha y traduce la respuesta a código de salida. Deriva el puerto de `ASPNETCORE_URLS` para no quedar desincronizada, y resuelve los comodines (`0.0.0.0`) al bucle local, que es a lo que sí se puede conectar.

Se comprobó en los dos sentidos: devuelve 1 sin nadie escuchando y 0 contra una instancia sana.

### El compose se ejecuta en el CI
Un `docker-compose.yml` que nadie arranca es código no ejecutado, y la documentación de instalación envejece peor que el código. El job `compose` lo construye, lo levanta con `--wait`, y comprueba: que el rol no tenga privilegios, que el esquema quedara aplicado (al menos 16 tablas), que la web devuelva el SPA también en una ruta del enrutador del cliente, que `/auth/yo` responda **401 y no 404** (prueba de que la petición atravesó el proxy y llegó a la Api), que un alta de empresa devuelva 201 —la cadena nginx → Api → PostgreSQL con RLS completa— y que el contenedor de la Api se declare sano.

## Pendientes
- **No verificado en este entorno:** no hay demonio de Docker en el contenedor de desarrollo, así que las imágenes y el compose solo se ejercitan en el runner. Lo que el CI no comprueba (por ejemplo, el comportamiento tras reiniciar la máquina anfitriona) sigue sin verificar.
- Copias de seguridad: el volumen `datos-db` persiste, pero no hay trabajo programado de respaldo. Va en H10 con el runbook.
- TLS: el compose sirve HTTP. Delante debe ir un proxy con certificado; sin él, las credenciales viajan en claro.
- Imagen de la web con nginx oficial: el proceso maestro corre como root y los trabajadores bajan a `nginx`. Una variante sin privilegios es mejor y queda pendiente.
