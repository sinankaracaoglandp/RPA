import { KeystrokeSequenceEditorComponent } from './keystroke-sequence-editor.component';

describe('KeystrokeSequenceEditorComponent', () => {
  let component: KeystrokeSequenceEditorComponent;

  beforeEach(() => {
    component = new KeystrokeSequenceEditorComponent();
  });

  it('parses incoming JSON value (chord + text)', () => {
    component.value = JSON.stringify([
      { type: 'chord', modifiers: ['ctrl'], key: 'A', waitMs: 0 },
      { type: 'text', text: '09.07.2026', waitMs: 100 },
    ]);

    expect(component.steps.length).toBe(2);
    expect(component.steps[0].type).toBe('chord');
    expect(component.steps[0].modifiers).toEqual(['ctrl']);
    expect(component.steps[0].key).toBe('A');
    expect(component.steps[1].type).toBe('text');
    expect(component.steps[1].text).toBe('09.07.2026');
    expect(component.steps[1].waitMs).toBe(100);
  });

  it('toggles the modifier on the clicked STEP, not the modifier position', () => {
    // Regresyon: şablonda iç içe @for vardı ve modifier checkbox'ı $index'i (modifier sırası)
    // adım index'i sanıp yanlış adımı değiştiriyordu. 5. adımda Ctrl işaretleyince 1. adım
    // değişiyor, 5. adım boş kalıyordu (kaydet+yenile sonrası "seçim kayboldu").
    component.value = JSON.stringify([
      { type: 'chord', modifiers: [], key: 'A', waitMs: 0 },
      { type: 'chord', modifiers: [], key: 'B', waitMs: 0 },
      { type: 'chord', modifiers: [], key: 'C', waitMs: 0 },
      { type: 'chord', modifiers: [], key: 'D', waitMs: 0 },
      { type: 'chord', modifiers: [], key: 'S', waitMs: 0 },
    ]);

    // 5. adıma (index 4) Ctrl; ayrıca 4. adıma (index 3) Alt — Alt modifier sırası 0 değil.
    component.toggleModifier(4, 'ctrl');
    component.toggleModifier(3, 'alt');

    expect(component.steps[4].modifiers).toEqual(['ctrl']);
    expect(component.steps[3].modifiers).toEqual(['alt']);
    // Diğer adımlara sızmamalı.
    expect(component.steps[0].modifiers).toEqual([]);
    expect(component.steps[2].modifiers).toEqual([]);
  });

  it('flags empty text steps so they are caught at design time', () => {
    component.value = JSON.stringify([
      { type: 'text', text: '{{fatura.FaturaNo}}', waitMs: 0 },
      { type: 'chord', modifiers: [], key: 'Enter', waitMs: 0 },
      { type: 'text', text: '   ', waitMs: 0 },
    ]);

    expect(component.isEmptyText(component.steps[0])).toBe(false);
    // Chord adımı metin doğrulamasına takılmamalı.
    expect(component.isEmptyText(component.steps[1])).toBe(false);
    // Boş/whitespace metin adımı çalışma anında BusinessException verir → işaretlenmeli.
    expect(component.isEmptyText(component.steps[2])).toBe(true);
  });

  it('parses legacy plain text as a single text step', () => {
    component.value = '09.07.2026';

    expect(component.steps.length).toBe(1);
    expect(component.steps[0].type).toBe('text');
    expect(component.steps[0].text).toBe('09.07.2026');
  });

  it('adds a chord step by default', () => {
    component.addStep();

    expect(component.steps.length).toBe(1);
    expect(component.steps[0].type).toBe('chord');
  });

  it('toggles a modifier on and off', () => {
    component.addStep();
    component.setKey(0, 'A');

    component.toggleModifier(0, 'ctrl');
    expect(component.steps[0].modifiers).toContain('ctrl');

    component.toggleModifier(0, 'ctrl');
    expect(component.steps[0].modifiers).not.toContain('ctrl');
  });

  it('emits chord JSON with modifiers and key', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe((v) => emitted.push(v));

    component.addStep();
    component.toggleModifier(0, 'ctrl');
    component.toggleModifier(0, 'shift');
    component.setKey(0, 'End');

    const last = JSON.parse(emitted[emitted.length - 1]);
    expect(last).toEqual([{ type: 'chord', modifiers: ['ctrl', 'shift'], key: 'End', waitMs: 0 }]);
  });

  it('emits text JSON when step type is text', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe((v) => emitted.push(v));

    component.addStep();
    component.setType(0, 'text');
    component.setText(0, 'hello');

    const last = JSON.parse(emitted[emitted.length - 1]);
    expect(last).toEqual([{ type: 'text', text: 'hello', waitMs: 0 }]);
  });

  it('removes a step', () => {
    component.value = JSON.stringify([
      { type: 'chord', key: 'A' },
      { type: 'chord', key: 'B' },
    ]);

    component.removeStep(0);

    expect(component.steps.length).toBe(1);
    expect(component.steps[0].key).toBe('B');
  });

  it('builds a human-readable preview label', () => {
    expect(component.previewOf({ type: 'chord', modifiers: ['ctrl', 'shift'], key: 'End', text: '', waitMs: 0 }))
      .toBe('Ctrl + Shift + End');
  });
});
