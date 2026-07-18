import { TestBed } from '@angular/core/testing';
import { StructuredItemComponent } from './structured-item.component';
import { step, container } from '../structured-model';

describe('StructuredItemComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [StructuredItemComponent] }));

  it('renders a step card with title and activity id', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', step({ id: 'n1', type: 'activity', activity: 'Web.Click' }));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="structured-step"]')).toBeTruthy();
    expect(el.textContent).toContain('Web.Click');
  });

  it('renders a container box with a type label and lane sections', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', container('if', { condition: '{{c}} == 1' }, {
      true: [step({ id: 't', type: 'activity', activity: 'A' })], false: [],
    }));
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="structured-container"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="lane-true"]')).toBeTruthy();
    expect(el.querySelector('[data-testid="lane-false"]')).toBeTruthy();
  });
});
