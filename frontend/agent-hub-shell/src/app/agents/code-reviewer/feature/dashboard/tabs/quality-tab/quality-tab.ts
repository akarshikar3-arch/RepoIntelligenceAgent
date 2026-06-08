import { ChangeDetectionStrategy, Component, Signal, computed } from '@angular/core';
import { PanelCard } from '../../_ui/panel-card';
import { FindingList } from '../../_ui/finding-list';
import { SeverityBar } from '../../_ui/severity-bar';
import { FindingsTabState, useFindings } from '../_shared/use-findings';
import { SeverityCount } from '../../../../data-access/models/dashboard.models';

@Component({
  selector: 'cr-quality-tab',
  standalone: true,
  imports: [PanelCard, FindingList, SeverityBar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (state().status === 'loading') {
      <p class="muted">Loading code quality findings…</p>
    } @else if (state().status === 'error') {
      <p class="muted error">{{ state().error }}</p>
    } @else {
      <div class="grid">
        <div class="top-grid">
          <cr-panel-card class="fill top-issue" title="Top Code Quality Issue" icon="priority_high">
            <p class="summary"><strong>{{ topQualityIssue().title }}</strong></p>
            <p class="sub">{{ topQualityIssue().detail }}</p>
            <p class="sub"><strong>{{ topQualityIssue().action }}</strong></p>
          </cr-panel-card>

          <cr-panel-card class="fill top-score" title="Code Quality Score" icon="fact_check">
            <p class="summary"><strong>{{ qualityScore().score }} / 100</strong></p>
            <p class="sub">{{ qualityScore().label }}</p>
          </cr-panel-card>

          <cr-panel-card class="fill severity-wide" title="Quality severity breakdown" icon="rule">
            <cr-severity-bar [counts]="counts()" />
          </cr-panel-card>
        </div>

        <details class="more-details">
          <summary>More Quality Details</summary>
          <cr-panel-card title="Category Coverage" icon="category">
            <ul class="categories">
              @for (c of categoryCoverage(); track c.label) {
                <li>
                  <span class="cat">{{ c.label }}</span>
                  <span class="cat-status">{{ c.status }}</span>
                </li>
              }
            </ul>
          </cr-panel-card>
        </details>

        <cr-panel-card title="Code quality findings" icon="list_alt">
          <cr-finding-list
            [findings]="state().findings"
            defaultGroupMode="topic"
            [showReasoning]="true" />
        </cr-panel-card>
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
        "issue score"
        "severity severity";
      grid-template-rows: minmax(0, auto) minmax(0, auto);
      align-items: start;
    }
    .top-issue { grid-area: issue; }
    .top-score { grid-area: score; }
    .severity-wide { grid-area: severity; }
    .top-grid > cr-panel-card { min-height: 0; }
    .more-details {
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
      border-radius: 12px;
      background: rgba(255,255,255,0.015);
      padding: 0.4rem 0.5rem;
    }
    .more-details summary {
      cursor: pointer;
      list-style: none;
      color: var(--text-2, #b8bdd1);
      font-size: 0.8rem;
      font-weight: 600;
    }
    .more-details summary::-webkit-details-marker { display: none; }
    .more-details cr-panel-card { margin-top: 0.4rem; display: block; }
    .muted { color: var(--text-2, #b8bdd1); }
    .error { color: #ff5470; }
    .summary { margin: 0; color: var(--text-1, #fff); font-size: 0.94rem; line-height: 1.4; }
    .sub { margin: 0.25rem 0 0; color: var(--text-2, #b8bdd1); font-size: 0.82rem; line-height: 1.33; }
    .categories { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.4rem; }
    .categories li {
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
      background: rgba(255,255,255,0.02);
      border-radius: 10px;
      padding: 0.5rem 0.65rem;
      display: flex;
      align-items: baseline;
      gap: 0.5rem;
    }
    .cat { color: var(--text-1, #fff); font-weight: 600; }
    .cat-status { margin-left: auto; color: var(--text-2, #b8bdd1); font-size: 0.78rem; }
    @media (max-width: 980px) {
      .top-grid {
        grid-template-columns: 1fr;
        grid-template-areas:
          "issue"
          "score"
          "severity";
      }
    }
  `,
})
export class QualityTab {
  private readonly findings = useFindings('quality');
  protected readonly state: Signal<FindingsTabState> = this.findings.state;
  protected readonly counts: Signal<SeverityCount[]> = this.findings.counts;

  protected readonly qualityScore = computed(() => {
    const bySeverity = new Map(this.counts().map(c => [c.severity, c.count]));
    const penalty =
      (bySeverity.get('critical') ?? 0) * 15 +
      (bySeverity.get('high') ?? 0) * 8 +
      (bySeverity.get('medium') ?? 0) * 3 +
      (bySeverity.get('low') ?? 0) * 0.4 +
      (bySeverity.get('info') ?? 0) * 0.2;
    const score = Math.max(0, Math.min(100, Math.round(100 - penalty)));

    const label = score >= 85
      ? 'Strong - minor maintainability concerns'
      : score >= 70
        ? 'Moderate - affected by type safety issues'
        : 'At risk - prioritize code quality remediation';

    return { score, label };
  });

  protected readonly topQualityIssue = computed(() => {
    const findings = this.state().findings;
    if (findings.length === 0) {
      return {
        title: 'No dominant issue detected',
        detail: 'Current quality findings indicate low immediate risk.',
        action: 'Continue monitoring for regressions.',
      };
    }

    const anyFindings = findings.filter(f => /\bany\b/i.test(`${f.message} ${f.snippet ?? ''} ${f.ruleId}`));
    const anyOccurrences = anyFindings.reduce((sum, f) => {
      const m = f.message.match(/(\d+)/);
      return sum + (m ? Number(m[1]) : 1);
    }, 0);

    if (anyFindings.length > 0) {
      return {
        title: `Excessive use of TypeScript 'any' (${anyOccurrences} occurrences)`,
        detail: 'This reduces type safety and makes refactoring riskier.',
        action: 'Recommended: introduce strict types (interfaces, concrete types, or generics).',
      };
    }

    const first = findings[0];
    return {
      title: first.message,
      detail: `${first.category} concern in ${first.file}:${first.line}.`,
      action: first.fixHint || 'Address this issue to improve maintainability confidence.',
    };
  });

  protected readonly categoryCoverage = computed(() => {
    const findings = this.state().findings;
    const hasByPrefix = (prefix: string) => findings.some(f => f.ruleId.toUpperCase().startsWith(prefix));
    const hasByWord = (word: RegExp) => findings.some(f => word.test(`${f.message} ${f.ruleId}`));

    return [
      {
        label: 'Type Safety',
        status: hasByPrefix('TS') || hasByWord(/\bany\b|type/i)
          ? 'Issues detected'
          : 'No major issues found in this category',
      },
      {
        label: 'Naming Conventions',
        status: hasByWord(/name|naming|identifier/i)
          ? 'Issues detected'
          : 'No major issues found in this category',
      },
      {
        label: 'Dead Code',
        status: hasByWord(/unused|dead code|unreachable/i)
          ? 'Issues detected'
          : 'No major issues found in this category',
      },
      {
        label: 'Complexity',
        status: hasByWord(/complexity|cyclomatic|nesting/i)
          ? 'Issues detected'
          : 'No major issues found in this category',
      },
      {
        label: 'Duplicate Logic',
        status: hasByWord(/duplicate|duplication|copy/i)
          ? 'Issues detected'
          : 'No major issues found in this category',
      },
    ];
  });
}
