import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, timer } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { of } from 'rxjs';
import { API_BASE_URL, POLL_INTERVAL_MS } from './config';
import { LiquidacaoRecente, MetricsSnapshot, TransacaoRecente } from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  /** Emite o snapshot a cada POLL_INTERVAL_MS; erro de rede vira null (dashboard mostra offline). */
  metricsStream(): Observable<MetricsSnapshot | null> {
    return timer(0, POLL_INTERVAL_MS).pipe(
      switchMap(() => this.http.get<MetricsSnapshot>(`${API_BASE_URL}/metrics`).pipe(
        catchError(() => of(null)),
      )),
    );
  }

  /** Feeds recentes, atualizados num intervalo mais folgado que o das métricas. */
  liquidacoesRecentesStream(limite = 12): Observable<LiquidacaoRecente[]> {
    return timer(0, POLL_INTERVAL_MS * 3).pipe(
      switchMap(() => this.http
        .get<LiquidacaoRecente[]>(`${API_BASE_URL}/liquidacoes/recentes?limite=${limite}`)
        .pipe(catchError(() => of([] as LiquidacaoRecente[])))),
    );
  }

  transacoesRecentesStream(limite = 12): Observable<TransacaoRecente[]> {
    return timer(0, POLL_INTERVAL_MS * 3).pipe(
      switchMap(() => this.http
        .get<TransacaoRecente[]>(`${API_BASE_URL}/transacoes/recentes?limite=${limite}`)
        .pipe(catchError(() => of([] as TransacaoRecente[])))),
    );
  }
}
