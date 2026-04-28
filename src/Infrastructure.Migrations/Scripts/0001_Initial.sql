-- 0001_Initial.sql
-- Ledger schema bootstrap (extracted from the EF Core 10.x migration that
-- preceded the DbUp adoption in Constitution v2.11.0). DbUp tracks applied
-- scripts via the SchemaVersions table; do not embed transaction control
-- here — DbUp wraps each script in a transaction unless told otherwise.

CREATE TABLE ledger (
    id                 uuid          NOT NULL,
    owner_id           uuid          NOT NULL,
    name               varchar(100)  NOT NULL,
    description        varchar(500),
    currency_code      char(3)       NOT NULL,
    status             smallint      NOT NULL DEFAULT 1,
    version            bigint        NOT NULL DEFAULT 1,
    created_at         timestamptz   NOT NULL,
    last_modified_at   timestamptz   NOT NULL,
    CONSTRAINT pk_ledger                   PRIMARY KEY (id),
    CONSTRAINT ck_ledger_currency_alpha3   CHECK (currency_code ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_ledger_status            CHECK (status IN (1, 2)),
    CONSTRAINT ck_ledger_version_positive  CHECK (version >= 1)
);

CREATE TABLE ledger_audit (
    id          bigserial    NOT NULL,
    actor_id    uuid         NOT NULL,
    ledger_id   uuid         NOT NULL,
    event_type  smallint     NOT NULL,
    event_at    timestamptz  NOT NULL DEFAULT now(),
    payload     jsonb        NOT NULL,
    CONSTRAINT pk_ledger_audit       PRIMARY KEY (id),
    CONSTRAINT ck_audit_event_type   CHECK (event_type IN (1, 2, 3))
);

CREATE INDEX ix_ledger_owner_status_lastmodified
    ON ledger (owner_id, status, last_modified_at DESC, id DESC);

CREATE INDEX ix_ledger_audit_event_at
    ON ledger_audit (event_at);

CREATE INDEX ix_ledger_audit_ledger_event_at
    ON ledger_audit (ledger_id, event_at DESC);

CREATE UNIQUE INDEX ux_ledger_owner_name_lower
    ON ledger (owner_id, lower(name));
