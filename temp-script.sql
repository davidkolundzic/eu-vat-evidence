CREATE TABLE users (
    id uuid NOT NULL,
    email character varying(320) NOT NULL,
    name text NOT NULL,
    password_hash text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_users PRIMARY KEY (id)
);


CREATE TABLE workspaces (
    id uuid NOT NULL,
    name character varying(200) NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_workspaces PRIMARY KEY (id)
);


CREATE TABLE exports (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    type integer NOT NULL,
    range_from timestamp with time zone NOT NULL,
    range_to timestamp with time zone NOT NULL,
    created_utc timestamp with time zone NOT NULL,
    file_path character varying(600) NOT NULL,
    file_hash character varying(64) NOT NULL,
    CONSTRAINT pk_exports PRIMARY KEY (id),
    CONSTRAINT fk_exports_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES workspaces (id) ON DELETE CASCADE
);


CREATE TABLE provider_connections (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    provider integer NOT NULL,
    mode integer NOT NULL,
    webhook_secret text NOT NULL,
    created_at timestamp with time zone NOT NULL,
    CONSTRAINT pk_provider_connections PRIMARY KEY (id),
    CONSTRAINT fk_provider_connections_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES workspaces (id) ON DELETE CASCADE
);


CREATE TABLE provider_events (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    provider integer NOT NULL,
    mode integer NOT NULL,
    provider_event_id character varying(200) NOT NULL,
    type character varying(200) NOT NULL,
    created_utc timestamp with time zone NOT NULL,
    received_utc timestamp with time zone NOT NULL,
    payload_json jsonb NOT NULL,
    payload_hash character varying(64) NOT NULL,
    processing_status integer NOT NULL,
    error text,
    CONSTRAINT pk_provider_events PRIMARY KEY (id),
    CONSTRAINT fk_provider_events_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES workspaces (id) ON DELETE CASCADE
);


CREATE TABLE transactions (
    id uuid NOT NULL,
    workspace_id uuid NOT NULL,
    provider integer NOT NULL,
    mode integer NOT NULL,
    provider_transaction_id character varying(200) NOT NULL,
    provider_charge_id character varying(200),
    amount_minor bigint NOT NULL,
    currency character varying(3) NOT NULL,
    customer_email character varying(320),
    created_utc timestamp with time zone NOT NULL,
    status integer NOT NULL,
    status_reason character varying(500),
    CONSTRAINT pk_transactions PRIMARY KEY (id),
    CONSTRAINT fk_transactions_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES workspaces (id) ON DELETE CASCADE
);


CREATE TABLE workspace_users (
    workspace_id uuid NOT NULL,
    user_id uuid NOT NULL,
    role integer NOT NULL,
    CONSTRAINT pk_workspace_users PRIMARY KEY (workspace_id, user_id),
    CONSTRAINT fk_workspace_users_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT fk_workspace_users_workspaces_workspace_id FOREIGN KEY (workspace_id) REFERENCES workspaces (id) ON DELETE CASCADE
);


CREATE TABLE evidence_records (
    id uuid NOT NULL,
    transaction_id uuid NOT NULL,
    captured_utc timestamp with time zone NOT NULL,
    evidence_type integer NOT NULL,
    country_code character varying(2) NOT NULL,
    value_raw jsonb,
    source_ref character varying(300) NOT NULL,
    record_hash character varying(64) NOT NULL,
    prev_record_hash character varying(64),
    CONSTRAINT pk_evidence_records PRIMARY KEY (id),
    CONSTRAINT fk_evidence_records_transactions_transaction_id FOREIGN KEY (transaction_id) REFERENCES transactions (id) ON DELETE CASCADE
);


CREATE INDEX ix_evidence_records_transaction_id_captured_utc ON evidence_records (transaction_id, captured_utc);


CREATE INDEX ix_exports_workspace_id ON exports (workspace_id);


CREATE UNIQUE INDEX ix_provider_connections_workspace_id_provider_mode ON provider_connections (workspace_id, provider, mode);


CREATE UNIQUE INDEX ix_provider_events_workspace_id_provider_mode_provider_event_id ON provider_events (workspace_id, provider, mode, provider_event_id);


CREATE UNIQUE INDEX ix_transactions_workspace_id_provider_mode_provider_transactio ON transactions (workspace_id, provider, mode, provider_transaction_id);


CREATE UNIQUE INDEX ix_users_email ON users (email);


CREATE INDEX ix_workspace_users_user_id ON workspace_users (user_id);


