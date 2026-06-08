/** Dashboard / analysis DTOs. Mirror RepoIntel.Contracts (backend). */

import { SessionSummary } from './session.models';

export type Severity = 'info' | 'low' | 'medium' | 'high' | 'critical';

export interface FileTypeBucket {
  extension: string;
  count: number;
  bytes: number;
}

export interface LanguageBucket {
  language: string;
  files: number;
  bytes: number;
}

export interface CategoryBreakdown {
  source: number;
  test: number;
  config: number;
  docs: number;
}

export interface AngularBreakdown {
  components: number;
  services: number;
  modules: number;
  guards: number;
  interceptors: number;
  pipes: number;
}

export interface DotNetBreakdown {
  controllers: number;
  services: number;
  entities: number;
  repositories: number;
}

export interface SeverityCount {
  severity: Severity;
  count: number;
}

export interface LargestFile {
  path: string;
  bytes: number;
  lines: number;
}

export interface DashboardMetrics {
  totalFiles: number;
  totalFolders: number;
  fileTypes: FileTypeBucket[];
  languages: LanguageBucket[];
  categories: CategoryBreakdown;
  angular?: AngularBreakdown | null;
  dotNet?: DotNetBreakdown | null;
  findingsBySeverity: SeverityCount[];
  largestFiles: LargestFile[];
}

export interface DashboardOverview {
  session: SessionSummary;
  metrics: DashboardMetrics;
  findings: Finding[];
}

export interface Finding {
  id: string;
  ruleId: string;
  severity: Severity;
  category: string;
  file: string;
  line: number;
  column: number;
  message: string;
  snippet?: string | null;
  fixHint?: string | null;
}

export interface GraphNode {
  id: string;
  label: string;
  kind: string;
  metrics?: Record<string, number> | null;
}
export interface GraphEdge {
  from: string;
  to: string;
  kind: string;
}
export interface ArchitectureGraph {
  nodes: GraphNode[];
  edges: GraphEdge[];
}
