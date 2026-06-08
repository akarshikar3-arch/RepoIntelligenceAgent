import { Injectable, computed, signal } from '@angular/core';
import {
  PipelineProgressEvent,
  PipelineStage,
  SessionSummary,
  StageStatus,
} from '../data-access/models/session.models';

const ALL_STAGES: PipelineStage[] = [
  'validate',
  'fetch',
  'scan',
  'metadata',
  'architecture',
  'analyze',
  'chunk',
  'embed',
  'index',
  'dashboard',
];

@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly _summary = signal<SessionSummary | null>(null);
  private readonly _stages = signal<Record<PipelineStage, StageStatus>>(
    Object.fromEntries(ALL_STAGES.map(s => [s, 'pending'])) as Record<
      PipelineStage,
      StageStatus
    >
  );
  private readonly _progressPercent = signal(0);
  private readonly _activeStage = signal<PipelineStage | null>(null);
  private readonly _error = signal<string | null>(null);

  readonly summary = this._summary.asReadonly();
  readonly stages = this._stages.asReadonly();
  readonly progressPercent = this._progressPercent.asReadonly();
  readonly activeStage = this._activeStage.asReadonly();
  readonly error = this._error.asReadonly();

  readonly isReady = computed(() => this._summary()?.status === 'ready');
  readonly stageList = computed(() =>
    ALL_STAGES.map(stage => ({ stage, status: this._stages()[stage] }))
  );

  setSummary(summary: SessionSummary): void {
    this._summary.set(summary);
  }

  applyProgress(evt: PipelineProgressEvent): void {
    this._stages.update(map => ({ ...map, [evt.stage]: evt.status }));
    this._progressPercent.set(evt.percent);
    this._activeStage.set(evt.status === 'running' ? evt.stage : null);
    if (evt.status === 'error') this._error.set(evt.message ?? 'Pipeline error');
  }

  reset(): void {
    this._summary.set(null);
    this._stages.set(
      Object.fromEntries(ALL_STAGES.map(s => [s, 'pending'])) as Record<
        PipelineStage,
        StageStatus
      >
    );
    this._progressPercent.set(0);
    this._activeStage.set(null);
    this._error.set(null);
  }
}
