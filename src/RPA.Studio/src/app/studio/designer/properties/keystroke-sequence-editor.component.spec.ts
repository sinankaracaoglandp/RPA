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
