---
estado: aceptado
fecha: 2026-10-15
---
# 0002 Licencia AGPL-3.0
**Contexto:** ERP open source para pymes; queremos evitar que un tercero lo ofrezca como SaaS cerrado sin devolver cambios.
**Decisión:** GNU AGPL v3 (`LICENSE`, texto SPDX `AGPL-3.0-only`).
**Alternativas:** Apache-2.0/MIT (más adopción corporativa, sin protección SaaS).
**Consecuencias:** quien ofrezca el sistema por red debe publicar sus modificaciones. Dependencias deben ser compatibles con AGPL (revisar en cada nuevo paquete). Reversible solo con consentimiento de todos los autores → decidirlo antes de aceptar contribuciones externas.
