import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { CategoryBreakdown } from '../../../data-access/models/dashboard.models';

const COLORS: Record<keyof CategoryBreakdown, string> = {
  source: '#7c5cff',
  test: '#2ee6a6',
  config: '#ffb547',
  docs: '#5cc8ff',
};

@Component({
  selector: 'cr-category-breakdown',
  standalone: true,
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="grid">
      @for (row of rows(); track row.key) {
        <div class="cat">
          <div class="head">
            <span class="dot" [style.background]="row.color"></span>
            <span class="key">{{ row.label }}</span>
            <span class="num">{{ row.value | number }}</span>
          </div>
          <div class="bar">
            <span class="fill" [style.width.%]="row.percent" [style.background]="row.color"></span>
          </div>
        </div>
      }
    </div>
  `,
  styles: `
    :host { display: block; }
    .grid { display: grid; gap: 0.6rem; }
    .head { display: flex; align-items: center; gap: 0.5rem; font-size: 0.82rem; }
    .dot { width: 10px; height: 10px; border-radius: 50%; }
    .key { color: var(--text-1, #fff); text-transform: capitalize; }
    .num { margin-left: auto; color: var(--text-2, #b8bdd1); }
    .bar { height: 6px; border-radius: 999px; background: rgba(255,255,255,0.06); overflow: hidden; margin-top: 0.25rem; }
    .fill { display: block; height: 100%; border-radius: 999px; }
  `,
})
export class CategoryBreakdownChart {
  readonly breakdown = input<CategoryBreakdown | null>(null);

  protected readonly rows = computed(() => {
    const b = this.breakdown();
    if (!b) return [];
    const total = (b.source + b.test + b.config + b.docs) || 1;
    return (Object.keys(COLORS) as Array<keyof CategoryBreakdown>).map(key => ({
      key,
      label: key,
      value: b[key],
      color: COLORS[key],
      percent: (b[key] / total) * 100,
    }));
  });
}
