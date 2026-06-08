import { ChangeDetectionStrategy, Component, Signal, computed } from '@angular/core';
import { PanelCard } from '../../_ui/panel-card';
import { FindingList } from '../../_ui/finding-list';
import { SeverityBar } from '../../_ui/severity-bar';
import { FindingsTabState, useFindings } from '../_shared/use-findings';
import { Finding, Severity, SeverityCount } from '../../../../data-access/models/dashboard.models';

@Component({
  selector: 'cr-security-tab',
  standalone: true,
  imports: [PanelCard, FindingList, SeverityBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (state().status === 'loading') {
      <p class="muted">Loading security findings…</p>
    } @else if (state().status === 'error') {
      <p class="muted error">{{ state().error }}</p>
    } @else {
      <div class="grid">
        <div class="top-grid">
          <cr-panel-card class="fill top-risk" title="Top Security Risk" icon="priority_high">
            <p class="summary"><strong>{{ topSecurityRisk().title }}</strong></p>
            <p class="sub">{{ topSecurityRisk().detail }}</p>
            <ul class="impact">
              <li>Data may be transmitted insecurely.</li>
              <li>Traffic is more vulnerable to man-in-the-middle attacks.</li>
              <li>May violate modern security standards and compliance baselines.</li>
            </ul>
            <p class="sub"><strong>{{ topSecurityRisk().action }}</strong></p>
          </cr-panel-card>

          <cr-panel-card class="fill top-score" title="Security Score" icon="verified_user">
            <p class="summary"><strong>{{ securityScore().score }} / 100</strong></p>
            <p class="sub">{{ securityScore().label }}</p>
            <p class="sub">Prod: {{ productionFindings().length }} · Non-prod: {{ nonProductionFindings().length }}</p>
          </cr-panel-card>

          <cr-panel-card class="fill risk-wide" title="Security risk profile" icon="shield">
            <cr-severity-bar [counts]="refinedCounts()" />
            <p class="sub">Production findings remain Medium priority; test/non-production findings are lowered to Low.</p>
          </cr-panel-card>
        </div>

        <details class="finding-sections" open>
          <summary>Detailed Security Findings</summary>
          <div class="findings-grid">
            <cr-panel-card title="HTTP URL detections" icon="link_off">
              <p class="sub"><strong>{{ dedupSummary().total }}</strong> occurrences detected across repository scan.</p>
              <ul class="examples">
                @for (e of dedupSummary().examples; track e) {
                  <li>{{ e }}</li>
                }
              </ul>
            </cr-panel-card>

            <cr-panel-card [title]="'Production Risk (' + productionFindings().length + ')'" icon="warning">
              <cr-finding-list [findings]="productionFindings()" defaultGroupMode="file" [showReasoning]="true" />
            </cr-panel-card>

            <cr-panel-card [title]="'Test/Non-production (' + nonProductionFindings().length + ')'" icon="science">
              @if (nonProductionFindings().length > 0) {
                <p class="sub">Note: Some findings are in test files and may not impact production security.</p>
              }
              <cr-finding-list [findings]="nonProductionFindings()" defaultGroupMode="file" [showReasoning]="true" />
            </cr-panel-card>
          </div>
        </details>
      </div>
    }
  `,
  styles: `
    :host { display: block; }
    .grid { display: grid; gap: 0.5rem; }
    .top-grid {
      display: grid;
      gap: 0.5rem;
      grid-template-columns: 1.45fr 1fr;
      grid-template-areas:
        "risk score"
        "profile profile";
      grid-template-rows: minmax(0, auto) minmax(0, auto);
      align-items: start;
    }
    .top-risk { grid-area: risk; }
    .top-score { grid-area: score; }
    .risk-wide { grid-area: profile; }
    .top-grid > cr-panel-card { min-height: 0; }
    .finding-sections {
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
      border-radius: 12px;
      background: rgba(255,255,255,0.015);
      padding: 0.4rem 0.5rem;
    }
    .finding-sections summary {
      cursor: pointer;
      list-style: none;
      color: var(--text-2, #b8bdd1);
      font-size: 0.8rem;
      font-weight: 600;
    }
    .finding-sections summary::-webkit-details-marker { display: none; }
    .findings-grid {
      margin-top: 0.45rem;
      display: grid;
      gap: 0.6rem;
    }
    .muted { color: var(--text-2, #b8bdd1); }
    .error { color: #ff5470; }
    .summary { margin: 0; color: var(--text-1, #fff); font-size: 0.94rem; line-height: 1.4; }
    .sub { margin: 0.25rem 0 0; color: var(--text-2, #b8bdd1); font-size: 0.82rem; line-height: 1.33; }
    .impact { margin: 0.35rem 0 0; padding-left: 1.05rem; color: var(--text-2, #b8bdd1); font-size: 0.78rem; display: grid; gap: 0.15rem; }
    .examples { list-style: none; margin: 0.35rem 0 0; padding: 0; display: grid; gap: 0.3rem; }
    .examples li {
      background: rgba(255,255,255,0.03);
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
      border-radius: 10px;
      padding: 0.35rem 0.5rem;
      color: var(--text-2, #b8bdd1);
      font-size: 0.74rem;
      font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
    }
    @media (max-width: 980px) {
      .top-grid {
        grid-template-columns: 1fr;
        grid-template-areas:
          "risk"
          "score"
          "profile";
      }
    }
  `,
})
export class SecurityTab {
  private readonly findings = useFindings('security');
  protected readonly state: Signal<FindingsTabState> = this.findings.state;
  protected readonly counts: Signal<SeverityCount[]> = this.findings.counts;

  protected readonly productionFindings = computed(() =>
    this.state().findings
      .filter(f => !this.isNonProductionPath(f.file))
      .map(f => ({ ...f, severity: this.refinedSeverity(f) }))
  );

  protected readonly nonProductionFindings = computed(() =>
    this.state().findings
      .filter(f => this.isNonProductionPath(f.file))
      .map(f => ({ ...f, severity: this.refinedSeverity(f) }))
  );

  protected readonly refinedCounts = computed<SeverityCount[]>(() => {
    const seed = new Map<Severity, number>([
      ['critical', 0],
      ['high', 0],
      ['medium', 0],
      ['low', 0],
      ['info', 0],
    ]);

    for (const f of [...this.productionFindings(), ...this.nonProductionFindings()]) {
      seed.set(f.severity, (seed.get(f.severity) ?? 0) + 1);
    }

    return [...seed.entries()].map(([severity, count]) => ({ severity, count }));
  });

  protected readonly securityScore = computed(() => {
    const prod = this.productionFindings().length;
    const nonProd = this.nonProductionFindings().length;
    const penalty = (prod * 3.2) + (nonProd * 0.8);
    const score = Math.max(0, Math.min(100, Math.round(100 - penalty)));

    const label = prod === 0
      ? 'Strong - no production security findings detected'
      : score >= 80
        ? 'Moderate - insecure HTTP usage detected in production code'
        : 'Elevated risk - prioritize HTTPS remediation in production paths';

    return { score, label };
  });

  protected readonly topSecurityRisk = computed(() => {
    const prod = this.productionFindings();
    if (prod.length === 0) {
      return {
        title: 'No production security hotspot detected',
        detail: 'Current findings are primarily in non-production paths.',
        action: 'Keep enforcing secure defaults for future production routes and configs.',
      };
    }

    return {
      title: `HTTP endpoints used in production code (${prod.length} instances)`,
      detail: 'Insecure transport patterns detected in deployable paths.',
      action: 'Recommended: enforce HTTPS across all configs, adapters, and routes.',
    };
  });

  protected readonly dedupSummary = computed(() => {
    const findings = this.state().findings;
    const examples = findings
      .slice(0, 6)
      .map(f => `${this.shortPath(f.file)}:${f.line}`);

    return { total: findings.length, examples };
  });

  private refinedSeverity(f: Finding): Severity {
    if (this.isNonProductionPath(f.file)) return 'low';
    if (f.severity === 'critical' || f.severity === 'high') return f.severity;
    return 'medium';
  }

  private isNonProductionPath(file: string): boolean {
    const p = file.replace(/\\/g, '/').toLowerCase();
    return /(^|\/)(test|tests|spec|specs|__tests__|fixtures|mock|mocks|samples|example|examples|e2e)(\/|$)/.test(p)
      || /\.spec\.[a-z0-9]+$/.test(p)
      || /\.test\.[a-z0-9]+$/.test(p);
  }

  private shortPath(file: string): string {
    const parts = file.replace(/\\/g, '/').split('/');
    return parts.length > 4 ? '.../' + parts.slice(-4).join('/') : file;
  }
}
