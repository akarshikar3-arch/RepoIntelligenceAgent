import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import type { Core, ElementDefinition, EventObject } from 'cytoscape';
import { ArchitectureGraph } from '../../../data-access/models/dashboard.models';

const DENSE_NODE_THRESHOLD = 120;
const DENSE_EDGE_THRESHOLD = 220;
const READABLE_NODE_THRESHOLD = 70;
const READABLE_EDGE_THRESHOLD = 70;
const MAX_DENSE_CONTAINS_EDGES = 180;
const MAX_DENSE_LABELED_NODES = 32;
const DENSE_LABEL_DEGREE_FLOOR = 4;
const MAX_BACKBONE_NODES = 180;

const SEMANTIC_KINDS = new Set([
  'Component',
  'Service',
  'Controller',
  'Repository',
  'Entity',
  'Guard',
  'Interceptor',
  'Pipe',
]);

type DensityPreset = 'dense' | 'readable' | 'normal';

interface PreparedGraph {
  graph: ArchitectureGraph;
  denseMode: boolean;
  visibleNodeCount: number;
  visibleEdgeCount: number;
}

interface GroupNode {
  id: string;
  label: string;
  kind: string;
  metrics: Record<string, number>;
}

type GroupEdge = ArchitectureGraph['edges'][number] & {
  _weight?: number;
};

const KIND_COLORS: Record<string, string> = {
  Module: '#c1121f',
  Component: '#d62839',
  Service: '#a4161a',
  Controller: '#e5383b',
  Repository: '#ba181b',
  Entity: '#9d0208',
  Pipe: '#b21e35',
  Guard: '#e01e37',
  Interceptor: '#ad2831',
  External: '#596273',
  File: '#7b8597',
  Folder: '#7b8597',
  default: '#7b8597',
};

@Component({
  selector: 'cr-architecture-graph',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="canvas" #canvas></div>
    <div class="controls">
      <button type="button" class="chip" [class.on]="groupedView()" (click)="toggleGroupedView()">
        Grouped
      </button>
      <button type="button" class="chip" [class.on]="simplified()" (click)="toggleSimplified()">
        Simplified
      </button>
      <button type="button" class="chip" [class.on]="showContains()" (click)="toggleContains()">
        Contains edges
      </button>
      <button type="button" class="chip" [class.on]="showAllLabels()" (click)="toggleAllLabels()">
        All labels
      </button>
      <button type="button" class="chip" [class.on]="focusSelection()" (click)="toggleFocusSelection()">
        Focus selection
      </button>
    </div>
    <div class="coverage">
      Showing {{ visibleNodeCount() }} / {{ graph().nodes.length }} nodes and
      {{ visibleEdgeCount() }} / {{ graph().edges.length }} edges
    </div>
    <div class="legend">
      <span><i class="swatch swatch-focus"></i> focus</span>
      <span><i class="swatch swatch-context"></i> context</span>
    </div>
    @if (denseMode()) {
      <div class="density-hint">Dense graph mode: labels and contains-edges are reduced for readability.</div>
    }
    @if (selected(); as s) {
      <aside class="node-info">
        <header>
          <span class="badge" [style.background]="colorFor(s.kind)">{{ s.kind }}</span>
          <h4>{{ s.label }}</h4>
        </header>
        <p class="id">{{ s.id }}</p>
        <ul class="io">
          <li><span>Outgoing</span><strong>{{ s.outgoing }}</strong></li>
          <li><span>Incoming</span><strong>{{ s.incoming }}</strong></li>
        </ul>
        @if (s.dependsOn.length > 0) {
          <p class="list-title">Depends on</p>
          <ul>
            @for (n of s.dependsOn; track n) {
              <li><span>{{ n }}</span></li>
            }
          </ul>
        }
        @if (s.usedBy.length > 0) {
          <p class="list-title">Used by</p>
          <ul>
            @for (n of s.usedBy; track n) {
              <li><span>{{ n }}</span></li>
            }
          </ul>
        }
        @if (s.metrics) {
          <p class="list-title">Metrics</p>
          <ul>
            @for (m of metricEntries(s.metrics); track m.key) {
              <li><span>{{ m.key }}</span><strong>{{ m.value }}</strong></li>
            }
          </ul>
        }
      </aside>
    }
  `,
  styles: `
    :host { display: block; position: relative; height: 460px; }
    .canvas {
      position: absolute; inset: 0;
      border-radius: 14px;
      background: color-mix(in srgb, var(--bg-1) 70%, transparent);
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
    }
    .density-hint {
      position: absolute;
      left: 12px;
      top: 78px;
      z-index: 2;
      border-radius: 999px;
      border: 1px solid color-mix(in srgb, var(--accent) 46%, var(--surface-border));
      background: color-mix(in srgb, var(--bg-0) 86%, transparent);
      color: var(--text-2, #b8bdd1);
      font-size: 0.7rem;
      letter-spacing: 0.02em;
      padding: 0.28rem 0.6rem;
      backdrop-filter: blur(8px);
    }
    .controls {
      position: absolute;
      top: 12px;
      left: 12px;
      right: 12px;
      z-index: 3;
      display: flex;
      gap: 0.4rem;
      flex-wrap: wrap;
    }
    .chip {
      border: 1px solid rgba(255, 255, 255, 0.18);
      background: color-mix(in srgb, var(--bg-0) 78%, transparent);
      color: var(--text-2, #b8bdd1);
      border-radius: 999px;
      font-size: 0.7rem;
      letter-spacing: 0.03em;
      padding: 0.22rem 0.55rem;
      cursor: pointer;
      transition: border-color 140ms ease, background 140ms ease, color 140ms ease;
    }
    .chip.on {
      border-color: color-mix(in srgb, var(--accent) 70%, #ffffff 10%);
      background: color-mix(in srgb, var(--accent) 28%, transparent);
      color: #ffe7ea;
    }
    .coverage {
      position: absolute;
      bottom: 12px;
      left: 12px;
      z-index: 2;
      border-radius: 999px;
      border: 1px solid rgba(255, 255, 255, 0.16);
      background: color-mix(in srgb, var(--bg-0) 84%, transparent);
      color: var(--text-2, #b8bdd1);
      font-size: 0.68rem;
      padding: 0.2rem 0.55rem;
      backdrop-filter: blur(8px);
    }
    .legend {
      position: absolute;
      right: 12px;
      bottom: 12px;
      z-index: 2;
      display: inline-flex;
      align-items: center;
      gap: 0.55rem;
      border-radius: 999px;
      border: 1px solid rgba(255, 255, 255, 0.16);
      background: color-mix(in srgb, var(--bg-0) 84%, transparent);
      color: var(--text-3, #9ca3af);
      font-size: 0.66rem;
      padding: 0.2rem 0.5rem;
      backdrop-filter: blur(8px);
    }
    .legend span {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
    }
    .swatch {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      display: inline-block;
    }
    .swatch-focus { background: color-mix(in srgb, var(--accent) 88%, #ffffff 12%); }
    .swatch-context { background: rgba(255, 255, 255, 0.28); }
    .node-info {
      position: absolute; top: 12px; right: 12px;
      max-width: 260px;
      background: color-mix(in srgb, var(--bg-0) 90%, transparent);
      border: 1px solid var(--surface-border, rgba(255,255,255,0.08));
      border-radius: 12px;
      padding: 0.7rem 0.85rem;
      backdrop-filter: blur(12px);
      color: var(--text-1, #fff);
      font-size: 0.8rem;
      box-shadow: 0 12px 28px rgba(0,0,0,0.35);
    }
    .node-info header { display: flex; align-items: center; gap: 0.5rem; margin-bottom: 0.3rem; }
    .badge {
      padding: 0.1rem 0.45rem; border-radius: 999px;
      font-size: 0.65rem; text-transform: uppercase; letter-spacing: 0.08em;
      color: #0d0f1c; font-weight: 700;
    }
    h4 { margin: 0; font-size: 0.9rem; }
    .id { margin: 0 0 0.4rem; font-family: ui-monospace, SFMono-Regular, Menlo, monospace; color: var(--text-2, #b8bdd1); font-size: 0.72rem; word-break: break-all; }
    .io { margin-bottom: 0.45rem; }
    .list-title {
      margin: 0.35rem 0 0.2rem;
      font-size: 0.68rem;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--text-3, #9ca3af);
    }
    ul { list-style: none; margin: 0; padding: 0; display: grid; gap: 0.2rem; }
    li { display: flex; gap: 0.5rem; }
    li span { color: var(--text-2, #b8bdd1); }
    li strong { margin-left: auto; }
  `,
})
export class ArchitectureGraphView implements AfterViewInit {
  readonly graph = input.required<ArchitectureGraph>();

  private readonly canvasRef = viewChild.required<ElementRef<HTMLDivElement>>('canvas');
  private readonly destroyRef = inject(DestroyRef);

  protected readonly selected = signal<{
    id: string;
    label: string;
    kind: string;
    incoming: number;
    outgoing: number;
    dependsOn: string[];
    usedBy: string[];
    metrics?: Record<string, number> | null;
  } | null>(null);
  protected readonly denseMode = signal(false);
  protected readonly groupedView = signal(false);
  protected readonly simplified = signal(false);
  protected readonly showContains = signal(true);
  protected readonly showLeafNodes = signal(true);
  protected readonly largestClusterOnly = signal(false);
  protected readonly showAllLabels = signal(true);
  protected readonly backboneOnly = signal(false);
  protected readonly focusSelection = signal(true);
  protected readonly visibleNodeCount = signal(0);
  protected readonly visibleEdgeCount = signal(0);

  private readonly appliedPreset = signal<DensityPreset | null>(null);

  private cy: Core | null = null;

  constructor() {
    // Preset effect: allowed to write signals to set defaults when graph changes.
    effect(() => {
      const g = this.graph();
      const preset = this.isDenseGraph(g)
        ? 'dense'
        : this.isClutteredGraph(g)
          ? 'readable'
          : 'normal';
      if (this.appliedPreset() !== preset) {
        this.applyDensityPreset(preset);
      }
    }, { allowSignalWrites: true });

    // Render effect: re-runs whenever graph data OR any toggle signal changes.
    effect(() => {
      const g = this.graph();
      // Explicitly read all toggle signals so this effect re-runs when they change.
      const _opts = [
        this.groupedView(),
        this.simplified(),
        this.showContains(),
        this.showLeafNodes(),
        this.largestClusterOnly(),
        this.showAllLabels(),
        this.backboneOnly(),
        this.focusSelection(),
      ];

      if (!this.cy) return;
      const prepared = this.prepareGraph(g);
      this.denseMode.set(prepared.denseMode);
      this.visibleNodeCount.set(prepared.visibleNodeCount);
      this.visibleEdgeCount.set(prepared.visibleEdgeCount);
      this.cy.elements().remove();
      this.cy.add(this.toElements(prepared.graph));
      this.runLayout();
      this.applyFocus(this.selected()?.id ?? null);
    }, { allowSignalWrites: true });

    effect(() => {
      const selectedId = this.selected()?.id ?? null;
      const focus = this.focusSelection();
      if (!this.cy) return;
      if (!focus || !selectedId) {
        this.applyFocus(null);
        return;
      }
      this.applyFocus(selectedId);
    });
  }

  ngAfterViewInit(): void {
    void this.bootstrap();
  }

  private async bootstrap(): Promise<void> {
    // Lazy-load to keep cytoscape out of the initial bundle.
    const cytoscape = (await import('cytoscape')).default;
    const host = this.canvasRef().nativeElement;
    if (!host) return;

    this.cy = cytoscape({
      container: host,
      elements: this.toElements(this.prepareGraph(this.graph()).graph),
      wheelSensitivity: 0.2,
      style: [
        {
          selector: 'node',
          style: {
            'background-color': (ele: cytoscape.NodeSingular) =>
              KIND_COLORS[ele.data('kind')] ?? KIND_COLORS['default'],
            label: 'data(displayLabel)',
            color: '#e8eaf2',
            'font-size': 10,
            'text-margin-y': -6,
            'text-outline-width': 2,
            'text-outline-color': '#0a0c16',
            width: 'mapData(degree, 0, 20, 16, 50)',
            height: 'mapData(degree, 0, 20, 16, 50)',
            'border-width': 1,
            'border-color': 'rgba(255,255,255,0.22)',
          } as cytoscape.Css.Node,
        },
        {
          selector: 'node[kind = "Module"]',
          style: { shape: 'round-rectangle' } as cytoscape.Css.Node,
        },
        {
          selector: 'node[kind = "External"]',
          style: { shape: 'diamond', opacity: 0.75 } as cytoscape.Css.Node,
        },
        {
          selector: 'edge',
          style: {
            width: 'mapData(weight, 1, 8, 1, 2.2)',
            'line-color': 'rgba(193,18,31,0.38)',
            'target-arrow-color': 'rgba(193,18,31,0.65)',
            'target-arrow-shape': 'triangle',
            'curve-style': 'bezier',
            opacity: 0.75,
          } as cytoscape.Css.Edge,
        },
        {
          selector: 'edge[kind = "contains"]',
          style: { 'line-style': 'dashed', opacity: 0.4 } as cytoscape.Css.Edge,
        },
        {
          selector: 'node:selected',
          style: { 'border-width': 3, 'border-color': '#ff6b76' } as cytoscape.Css.Node,
        },
        {
          selector: 'node.muted',
          style: { opacity: 0.18, label: '' } as cytoscape.Css.Node,
        },
        {
          selector: 'edge.muted',
          style: { opacity: 0.1 } as cytoscape.Css.Edge,
        },
      ],
    });

    this.cy.on('tap', 'node', (evt: EventObject) => {
      const data = evt.target.data();
      this.selected.set({
        id: data.id,
        label: data.label,
        kind: data.kind,
        incoming: data.incoming ?? 0,
        outgoing: data.outgoing ?? 0,
        dependsOn: data.dependsOn ?? [],
        usedBy: data.usedBy ?? [],
        metrics: data.metrics ?? null,
      });
    });

    this.cy.on('tap', (evt: EventObject) => {
      if (evt.target === this.cy) this.selected.set(null);
    });

    const prepared = this.prepareGraph(this.graph());
    this.denseMode.set(prepared.denseMode);
    this.visibleNodeCount.set(prepared.visibleNodeCount);
    this.visibleEdgeCount.set(prepared.visibleEdgeCount);

    this.runLayout();

    this.destroyRef.onDestroy(() => {
      this.cy?.destroy();
      this.cy = null;
    });
  }

  private runLayout(): void {
    if (!this.cy) return;

    const nodeCount = this.cy.nodes().length;
    const edgeCount = this.cy.edges().length;
    const denseMode = nodeCount >= DENSE_NODE_THRESHOLD || edgeCount >= DENSE_EDGE_THRESHOLD;

    const layout: cytoscape.LayoutOptions = this.groupedView()
      ? {
          name: 'cose',
          animate: false,
          nodeRepulsion: () => 28000,
          idealEdgeLength: () => 220,
          gravity: 0.12,
          numIter: 1000,
          fit: true,
          padding: 56,
        }
      : denseMode
      ? {
          name: 'cose',
          animate: false,
          nodeRepulsion: () => 14000,
          idealEdgeLength: () => 130,
          gravity: 0.35,
          numIter: 1300,
          fit: true,
          padding: 44,
        }
      : {
          name: 'cose',
          animate: false,
          nodeRepulsion: () => 6000,
          idealEdgeLength: () => 90,
          gravity: 0.6,
          numIter: 800,
          fit: true,
          padding: 32,
        };

    this.cy.layout(layout).run();
  }

  private prepareGraph(g: ArchitectureGraph): PreparedGraph {
    if (this.groupedView()) {
      return this.prepareGroupedGraph(g);
    }

    const uniqueEdges = new Map<string, { from: string; to: string; kind: string }>();
    for (const e of g.edges) {
      if (!e.from || !e.to || e.from === e.to) continue;
      const key = `${e.from}|${e.to}|${e.kind}`;
      if (!uniqueEdges.has(key)) uniqueEdges.set(key, e);
    }

    const edges = [...uniqueEdges.values()];
    const denseMode = this.isDenseGraph({ ...g, edges });

    let visibleEdges = edges.filter(e => this.showContains() || e.kind !== 'contains');

    if (denseMode && this.showContains()) {
      const contains = visibleEdges.filter(e => e.kind === 'contains');
      const nonContains = visibleEdges.filter(e => e.kind !== 'contains');
      visibleEdges = [...nonContains, ...contains.slice(0, MAX_DENSE_CONTAINS_EDGES)];
    }

    const degree = new Map<string, number>();
    for (const e of visibleEdges) {
      degree.set(e.from, (degree.get(e.from) ?? 0) + 1);
      degree.set(e.to, (degree.get(e.to) ?? 0) + 1);
    }

    const coreKinds = SEMANTIC_KINDS;
    let visibleNodeIds = new Set(
      g.nodes
        .filter(n => this.showLeafNodes() || (degree.get(n.id) ?? 0) > 1 || coreKinds.has(n.kind))
        .map(n => n.id)
    );

    visibleEdges = visibleEdges.filter(e => visibleNodeIds.has(e.from) && visibleNodeIds.has(e.to));

    if (this.backboneOnly() || this.simplified()) {
      const compact = this.prunePeripheralChains(visibleNodeIds, visibleEdges, g, coreKinds, denseMode);
      visibleNodeIds = compact.nodeIds;
      visibleEdges = compact.edges;
    }

    if (this.largestClusterOnly()) {
      const largest = this.findLargestCluster(visibleNodeIds, visibleEdges);
      if (largest.size > 0) {
        visibleNodeIds = largest;
        visibleEdges = visibleEdges.filter(e => visibleNodeIds.has(e.from) && visibleNodeIds.has(e.to));
      }
    }

    degree.clear();
    for (const e of visibleEdges) {
      degree.set(e.from, (degree.get(e.from) ?? 0) + 1);
      degree.set(e.to, (degree.get(e.to) ?? 0) + 1);
    }

    const labelAllowlist = new Set<string>();
    const simplifyLabels = this.simplified() || denseMode;
    if (simplifyLabels && !this.showAllLabels()) {
      const ranked = [...degree.entries()]
        .sort((a, b) => b[1] - a[1])
        .filter(([, d]) => d >= DENSE_LABEL_DEGREE_FLOOR)
        .slice(0, MAX_DENSE_LABELED_NODES)
        .map(([id]) => id);
      for (const n of g.nodes) {
        if (coreKinds.has(n.kind)) labelAllowlist.add(n.id);
      }
      for (const id of ranked) labelAllowlist.add(id);
    }

    const nodes = g.nodes
      .filter(n => visibleNodeIds.has(n.id))
      .map(n => {
        const d = degree.get(n.id) ?? 0;
        const showLabel = this.showAllLabels() || !simplifyLabels || labelAllowlist.has(n.id);
        return {
          ...n,
          metrics: n.metrics ?? null,
          _degree: d,
          _displayLabel: showLabel ? n.label : '',
        };
      });

    const visibleNodeSet = new Set(nodes.map(n => n.id));
    visibleEdges = visibleEdges.filter(e => visibleNodeSet.has(e.from) && visibleNodeSet.has(e.to));

    return {
      denseMode,
      visibleNodeCount: nodes.length,
      visibleEdgeCount: visibleEdges.length,
      graph: {
        nodes,
        edges: visibleEdges,
      },
    };
  }

  private prepareGroupedGraph(g: ArchitectureGraph): PreparedGraph {
    const nodeById = new Map(g.nodes.map(n => [n.id, n]));
    const filteredEdges = g.edges.filter(e => this.showContains() || e.kind !== 'contains');

    const groups = new Map<string, GroupNode>();
    for (const n of g.nodes) {
      const key = n.kind || 'Unknown';
      const existing = groups.get(key);
      if (existing) {
        existing.metrics['nodes'] += 1;
      } else {
        groups.set(key, {
          id: `kind:${key}`,
          label: key,
          kind: key,
          metrics: { nodes: 1 },
        });
      }
    }

    const edgeAgg = new Map<string, GroupEdge>();
    for (const e of filteredEdges) {
      const from = nodeById.get(e.from);
      const to = nodeById.get(e.to);
      if (!from || !to) continue;
      const fromGroup = from.kind || 'Unknown';
      const toGroup = to.kind || 'Unknown';
      if (fromGroup === toGroup) continue;

      const key = `${fromGroup}->${toGroup}`;
      const existing = edgeAgg.get(key);
      if (existing) {
        existing._weight = (existing._weight ?? 1) + 1;
      } else {
        edgeAgg.set(key, {
          from: `kind:${fromGroup}`,
          to: `kind:${toGroup}`,
          kind: 'grouped',
          _weight: 1,
        });
      }
    }

    const nodes = [...groups.values()]
      .map(n => ({
        id: n.id,
        label: `${n.label} (${n.metrics['nodes']})`,
        kind: n.kind,
        metrics: n.metrics,
      }))
      .sort((a, b) => (b.metrics?.['nodes'] ?? 0) - (a.metrics?.['nodes'] ?? 0));

    const nodeSet = new Set(nodes.map(n => n.id));
    const edges = [...edgeAgg.values()]
      .filter(e => nodeSet.has(e.from) && nodeSet.has(e.to))
      .sort((a, b) => (b._weight ?? 0) - (a._weight ?? 0))
      .slice(0, 48);

    return {
      denseMode: false,
      visibleNodeCount: nodes.length,
      visibleEdgeCount: edges.length,
      graph: {
        nodes,
        edges,
      },
    };
  }

  private isDenseGraph(g: ArchitectureGraph): boolean {
    return g.nodes.length >= DENSE_NODE_THRESHOLD || g.edges.length >= DENSE_EDGE_THRESHOLD;
  }

  private isClutteredGraph(g: ArchitectureGraph): boolean {
    return g.nodes.length >= READABLE_NODE_THRESHOLD || g.edges.length >= READABLE_EDGE_THRESHOLD;
  }

  private applyDensityPreset(preset: DensityPreset): void {
    this.appliedPreset.set(preset);
    if (preset === 'dense') {
      this.groupedView.set(true);
      this.simplified.set(true);
      this.backboneOnly.set(true);
      this.showContains.set(false);
      this.showLeafNodes.set(false);
      this.largestClusterOnly.set(true);
      this.showAllLabels.set(false);
      return;
    }

    if (preset === 'readable') {
      this.groupedView.set(false);
      this.simplified.set(true);
      this.backboneOnly.set(true);
      this.showContains.set(false);
      this.showLeafNodes.set(false);
      this.largestClusterOnly.set(true);
      this.showAllLabels.set(false);
      return;
    }

    this.groupedView.set(false);
    this.simplified.set(false);
    this.backboneOnly.set(false);
    this.showContains.set(true);
    this.showLeafNodes.set(true);
    this.largestClusterOnly.set(false);
    this.showAllLabels.set(false);
  }

  private applyFocus(nodeId: string | null): void {
    if (!this.cy) return;

    this.cy.nodes().removeClass('muted');
    this.cy.edges().removeClass('muted');

    if (!this.focusSelection() || !nodeId) return;

    const center = this.cy.getElementById(nodeId);
    if (!center || center.empty()) return;

    const neighborhood = center.closedNeighborhood();
    const keepNodes = neighborhood.nodes();
    const keepEdges = neighborhood.edges();

    this.cy.nodes().difference(keepNodes).addClass('muted');
    this.cy.edges().difference(keepEdges).addClass('muted');
  }

  private prunePeripheralChains(
    nodeIds: Set<string>,
    edges: Array<{ from: string; to: string; kind: string }>,
    graph: ArchitectureGraph,
    coreKinds: Set<string>,
    denseMode: boolean
  ): { nodeIds: Set<string>; edges: Array<{ from: string; to: string; kind: string }> } {
    const nodeKind = new Map(graph.nodes.map(n => [n.id, n.kind]));
    const working = new Set(nodeIds);
    const minBackboneDegree = denseMode ? 3 : 2;

    let changed = true;
    while (changed) {
      changed = false;
      const degree = new Map<string, number>();
      for (const e of edges) {
        if (!working.has(e.from) || !working.has(e.to)) continue;
        degree.set(e.from, (degree.get(e.from) ?? 0) + 1);
        degree.set(e.to, (degree.get(e.to) ?? 0) + 1);
      }

      const remove: string[] = [];
      for (const id of working) {
        const kind = nodeKind.get(id) ?? 'default';
        const d = degree.get(id) ?? 0;
        if (coreKinds.has(kind)) continue;
        if (d < minBackboneDegree) remove.push(id);
      }

      if (remove.length > 0) {
        for (const id of remove) working.delete(id);
        changed = true;
      }
    }

    let filteredEdges = edges.filter(e => working.has(e.from) && working.has(e.to));

    if (working.size > MAX_BACKBONE_NODES) {
      const degree = new Map<string, number>();
      for (const e of filteredEdges) {
        degree.set(e.from, (degree.get(e.from) ?? 0) + 1);
        degree.set(e.to, (degree.get(e.to) ?? 0) + 1);
      }

      const mustKeep = new Set<string>();
      for (const id of working) {
        const kind = nodeKind.get(id) ?? 'default';
        if (coreKinds.has(kind)) mustKeep.add(id);
      }

      const remainingSlots = Math.max(MAX_BACKBONE_NODES - mustKeep.size, 0);
      const ranked = [...working]
        .filter(id => !mustKeep.has(id))
        .sort((a, b) => (degree.get(b) ?? 0) - (degree.get(a) ?? 0))
        .slice(0, remainingSlots);

      const finalNodes = new Set<string>([...mustKeep, ...ranked]);
      filteredEdges = filteredEdges.filter(e => finalNodes.has(e.from) && finalNodes.has(e.to));
      return { nodeIds: finalNodes, edges: filteredEdges };
    }

    return { nodeIds: working, edges: filteredEdges };
  }

  private findLargestCluster(nodeIds: Set<string>, edges: Array<{ from: string; to: string }>): Set<string> {
    const adjacency = new Map<string, Set<string>>();
    for (const id of nodeIds) adjacency.set(id, new Set<string>());

    for (const e of edges) {
      if (!nodeIds.has(e.from) || !nodeIds.has(e.to)) continue;
      adjacency.get(e.from)?.add(e.to);
      adjacency.get(e.to)?.add(e.from);
    }

    const unvisited = new Set(nodeIds);
    let largest = new Set<string>();

    while (unvisited.size > 0) {
      const start = unvisited.values().next().value as string;
      const queue: string[] = [start];
      const component = new Set<string>([start]);
      unvisited.delete(start);

      while (queue.length > 0) {
        const current = queue.shift();
        if (!current) continue;
        for (const next of adjacency.get(current) ?? []) {
          if (!unvisited.has(next)) continue;
          unvisited.delete(next);
          component.add(next);
          queue.push(next);
        }
      }

      if (component.size > largest.size) largest = component;
    }

    return largest;
  }

  protected toggleGroupedView(): void {
    this.groupedView.set(!this.groupedView());
  }

  protected toggleSimplified(): void {
    this.simplified.set(!this.simplified());
    if (!this.simplified()) this.showAllLabels.set(true);
  }

  protected toggleContains(): void {
    this.showContains.set(!this.showContains());
  }

  protected toggleBackbone(): void {
    this.backboneOnly.set(!this.backboneOnly());
  }

  protected toggleLeafNodes(): void {
    this.showLeafNodes.set(!this.showLeafNodes());
  }

  protected toggleLargestCluster(): void {
    this.largestClusterOnly.set(!this.largestClusterOnly());
  }

  protected toggleAllLabels(): void {
    this.showAllLabels.set(!this.showAllLabels());
  }

  protected toggleFocusSelection(): void {
    this.focusSelection.set(!this.focusSelection());
    if (!this.focusSelection()) this.applyFocus(null);
  }

  private toElements(g: ArchitectureGraph): ElementDefinition[] {
    const incoming = new Map<string, string[]>();
    const outgoing = new Map<string, string[]>();

    for (const e of g.edges) {
      const out = outgoing.get(e.from) ?? [];
      out.push(e.to);
      outgoing.set(e.from, out);

      const inc = incoming.get(e.to) ?? [];
      inc.push(e.from);
      incoming.set(e.to, inc);
    }

    const uniq = (arr: string[]) => [...new Set(arr)].slice(0, 5);

    const nodes: ElementDefinition[] = g.nodes.map(n => ({
      data: {
        id: n.id,
        label: n.label,
        displayLabel: (n as typeof n & { _displayLabel?: string })._displayLabel ?? n.label,
        kind: n.kind,
        metrics: n.metrics ?? null,
        degree: (n as typeof n & { _degree?: number })._degree ?? 0,
        outgoing: (outgoing.get(n.id) ?? []).length,
        incoming: (incoming.get(n.id) ?? []).length,
        dependsOn: uniq(outgoing.get(n.id) ?? []),
        usedBy: uniq(incoming.get(n.id) ?? []),
      },
    }));

    const edges: ElementDefinition[] = g.edges.map(e => ({
      data: {
        id: `${e.from}->${e.to}:${e.kind}`,
        source: e.from,
        target: e.to,
        kind: e.kind,
        weight:
          (e as typeof e & { _weight?: number })._weight ??
          (e.kind === 'depends_on' ? 8 : e.kind === 'imports' ? 5 : e.kind === 'calls' ? 4 : 1),
      },
    }));

    return [...nodes, ...edges];
  }

  protected colorFor(kind: string): string {
    return KIND_COLORS[kind] ?? KIND_COLORS['default'];
  }

  protected metricEntries(metrics: Record<string, number>) {
    return Object.entries(metrics).map(([key, value]) => ({ key, value }));
  }
}
