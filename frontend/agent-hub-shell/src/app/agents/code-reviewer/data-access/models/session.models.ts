/** DTOs mirroring the .NET API contract. Kept thin in P1; expanded in later phases. */

export type SessionKind = 'repo' | 'snippet';

export type PipelineStage =
  | 'validate'
  | 'fetch'
  | 'scan'
  | 'metadata'
  | 'architecture'
  | 'analyze'
  | 'chunk'
  | 'embed'
  | 'index'
  | 'dashboard';

export type StageStatus = 'pending' | 'running' | 'done' | 'error';

export interface PipelineProgressEvent {
  stage: PipelineStage;
  status: StageStatus;
  percent: number;
  message?: string;
}

export interface SnippetFileInput {
  name: string;
  language?: string;
  content: string;
}

export interface RepoIngestRequest {
  url: string;
  branch?: string;
  reviewProfile?: 'strict' | 'balanced' | 'relaxed';
}

export interface SnippetIngestRequest {
  files: SnippetFileInput[];
  reviewProfile?: 'strict' | 'balanced' | 'relaxed';
}

export interface IngestResponse {
  sessionId: string;
}

export interface SessionSummary {
  id: string;
  kind: SessionKind;
  title: string;
  framework?: string;
  primaryLanguage?: string;
  healthScore?: number;
  confidenceScore?: number;
  createdAt: string;
  status: 'pending' | 'running' | 'ready' | 'error';
}
