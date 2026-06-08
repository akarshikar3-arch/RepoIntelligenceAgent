import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { DecimalPipe } from '@angular/common';
import { catchError, map, of, switchMap, takeWhile, timer, forkJoin } from 'rxjs';
import { DashboardApi } from '../../../../data-access/api/dashboard.api';
import { ArchitectureGraph } from '../../../../data-access/models/dashboard.models';
import { PanelCard } from '../../_ui/panel-card';
import { ArchitectureGraphView } from '../../_ui/architecture-graph';
import { TabPlaceholder } from '../_shared/tab-placeholder';

interface State {
  status: 'loading' | 'ready' | 'error';
  graph: ArchitectureGraph | null;
  error: string | null;
}

const ARCH_INITIAL: State = { status: 'loading', graph: null, error: null };

@Component({
  selector: 'cr-architecture-tab',
  standalone: true,
  imports: [DecimalPipe, PanelCard, ArchitectureGraphView, TabPlaceholder],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (state().status === 'loading') {
      <p class="muted">Loading architecture graph…</p>
    } @else if (state().status === 'error' || !state().graph) {
      <cr-tab-placeholder
        title="Architecture"
        icon="hub"
        message="Dependency graph will appear once analysis completes." />
    } @else if (state().graph!.nodes.length === 0) {
      <cr-tab-placeholder
        title="Architecture"
        icon="hub"
        message="No graph nodes yet — finish analysis to populate dependencies." />
    } @else {
      <div class="layout">
        <div class="kpi-grid">
          <div class="kpi-left">
            <cr-panel-card title="Architecture Health" icon="monitor_heart">
              <p class="summary"><strong>{{ architectureHealth().score }} / 100</strong> — {{ architectureHealth().label }}</p>
            </cr-panel-card>

            <cr-panel-card title="Top Risk Node" icon="priority_high">
              @if (topRiskNode(); as r) {
                <p class="summary"><strong>{{ r.label }}</strong> ({{ r.total }} connections, {{ r.percent }}% of edges)</p>
                <p class="summary"><strong>Recommended Action</strong></p>
                <p class="summary">Break down src into smaller domain modules (e.g. routing, adapters, services) to reduce coupling and improve maintainability.</p>
              } @else {
                <p class="muted">No dominant risk node detected yet.</p>
              }
            </cr-panel-card>
          </div>

          <div class="kpi-right">
            <cr-panel-card class="fill" title="Architecture Stats" icon="hub">
              <ul class="stats">
                <li><span class="lbl">Nodes</span><strong>{{ state().graph!.nodes.length | number }}</strong></li>
                <li><span class="lbl">Edges</span><strong>{{ state().graph!.edges.length | number }}</strong></li>
                <li><span class="lbl">Kinds</span><strong>{{ kinds().length | number }}</strong></li>
              </ul>
              <p class="stat-insight">{{ edgeDensityInsight() }}</p>
              <p class="stat-insight">{{ externalDependencyInsight() }}</p>
            </cr-panel-card>
          </div>
        </div>

        <div class="main-grid">
          <cr-panel-card title="Dependency graph" icon="account_tree" subtitle="click a node to inspect">
            <cr-architecture-graph [graph]="state().graph!" />
          </cr-panel-card>

          <cr-panel-card title="Architecture Summary" icon="insights">
            <p class="summary">{{ executiveSummary() }}</p>
          </cr-panel-card>

          <cr-panel-card title="Top Dependency Hubs" icon="device_hub">
            <ol class="hubs">
              @for (h of topHubs(); track h.id) {
                <li>
                  <span class="node" [title]="h.label">{{ h.label }}</span>
                  <span class="hub-meta">{{ h.total }} connections ({{ h.percent }}% of edges) · {{ h.outgoing }} out · {{ h.incoming }} in</span>
                </li>
              }
              @if (topHubs().length === 0) {
                <li class="muted">No hub candidates resolved yet.</li>
              }
            </ol>
          </cr-panel-card>

          <cr-panel-card title="Architecture Risk Signals" icon="warning">
            <ul class="signals compact-signals">
              @for (s of riskSignals(); track s) {
                <li>{{ s }}</li>
              }
            </ul>
          </cr-panel-card>
        </div>

        <details class="more-details">
          <summary>More Architecture Details</summary>
          <div class="extra-grid">
            <cr-panel-card title="Node kinds" icon="category">
              <ul class="kinds">
                @for (k of kinds(); track k.kind) {
                  <li><span class="key">{{ k.kind }}</span><strong>{{ k.count | number }}</strong></li>
                }
              </ul>
            </cr-panel-card>

            <cr-panel-card title="Top Connections" icon="link">
              <ol class="edges">
                @for (e of topEdges(); track e.id) {
                  <li>
                    <span class="from">{{ e.from }}</span>
                    <span class="arrow">→</span>
                    <span class="to">{{ e.to }}</span>
                    <span class="kind">{{ e.kind }}</span>
                  </li>
                }
                @if (topEdges().length === 0) {
                  <li class="muted">No edges resolved yet.</li>
                }
              </ol>
            </cr-panel-card>

            <cr-panel-card class="span-2" title="Why This Matters" icon="help">
              <p class="summary">{{ whyMatters() }}</p>
            </cr-panel-card>
          </div>
        </details>
      </div>
    }
  `,
  styles: `
    :host { display: block; }
    .layout { display: grid; gap: 0.65rem; }
    .kpi-grid {
      display: grid;
      gap: 0.65rem;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      align-items: stretch;
    }
    .kpi-left {
      display: grid;
      gap: 0.65rem;
      grid-template-columns: minmax(0, 1fr);
      align-content: start;
    }
    .kpi-right {
      min-height: 0;
    }
    .main-grid {
      display: grid;
      gap: 0.65rem;
      grid-template-columns: minmax(0, 1fr);
      align-items: start;
    }
    .extra-grid {
      margin-top: 0.45rem;
      display: grid;
      gap: 0.65rem;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      align-items: start;
    }
    .span-2 { grid-column: span 2; }
    .more-details {
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
      border-radius: 14px;
      padding: 0.5rem 0.65rem;
      background: rgba(255,255,255,0.015);
    }
    .more-details summary {
      cursor: pointer;
      list-style: none;
      color: var(--text-2, #b8bdd1);
      font-size: 0.84rem;
      font-weight: 600;
      letter-spacing: 0.02em;
    }
    .more-details summary::-webkit-details-marker { display: none; }
    .muted { color: var(--text-2, #b8bdd1); }
    .summary { margin: 0; color: var(--text-1, #fff); font-size: 0.9rem; line-height: 1.45; }
    .stat-insight {
      margin: 0.45rem 0 0;
      color: var(--text-2, #b8bdd1);
      font-size: 0.82rem;
      line-height: 1.4;
    }
    .stats, .kinds, .edges, .signals, .hubs { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.45rem; }
    .stats { grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); }
    .stats li, .kinds li {
      background: rgba(255,255,255,0.03);
      border: 1px solid var(--surface-border, rgba(255,255,255,0.06));
      padding: 0.55rem 0.7rem; border-radius: 12px;
      display: flex; gap: 0.5rem; align-items: baseline;
    }
    .signals li, .hubs li {
      background: rgba(255,255,255,0.03);
      border: 1px solid var(--surface-border, rgba(255,255,255,0.06));
      padding: 0.55rem 0.7rem;
      border-radius: 12px;
      color: var(--text-1, #fff);
      font-size: 0.84rem;
    }
    .compact-signals { grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.35rem; }
    .hubs li {
      display: flex;
      gap: 0.6rem;
      align-items: baseline;
    }
    .node { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .hub-meta { margin-left: auto; color: var(--text-2, #b8bdd1); font-size: 0.75rem; }
    .stats .lbl, .kinds .key { color: var(--text-2, #b8bdd1); font-size: 0.78rem; text-transform: capitalize; }
    .stats strong, .kinds strong { margin-left: auto; color: var(--text-1, #fff); }
    .edges li {
      display: flex; gap: 0.5rem; align-items: center; font-size: 0.85rem;
      padding: 0.4rem 0.6rem; border-radius: 10px; background: rgba(255,255,255,0.03);
    }
    .from, .to { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; color: var(--text-1, #fff); }
    .arrow { color: var(--accent, #7c5cff); }
    .kind { margin-left: auto; color: var(--text-2, #b8bdd1); font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.08em; }
    @media (max-width: 1100px) {
      .kpi-grid { grid-template-columns: 1fr; }
    }
    @media (max-width: 860px) {
      .kpi-grid,
      .kpi-left,
      .extra-grid,
      .compact-signals {
        grid-template-columns: 1fr;
      }
      .span-2,
      .kpi-grid cr-panel-card:last-child {
        grid-column: auto;
      }
    }
  `,
})
export class ArchitectureTab {
  private readonly api = inject(DashboardApi);
  private readonly route = inject(ActivatedRoute);

  protected readonly state = toSignal(
    this.route.parent!.paramMap.pipe(
      switchMap(p => {
        const id = p.get('sessionId');
        if (!id) return of<State>({ status: 'ready', graph: null, error: null });
        return timer(0, 2000).pipe(
          switchMap(() =>
            forkJoin({
              session: this.api.getSession(id),
              graph: this.api.getArchitecture(id),
            })
          ),
          map(({ session, graph }) => ({
            status: 'ready' as const,
            graph,
            error: null,
            sessionStatus: session.status,
          })),
          takeWhile(
            v => v.sessionStatus !== 'ready' && v.sessionStatus !== 'error',
            true
          ),
          map(v => ({ status: v.status, graph: v.graph, error: v.error }) as State),
          catchError(err =>
            of<State>({
              status: 'error',
              graph: null,
              error: err?.message ?? 'Failed to load architecture',
            })
          )
        );
      })
    ),
    { initialValue: ARCH_INITIAL }
  );

  protected readonly kinds = computed(() => {
    const nodes = this.state().graph?.nodes ?? [];
    const map = new Map<string, number>();
    for (const n of nodes) map.set(n.kind, (map.get(n.kind) ?? 0) + 1);
    return [...map.entries()]
      .map(([kind, count]) => ({ kind, count }))
      .sort((a, b) => b.count - a.count);
  });

  protected readonly topEdges = computed(() => {
    const edges = this.state().graph?.edges ?? [];
    return edges.slice(0, 8).map((e, i) => ({ ...e, id: `${e.from}->${e.to}#${i}` }));
  });

  protected readonly topHubs = computed(() => {
    const graph = this.state().graph;
    if (!graph) return [] as Array<{ id: string; label: string; incoming: number; outgoing: number; total: number; percent: number }>;

    const labels = new Map(graph.nodes.map(n => [n.id, n.label]));
    const incoming = new Map<string, number>();
    const outgoing = new Map<string, number>();

    for (const e of graph.edges) {
      outgoing.set(e.from, (outgoing.get(e.from) ?? 0) + 1);
      incoming.set(e.to, (incoming.get(e.to) ?? 0) + 1);
    }

    const edgeTotal = graph.edges.length || 1;

    return graph.nodes
      .map(n => {
        const inCount = incoming.get(n.id) ?? 0;
        const outCount = outgoing.get(n.id) ?? 0;
        const total = inCount + outCount;
        return {
          id: n.id,
          label: labels.get(n.id) ?? n.id,
          incoming: inCount,
          outgoing: outCount,
          total,
          percent: Math.round((total / edgeTotal) * 100),
        };
      })
      .filter(n => n.total > 0)
      .sort((a, b) => b.total - a.total)
      .slice(0, 8);
  });

  protected readonly topRiskNode = computed(() => this.topHubs()[0] ?? null);

  protected readonly architectureHealth = computed(() => {
    const graph = this.state().graph;
    if (!graph) return { score: 0, label: 'Awaiting analysis' };

    const nodeCount = graph.nodes.length;
    const edgeCount = graph.edges.length;
    const density = nodeCount > 0 ? edgeCount / nodeCount : 0;
    const externalRatio = nodeCount > 0
      ? graph.nodes.filter(n => n.kind === 'External').length / nodeCount
      : 0;
    const hubShare = (this.topRiskNode()?.percent ?? 0) / 100;

    const penalty = (density * 18) + (externalRatio * 25) + (hubShare * 30);
    const score = Math.max(0, Math.min(100, Math.round(100 - penalty)));

    const label = score >= 80
      ? 'Low coupling risk'
      : score >= 60
        ? 'Moderate coupling detected'
        : 'High coupling detected - module boundaries may be weak';

    return { score, label };
  });

  protected readonly edgeDensityInsight = computed(() => {
    const graph = this.state().graph;
    if (!graph || graph.nodes.length === 0) return 'Graph density insight will appear after analysis completes.';

    const density = graph.edges.length / graph.nodes.length;
    if (density >= 1.2) return 'Graph density suggests moderate-to-high inter-module dependency.';
    if (density >= 0.8) return 'Graph density suggests moderate inter-module dependency.';
    return 'Graph density suggests relatively low inter-module dependency.';
  });

  protected readonly externalDependencyInsight = computed(() => {
    const graph = this.state().graph;
    if (!graph || graph.nodes.length === 0) return 'External dependency insight will appear after analysis completes.';

    const external = graph.nodes.filter(n => n.kind === 'External').length;
    const ratio = external / graph.nodes.length;
    const percent = Math.round(ratio * 100);

    if (ratio >= 0.45) {
      return `External dependencies form ~${percent}% of graph -> moderate reliance on third-party modules.`;
    }

    return `External dependencies form ~${percent}% of graph -> contained reliance on third-party modules.`;
  });

  protected readonly executiveSummary = computed(() => {
    const graph = this.state().graph;
    if (!graph) return 'Architecture summary will appear after analysis completes.';

    const nodeCount = graph.nodes.length;
    const edgeCount = graph.edges.length;
    const kindMap = new Map<string, number>();
    for (const n of graph.nodes) {
      kindMap.set(n.kind, (kindMap.get(n.kind) ?? 0) + 1);
    }
    const external = kindMap.get('External') ?? 0;
    const module = kindMap.get('Module') ?? 0;
    const hubs = this.topHubs().slice(0, 4).map(h => h.label).join(', ');

    return `The repository has a ${edgeCount > nodeCount * 1.5 ? 'dense' : 'moderately connected'} module graph with ${nodeCount} nodes and ${edgeCount} edges. Internal and external dependencies are ${Math.abs(module - external) <= 3 ? 'nearly balanced' : module > external ? 'internally weighted' : 'externally weighted'} (${module} modules vs ${external} external nodes). Key dependency concentration appears around ${hubs || 'core nodes'}.`;
  });

  protected readonly riskSignals = computed(() => {
    const graph = this.state().graph;
    if (!graph) return ['Architecture risk signals will appear after analysis completes.'];

    const signals: string[] = [];
    const nodeCount = graph.nodes.length;
    const edgeCount = graph.edges.length;
    const density = nodeCount > 1 ? edgeCount / nodeCount : 0;

    if (density >= 1.3) {
      signals.push('Core dependency concentration is high; boundary clarity may be reduced.');
    }

    const externalCount = graph.nodes.filter(n => n.kind === 'External').length;
    const externalRatio = nodeCount > 0 ? externalCount / nodeCount : 0;
    if (externalRatio >= 0.35) {
      signals.push('External module usage is widespread across top dependency flows.');
    }

    const topHub = this.topHubs()[0];
    if (topHub && topHub.total >= 10) {
      signals.push(`High-influence node detected (${topHub.label}) with ${topHub.total} direct connections.`);
    }

    if (signals.length === 0) {
      signals.push('No strong structural risk signals detected in current dependency topology.');
    }

    return signals;
  });

  protected readonly whyMatters = computed(() => {
    return 'Highly connected modules are harder to change safely and more likely to spread architectural impact across the repository.';
  });
}
