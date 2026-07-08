import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { WebPropertyRouterComponent } from './web-property-router.component';

function setValue(fixture: ComponentFixture<WebPropertyRouterComponent>, testId: string, value: string): void {
  const input: HTMLInputElement = fixture.nativeElement.querySelector(`[data-testid="${testId}"]`);
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

describe('WebPropertyRouterComponent', () => {
  let fixture: ComponentFixture<WebPropertyRouterComponent>;
  let component: WebPropertyRouterComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WebPropertyRouterComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(WebPropertyRouterComponent);
    component = fixture.componentInstance;
  });

  function select(activityType: string, properties: Record<string, unknown> = {}): void {
    fixture.componentRef.setInput('activityType', activityType);
    fixture.componentRef.setInput('properties', properties);
    fixture.detectChanges();
  }

  it('routes Web.Navigate to the navigate editor', () => {
    select('Web.Navigate');
    expect(fixture.nativeElement.querySelector('[data-testid="web-navigate-form"]')).toBeTruthy();
  });

  it('routes Web.Click to the click editor', () => {
    select('Web.Click');
    expect(fixture.nativeElement.querySelector('[data-testid="web-click-form"]')).toBeTruthy();
  });

  it('routes Web.SetText to the set-text editor', () => {
    select('Web.SetText');
    expect(fixture.nativeElement.querySelector('[data-testid="web-set-text-form"]')).toBeTruthy();
  });

  it('routes Web.GetText to the get-text editor', () => {
    select('Web.GetText');
    expect(fixture.nativeElement.querySelector('[data-testid="web-get-text-form"]')).toBeTruthy();
  });

  it('routes Web.WaitForSelector to the wait-for-selector editor', () => {
    select('Web.WaitForSelector');
    expect(fixture.nativeElement.querySelector('[data-testid="web-wait-for-selector-form"]')).toBeTruthy();
  });

  it('renders nothing for an unknown/unset activity type', () => {
    select('Sap.Gui.Connect');
    expect(fixture.nativeElement.querySelector('[data-testid="web-navigate-form"]')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('[data-testid="web-click-form"]')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('[data-testid="web-set-text-form"]')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('[data-testid="web-get-text-form"]')).toBeFalsy();
    expect(fixture.nativeElement.querySelector('[data-testid="web-wait-for-selector-form"]')).toBeFalsy();
  });

  it('renders nothing when activityType is null', () => {
    select(null as unknown as string);
    expect(fixture.nativeElement.querySelector('[data-testid="web-property-router"]').children.length).toBe(0);
  });

  it('Navigate form is invalid until the required url field is filled, and updates on input', () => {
    select('Web.Navigate');
    let emitted: Record<string, unknown> | undefined;
    component.propertiesChange.subscribe((v) => (emitted = v));

    expect(emitted).toBeUndefined();

    setValue(fixture, 'web-navigate-url-input', 'https://example.com');
    expect(emitted).toEqual({ url: 'https://example.com' });
  });

  it('Click form waits for the add button before emitting a click step', () => {
    select('Web.Click');
    let emitted: Record<string, unknown> | undefined;
    component.propertiesChange.subscribe((v) => (emitted = v));

    setValue(fixture, 'web-click-selector-input', '');
    expect(emitted).toBeUndefined();

    setValue(fixture, 'web-click-selector-input', '#submit');
    expect(emitted).toBeUndefined();

    fixture.nativeElement.querySelector('[data-testid="web-click-add-step"]').click();
    fixture.detectChanges();

    expect(emitted).toEqual({
      steps: [{ selector: '#submit', action: 'click', timeoutMs: 30000 }],
      selector: '#submit',
      action: 'click',
      timeoutMs: 30000,
    });
    expect(fixture.nativeElement.querySelector('[data-testid="web-click-selector-input"]').value).toBe('');
  });

  it('Click form supports hover and waiting for an opened submenu', () => {
    select('Web.Click');
    let emitted: Record<string, unknown> | undefined;
    component.propertiesChange.subscribe((v) => (emitted = v));

    setValue(fixture, 'web-click-selector-input', '#top-menu');

    const actionSelect: HTMLSelectElement = fixture.nativeElement.querySelector(
      '[data-testid="web-click-action-select"]',
    );
    actionSelect.value = 'hover';
    actionSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    setValue(fixture, 'web-click-wait-selector-input', '#top-menu .submenu-item');

    const timeoutInput: HTMLInputElement = fixture.nativeElement.querySelector(
      '[data-testid="web-click-timeout-input"]',
    );
    timeoutInput.value = '7000';
    timeoutInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="web-click-add-step"]').click();
    fixture.detectChanges();

    expect(emitted).toEqual({
      steps: [
        {
          selector: '#top-menu',
          action: 'hover',
          waitSelector: '#top-menu .submenu-item',
          timeoutMs: 7000,
        },
      ],
      selector: '#top-menu',
      action: 'hover',
      waitSelector: '#top-menu .submenu-item',
      timeoutMs: 7000,
    });
  });

  it('Click form adds selectors to a numbered click step list', () => {
    select('Web.Click');
    let emitted: Record<string, unknown> | undefined;
    component.propertiesChange.subscribe((v) => (emitted = v));

    setValue(fixture, 'web-click-selector-input', '#top-menu');
    fixture.nativeElement.querySelector('[data-testid="web-click-add-step"]').click();
    fixture.detectChanges();

    setValue(fixture, 'web-click-selector-input', '#top-menu .settings');
    fixture.nativeElement.querySelector('[data-testid="web-click-add-step"]').click();
    fixture.detectChanges();

    const items = fixture.nativeElement.querySelectorAll('[data-testid="web-click-step-item"]');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('1');
    expect(items[0].textContent).toContain('#top-menu');
    expect(items[1].textContent).toContain('2');
    expect(items[1].textContent).toContain('#top-menu .settings');
    expect(emitted).toEqual({
      steps: [
        { selector: '#top-menu', action: 'click', timeoutMs: 30000 },
        { selector: '#top-menu .settings', action: 'click', timeoutMs: 30000 },
      ],
      selector: '#top-menu',
      action: 'click',
      timeoutMs: 30000,
    });
  });

  it('Click form removes a click step and emits an empty step list when the last item is removed', () => {
    select('Web.Click', { steps: [{ selector: '#submit', action: 'click', timeoutMs: 30000 }] });
    let emitted: Record<string, unknown> | undefined;
    component.propertiesChange.subscribe((v) => (emitted = v));

    fixture.nativeElement.querySelector('[data-testid="web-click-remove-step"]').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('[data-testid="web-click-step-item"]').length).toBe(0);
    expect(emitted).toEqual({ steps: [], selector: '', action: 'click', timeoutMs: 30000 });
  });

  it('SetText form requires both selector and text before emitting', () => {
    select('Web.SetText');
    let emitted: Record<string, unknown> | undefined;
    component.propertiesChange.subscribe((v) => (emitted = v));

    setValue(fixture, 'web-set-text-selector-input', '#field');
    expect(emitted).toBeUndefined();

    setValue(fixture, 'web-set-text-text-input', 'hello');
    expect(emitted).toEqual({ selector: '#field', text: 'hello' });
  });

  it('WaitForSelector form requires selector and a positive timeoutMs', () => {
    select('Web.WaitForSelector', { selector: '#loaded', timeoutMs: 5000 });
    let emitted: Record<string, unknown> | undefined;
    component.propertiesChange.subscribe((v) => (emitted = v));

    const timeoutInput: HTMLInputElement = fixture.nativeElement.querySelector(
      '[data-testid="web-wait-for-selector-timeout-input"]',
    );
    expect(timeoutInput.value).toBe('5000');

    timeoutInput.value = '0';
    timeoutInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(emitted).toBeUndefined();

    timeoutInput.value = '10000';
    timeoutInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(emitted).toEqual({ selector: '#loaded', timeoutMs: 10000 });
  });

  it('GetText form requires selector and a valid output variable name', () => {
    select('Web.GetText');
    let emitted: Record<string, unknown> | undefined;
    component.propertiesChange.subscribe((v) => (emitted = v));

    setValue(fixture, 'web-get-text-selector-input', '.result');
    setValue(fixture, 'web-get-text-output-variable-input', '1invalid');
    expect(emitted).toBeUndefined();

    setValue(fixture, 'web-get-text-output-variable-input', 'resultText');
    expect(emitted).toEqual({ selector: '.result', outputVariable: 'resultText' });
  });

  it('renders the expression editor with a variable-insert control for text-like fields', () => {
    select('Web.Navigate');
    const wrapper = fixture.nativeElement.querySelectorAll('[data-testid="expression-input"]');
    expect(wrapper.length).toBeGreaterThan(0);
    const insertBtn = fixture.nativeElement.querySelector('[data-testid="expression-insert-variable"]');
    expect(insertBtn).toBeTruthy();
  });

  it('pre-fills existing properties when editing an already-configured node', () => {
    select('Web.Click', { selector: '#existing', action: 'hover', waitSelector: '.loaded', timeoutMs: 9000 });
    const actionSelect: HTMLSelectElement = fixture.nativeElement.querySelector(
      '[data-testid="web-click-action-select"]',
    );
    const timeoutInput: HTMLInputElement = fixture.nativeElement.querySelector(
      '[data-testid="web-click-timeout-input"]',
    );
    const selectorInput: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="web-click-selector-input"]');
    expect(fixture.nativeElement.querySelector('[data-testid="web-click-step-item"]')).toBeFalsy();
    expect(selectorInput.value).toBe('#existing');
    expect(actionSelect.value).toBe('hover');
    expect(timeoutInput.value).toBe('9000');
  });
});
