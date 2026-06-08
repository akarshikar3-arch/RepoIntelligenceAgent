import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { LanguageBucket } from '../../../data-access/models/dashboard.models';

const PALETTE = [
  '#7c5cff', '#2ee6a6', '#ffb547', '#ff5470', '#5cc8ff',
  '#c77dff', '#80ffdb', '#ffd166', '#ef476f', '#06d6a0',
];

@Component({
  selector: 'cr-language-bar',
  standalone: true,
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rows().length === 0) {
      <p class="empty">No language data yet.</p>
    } @else {
      <ul class="rows">
        @for (row of rows(); track row.language) {
          <li>
            <div class="meta">
              <span class="dot" [style.background]="row.color"></span>
              <span class="lang">{{ row.language }}</span>
              <span class="count">{{ row.files | number }} files</span>
            </div>
            <div class="bar">
              <span class="fill" [style.width.%]="row.percent" [style.background]="row.color"></span>
            </div>
            <span class="pct">{{ row.percent | number:'1.0-1' }}%</span>
          </li>
        }
      </ul>

        @if (canExpand()) {
          <button type="button" class="toggle" (click)="toggleExpanded()">{{ toggleLabel() }}</button>
        }
    }
  `,
  styles: `
    :host { display: block; }
    .empty { margin: 0; color: var(--text-2, #b8bdd1); font-size: 0.85rem; }
    .rows { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.6rem; }
    li {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 2fr) auto;
      gap: 0.75rem; align-items: center;
    }
    .meta { display: flex; align-items: center; gap: 0.45rem; min-width: 0; }
    .dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
    .lang { color: var(--text-1, #fff); text-transform: capitalize; font-size: 0.85rem; }
    .count { color: var(--text-2, #b8bdd1); font-size: 0.75rem; margin-left: auto; }
    .bar {
      height: 8px; border-radius: 999px;
      background: rgba(255,255,255,0.06); overflow: hidden;
    }
    .fill { display: block; height: 100%; border-radius: 999px; transition: width 0.4s ease; }
    .pct { color: var(--text-2, #b8bdd1); font-size: 0.78rem; min-width: 3.5ch; text-align: right; }
    .toggle {
      margin-top: 0.75rem;
      border: 1px solid rgba(124, 92, 255, 0.26);
      background: rgba(124, 92, 255, 0.08);
      color: #6b4fd4;
      border-radius: 999px;
      padding: 0.32rem 0.72rem;
      font-size: 0.78rem;
      font-weight: 700;
      cursor: pointer;
    }
  `,
})
export class LanguageBar {
  readonly buckets = input<LanguageBucket[]>([]);
  readonly limit = input<number>(5);

  private readonly expanded = signal(false);

  protected readonly canExpand = computed(() => this.buckets().length > this.limit());
  protected readonly toggleLabel = computed(() => this.expanded() ? 'Show less' : 'See all');

  protected readonly rows = computed(() => {
    const buckets = [...this.buckets()].sort((a, b) => b.files - a.files);
    const top = this.expanded() ? buckets : buckets.slice(0, this.limit());
    const total = top.reduce((acc, b) => acc + b.files, 0) || 1;
    return top.map((b, i) => ({
      ...b,
      color: PALETTE[i % PALETTE.length],
      percent: (b.files / total) * 100,
    }));
  });

  protected toggleExpanded(): void {
    this.expanded.update(v => !v);
  }
}
