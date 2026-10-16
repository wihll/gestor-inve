---
estado: vigente
actualizado: 2026-10-16
---
# Instalación con Docker Compose

Forma soportada de poner el sistema en marcha en una sola máquina (Linux o Windows con Docker Desktop). Las decisiones de fondo están en [[../decisiones/0008-despliegue-con-contenedores]].

## Requisitos
- Docker Engine 24 o superior con el plugin `compose`.
- 2 GB de RAM libres y 5 GB de disco para el volumen de la base de datos.

## Pasos

```bash
git clone https://github.com/wihll/gestor-inve.git
cd gestor-inve
```

Generar `.env` con los tres secretos. Se escribe de una vez, sin copiar `.env.ejemplo` primero: así no quedan dos veces las mismas claves en el archivo.

```bash
cat > .env <<ENV
POSTGRES_CLAVE=$(openssl rand -base64 32)
GESTORINVE_CLAVE_BD=$(openssl rand -base64 32 | tr -d '/+=')
JWT_CLAVE=$(openssl rand -base64 48)
PUERTO_WEB=8080
ENV
```

> `JWT_CLAVE` firma los tokens de sesión. **No se reutiliza entre instalaciones:** dos instalaciones con la misma clave aceptan mutuamente los tokens de la otra, así que un usuario de una podría operar en la otra. `GESTORINVE_CLAVE_BD` se filtra de caracteres que la cadena de conexión interpretaría de otra forma.

```bash
docker compose up --build --detach --wait
```

La web queda en `http://localhost:8080`. La Api **no publica puerto**: se alcanza a través de nginx, en el mismo origen.

El primer alta de empresa se hace desde la propia pantalla de registro. No hay usuario por omisión: un ERP con credenciales conocidas de fábrica es una puerta abierta.

## Lo que hace el arranque, para que no sorprenda
1. PostgreSQL crea el rol `gestorinve` **sin** `SUPERUSER` ni `BYPASSRLS`, y aborta si quedó privilegiado. De eso depende el aislamiento entre empresas.
2. La Api arranca con `GESTORINVE_MIGRAR=true` y aplica las migraciones pendientes, dejando en la traza cuáles fueron.
3. `--wait` espera a que los tres contenedores se declaren sanos. El de la Api comprueba `/ready`, que toca la base de datos: «sano» significa que la Api puede consultar de verdad, no solo que el proceso vive.

## Antes de exponerlo a internet
- **TLS es obligatorio.** El compose sirve HTTP; delante debe ir un proxy con certificado (Caddy, nginx o Traefik). Sin él, las contraseñas viajan en claro.
- Cerrar el puerto 8080 al exterior y publicar solo el proxy.
- Programar copias del volumen `datos-db` (ver `copias-de-seguridad.md` cuando exista; **hoy no hay respaldo automático**).

## Actualizar

```bash
git pull
docker compose up --build --detach --wait
```

Antes de actualizar, **copia de la base**. Las migraciones de este proyecto no tienen vuelta atrás automática: si una falla a medias, el camino de recuperación es restaurar.

```bash
docker compose exec -T db pg_dump -U gestorinve gestorinve | gzip > respaldo-$(date +%F).sql.gz
```

## Diagnóstico

| Síntoma | Qué mirar |
|---|---|
| `docker compose up` falla con `falta JWT_CLAVE` | `.env` no existe o la variable está vacía. Es intencional: no hay secretos por omisión |
| El contenedor `api` queda en `unhealthy` | `docker compose logs api`. Casi siempre es la cadena de conexión o que `db` no llegó a estar sano |
| La web carga pero toda petición da 502 | nginx no alcanza `api`. Comprobar que el servicio `api` está arriba y escuchando en 8080 |
| Un alta responde 409 | El NIT ya existe en el directorio de empresas |
