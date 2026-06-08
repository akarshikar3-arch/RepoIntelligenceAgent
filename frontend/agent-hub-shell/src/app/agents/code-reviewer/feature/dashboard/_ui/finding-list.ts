import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { NgClass } from '@angular/common';
import { Finding } from '../../../data-access/models/dashboard.models';

type GroupMode = 'topic' | 'file';
type Tier = 'must' | 'should' | 'style';

interface GroupedFindings {
  key: string;
  label: string;
  items: Finding[];
  critical: number;
  high: number;
  medium: number;
  low: number;
  tier: Tier;
}

@Component({
  selector: 'cr-finding-list',
  standalone: true,
  imports: [NgClass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (findings().length === 0) {
      <p class="empty">No findings for this view.</p>
    } @else {
      <!-- Summary bar -->
      <div class="summary-bar">
        @if (mustFixCount() > 0) {
          <span class="sum-chip must">{{ mustFixCount() }} must fix</span>
        }
        @if (shouldFixCount() > 0) {
          <span class="sum-chip should">{{ shouldFixCount() }} should fix</span>
        }
        @if (styleCount() > 0) {
          <span class="sum-chip style">{{ styleCount() }} style suggestions</span>
        }
        <span class="sum-divider"></span>
        <span class="toolbar-label">Group by</span>
        <button type="button" [class.active]="groupMode() === 'topic'" (click)="setMode('topic')">Topic</button>
        <button type="button" [class.active]="groupMode() === 'file'" (click)="setMode('file')">File</button>
      </div>

      <!-- Must fix section (always shown) -->
      @if (mustGroups().length > 0) {
        <div class="tier-section">
          <p class="tier-header must-header">High priority improvement — not blocking, but important for long-term health</p>
          @for (g of mustGroups(); track g.key) {
            <details class="group must-group" open>
              <summary>
                <span class="chevron">▸</span>
                <span class="group-title">{{ g.label }}</span>
                @if (g.critical > 0) { <span class="pill critical">{{ g.critical }} critical</span> }
                @if (g.high > 0) { <span class="pill high">{{ g.high }} high</span> }
                <span class="group-meta">{{ g.items.length }} issue{{ g.items.length !== 1 ? 's' : '' }}</span>
              </summary>
              <ul class="list">
                @for (f of g.items; track f.id) {
                  <li [ngClass]="'row-' + f.severity">
                    <span class="sev" [ngClass]="'sev-' + f.severity">{{ sevLabel(f.severity) }}</span>
                    <div class="body">
                      <p class="msg">{{ f.message }}</p>
                      <p class="meta">
                        <span class="file-path">{{ shortPath(f.file) }}:{{ f.line }}</span>
                        <span class="sep">·</span>
                        <span class="rule">{{ f.ruleId }}</span>
                      </p>
                      @if (f.fixHint) { <p class="hint">{{ f.fixHint }}</p> }
                      @if (showReasoning()) {
                        <p class="detail-title">Impact</p>
                        <ul class="impact-list">
                          @for (impact of impactBullets(f); track impact) {
                            <li>{{ impact }}</li>
                          }
                        </ul>
                        <p class="detail-line"><strong>Severity:</strong> {{ sevLabel(f.severity) }}</p>
                        <p class="detail-line"><strong>Reason:</strong> {{ severityReason(f) }}</p>
                        <p class="detail-line"><strong>Effort:</strong> {{ fixEffort(f) }}</p>
                        <p class="detail-line"><strong>Example fix:</strong> {{ quickFixSuggestion(f) }}</p>
                      }
                    </div>
                  </li>
                }
              </ul>
            </details>
          }
        </div>
      }

      <!-- Should fix section -->
      @if (shouldGroups().length > 0) {
        <div class="tier-section">
          <p class="tier-header should-header">Should fix — code quality debt</p>
          @for (g of shouldGroups(); track g.key) {
            <details class="group should-group" open>
              <summary>
                <span class="chevron">▸</span>
                <span class="group-title">{{ g.label }}</span>
                <span class="pill medium">{{ g.medium }} medium</span>
                <span class="group-meta">{{ g.items.length }} issue{{ g.items.length !== 1 ? 's' : '' }}</span>
              </summary>
              <ul class="list">
                @for (f of g.items; track f.id) {
                  <li [ngClass]="'row-' + f.severity">
                    <span class="sev" [ngClass]="'sev-' + f.severity">{{ sevLabel(f.severity) }}</span>
                    <div class="body">
                      <p class="msg">{{ f.message }}</p>
                      <p class="meta">
                        <span class="file-path">{{ shortPath(f.file) }}:{{ f.line }}</span>
                        <span class="sep">·</span>
                        <span class="rule">{{ f.ruleId }}</span>
                      </p>
                      @if (f.fixHint) { <p class="hint">{{ f.fixHint }}</p> }
                      @if (showReasoning()) {
                        <p class="detail-title">Impact</p>
                        <ul class="impact-list">
                          @for (impact of impactBullets(f); track impact) {
                            <li>{{ impact }}</li>
                          }
                        </ul>
                        <p class="detail-line"><strong>Severity:</strong> {{ sevLabel(f.severity) }}</p>
                        <p class="detail-line"><strong>Reason:</strong> {{ severityReason(f) }}</p>
                        <p class="detail-line"><strong>Effort:</strong> {{ fixEffort(f) }}</p>
                        <p class="detail-line"><strong>Example fix:</strong> {{ quickFixSuggestion(f) }}</p>
                      }
                    </div>
                  </li>
                }
              </ul>
            </details>
          }
        </div>
      }

      <!-- Style/low section — collapsed by default -->
      @if (styleGroups().length > 0) {
        <div class="tier-section">
          <details class="style-section-toggle">
            <summary class="tier-header style-header">
              <span class="chevron">▸</span>
              Style suggestions &amp; minor hints
              <span class="sum-chip style">{{ styleCount() }} hidden by default</span>
            </summary>
            @for (g of styleGroups(); track g.key) {
              <details class="group style-group">
                <summary>
                  <span class="chevron">▸</span>
                  <span class="group-title">{{ g.label }}</span>
                  <span class="group-meta">{{ g.items.length }} suggestion{{ g.items.length !== 1 ? 's' : '' }}</span>
                </summary>
                <ul class="list">
                  @for (f of g.items; track f.id) {
                    <li [ngClass]="'row-' + f.severity">
                      <span class="sev" [ngClass]="'sev-' + f.severity">{{ sevLabel(f.severity) }}</span>
                      <div class="body">
                        <p class="msg">{{ f.message }}</p>
                        <p class="meta">
                          <span class="file-path">{{ shortPath(f.file) }}:{{ f.line }}</span>
                          <span class="sep">·</span>
                          <span class="rule">{{ f.ruleId }}</span>
                        </p>
                        @if (f.fixHint) { <p class="hint">{{ f.fixHint }}</p> }
                        @if (showReasoning()) {
                          <p class="detail-title">Impact</p>
                          <ul class="impact-list">
                            @for (impact of impactBullets(f); track impact) {
                              <li>{{ impact }}</li>
                            }
                          </ul>
                          <p class="detail-line"><strong>Severity:</strong> {{ sevLabel(f.severity) }}</p>
                          <p class="detail-line"><strong>Reason:</strong> {{ severityReason(f) }}</p>
                          <p class="detail-line"><strong>Effort:</strong> {{ fixEffort(f) }}</p>
                          <p class="detail-line"><strong>Example fix:</strong> {{ quickFixSuggestion(f) }}</p>
                        }
                      </div>
                    </li>
                  }
                </ul>
              </details>
            }
          </details>
        </div>
      }

      @if (mustFixCount() === 0 && shouldFixCount() === 0) {
        <p class="all-good">No blocking issues — only style suggestions remain.</p>
      }
    }
  `,
  styles: `
    :host { display: block; }
    .empty, .all-good { margin: 0; color: var(--text-2, #b8bdd1); font-size: 0.85rem; }
    .all-good { color: #2ee6a6; }

    /* Summary bar */
    .summary-bar {
      display: flex; align-items: center; gap: 0.45rem;
      margin-bottom: 1rem; flex-wrap: wrap;
    }
    .sum-divider { flex: 1; }
    .toolbar-label { color: var(--text-2, #b8bdd1); font-size: 0.78rem; }
    .summary-bar button {
      border: 1px solid var(--surface-border, rgba(255,255,255,0.1));
      background: rgba(255,255,255,0.03); color: var(--text-2, #b8bdd1);
      border-radius: 999px; font-size: 0.78rem; padding: 0.28rem 0.7rem; cursor: pointer;
    }
    .summary-bar button.active {
      color: #fff; border-color: rgba(124,92,255,0.55); background: rgba(124,92,255,0.22);
    }
    .sum-chip {
      font-size: 0.72rem; font-weight: 700; padding: 0.18rem 0.6rem;
      border-radius: 999px; letter-spacing: 0.04em;
    }
    .sum-chip.must   { background: rgba(255,56,96,0.15); color: #ff3860; border: 1px solid rgba(255,56,96,0.35); }
    .sum-chip.should { background: rgba(255,181,71,0.12); color: #ffb547; border: 1px solid rgba(255,181,71,0.3); }
    .sum-chip.style  { background: rgba(92,200,255,0.08); color: #5cc8ff; border: 1px solid rgba(92,200,255,0.22); }

    /* Tier sections */
    .tier-section { margin-bottom: 0.85rem; }
    .tier-header {
      margin: 0 0 0.4rem 0; font-size: 0.72rem; font-weight: 700;
      text-transform: uppercase; letter-spacing: 0.1em;
    }
    .must-header   { color: #ff5470; }
    .should-header { color: #ffb547; }
    .style-header  {
      display: flex; align-items: center; gap: 0.5rem;
      color: var(--text-2, #b8bdd1); cursor: pointer; list-style: none;
    }
    .style-header::-webkit-details-marker { display: none; }
    .style-section-toggle { }

    /* Groups */
    .group {
      border-radius: 12px; overflow: hidden; margin-bottom: 0.5rem;
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
    }
    .must-group  { border-color: rgba(255,56,96,0.25); background: rgba(255,56,96,0.04); }
    .should-group { border-color: rgba(255,181,71,0.18); background: rgba(255,181,71,0.03); }
    .style-group { background: rgba(255,255,255,0.01); }

    summary {
      list-style: none; cursor: pointer;
      display: flex; align-items: center; gap: 0.5rem;
      padding: 0.6rem 0.75rem; user-select: none;
    }
    summary::-webkit-details-marker { display: none; }
    details[open] > summary .chevron { transform: rotate(90deg); }
    .chevron {
      color: var(--text-2, #b8bdd1); font-size: 0.65rem;
      transition: transform 0.15s ease; display: inline-block;
    }
    .group-title { color: var(--text-1, #fff); font-size: 0.85rem; font-weight: 600; }
    .group-meta  { color: var(--text-2, #b8bdd1); font-size: 0.76rem; margin-left: auto; }

    /* Pills */
    .pill {
      font-size: 0.65rem; padding: 0.1rem 0.45rem; border-radius: 999px;
      border: 1px solid transparent; text-transform: uppercase;
      letter-spacing: 0.06em; font-weight: 700;
    }
    .pill.critical { color: #ff5470; background: rgba(255,84,112,0.12); border-color: rgba(255,84,112,0.3); }
    .pill.high     { color: #ffb547; background: rgba(255,181,71,0.1);  border-color: rgba(255,181,71,0.28); }
    .pill.medium   { color: #ffb547; background: rgba(255,181,71,0.08); border-color: rgba(255,181,71,0.2); }

    /* Finding rows */
    .list { list-style: none; margin: 0; padding: 0.35rem 0.6rem 0.6rem; display: grid; gap: 0.45rem; }
    li {
      display: grid; grid-template-columns: auto 1fr; gap: 0.65rem;
      padding: 0.55rem 0.7rem; border-radius: 10px;
      background: rgba(255,255,255,0.025);
      border: 1px solid var(--surface-border, rgba(255,255,255,0.05));
    }
    .row-critical { border-color: rgba(255,56,96,0.2); }
    .row-high     { border-color: rgba(255,181,71,0.15); }
    .sev {
      align-self: start; padding: 0.15rem 0.45rem; border-radius: 999px;
      font-size: 0.65rem; text-transform: uppercase; letter-spacing: 0.07em; font-weight: 700;
    }
    .sev-critical { background: rgba(255,56,96,0.18); color: #ff3860; }
    .sev-high     { background: rgba(255,84,112,0.18); color: #ff5470; }
    .sev-medium   { background: rgba(255,181,71,0.15); color: #ffb547; }
    .sev-low      { background: rgba(92,200,255,0.1);  color: #5cc8ff; }
    .sev-info     { background: rgba(124,92,255,0.12); color: #c0b3ff; }
    .body { display: grid; gap: 0.18rem; min-width: 0; }
    .msg  { margin: 0; color: var(--text-1, #fff); font-size: 0.875rem; line-height: 1.35; }
    .meta {
      margin: 0; color: var(--text-2, #b8bdd1); font-size: 0.75rem;
      display: flex; gap: 0.3rem; flex-wrap: wrap; align-items: center;
    }
    .file-path { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .rule { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; opacity: 0.7; }
    .sep { opacity: 0.4; }
    .hint {
      margin: 0; padding: 0.35rem 0.5rem; border-radius: 7px;
      background: rgba(46,230,166,0.07); border-left: 2px solid #2ee6a6;
      color: var(--text-2, #b8bdd1); font-size: 0.78rem;
    }
    .detail-title {
      margin: 0.25rem 0 0;
      color: var(--text-3, #9ca3af);
      font-size: 0.7rem;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      font-weight: 700;
    }
    .impact-list {
      margin: 0;
      padding-left: 1rem;
      color: var(--text-2, #b8bdd1);
      font-size: 0.78rem;
      display: grid;
      gap: 0.15rem;
    }
    .detail-line {
      margin: 0;
      color: var(--text-2, #b8bdd1);
      font-size: 0.78rem;
      line-height: 1.35;
    }
  `,
})
export class FindingList {
  readonly findings = input<Finding[]>([]);
  readonly defaultGroupMode = input<GroupMode>('topic');
  readonly showReasoning = input(false);

  protected readonly groupMode = signal<GroupMode>(this.defaultGroupMode());

  protected readonly sorted = computed(() =>
    [...this.findings()].sort((a, b) => {
      const s = this.sevRank(a.severity) - this.sevRank(b.severity);
      if (s !== 0) return s;
      return a.file.localeCompare(b.file) || a.line - b.line;
    })
  );

  protected readonly mustFixCount  = computed(() => this.sorted().filter(f => f.severity === 'critical' || f.severity === 'high').length);
  protected readonly shouldFixCount = computed(() => this.sorted().filter(f => f.severity === 'medium').length);
  protected readonly styleCount    = computed(() => this.sorted().filter(f => f.severity === 'low' || f.severity === 'info').length);

  protected readonly mustGroups   = computed(() => this.buildGroups(this.sorted().filter(f => f.severity === 'critical' || f.severity === 'high')));
  protected readonly shouldGroups = computed(() => this.buildGroups(this.sorted().filter(f => f.severity === 'medium')));
  protected readonly styleGroups  = computed(() => this.buildGroups(this.sorted().filter(f => f.severity === 'low' || f.severity === 'info')));

  protected setMode(m: GroupMode) { this.groupMode.set(m); }

  protected sevLabel(s: string): string {
    return ({ critical: 'Critical', high: 'High', medium: 'Medium', low: 'Low', info: 'Info' } as Record<string, string>)[s] ?? s;
  }

  protected shortPath(file: string): string {
    const parts = file.replace(/\\/g, '/').split('/');
    // Show last 3 segments so same-named files in different folders are distinguishable
    return parts.length > 3 ? '…/' + parts.slice(-3).join('/') : file;
  }

  protected impactBullets(f: Finding): string[] {
    const text = `${f.message} ${f.ruleId} ${f.fixHint ?? ''}`.toLowerCase();
    if (/http:\/\//i.test(`${f.message} ${f.snippet ?? ''} ${f.file}`) || /https?|tls|insecure/i.test(text)) {
      return [
        'Data may be transmitted insecurely',
        'Increases man-in-the-middle exposure risk',
        'Can violate modern security standards',
      ];
    }
    if (/\bany\b|type/i.test(text)) {
      return [
        'Reduced type safety',
        'Higher runtime bug risk',
        'Lower refactor confidence',
      ];
    }
    if (/unused|dead code|unreachable/i.test(text)) {
      return [
        'Adds maintenance overhead',
        'Can hide active logic issues',
      ];
    }
    if (/complex|cyclomatic|nesting/i.test(text)) {
      return [
        'Increases cognitive load for reviewers',
        'Raises regression risk during changes',
      ];
    }
    return [
      'Impacts long-term maintainability',
      'May slow safe iteration speed',
    ];
  }

  protected severityReason(f: Finding): string {
    const similarCount = this.findings().filter(x => x.ruleId === f.ruleId).length;
    if (f.severity === 'critical') return 'Exploitable risk or severe correctness concern.';
    if (f.severity === 'high') return similarCount > 1
      ? 'High impact pattern repeated across multiple files.'
      : 'High impact issue with broad code health effect.';
    if (f.severity === 'medium') return similarCount > 1
      ? 'Frequent usage across multiple files increases risk.'
      : 'Moderate impact on maintainability and reliability.';
    if (f.severity === 'low') return 'Localized quality issue with limited immediate impact.';
    return 'Informational guidance to improve consistency.';
  }

  protected fixEffort(f: Finding): string {
    const similarCount = this.findings().filter(x => x.ruleId === f.ruleId).length;
    const text = `${f.message} ${f.ruleId} ${f.fixHint ?? ''}`.toLowerCase();
    if (f.severity === 'critical' || f.severity === 'high') return `High (touches ${similarCount} related finding${similarCount !== 1 ? 's' : ''})`;
    if (f.severity === 'medium' && /\bany\b|type/i.test(text)) return `Medium (requires updating types across ${Math.max(1, Math.min(similarCount, 5))} file${similarCount === 1 ? '' : 's'})`;
    if (f.severity === 'medium') return `Medium (requires updates in ${Math.min(similarCount, 5)} focused area${similarCount === 1 ? '' : 's'})`;
    return 'Low (localized style or naming cleanup)';
  }

  protected quickFixSuggestion(f: Finding): string {
    const text = `${f.message} ${f.ruleId} ${f.fixHint ?? ''}`.toLowerCase();
    if (/http:\/\//i.test(`${f.message} ${f.snippet ?? ''} ${f.file}`) || /https?|tls|insecure/i.test(text)) {
      return 'http://api.example.com -> https://api.example.com';
    }
    if (/\bany\b|type/i.test(text)) {
      return 'function foo(x: any) -> function foo(x: unknown)';
    }
    if (/unused|dead code/i.test(text)) {
      return 'Remove unused declarations or gate with feature flags.';
    }
    if (/naming|identifier/i.test(text)) {
      return 'Rename symbols to follow project naming conventions.';
    }
    return f.fixHint || 'Apply a focused refactor in this file and re-run analysis.';
  }

  private buildGroups(items: Finding[]): GroupedFindings[] {
    const mode = this.groupMode();
    const map = new Map<string, GroupedFindings>();
    for (const f of items) {
      const key   = mode === 'file' ? f.file : this.topicKey(f);
      const label = mode === 'file' ? this.shortPath(f.file) : this.topicLabel(f);
      const g = map.get(key);
      if (g) {
        g.items.push(f);
        if (f.severity === 'critical') g.critical++;
        if (f.severity === 'high')     g.high++;
        if (f.severity === 'medium')   g.medium++;
        if (f.severity === 'low' || f.severity === 'info') g.low++;
      } else {
        map.set(key, {
          key, label, items: [f],
          critical: f.severity === 'critical' ? 1 : 0,
          high:     f.severity === 'high' ? 1 : 0,
          medium:   f.severity === 'medium' ? 1 : 0,
          low:      (f.severity === 'low' || f.severity === 'info') ? 1 : 0,
          tier:     (f.severity === 'critical' || f.severity === 'high') ? 'must'
                    : f.severity === 'medium' ? 'should' : 'style',
        });
      }
    }
    return [...map.values()].sort((a, b) => (b.critical - a.critical) || (b.high - a.high) || (b.medium - a.medium) || a.label.localeCompare(b.label));
  }

  private topicKey(f: Finding): string {
    return (f.ruleId.split('-')[0] || 'GEN').toUpperCase();
  }

  private topicLabel(f: Finding): string {
    return ({ SEC: 'Security', TS: 'Type Safety', NG: 'Angular Architecture', TEST: 'Testing', BP: 'Best Practices', STRUCT: 'Code Structure' } as Record<string, string>)[this.topicKey(f)] ?? 'General';
  }

  private sevRank(s: string): number {
    return ({ critical: 0, high: 1, medium: 2, low: 3, info: 4 } as Record<string, number>)[s] ?? 9;
  }
}
