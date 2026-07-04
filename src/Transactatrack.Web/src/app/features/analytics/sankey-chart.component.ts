import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  effect,
  input,
  viewChild,
} from '@angular/core';
import { SankeyData } from './analytics.service';

const COLOR_BY_KIND: Record<string, string> = {
  income: '#2e7d32',
  source: '#43a047', // "Transfers in" — an inflow whose paying account isn't in view
  account: '#1976d2',
  category: '#b00020',
  sink: '#ef6c00', // "Transfers out"
};

const currency = (v: number) =>
  Intl.NumberFormat(undefined, { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }).format(v);

@Component({
  selector: 'app-sankey-chart',
  standalone: true,
  template: `
    <div #host class="sankey-host" [style.display]="hasData() ? 'block' : 'none'"></div>
    @if (!hasData()) {
      <div class="empty">No money-flow data in this range.</div>
    }
  `,
  styles: [`
    :host { display: block; }
    .sankey-host { width: 100%; height: 480px; }
    .empty { padding: 48px; text-align: center; color: rgba(0,0,0,0.55); }
  `],
})
export class SankeyChartComponent implements AfterViewInit, OnDestroy {
  data = input<SankeyData | null>(null);

  private host = viewChild.required<ElementRef<HTMLDivElement>>('host');
  private chart: any = null;
  private echarts: any = null;
  private resizeObs?: ResizeObserver;

  constructor() {
    // Re-render whenever the data input changes (only once the chart is initialized).
    effect(() => {
      const d = this.data();
      if (this.chart) this.render(d);
    });
  }

  hasData(): boolean {
    return (this.data()?.links?.length ?? 0) > 0;
  }

  async ngAfterViewInit() {
    // Lazy-load echarts so its ~1MB bundle stays out of the main chunk.
    this.echarts = await import('echarts');
    this.chart = this.echarts.init(this.host().nativeElement);
    this.resizeObs = new ResizeObserver(() => this.chart?.resize());
    this.resizeObs.observe(this.host().nativeElement);
    this.render(this.data());
  }

  ngOnDestroy() {
    this.resizeObs?.disconnect();
    this.chart?.dispose();
  }

  private render(d: SankeyData | null) {
    if (!this.chart) return;
    if (!d || d.links.length === 0) {
      this.chart.clear();
      return;
    }

    const labelById: Record<string, string> = {};
    for (const n of d.nodes) labelById[n.id] = n.label;

    const nodes = d.nodes.map(n => ({
      name: n.id,
      itemStyle: { color: COLOR_BY_KIND[n.kind] ?? '#90a4ae' },
    }));
    const links = d.links.map(l => ({ source: l.source, target: l.target, value: l.value }));

    this.chart.setOption(
      {
        tooltip: {
          trigger: 'item',
          formatter: (p: any) => {
            if (p.dataType === 'edge') {
              const s = labelById[p.data.source] ?? p.data.source;
              const t = labelById[p.data.target] ?? p.data.target;
              return `${s} → ${t}<br/><b>${currency(p.data.value)}</b>`;
            }
            return `${labelById[p.name] ?? p.name}: <b>${currency(p.value)}</b>`;
          },
        },
        series: [
          {
            type: 'sankey',
            data: nodes,
            links,
            emphasis: { focus: 'adjacency' },
            nodeAlign: 'justify',
            label: {
              formatter: (p: any) => labelById[p.name] ?? p.name,
              fontSize: 12,
            },
            lineStyle: { color: 'gradient', curveness: 0.5, opacity: 0.4 },
          },
        ],
      },
      true,
    );
  }
}
