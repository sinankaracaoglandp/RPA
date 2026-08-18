import { TestBed } from '@angular/core/testing';
import { StructuredSequenceComponent } from './structured-sequence.component';
import { step } from '../structured-model';

describe('StructuredSequenceComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [StructuredSequenceComponent] }));

  it('renders one item element per sequence entry', () => {
    const f = TestBed.createComponent(StructuredSequenceComponent);
    f.componentRef.setInput('items', [
      step({ id: 'a', type: 'activity', activity: 'A' }),
      step({ id: 'b', type: 'activity', activity: 'B' }),
    ]);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelectorAll('app-structured-item').length).toBe(2);
  });

  it('shows an empty hint for an empty sequence', () => {
    const f = TestBed.createComponent(StructuredSequenceComponent);
    f.componentRef.setInput('items', []);
    f.detectChanges();
    expect((f.nativeElement as HTMLElement).querySelector('[data-testid="sequence-empty"]')).toBeTruthy();
  });
});
