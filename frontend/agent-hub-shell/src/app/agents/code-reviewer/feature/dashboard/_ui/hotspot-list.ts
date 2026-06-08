import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { LargestFile } from '../../../data-access/models/dashboard.models';

@Component({
  selector: 'cr-hotspot-list',
  standalone: true,
  imports: [DecimalPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rows().length === 0) {
      <p class="empty">No hotspots detected yet.</p>
    } @else {
      <ol class="hot">
        @for (row of rows(); track row.path) {
          <li>
            <div class="head">
              <span class="path" [title]="row.path">{{ row.path }}</span>
              <span class="size">{{ row.kb | number:'1.0-1' }} KB · {{ row.lines | number }} lines</span>
            </div>
            <div class="bar">
              <span class="fill" [style.width.%]="row.percent"></span>
            </div>
          </li>
        }
      </ol>

      @if (canExpand()) {
        <button type="button" class="toggle" (click)="toggleExpanded()">{{ toggleLabel() }}</button>
      }
    }
  `,
  styles: `
    :host { display: block; }
    .empty { margin: 0; color: var(--text-2, #b8bdd1); font-size: 0.85rem; }
    .hot { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.6rem; counter-reset: hot; }
    li {
      display: grid; gap: 0.3rem;
      padding: 0.55rem 0.7rem;
      border-radius: 12px;
      background: rgba(255,255,255,0.03);
      border: 1px solid var(--surface-border, rgba(255,255,255,0.06));
      counter-increment: hot;
    }
    li::before { content: counter(hot, decimal-leading-zero); color: var(--text-2, #b8bdd1); font-size: 0.7rem; letter-spacing: 0.1em; }
    .head { display: flex; gap: 0.6rem; align-items: baseline; min-width: 0; }
    .path {
      color: var(--text-1, #fff); font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
      font-size: 0.82rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; flex: 1;
    }
    .size { color: var(--text-2, #b8bdd1); font-size: 0.75rem; flex-shrink: 0; }
    .bar { height: 5px; border-radius: 999px; background: rgba(255,255,255,0.05); overflow: hidden; }
    .fill {
      display: block; height: 100%;
      background: linear-gradient(90deg, #7c5cff, #ff5470);
      border-radius: 999px;
    }
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
export class HotspotList {
  readonly files = input<LargestFile[]>([]);
  readonly limit = input<number>(3);

  private readonly expanded = signal(false);

  protected readonly canExpand = computed(() => this.files().length > this.limit());
  protected readonly toggleLabel = computed(() => this.expanded() ? 'Show less' : 'See all');

  protected readonly rows = computed(() => {
    const sorted = [...this.files()].sort((a, b) => b.bytes - a.bytes);
    const top = this.expanded() ? sorted : sorted.slice(0, this.limit());
    const max = top[0]?.bytes ?? 1;
    return top.map(f => ({
      ...f,
      kb: f.bytes / 1024,
      percent: (f.bytes / max) * 100,
    }));
  });

  protected toggleExpanded(): void {
    this.expanded.update(v => !v);
  }
}
