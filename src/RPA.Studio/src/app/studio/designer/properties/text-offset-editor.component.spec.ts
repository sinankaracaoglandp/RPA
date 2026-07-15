import { TextOffsetEditorComponent } from './text-offset-editor.component';

describe('TextOffsetEditorComponent', () => {
  let component: TextOffsetEditorComponent;

  beforeEach(() => {
    component = new TextOffsetEditorComponent();
  });

  it('parses incoming JSON value', () => {
    component.value = '{"anchorText":"Malzeme No","dx":120,"dy":-4}';
    expect(component.anchorText).toBe('Malzeme No');
    expect(component.dx).toBe(120);
    expect(component.dy).toBe(-4);
  });

  it('emits JSON on field change', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe((v) => emitted.push(v));
    component.anchorText = 'Miktar';
    component.dx = 50;
    component.dy = 0;
    component.emit();
    expect(JSON.parse(emitted[emitted.length - 1])).toEqual({ anchorText: 'Miktar', dx: 50, dy: 0 });
  });

  it('applies picker result (anchorText + dx/dy)', () => {
    const emitted: string[] = [];
    component.valueChange.subscribe((v) => emitted.push(v));
    component.onPicked({ sessionId: 's', kind: 'text-offset', elementId: 'text-offset', anchorText: 'Tutar', dx: 200, dy: 10 });
    expect(JSON.parse(emitted[emitted.length - 1])).toEqual({ anchorText: 'Tutar', dx: 200, dy: 10 });
  });
});
