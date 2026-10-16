# gestor-inve
Gestor de inventarios para empresas pequeñas y medianas.

ERP de inventarios **open source** (AGPL-3.0) para Windows y Linux. Stack: .NET 10, PostgreSQL 16 con Row Level Security, React + TypeScript.

- Documentación y decisiones: [`brain/INDEX.md`](brain/INDEX.md)
- Cómo contribuir: [`CONTRIBUTING.md`](CONTRIBUTING.md) · Contrato de calidad: [`CONSTRAINTS.md`](CONSTRAINTS.md)
- Hoja de ruta: [`brain/plan-esqueleto.md`](brain/plan-esqueleto.md)

## Instalar

```bash
cp .env.ejemplo .env     # y generar los tres secretos; ver el runbook
docker compose up --build --detach --wait
```

La web queda en `http://localhost:8080`. Pasos completos, actualización y diagnóstico: [`brain/runbooks/instalacion.md`](brain/runbooks/instalacion.md).

**No hay usuario por omisión:** la primera empresa se registra desde la pantalla de alta. Y el compose **falla si no se generan los secretos**, a propósito: una clave de firma por omisión en el repositorio sería la clave de todas las instalaciones que no la cambiaran.

## Qué hace hoy
Multiempresa con aislamiento por Row Level Security; catálogo con productos, variantes, unidades fraccionadas y códigos de barras; bodegas y ubicaciones; **lotes y seriales**, con salida por vencimiento más próximo (FEFO) y bloqueo de la mercancía vencida; kardex **inmutable** (se corrige con movimientos de reversa) valorizado por promedio ponderado **por lote**; entradas, salidas, ajustes y traslados con bloqueo pesimista; autenticación con tokens de acceso y refresco rotativo; y la interfaz web para todo lo anterior.

## Qué no hace todavía
Costeo PEPS, reportes, importación y exportación XLSX, compras, ventas, contabilidad y facturación electrónica DIAN. Ver la hoja de ruta.
