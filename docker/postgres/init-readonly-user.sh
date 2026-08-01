#!/bin/sh
set -eu

if [ -z "${APP_READONLY_PASSWORD:-}" ]; then
    echo "APP_READONLY_PASSWORD must be set" >&2
    exit 1
fi

psql \
    --set=ON_ERROR_STOP=1 \
    --set=reader_password="$APP_READONLY_PASSWORD" \
    --set=database_name="$POSTGRES_DB" \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" <<'SQL'
SELECT 'CREATE ROLE schema_rag_reader LOGIN'
WHERE NOT EXISTS (
    SELECT 1 FROM pg_catalog.pg_roles WHERE rolname = 'schema_rag_reader'
) \gexec

SELECT format(
    'ALTER ROLE schema_rag_reader PASSWORD %L',
    :'reader_password'
) \gexec

ALTER ROLE schema_rag_reader
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS;
ALTER ROLE schema_rag_reader SET default_transaction_read_only = on;
ALTER ROLE schema_rag_reader SET statement_timeout = '5s';

GRANT CONNECT ON DATABASE :"database_name" TO schema_rag_reader;
REVOKE TEMPORARY ON DATABASE :"database_name" FROM schema_rag_reader;
REVOKE TEMPORARY ON DATABASE :"database_name" FROM PUBLIC;
SQL
