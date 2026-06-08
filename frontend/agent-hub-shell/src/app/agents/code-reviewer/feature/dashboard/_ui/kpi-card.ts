import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';

@Component({
  selector: 'cr-kpi-card',
  standalone: true,
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <article class="kpi" [attr.data-tone]="tone()">
      <header>
        <span class="material-symbols">{{ icon() }}</span>
        <span class="label">{{ label() }}</span>
      </header>
      <div class="value">
        @if (value() !== null && value() !== undefined) {
          <span class="num">{{ value() | number }}</span>
        } @else {
          <span class="dash">—</span>
        }
        @if (suffix()) { <span class="suffix">{{ suffix() }}</span> }
      </div>
      @if (hint()) { <p class="hint">{{ hint() }}</p> }
    </article>
  `,
  styles: `
    :host { display: block; }
    .kpi {
      background: var(--surface-glass, rgba(20,22,36,0.6));
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
      border-top: 2px solid color-mix(in srgb, var(--accent) 36%, var(--surface-border));
      border-radius: var(--r-lg, 16px);
      padding: 1rem 1.1rem;
      display: grid;
      gap: 0.4rem;
      min-height: 120px;
      backdrop-filter: blur(18px);
      position: relative;
      overflow: hidden;
      box-shadow: var(--shadow-1);
      transition: transform var(--dur-2, 200ms) var(--ease-out, ease), border-color var(--dur-2, 200ms) var(--ease-out, ease), box-shadow var(--dur-2, 200ms) var(--ease-out, ease);
    }
    .kpi:hover {
      transform: translateY(-2px);
      border-color: var(--surface-border-strong, rgba(255,255,255,0.16));
      box-shadow: var(--shadow-2, 0 14px 40px -20px rgba(0,0,0,0.55));
    }
    .kpi[data-tone='success'] { border-top-color: rgba(46, 230, 166, 0.65); }
    .kpi[data-tone='warning'] { border-top-color: rgba(255, 181, 71, 0.7); }
    .kpi[data-tone='danger']  { border-top-color: rgba(255, 84, 112, 0.72); }
    header {
      display: flex; align-items: center; gap: 0.45rem;
      color: var(--text-2, #b8bdd1);
      font-size: 0.78rem; text-transform: uppercase; letter-spacing: 0.08em;
    }
    .material-symbols {
      font-family: 'Material Symbols Rounded', system-ui;
      font-size: 1.1rem; color: var(--accent, #7c5cff);
    }
    .value { display: flex; align-items: baseline; gap: 0.4rem; }
    .num { font-size: 2rem; font-weight: 700; color: var(--text-1, #fff); line-height: 1; font-feature-settings: 'tnum'; font-variant-numeric: tabular-nums; }
    .dash { font-size: 1.6rem; color: var(--text-2, #b8bdd1); }
    .suffix { font-size: 0.85rem; color: var(--text-2, #b8bdd1); }
    .hint { margin: 0; font-size: 0.78rem; color: var(--text-2, #b8bdd1); }
  `,
})
export class KpiCard {
  readonly label = input.required<string>();
  readonly icon = input<string>('insights');
  readonly value = input<number | null | undefined>(null);
  readonly suffix = input<string | null>(null);
  readonly hint = input<string | null>(null);
  readonly tone = input<'default' | 'success' | 'warning' | 'danger'>('default');

  protected readonly hasValue = computed(() => {
    const v = this.value();
    return v !== null && v !== undefined;
  });
}
