import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { Severity, SeverityCount } from '../../../data-access/models/dashboard.models';

const SEVERITY_ORDER: Severity[] = ['critical', 'high', 'medium', 'low', 'info'];
const SEVERITY_COLOR: Record<Severity, string> = {
  critical: '#ff3860',
  high: '#ff5470',
  medium: '#ffb547',
  low: '#5cc8ff',
  info: '#7c5cff',
};

@Component({
  selector: 'cr-severity-bar',
  standalone: true,
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="totals">
      <span class="total">{{ total() | number }}</span>
      <span class="label">finding{{ total() === 1 ? '' : 's' }}</span>
    </div>

    @if (total() === 0) {
      <p class="empty">No findings reported.</p>
    } @else {
      <div class="stack" role="img" aria-label="Findings by severity">
        @for (seg of segments(); track seg.severity) {
          @if (seg.percent > 0) {
            <span
              class="seg"
              [style.width.%]="seg.percent"
              [style.background]="seg.color"
              [title]="seg.severity + ': ' + seg.count">
            </span>
          }
        }
      </div>

      <ul class="legend">
        @for (seg of segments(); track seg.severity) {
          <li>
            <span class="dot" [style.background]="seg.color"></span>
            <span class="sev">{{ seg.severity }}</span>
            <span class="num">{{ seg.count | number }}</span>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host { display: grid; gap: 0.7rem; }
    .totals { display: flex; align-items: baseline; gap: 0.4rem; }
    .total { font-size: 1.6rem; font-weight: 700; color: var(--text-1, #fff); }
    .label { color: var(--text-2, #b8bdd1); font-size: 0.85rem; }
    .empty { margin: 0; color: var(--text-2, #b8bdd1); font-size: 0.85rem; }
    .stack {
      display: flex; height: 10px; border-radius: 999px; overflow: hidden;
      background: rgba(255,255,255,0.06);
    }
    .seg { display: block; height: 100%; }
    .legend {
      list-style: none; margin: 0; padding: 0;
      display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
      gap: 0.4rem 0.8rem;
    }
    .legend li { display: flex; align-items: center; gap: 0.45rem; font-size: 0.8rem; }
    .dot { width: 10px; height: 10px; border-radius: 50%; }
    .sev { text-transform: capitalize; color: var(--text-1, #fff); }
    .num { margin-left: auto; color: var(--text-2, #b8bdd1); }
  `,
})
export class SeverityBar {
  readonly counts = input<SeverityCount[]>([]);

  protected readonly total = computed(() =>
    this.counts().reduce((acc, c) => acc + c.count, 0)
  );

  protected readonly segments = computed(() => {
    const map = new Map(this.counts().map(c => [c.severity, c.count]));
    const total = this.total() || 1;
    return SEVERITY_ORDER.map(severity => {
      const count = map.get(severity) ?? 0;
      return {
        severity,
        count,
        color: SEVERITY_COLOR[severity],
        percent: (count / total) * 100,
      };
    });
  });
}
