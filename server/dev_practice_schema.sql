-- P1-POSTGRES-TRANSACTIONAL-PRACTICE: DEVELOPMENT-ONLY additive schema for the local practice host's optional PostgreSQL backend
-- (MemoryForge:PracticeBackend=postgres). PostgreSQL 16. An administrator or test harness applies it explicitly to a dedicated
-- development/test database, after server/schema.sql and without changing it; the host itself never runs DDL.
--
-- Nothing here touches or references the original account/run/memory_deck/command_receipt tables: practice fixture aliases and
-- pr-<hex> run IDs are not production UUID accounts or runs, and no row here is a production asset or migration source. Each
-- authenticated fixture alias owns one row holding the exact Envelopes.Encode bytes as opaque bytea (never jsonb, which would reorder
-- keys and rewrite numbers and so break exact receipts and state digests). The host only INSERTs (ON CONFLICT DO NOTHING) and UPDATEs
-- rows; it never deletes data.
--
-- The application role needs exactly: USAGE on schema dev_practice, SELECT on dev_practice.schema_marker, and SELECT, INSERT, UPDATE on
-- dev_practice.account_state (no DELETE, no DDL). The host verifies the marker and those privileges before it listens. Grant them
-- separately for the role your harness creates, for example:
--   GRANT USAGE ON SCHEMA dev_practice TO <app_role>;
--   GRANT SELECT ON dev_practice.schema_marker TO <app_role>;
--   GRANT SELECT, INSERT, UPDATE ON dev_practice.account_state TO <app_role>;

BEGIN;

CREATE SCHEMA dev_practice;

CREATE TABLE dev_practice.schema_marker (
  purpose  text PRIMARY KEY CHECK (purpose = 'development-practice'),
  format   text NOT NULL CHECK (format = 'mf-dev-practice-pg-1')
);
INSERT INTO dev_practice.schema_marker (purpose, format) VALUES ('development-practice', 'mf-dev-practice-pg-1');

-- One row per authenticated fixture alias (the same rule as the practice fixture's accountId). The size bound is the host's
-- MaxEnvelopeBytes (512 MiB) dev-store boundary; growing past it needs reviewed database/migration work, never history truncation.
CREATE TABLE dev_practice.account_state (
  account_alias  text PRIMARY KEY CHECK (account_alias ~ '^[a-z][a-z0-9-]{1,31}$'),
  envelope       bytea NOT NULL CHECK (octet_length(envelope) BETWEEN 1 AND 536870912)
);

COMMIT;
