import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { catchError, map, of, switchMap, takeWhile, tap, timer } from 'rxjs';
import { DecimalPipe, NgIf } from '@angular/common';
import { DashboardApi } from '../../../../data-access/api/dashboard.api';
import { SessionStore } from '../../../../state/session.store';
import { PanelCard } from '../../_ui/panel-card';
import { KpiCard } from '../../_ui/kpi-card';
import { LanguageBar } from '../../_ui/language-bar';
import { SeverityBar } from '../../_ui/severity-bar';
import { HotspotList } from '../../_ui/hotspot-list';
import { CategoryBreakdownChart } from '../../_ui/category-breakdown';
import {
  AngularBreakdown,
  DashboardMetrics,
  DotNetBreakdown,
  Finding,
  Severity,
} from '../../../../data-access/models/dashboard.models';

interface MetricsState {
  status: 'idle' | 'loading' | 'ready' | 'error';
  data: DashboardMetrics | null;
  findings: Finding[];
  error: string | null;
}

const METRICS_INITIAL: MetricsState = {
  status: 'loading',
  data: null,
  findings: [],
  error: null,
};

const WARNING_SEVERITIES: Severity[] = ['high', 'medium', 'low'];

@Component({
  selector: 'cr-overview-tab',
  standalone: true,
  imports: [
    NgIf,
    DecimalPipe,
    PanelCard,
    KpiCard,
    LanguageBar,
    SeverityBar,
    HotspotList,
    CategoryBreakdownChart,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './overview-tab.html',
  styleUrl: './overview-tab.scss',
})
export class OverviewTab {
  private readonly api = inject(DashboardApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  protected readonly store = inject(SessionStore);

  protected readonly summary = this.store.summary;
  private readonly loadingTick = toSignal(timer(0, 1400), { initialValue: 0 });

  private readonly state = toSignal(
    this.route.parent!.paramMap.pipe(
      switchMap(p => {
        const id = p.get('sessionId');
        if (!id) {
          return of<MetricsState>({ status: 'idle', data: null, findings: [], error: null });
        }
        return timer(0, 2000).pipe(
          switchMap(() => this.api.getOverview(id)),
          tap(overview => this.store.setSummary(overview.session)),
          map(overview => {
            const sessionStatus = overview.session.status;
            const isReady = sessionStatus === 'ready';
            const isError = sessionStatus === 'error';

            return {
              status: (isReady ? 'ready' : isError ? 'error' : 'loading') as MetricsState['status'],
              data: isReady ? overview.metrics : null,
              findings: isReady ? overview.findings : [],
              error: isError
                ? (overview.session.error?.trim() || 'Analysis failed. Please retry ingest.')
                : null,
              sessionStatus,
            };
          }),
          takeWhile(
            v => v.sessionStatus !== 'ready' && v.sessionStatus !== 'error',
            true
          ),
          map(v => ({
            status: v.status,
            data: v.data,
            findings: v.findings,
            error: v.error,
          }) as MetricsState),
          catchError(err =>
            of<MetricsState>({
              status: 'error',
              data: null,
              findings: [],
              error: err?.message ?? 'Failed to load metrics',
            })
          )
        );
      })
    ),
    { initialValue: METRICS_INITIAL }
  );

  protected readonly metrics = computed(() => this.state().data);
  protected readonly findings = computed(() => this.state().findings);
  protected readonly status = computed(() => this.state().status);
  protected readonly error = computed(() => this.state().error);

  protected readonly sessionPending = computed(() => {
    const s = this.summary()?.status;
    return s === 'pending' || s === 'running';
  });
  protected readonly sessionStatusLabel = computed(() => {
    switch (this.summary()?.status) {
      case 'pending':
        return 'Queued';
      case 'running':
        return 'Analyzing…';
      case 'ready':
        return 'Ready';
      case 'error':
        return 'Error';
      default:
        return '…';
    }
  });

  protected readonly languageCount = computed(
    () => this.metrics()?.languages.length ?? null
  );
  protected readonly frameworkDisplay = computed(() => {
    const framework = (this.summary()?.framework ?? '').trim();
    const lang = (this.summary()?.primaryLanguage ?? '').toLowerCase();
    if (framework && framework.toLowerCase() !== 'unknown') return framework;
    if (lang === 'javascript' || lang === 'typescript') return 'Node.js (inferred)';
    if (lang === 'c#' || lang === 'f#') return '.NET (inferred)';
    if (lang === 'go') return 'Go services (inferred)';
    if (lang === 'python') return 'Python app (inferred)';
    return 'Unknown';
  });
  protected readonly findingsTotal = computed(
    () => this.metrics()?.findingsBySeverity.reduce((a, c) => a + c.count, 0) ?? null
  );

  protected readonly criticalFindings = computed(() =>
    this.findings().filter(f => f.severity === 'critical')
  );

  protected readonly warningFindings = computed(() =>
    this.findings().filter(f => WARNING_SEVERITIES.includes(f.severity))
  );

  protected readonly reviewScore = computed(() => {
    const counts = this.metrics()?.findingsBySeverity ?? [];
    const bySeverity = new Map(counts.map(c => [c.severity, c.count]));
    const penalty =
      (bySeverity.get('critical') ?? 0) * 4 +
      (bySeverity.get('high') ?? 0) * 2 +
      (bySeverity.get('medium') ?? 0) * 1 +
      (bySeverity.get('low') ?? 0) * 0.5;
    return Math.max(0, Math.round((10 - penalty) * 10) / 10);
  });

  protected readonly mergeVerdict = computed(() => {
    const critical = this.criticalFindings().length;
    const high = this.findings().filter(f => f.severity === 'high').length;
    if (critical > 0) return 'Do not merge - blocking issues found';
    if (high > 0) return 'Safe to merge (no blocking issues), but high-priority items need attention';
    return 'Safe to merge (no blocking issues)';
  });

  protected readonly mergeSubtext = computed(() => {
    const warnings = this.warningFindings().length;
    if (warnings === 0) return 'No non-blocking issues currently on watchlist.';
    return `${warnings} non-blocking issues should be addressed over time.`;
  });

  protected readonly verdictRoute = computed(() =>
    this.criticalFindings().length > 0 ? 'security' : 'quality'
  );

  protected readonly verdictRouteLabel = computed(() =>
    this.verdictRoute() === 'security' ? 'Open Security tab' : 'Open Code Quality tab'
  );

  protected readonly healthNarrative = computed(() => {
    const health = this.summary()?.healthScore;
    if (health == null) return 'Health score pending analysis context.';
    if (health >= 80) return 'Strong health with manageable risk profile.';
    if (health >= 60) return 'Moderate - affected by quality warnings and large files.';
    return 'Elevated risk - prioritize quality and structural remediation.';
  });

  protected readonly overallInsight = computed(() => {
    const critical = this.criticalFindings().length;
    const warnings = this.warningFindings().length;
    if (critical > 0) {
      return 'Overall Insight: This repository has blocking risk due to critical issues and needs remediation before merge.';
    }
    if (warnings > 0) {
      return 'Overall Insight: This repository is moderately healthy and safe to merge, but non-blocking issues and large files indicate maintainability risk.';
    }
    return 'Overall Insight: This repository is healthy with no blocking findings and low immediate risk.';
  });

  protected readonly impactSummary = computed(() => {
    const largest = this.metrics()?.largestFiles[0];
    if (largest && largest.lines >= 1000) {
      return 'Impact: Maintainability risk from large files may slow development and code reviews.';
    }
    return 'Impact: Current structure supports stable delivery velocity with manageable maintenance overhead.';
  });

  protected readonly watchlistHighlights = computed(() => {
    const warnings = this.warningFindings();
    if (warnings.length === 0) return [] as string[];

    const hasHttp = warnings.some(f => /http:\/\//i.test(`${f.message} ${f.snippet ?? ''} ${f.file}`));
    const qualityCount = warnings.filter(f => f.category === 'quality').length;
    const securityCount = warnings.filter(f => f.category === 'security').length;

    const rows: string[] = [];
    if (hasHttp) rows.push('Insecure HTTP usage signals detected');
    if (qualityCount > 0) rows.push(`Code quality smells (${qualityCount})`);
    if (securityCount > 0) rows.push(`Security watch items (${securityCount})`);
    if (rows.length === 0) rows.push('Minor structural and maintainability issues detected');
    return rows;
  });

  protected readonly topIssue = computed(() => {
    const largest = this.metrics()?.largestFiles[0];
    if (largest && largest.lines >= 1200) {
      return {
        title: 'Large files impacting maintainability',
        detail: `${largest.path} (${largest.lines.toLocaleString()} lines) is likely increasing review and runtime complexity.`,
        priority: 'High (recommended early fix)',
      };
    }

    const topWarning = this.warningFindings()[0];
    if (topWarning) {
      return {
        title: topWarning.message,
        detail: `${topWarning.category} impact at ${topWarning.file}:${topWarning.line}.`,
        priority: 'Medium',
      };
    }

    return {
      title: 'No dominant issue detected',
      detail: 'Current findings indicate low immediate risk.',
      priority: 'Low',
    };
  });

  protected readonly angular = computed<AngularBreakdown | null>(
    () => this.metrics()?.angular ?? null
  );
  protected readonly dotnet = computed<DotNetBreakdown | null>(
    () => this.metrics()?.dotNet ?? null
  );

  protected readonly analyzingSteps = [
    'Scanning repository files and folders',
    'Extracting metadata and language profile',
    'Running rule checks and finding triage',
    'Building dashboard insights',
  ];

  protected readonly activeLoadingStep = computed(() => {
    const status = this.summary()?.status;
    if (status === 'pending') return 0;
    if (status === 'running') return 1 + (this.loadingTick() % 3);
    return this.analyzingSteps.length - 1;
  });

  protected readonly loadingProgressPercent = computed(() =>
    Math.min(100, Math.round(((this.activeLoadingStep() + 1) / this.analyzingSteps.length) * 100))
  );

  protected loadingStepState(index: number): 'queued' | 'active' | 'done' {
    const active = this.activeLoadingStep();
    if (index < active) return 'done';
    if (index === active) return 'active';
    return 'queued';
  }

  protected navigateToTab(path: string): void {
    void this.router.navigate(['../', path], { relativeTo: this.route });
  }

  protected openVerdictTab(): void {
    this.navigateToTab(this.verdictRoute());
  }
}
