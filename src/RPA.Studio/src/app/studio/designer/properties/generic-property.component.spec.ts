import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GenericPropertyComponent } from './generic-property.component';
import { SpyService } from '../../../shared/services/spy.service';

class FakeSpyService {
  pick = vi.fn();
}

describe('GenericPropertyComponent', () => {
  let fixture: ComponentFixture<GenericPropertyComponent>;
  let component: GenericPropertyComponent;
  let http: HttpTestingController;
  let spy: FakeSpyService;

  beforeEach(async () => {
    spy = new FakeSpyService();
    await TestBed.configureTestingModule({
      imports: [GenericPropertyComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: SpyService, useValue: spy }],
    }).compileComponents();
    fixture = TestBed.createComponent(GenericPropertyComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
  });

  it('renders a correctly-typed form field for every supported port type', () => {
    component.activityType = 'Test.AllTypes';
    component.properties = {};
    fixture.detectChanges();

    http.expectOne('/api/activities/Test.AllTypes').flush({
      activityId: 'Test.AllTypes',
      displayName: 'Tüm Tipler',
      inputs: [
        { name: 'fString', type: 'string', required: true },
        { name: 'fInt', type: 'int' },
        { name: 'fNumber', type: 'number' },
        { name: 'fDecimal', type: 'decimal' },
        { name: 'fBool', type: 'bool' },
        { name: 'fBoolean', type: 'boolean' },
        { name: 'fJson', type: 'JSON' },
        { name: 'fTable', type: 'DataTable' },
        { name: 'fCred', type: 'Credential' },
      ],
    });
    fixture.detectChanges();

    const typeOf = (name: string): string =>
      (fixture.nativeElement.querySelector(`[data-testid="prop-${name}"]`) as HTMLInputElement)
        .type;

    expect(typeOf('fString')).toBe('text');
    expect(typeOf('fInt')).toBe('number');
    expect(typeOf('fNumber')).toBe('number');
    expect(typeOf('fDecimal')).toBe('number');
    expect(fixture.nativeElement.querySelector('[data-testid="prop-fBool"]').tagName).toBe('BUTTON');
    expect(fixture.nativeElement.querySelector('[data-testid="prop-fBool"]').getAttribute('aria-pressed')).toBe('false');
    expect(fixture.nativeElement.querySelector('[data-testid="prop-fBoolean"]').tagName).toBe('BUTTON');
    expect(typeOf('fJson')).toBe('text');
    expect(typeOf('fTable')).toBe('text');
    expect(typeOf('fCred')).toBe('password'); // Credential asla düz metin gösterilmez
  });

  it('shows a visible error message when catalog metadata cannot be loaded', () => {
    component.activityType = 'Missing.Activity';
    fixture.detectChanges();
    http.expectOne('/api/activities/Missing.Activity').flush(
      { error: 'yok' },
      { status: 404, statusText: 'Not Found' },
    );
    fixture.detectChanges();

    const status = fixture.nativeElement.querySelector('.generic-property__status--error');
    expect(status).toBeTruthy();
    expect(status.textContent).toContain('yüklenemedi');
  });

  it('toggles boolean properties with a button control', () => {
    component.activityType = 'Test.Bool';
    component.properties = { headless: false };
    const emitted: Record<string, unknown>[] = [];
    component.propertiesChange.subscribe((value) => emitted.push(value));
    fixture.detectChanges();

    http.expectOne('/api/activities/Test.Bool').flush({
      activityId: 'Test.Bool',
      displayName: 'Bool',
      inputs: [{ name: 'headless', type: 'bool' }],
    });
    fixture.detectChanges();

    const button = fixture.nativeElement.querySelector('[data-testid="prop-headless"]') as HTMLButtonElement;
    button.click();
    fixture.detectChanges();

    expect(button.getAttribute('aria-pressed')).toBe('true');
    expect(emitted.at(-1)).toEqual({ headless: true });
  });

  it('renders a picker button for inputs with pickerKind and writes the selected element id', async () => {
    component.activityType = 'Sap.Gui.Click';
    component.properties = {};
    fixture.detectChanges();

    http.expectOne('/api/activities/Sap.Gui.Click').flush({
      activityId: 'Sap.Gui.Click',
      displayName: 'SAP GUI Tikla',
      inputs: [{ name: 'elementId', type: 'string', required: true, pickerKind: 'sap' }],
    });
    spy.pick.mockResolvedValue({
      sessionId: 's1',
      kind: 'sap',
      elementId: 'wnd[0]/usr/btn[OK]',
    });
    const emitted: Record<string, unknown>[] = [];
    component.propertiesChange.subscribe((value) => emitted.push(value));
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-testid="selector-picker"]').click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(spy.pick).toHaveBeenCalledWith('sap');
    expect((fixture.nativeElement.querySelector('[data-testid="prop-elementId"]') as HTMLInputElement).value)
      .toBe('wnd[0]/usr/btn[OK]');
    expect(emitted.at(-1)).toEqual({ elementId: 'wnd[0]/usr/btn[OK]' });
  });

  it('does not render a picker button for credential inputs', () => {
    component.activityType = 'Test.Credential';
    component.properties = {};
    fixture.detectChanges();

    http.expectOne('/api/activities/Test.Credential').flush({
      activityId: 'Test.Credential',
      displayName: 'Credential',
      inputs: [{ name: 'password', type: 'Credential', pickerKind: 'sap' }],
    });
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="selector-picker"]')).toBeFalsy();
  });

  it('renders select options for metadata-driven choice inputs', () => {
    component.activityType = 'Web.Open';
    component.properties = { browser: 'chromium' };
    const emitted: Record<string, unknown>[] = [];
    component.propertiesChange.subscribe((value) => emitted.push(value));
    fixture.detectChanges();

    http.expectOne('/api/activities/Web.Open').flush({
      activityId: 'Web.Open',
      displayName: 'Tarayici Ac',
      inputs: [{ name: 'browser', type: 'string', options: ['chromium', 'chrome', 'edge'] }],
    });
    fixture.detectChanges();

    const select = fixture.nativeElement.querySelector('[data-testid="prop-browser"]') as HTMLSelectElement;
    expect([...select.options].map((option) => option.value)).toEqual(['chromium', 'chrome', 'edge']);

    select.value = 'edge';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(emitted.at(-1)).toEqual({ browser: 'edge' });
  });

  it('shows condition expression examples for Logic.If', () => {
    component.activityType = 'Logic.If';
    component.properties = {};
    fixture.detectChanges();

    http.expectOne('/api/activities/Logic.If').flush({
      activityId: 'Logic.If',
      displayName: 'Eger / Kosul',
      inputs: [{ name: 'condition', type: 'string', description: 'Kosul', required: true }],
    });
    fixture.detectChanges();

    const examples = fixture.nativeElement.querySelector('[data-testid="prop-condition-examples"]') as HTMLElement;
    expect(examples).toBeTruthy();
    expect(examples.textContent).toContain('{{karar}} == 1');
    expect(examples.textContent).toContain('{{karar}} != 0');
    expect(examples.textContent).toContain('{{aktif}} == true');
    expect(examples.textContent).toContain('{{tarih}} == "2026-07-09T08:30:00"');
  });
});
