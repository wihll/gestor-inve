---
estado: propuesto
fecha: 2026-10-15
---
# 0001 Stack tecnológico
**Contexto:** necesidad de multiplataforma real, aritmética decimal exacta y tipado fuerte.
**Decisión:** .NET 9 + PostgreSQL 16 + React/TS. Alternativa descartada por ahora: NestJS (aritmética decimal propensa a error).
**Consecuencias:** requiere runtime .NET en el instalador (self-contained publish).
