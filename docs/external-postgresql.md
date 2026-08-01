# Connect an external PostgreSQL database

SchemaRag supports multiple PostgreSQL databases through server-side profiles.
Browser users select a configured profile and its tables; they cannot submit a
host, username, password, or connection string.

## 1. Create a least-privilege role

Run the following as a database administrator and replace the database, schema,
role, and password values for your environment:

```sql
CREATE ROLE schema_rag_reader
WITH
    LOGIN
    PASSWORD '<generate-a-strong-password>'
    NOSUPERUSER
    NOCREATEDB
    NOCREATEROLE
    NOINHERIT
    NOREPLICATION
    NOBYPASSRLS
    CONNECTION LIMIT 5;

GRANT CONNECT ON DATABASE reporting TO schema_rag_reader;
GRANT USAGE ON SCHEMA public TO schema_rag_reader;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO schema_rag_reader;

ALTER ROLE schema_rag_reader SET default_transaction_read_only = on;
ALTER ROLE schema_rag_reader SET statement_timeout = '5s';
ALTER ROLE schema_rag_reader SET lock_timeout = '1s';
ALTER ROLE schema_rag_reader SET idle_in_transaction_session_timeout = '10s';
ALTER ROLE schema_rag_reader SET search_path = pg_catalog;
```

Grant only the schemas and tables that this application may query. Do not reuse
an owner, migration, application-write, superuser, or cloud-admin account.

Future tables are intentionally not granted automatically. If you choose to use
`ALTER DEFAULT PRIVILEGES`, first confirm which owner creates tables and whether
automatic access is acceptable for your data-governance policy.

## 2. Configure the local profile

Copy the safe Compose template:

```bash
cp compose.external-db.example.yaml compose.external-db.yaml
```

Add these values to `.env`:

```dotenv
EXTERNAL_DB_DISPLAY_NAME=Reporting database
EXTERNAL_DB_CONNECTION_STRING=Host=host.docker.internal;Port=5432;Database=reporting;Username=schema_rag_reader;Password=<reader-password>;SSL Mode=Prefer;Maximum Pool Size=5;Command Timeout=5;Include Error Detail=false
EXTERNAL_DB_SCHEMA=public
```

`compose.external-db.yaml` and `.env` are ignored by Git. For a shared or hosted
environment, inject the connection string from a secret manager rather than a
checked-in file.

Use a host reachable from the **API container**. `localhost` inside the container
refers to the API container, not the Docker host.

## 3. Start the API with the profile

```bash
docker compose \
  -f compose.yaml \
  -f compose.external-db.yaml \
  up -d --build api
```

Open <http://localhost:8080/>, select the external profile, and click **Discover
tables**. Select only the tables needed for the demo, then index them.

Reindexing replaces the existing Qdrant documents for that profile. It does not
append to the previous selection.

## 4. Verify the role

Connect with the reader credentials and verify:

```sql
SHOW transaction_read_only;
SELECT has_database_privilege(current_user, current_database(), 'TEMP');
SELECT has_schema_privilege(current_user, 'public', 'CREATE');

SELECT table_schema, table_name, privilege_type
FROM information_schema.role_table_grants
WHERE grantee = current_user
  AND privilege_type <> 'SELECT';
```

The first result must be `on`, schema `CREATE` must be `false`, and the final
query must return no rows. `TEMP` should also be `false` when the database policy
allows it. PostgreSQL commonly grants temporary-table access through `PUBLIC`,
so review the impact on other roles before revoking that database-wide grant.

## Security notes

- Prefer TLS with certificate verification for remote databases.
- Restrict network access to the API runtime and approved database host.
- Use curated reporting views when tables contain sensitive columns.
- Rotate credentials and audit grants regularly.
- Authentication and per-source authorization are required before exposing the
  API beyond a trusted local environment.
- Table selection improves relevance; PostgreSQL permissions remain the security
  boundary.
