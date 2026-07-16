import { ComponentFixture, TestBed } from '@angular/core/testing';
import { VariablesPanelComponent } from './variables-panel.component';

describe('VariablesPanelComponent', () => {
  let fixture: ComponentFixture<VariablesPanelComponent>;
  let component: VariablesPanelComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [VariablesPanelComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(VariablesPanelComponent);
    component = fixture.componentInstance;
  });

  it('adds a global string variable and emits the updated list', () => {
    let emitted: unknown;
    component.variablesChange.subscribe((value) => (emitted = value));

    fixture.detectChanges();
    fixture.nativeElement.querySelector('[data-testid="variables-add"]').click();
    fixture.detectChanges();

    const nameInput = fixture.nativeElement.querySelector('[data-testid="variable-name-0"]') as HTMLInputElement;
    nameInput.value = 'okunanMetin';
    nameInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(emitted).toEqual([
      { name: 'okunanMetin', type: 'string', scope: 'global', default: '' },
    ]);
  });

  it('updates type and default value for an existing variable', () => {
    fixture.componentRef.setInput('variables', [
      { name: 'toplam', type: 'string', scope: 'global', default: '' },
    ]);
    let emitted: unknown;
    component.variablesChange.subscribe((value) => (emitted = value));
    fixture.detectChanges();

    const typeSelect = fixture.nativeElement.querySelector('[data-testid="variable-type-0"]') as HTMLSelectElement;
    typeSelect.value = 'int';
    typeSelect.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const defaultInput = fixture.nativeElement.querySelector('[data-testid="variable-default-0"]') as HTMLInputElement;
    defaultInput.value = '42';
    defaultInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(emitted).toEqual([
      { name: 'toplam', type: 'int', scope: 'global', default: 42 },
    ]);
  });

  it('removes a variable', () => {
    fixture.componentRef.setInput('variables', [
      { name: 'okunanMetin', type: 'string', scope: 'global', default: '' },
    ]);
    let emitted: unknown;
    component.variablesChange.subscribe((value) => (emitted = value));
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-testid="variable-remove-0"]').click();
    fixture.detectChanges();

    expect(emitted).toEqual([]);
  });

  it('supports DateTime variables with a datetime-local default editor', () => {
    fixture.componentRef.setInput('variables', [
      { name: 'planlananTarih', type: 'DateTime', scope: 'global', default: '2026-07-09T10:30:00.000Z' },
    ]);
    let emitted: unknown;
    component.variablesChange.subscribe((value) => (emitted = value));
    fixture.detectChanges();

    const defaultInput = fixture.nativeElement.querySelector('[data-testid="variable-default-0"]') as HTMLInputElement;
    expect(defaultInput.type).toBe('datetime-local');
    expect(defaultInput.value).toBe('2026-07-09T10:30');

    defaultInput.value = '2026-07-10T14:45';
    defaultInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(emitted).toEqual([
      { name: 'planlananTarih', type: 'DateTime', scope: 'global', default: '2026-07-10T14:45:00.000Z' },
    ]);
  });

  it('offers collection item fields inside foreach from schema-aware variables', () => {
    fixture.componentRef.setInput('variables', [
      {
        name: 'fatura',
        type: 'object',
        scope: 'global',
        schema: {
          type: 'object',
          properties: {
            faturaNo: { type: 'string' },
            satirlar: {
              type: 'array',
              items: {
                type: 'object',
                properties: {
                  MalzemeKodu: { type: 'string' },
                  Aciklama: { type: 'string' },
                },
              },
            },
          },
        },
      },
    ]);

    expect(component.variablePathsFor('fatura.satirlar')).toContain('satir.MalzemeKodu');
    expect(component.variablePathsFor('fatura')).toContain('fatura.faturaNo');
    expect(component.variablePathsFor('fatura')).toContain('fatura.satirlar');
  });
});
