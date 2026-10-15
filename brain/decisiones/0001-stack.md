---
estado: aceptado
fecha: 2026-10-15
---
# 0001 Stack tecnológico
**Contexto:** multiplataforma real (Windows/Linux), aritmética decimal exacta, tipado fuerte, empaquetado nativo.
**Decisión:** **.NET 10 (LTS)** + ASP.NET Core + EF Core, PostgreSQL 16, React + TypeScript (Vite).
**Por qué .NET 10 y no 9:** .NET 9 es STS y sale de soporte en nov-2026; 8 (LTS) también termina en nov-2026. .NET 10 es la LTS vigente y está en los repos de Ubuntu 24.04 (`dotnet-sdk-10.0`).
**Descartado:** NestJS/TypeScript (aritmética decimal propensa a error).
**Consecuencias:** publicar *self-contained* en instaladores; fijar SDK en `global.json`.
