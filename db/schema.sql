CREATE TABLE IF NOT EXISTS transacoes_pendentes (
    id            UUID PRIMARY KEY,
    valor         NUMERIC(18,2) NOT NULL,
    moeda         CHAR(3)       NOT NULL,
    conta_origem  TEXT          NOT NULL,
    conta_destino TEXT          NOT NULL,
    tipo          TEXT          NOT NULL CHECK (tipo IN ('PIX','TED','BOLETO')),
    status        TEXT          NOT NULL DEFAULT 'PENDENTE' CHECK (status IN ('PENDENTE','ENVIADA')),
    criado_em     TIMESTAMPTZ   NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_transacoes_pendentes_status ON transacoes_pendentes (status);

CREATE TABLE IF NOT EXISTS liquidacoes (
    transacao_id UUID PRIMARY KEY,
    status       TEXT          NOT NULL DEFAULT 'LIQUIDADA',
    valor        NUMERIC(18,2) NOT NULL,
    liquidado_em TIMESTAMPTZ   NOT NULL DEFAULT now()
);
