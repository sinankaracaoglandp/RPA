import { TestBed } from '@angular/core/testing';
import { StructuredItemComponent, StructuredAction } from './structured-item.component';
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

  it('emits delete action carrying the item reference when editable', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    const item = step({ id: 's', type: 'activity', activity: 'A' });
    f.componentRef.setInput('item', item);
    f.componentRef.setInput('editable', true);
    f.detectChanges();
    const events: StructuredAction[] = [];
    f.componentInstance.action.subscribe((e) => events.push(e));
    (f.nativeElement.querySelector('[data-testid="item-delete"]') as HTMLButtonElement).click();
    expect(events[0]).toEqual({ kind: 'delete', target: item });
  });

  it('does not render edit controls when not editable', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', step({ id: 's', type: 'activity', activity: 'A' }));
    f.componentRef.setInput('editable', false);
    f.detectChanges();
    expect(f.nativeElement.querySelector('[data-testid="item-delete"]')).toBeFalsy();
  });

  it('renders lanes as cdkDropList and items as cdkDrag when editable', () => {
    const f = TestBed.createComponent(StructuredItemComponent);
    f.componentRef.setInput('item', container('forEach', {}, { body: [step({ id: 'b', type: 'activity', activity: 'A' })] }));
    f.componentRef.setInput('editable', true);
    f.detectChanges();
    expect(f.nativeElement.querySelector('.cdk-drop-list')).toBeTruthy();
    expect(f.nativeElement.querySelector('.cdk-drag')).toBeTruthy();
  });
});
