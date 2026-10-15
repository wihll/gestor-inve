---
estado: aceptado
fecha: 2026-10-15
---
# 0004 Autenticación y autorización

## Contexto
Un ERP guarda costos, márgenes y datos fiscales de varias empresas en el mismo despliegue. El acceso es la puerta de entrada a todo eso, y los fallos típicos (contraseñas mal guardadas, tokens eternos, enumeración de usuarios) no se notan hasta que ya hubo un incidente.

## Decisiones

### Contraseñas
**PBKDF2-HMAC-SHA512, 210.000 iteraciones, sal de 16 bytes por contraseña.** Es el mínimo que recomienda OWASP para esta primitiva. Se eligió PBKDF2 y no Argon2id (preferible en teoría) porque está en la BCL y no añade una dependencia de criptografía de terceros a un proyecto que aún no tiene mantenedores. El formato guardado es `pbkdf2-sha512$iteraciones$sal$hash`: lleva delante el algoritmo y el coste, así que **endurecerlo o migrar a Argon2id no invalida las claves existentes**.

Comparación en tiempo constante (`CryptographicOperations.FixedTimeEquals`). Un hash con formato inesperado devuelve `false` en vez de lanzar: un error distinto según lo guardado sería un canal lateral.

### No delatar quién está registrado
Un NIT inexistente, un usuario inexistente y una contraseña equivocada devuelven **la misma respuesta** y consumen **el mismo trabajo** (`VerificarContraFantasma` hace una derivación completa cuando no hay usuario). Sin esto, el tiempo de respuesta o el código de error permiten enumerar clientes y empleados. Hay una prueba que compara las tres respuestas.

### Bloqueo por intentos
5 intentos fallidos consecutivos → 15 minutos de bloqueo. El orden de las comprobaciones importa y está probado: **primero el bloqueo, después la contraseña**. Si se validara la clave antes, acertarla saltaría el bloqueo y la fuerza bruta no encontraría freno. El contador se persiste también cuando el intento falla; si no, el bloqueo no existiría en la práctica.

### Tokens
- **Acceso**: JWT HS256, 15 minutos, sin margen de reloj (`ClockSkew = TimeSpan.Zero`; el valor por omisión de 5 minutos alarga la vida de un token ya caducado). No se puede revocar, solo esperar: de ahí la vida corta.
- **Refresco**: 32 bytes aleatorios, **guardado solo como hash** SHA-256 (el token es aleatorio, no hay nada que adivinar por fuerza bruta: no necesita PBKDF2). Dura 14 días y **es de un solo uso**.
- **Rotación y detección de reutilización**: cada refresco emite un token nuevo en la misma *familia*. Si llega uno ya consumido, se revoca **la familia completa**, porque no se puede distinguir al usuario legítimo del atacante que copió el token. Probado de punta a punta.
- El token de refresco viaja como `<empresa>.<secreto>`: al refrescar, el token de acceso puede estar caducado y no se puede confiar en sus reclamaciones para saber la empresa. El identificador no es un secreto; la empresa efectiva siempre la fija el servidor.
- **La clave de firma no tiene valor por defecto**: si falta o mide menos de 32 bytes, el arranque falla (verificado ejecutando la Api). Un sistema que arranca con una clave de ejemplo acepta tokens que cualquiera fabrica.

### Autorización
RBAC con permisos `recurso:accion` (`Permiso.Todos`). Los roles pertenecen a la empresa, y cada permiso es una política de ASP.NET Core: el endpoint declara el permiso que exige y no interpreta roles. Los permisos efectivos viajan en el token como reclamaciones.

### La empresa de la petición
`ContextoTenantHttp` la toma **solo del token ya validado**, nunca de una cabecera, la ruta o el cuerpo. Si el cliente pudiera influir en ella, el aislamiento multiempresa caería con un encabezado falsificado. Sin usuario autenticado no hay empresa, y el contexto queda cerrado por defecto.

### Lo que se construyó a mano, y lo que no
Las primitivas son de la plataforma (PBKDF2, `RandomNumberGenerator`, validación de JWT de Microsoft). Lo propio es el agregado de usuario y el cableado, en vez de ASP.NET Core Identity, cuyo esquema y sus almacenes no encajan con el aislamiento por empresa y las convenciones de este proyecto. Es una decisión consciente: el riesgo se acota con pruebas sobre cada invariante (orden del bloqueo, rotación, equivalencia de respuestas).

## Pendientes
- **Tabla de auditoría de accesos**: hoy los intentos fallidos se registran con `ILogger` y en el contador del usuario. Falta un registro consultable; `auditoria.registros` exige empresa, y un intento con NIT inexistente no la tiene.
- Alta de usuarios y roles por parte del administrador (solo existe el primer administrador del alta de empresa).
- Recuperación de contraseña, segundo factor, y expiración de sesiones por inactividad.
- Límite de peticiones por IP: el bloqueo protege la cuenta, no al servidor de una avalancha.
