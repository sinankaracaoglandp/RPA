import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ExpressionInputComponent } from './expression-input.component';
import { ExpressionFunctionInfo, ExpressionFunctionService } from '../../../shared/services/expression-function.service';

function fnInfo(name: string, category: string): ExpressionFunctionInfo {
  return { name, category, returnType: 'string', parameters: [], description: '', example: `${name}()` };
}

describe('ExpressionInputComponent autocomplete', () => {
  let fixture: ComponentFixture<ExpressionInputComponent>;
  let component: ExpressionInputComponent;
  const fnService = {
    load: () => ({ subscribe: () => undefined }),
    filter: (prefix: string) =>
      [fnInfo('Format', 'Tarih'), fnInfo('Upper', 'Metin')].filter((f) =>
        f.name.toLowerCase().startsWith(prefix.toLowerCase()),
      ),
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExpressionInputComponent],
      providers: [{ provide: ExpressionFunctionService, useValue: fnService }],
    }).compileComponents();
    fixture = TestBed.createComponent(ExpressionInputComponent);
    component = fixture.componentInstance;
    component.inputId = 'expr';
    component.label = 'Ifade';
    component.variables = [{ name: 'ad', type: 'string' } as never];
  });

  it('suggests matching functions and variables for a partial word', () => {
    component.updateSuggestions('Up');
    expect(component.suggestions.some((s) => s.kind === 'function' && s.label === 'Upper')).toBe(true);
    expect(component.suggestionsOpen).toBe(true);
  });

  it('suggests variables by partial name', () => {
    component.updateSuggestions('a');
    expect(component.suggestions.some((s) => s.kind === 'variable' && s.label === 'ad')).toBe(true);
  });

  it('inserting a function replaces the trailing partial word with Name()', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe((v) => emitted.push(v));
    // Kullanıcı "x = Up" yazdı; öneri son kelime "Up"a göre açıldı.
    component.value = 'x = Up';
    component.updateSuggestions('Up');
    const upper = component.suggestions.find((s) => s.label === 'Upper')!;
    component.applySuggestion(upper);
    // "Up" silinip "Upper()" ile değişmeli → "x = Upper()" (UpUpper() DEĞİL).
    expect(emitted[emitted.length - 1]).toBe('x = Upper()');
  });

  it('Escape closes the suggestion list', () => {
    component.updateSuggestions('Up');
    component.onKeydown(new KeyboardEvent('keydown', { key: 'Escape' }));
    expect(component.suggestionsOpen).toBe(false);
  });
});
