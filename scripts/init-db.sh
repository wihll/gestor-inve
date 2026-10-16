#!/bin/bash
# Crea el rol y la base de datos de la aplicación la primera vez que arranca el volumen.
#
# Lo que importa aquí es lo que **no** se hace: el rol `gestorinve` no es superusuario y no
# tiene BYPASSRLS. Cualquiera de las dos cosas anularía las políticas de Row Level Security
# que aíslan a las empresas entre sí (ADR 0003): con un superusuario, una consulta mal
# filtrada devolvería los datos de todas las empresas y ninguna prueba lo notaría.
set -euo pipefail

: "${GESTORINVE_CLAVE_BD:?falta GESTORINVE_CLAVE_BD}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-SQL
	create role gestorinve login password '${GESTORINVE_CLAVE_BD}'
	    nosuperuser nocreatedb nocreaterole noinherit nobypassrls;
	create database gestorinve owner gestorinve;
SQL

# Comprobación en la propia instalación, no solo en el CI: si el rol quedara con privilegios
# el arranque debe fallar aquí y no silenciosamente en producción.
privilegiado=$(psql -tAc \
	"select rolsuper or rolbypassrls from pg_roles where rolname = 'gestorinve'" \
	--username "$POSTGRES_USER" --dbname postgres)

if [ "$privilegiado" != "f" ]; then
	echo "El rol gestorinve tiene privilegios que anularían el aislamiento RLS." >&2
	exit 1
fi

# El esquema `public` queda sin permiso de creación para el resto: desde PostgreSQL 15 ya es
# así por omisión, se deja explícito para que un `initdb` de otra versión no lo relaje.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname gestorinve <<-'SQL'
	revoke create on schema public from public;
SQL
