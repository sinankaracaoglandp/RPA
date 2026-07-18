import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { StructuredAddMenuComponent } from './structured-add-menu.component';
import { ContainerItem } from '../structured-model';

describe('StructuredAddMenuComponent', () => {
  let http: HttpTestingController;
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StructuredAddMenuComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
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
