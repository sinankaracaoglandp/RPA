import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { StructuredPaletteComponent } from './structured-palette.component';
import { StructuredPaletteFilter } from './structured-palette-filter';
import { ContainerItem } from '../structured-model';

describe('StructuredPaletteComponent', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StructuredPaletteComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), StructuredPaletteFilter],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
  });

  it('exposes control-type chips whose factory builds a container', () => {
    const f = TestBed.createComponent(StructuredPaletteComponent);
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([]));
    const chip = f.componentInstance.controlChips.find((c) => c.type === 'if')!;
    expect((chip.factory() as ContainerItem).type).toBe('if');
  });

  it('builds activity chips from the catalog whose factory builds a step', () => {
    const f = TestBed.createComponent(StructuredPaletteComponent);
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([
      { activityId: 'Web.Click', displayName: 'Tıkla', category: 'Web', inputs: [], outputs: [] },
    ]));
    f.detectChanges();
    const chip = f.componentInstance.activityChips[0];
    expect((chip.factory() as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });

  it('filters chips by the selected category (and Kontrol shows only control chips)', () => {
    const f = TestBed.createComponent(StructuredPaletteComponent);
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([
      { activityId: 'Web.Click', displayName: 'Tıkla', category: 'Web', inputs: [], outputs: [] },
      { activityId: 'Excel.Read', displayName: 'Excel Oku', category: 'Excel', inputs: [], outputs: [] },
    ]));
    f.detectChanges();
    const cmp = f.componentInstance;

    // filtre yok → hepsi
    expect(cmp.visibleControlChips.length).toBeGreaterThan(0);
    expect(cmp.visibleActivityChips.map((c) => c.label)).toEqual(['Tıkla', 'Excel Oku']);

    // Web seçili → yalnız Web aktiviteleri, kontrol çipleri gizli
    cmp.select('Web');
    expect(cmp.visibleControlChips).toEqual([]);
    expect(cmp.visibleActivityChips.map((c) => c.label)).toEqual(['Tıkla']);

    // Kontrol seçili → yalnız kontrol çipleri, aktivite yok
    cmp.select('Kontrol');
    expect(cmp.visibleControlChips.length).toBeGreaterThan(0);
    expect(cmp.visibleActivityChips).toEqual([]);

    // aynı kategoriye tekrar → filtre kalkar
    cmp.select('Kontrol');
    expect(cmp.visibleActivityChips.length).toBe(2);
  });
});
