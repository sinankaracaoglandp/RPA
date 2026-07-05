import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CanvasNodeView, NodeComponent } from './node.component';

describe('NodeComponent', () => {
  let fixture: ComponentFixture<NodeComponent>;
  let component: NodeComponent;

  const view: CanvasNodeView = {
    id: 'node-1',
    label: 'Click Element',
    nodeType: 'activity',
    activityId: 'Web.Click',
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NodeComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(NodeComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('node', view);
    fixture.detectChanges();
  });

  it('renders the node title and activity id', () => {
    const title = fixture.nativeElement.querySelector('[data-testid="canvas-node-title"]');
    expect(title.textContent).toContain('Click Element');
    expect(fixture.nativeElement.textContent).toContain('Web.Click');
  });

  it('emits nodeSelect on click', () => {
    let selected: string | undefined;
    component.nodeSelect.subscribe((id) => (selected = id));
    fixture.nativeElement.querySelector('[data-testid="canvas-node"]').click();
    expect(selected).toBe('node-1');
  });

  it('emits nodeDelete when the delete button is clicked', () => {
    let deleted: string | undefined;
    component.nodeDelete.subscribe((id) => (deleted = id));
    fixture.nativeElement.querySelector('[data-testid="canvas-node-delete"]').click();
    expect(deleted).toBe('node-1');
  });

  it('reflects the selected state via aria-selected', () => {
    fixture.componentRef.setInput('node', { ...view, selected: true });
    fixture.detectChanges();
    const el = fixture.nativeElement.querySelector('[data-testid="canvas-node"]');
    expect(el.getAttribute('aria-selected')).toBe('true');
  });
});
