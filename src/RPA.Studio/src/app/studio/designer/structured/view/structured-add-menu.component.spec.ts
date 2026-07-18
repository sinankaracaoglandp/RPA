import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { StructuredAddMenuComponent } from './structured-add-menu.component';
import { StructuredPaletteFilter } from './structured-palette-filter';
import { ContainerItem } from '../structured-model';

describe('StructuredAddMenuComponent', () => {
  let http: HttpTestingController;
  let filter: StructuredPaletteFilter;
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StructuredAddMenuComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), StructuredPaletteFilter],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    filter = TestBed.inject(StructuredPaletteFilter);
  });

  it('emits a container item when a control type is chosen', () => {
    const f = TestBed.createComponent(StructuredAddMenuComponent);
    f.componentInstance.open = true;
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([]));
    let picked: unknown;
    f.componentInstance.pick.subscribe((i) => (picked = i));
    (f.nativeElement.querySelector('[data-testid="add-type-if"]') as HTMLButtonElement).click();
    expect((picked as ContainerItem).type).toBe('if');
  });

  it('excludes control-flow activities (Logic.ForEach) from the activity list', () => {
    const f = TestBed.createComponent(StructuredAddMenuComponent);
    f.componentInstance.open = true;
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([
      { activityId: 'Logic.ForEach', displayName: 'Her Biri İçin', category: 'Logic', inputs: [], outputs: [] },
      { activityId: 'Web.Click', displayName: 'Tıkla', category: 'Web', inputs: [], outputs: [] },
    ]));
    f.detectChanges();
    expect(f.componentInstance.activities.map((a) => a.activityId)).toEqual(['Web.Click']);
  });

  it('mirrors the palette category filter in the activity dropdown', () => {
    const f = TestBed.createComponent(StructuredAddMenuComponent);
    f.componentInstance.open = true;
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([
      { activityId: 'Web.Click', displayName: 'Tıkla', category: 'Web', inputs: [], outputs: [] },
      { activityId: 'Excel.Read', displayName: 'Excel Oku', category: 'Excel', inputs: [], outputs: [] },
    ]));
    f.detectChanges();
    const cmp = f.componentInstance;

    // filtre yok → kontrol düğmeleri + tüm aktiviteler
    expect(cmp.showControls).toBe(true);
    expect(cmp.filteredActivities.length).toBe(2);

    // Excel seçili → yalnız Excel aktivitesi, kontrol düğmeleri gizli
    filter.toggle('Excel');
    expect(cmp.showControls).toBe(false);
    expect(cmp.filteredActivities.map((a) => a.activityId)).toEqual(['Excel.Read']);

    // Kontrol seçili → kontrol düğmeleri var, aktivite yok
    filter.toggle('Excel');
    filter.toggle('Kontrol');
    expect(cmp.showControls).toBe(true);
    expect(cmp.filteredActivities).toEqual([]);
  });

  it('emits a step item when an activity is chosen', () => {
    const f = TestBed.createComponent(StructuredAddMenuComponent);
    f.componentInstance.open = true;
    f.detectChanges();
    http.match('/api/activities').forEach((r) => r.flush([
      { activityId: 'Web.Click', displayName: 'Tıkla', category: 'Web', inputs: [], outputs: [] },
    ]));
    f.detectChanges();
    let picked: unknown;
    f.componentInstance.pick.subscribe((i) => (picked = i));
    f.componentInstance.chooseActivity('Web.Click');
    expect((picked as { node: { activity: string } }).node.activity).toBe('Web.Click');
  });
});
