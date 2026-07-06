import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PropertiesPanelComponent } from './properties-panel.component';

describe('PropertiesPanelComponent', () => {
  let fixture: ComponentFixture<PropertiesPanelComponent>;
  let component: PropertiesPanelComponent;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PropertiesPanelComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(PropertiesPanelComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  it('shows the empty state when no activity is selected', () => {
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[data-testid="properties-panel-empty"]'),
    ).toBeTruthy();
  });

  it('renders the generic editor form for a non-web activity from plain inputs', () => {
    component.activityType = 'Sap.Gui.Click';
    component.properties = { elementId: 'wnd[0]/usr/btn' };
    fixture.detectChanges();

    // GenericPropertyComponent metadata'yı katalogdan çeker.
    const req = http.expectOne('/api/activities/Sap.Gui.Click');
    req.flush({
      activityId: 'Sap.Gui.Click',
      displayName: 'SAP GUI Tıkla',
      inputs: [{ name: 'elementId', type: 'string', required: true }],
    });
    fixture.detectChanges();

    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('[data-testid="prop-elementId"]');
    expect(input).toBeTruthy();
    expect(input.value).toBe('wnd[0]/usr/btn');
  });

  it('emits propertiesChange when a field is edited', () => {
    component.activityType = 'Sap.Gui.Click';
    component.properties = {};
    fixture.detectChanges();
    http.expectOne('/api/activities/Sap.Gui.Click').flush({
      activityId: 'Sap.Gui.Click',
      displayName: 'SAP GUI Tıkla',
      inputs: [{ name: 'elementId', type: 'string', required: true }],
    });
    fixture.detectChanges();

    const emitted: Record<string, unknown>[] = [];
    component.propertiesChange.subscribe((v) => emitted.push(v));

    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('[data-testid="prop-elementId"]');
    input.value = 'wnd[0]/usr/txtNew';
    input.dispatchEvent(new Event('input', { bubbles: true }));

    expect(emitted).toEqual([{ elementId: 'wnd[0]/usr/txtNew' }]);
  });
});
