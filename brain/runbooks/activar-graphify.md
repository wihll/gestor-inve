---
estado: pendiente-de-ejecutar
actualizado: 2026-10-15
---
# Activar graphify (una vez por clon)
Los hooks modifican la configuración del agente, por lo que se ejecutan a mano:

```bash
./scripts/setup-graphify.sh
```
Equivale a `graphify claude install` + `graphify hook install`. Después, con código presente: `/graphify .` y commitear `graphify-out/`.
Ver [[../INDEX]].
