# Security policy

## Supported use

SchemaRag is an educational local-development sample. It is not a production
security boundary and should not be exposed directly to the internet.

The current repository is maintained as a single evolving sample. Security fixes
are applied to the latest revision only.

## Reporting a vulnerability

Please use GitHub's private vulnerability reporting feature for the repository.
Do not open a public issue containing credentials, database contents, exploit
details, or personally identifiable information.

Include:

- the affected revision;
- the vulnerable component or endpoint;
- reproduction steps using non-sensitive test data; and
- the expected security impact.

## Before publishing or deploying

- Keep `.env` and local Compose overlays untracked.
- Run a secret scanner against the working tree and Git history.
- Use different random passwords for the setup and read-only PostgreSQL roles.
- Keep PostgreSQL, Qdrant, and Ollama bound to loopback unless explicitly secured.
- Add authentication and per-source authorization before network exposure.
- Restrict the API with a firewall or private-network ACL.
- Use a secret manager and TLS for external PostgreSQL sources.
- Replace lexical SQL validation with AST-based policy enforcement for production.
- Query curated views or a reporting replica that excludes sensitive data.
- Enable auditing, query-cost controls, monitoring, and credential rotation.

Never rely on the prompt or model behavior as a security control. Generated SQL
must remain untrusted even when it passes this sample's validator.
