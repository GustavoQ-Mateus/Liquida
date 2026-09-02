// Espelham os DTOs da Liquida.Api (spec-v1.1.0 §4).

export interface MetricsSnapshot {
  capturadoEm: string;
  pendentes: number;
  enviadas: number;
  liquidadas: number;
  rpsLiquidacao: number;
  fila: number | null;
  dlq: number | null;
  rateLimited: number;
}

export interface LiquidacaoRecente {
  transacaoId: string;
  valor: number;
  liquidadoEm: string;
}

export interface TransacaoRecente {
  id: string;
  valor: number;
  moeda: string;
  tipo: string;
  status: string;
  criadoEm: string;
}
