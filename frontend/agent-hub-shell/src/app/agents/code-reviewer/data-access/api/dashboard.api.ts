import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, forkJoin, map, throwError } from 'rxjs';
import { REPO_INTEL_CONFIG, RepoIntelAgentConfig } from '../../code-reviewer.providers';
import { SessionSummary } from '../models/session.models';
import {
  ArchitectureGraph,
  DashboardMetrics,
  DashboardOverview,
  Finding,
  Severity,
} from '../models/dashboard.models';
import { ChatRequest, ChatResponse, ChatTurn } from '../models/chat.models';

@Injectable({ providedIn: 'root' })
export class DashboardApi {
  private readonly http = inject(HttpClient);
  private readonly config = inject<RepoIntelAgentConfig>(REPO_INTEL_CONFIG);

  private url(path: string): string {
    return `${this.config.apiBaseUrl.replace(/\/$/, '')}${path}`;
  }

  getSession(sessionId: string): Observable<SessionSummary> {
    return this.http.get<SessionSummary>(this.url(`/api/sessions/${sessionId}`));
  }

  getMetrics(sessionId: string): Observable<DashboardMetrics> {
    return this.http.get<DashboardMetrics>(this.url(`/api/sessions/${sessionId}/metrics`));
  }

  getOverview(sessionId: string): Observable<DashboardOverview> {
    return this.http
      .get<DashboardOverview>(this.url(`/api/sessions/${sessionId}/overview`))
      .pipe(
        catchError(err => {
          if (err?.status !== 404) {
            return throwError(() => err);
          }

          return forkJoin({
            session: this.getSession(sessionId),
            metrics: this.getMetrics(sessionId),
            findings: this.getFindings(sessionId),
          }).pipe(map(v => ({ session: v.session, metrics: v.metrics, findings: v.findings })));
        })
      );
  }

  getFindings(
    sessionId: string,
    filter: { severity?: Severity; category?: string } = {}
  ): Observable<Finding[]> {
    const params: Record<string, string> = {};
    if (filter.severity) params['severity'] = filter.severity;
    if (filter.category) params['category'] = filter.category;
    return this.http.get<Finding[]>(this.url(`/api/sessions/${sessionId}/findings`), { params });
  }

  getArchitecture(sessionId: string): Observable<ArchitectureGraph> {
    return this.http.get<ArchitectureGraph>(this.url(`/api/sessions/${sessionId}/architecture`));
  }

  ingestRepo(
    url: string,
    branch?: string,
    reviewProfile: 'strict' | 'balanced' | 'relaxed' = 'balanced'
  ): Observable<{ sessionId: string }> {
    return this.http.post<{ sessionId: string }>(
      this.url('/api/ingest/repo'),
      { url, branch: branch || null, reviewProfile }
    );
  }

  ingestSnippet(
    files: { name: string; language: string | null; content: string }[],
    reviewProfile: 'strict' | 'balanced' | 'relaxed' = 'balanced'
  ): Observable<{ sessionId: string }> {
    return this.http.post<{ sessionId: string }>(
      this.url('/api/ingest/snippet'),
      { files, reviewProfile }
    );
  }

  ask(sessionId: string, request: ChatRequest): Observable<ChatResponse> {
    return this.http.post<ChatResponse>(
      this.url(`/api/sessions/${sessionId}/chat`),
      request
    );
  }

  getChatHistory(sessionId: string): Observable<ChatTurn[]> {
    return this.http.get<ChatTurn[]>(
      this.url(`/api/sessions/${sessionId}/chat/history`)
    );
  }

  clearChatHistory(sessionId: string): Observable<void> {
    return this.http.delete<void>(
      this.url(`/api/sessions/${sessionId}/chat/history`)
    );
  }
}
