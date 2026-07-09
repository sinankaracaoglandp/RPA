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
    let selected: { nodeId: string; additive: boolean } | undefined;
    component.nodeSelect.subscribe((event) => (selected = event));
    fixture.nativeElement.querySelector('[data-testid="canvas-node"]').click();
    expect(selected).toEqual({ nodeId: 'node-1', additive: false });
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

  it('emits connectStart on pointerdown at the out socket', () => {
    const emitted: Array<{ nodeId: string; port: string }> = [];
    component.connectStart.subscribe((event) => emitted.push(event));
    fixture.detectChanges();

    const outSocket: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="canvas-node-socket-out"]');
    outSocket.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));

    expect(emitted).toEqual([{ nodeId: component.node.id, port: 'out' }]);
  });

  it('emits connectStart even when an ancestor swallows bubbling pointerdown (Rete drag simulation)', () => {
    // rete-area-plugin mounts NodeComponent directly onto its own node-host
    // element (createComponent hostElement) and attaches a bubble-phase
    // pointerdown listener there that calls stopPropagation() once it fires
    // (Drag.down). Wrap the fixture root in such an ancestor to reproduce
    // the real DOM shape and assert the socket still wins the race via the
    // capture-phase safeguard in ngAfterViewInit.
    const ancestor = document.createElement('div');
    ancestor.appendChild(fixture.nativeElement);
    document.body.appendChild(ancestor);
    let ancestorSaw = false;
    ancestor.addEventListener('pointerdown', (e) => {
      ancestorSaw = true;
      e.stopPropagation();
    });

    const emitted: Array<{ nodeId: string; port: string }> = [];
    component.connectStart.subscribe((event) => emitted.push(event));
    fixture.detectChanges();

    const outSocket: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="canvas-node-socket-out"]');
    outSocket.dispatchEvent(new MouseEvent('pointerdown', { bubbles: true }));

    expect(emitted).toEqual([{ nodeId: component.node.id, port: 'out' }]);
    expect(ancestorSaw).toBe(false);

    ancestor.removeChild(fixture.nativeElement);
    document.body.removeChild(ancestor);
  });

  it('emits connectDrop on pointerup over the card', () => {
    const emitted: string[] = [];
    component.connectDrop.subscribe((id) => emitted.push(id));
    fixture.detectChanges();

    const card: HTMLElement =
      fixture.nativeElement.querySelector('[data-testid="canvas-node"]');
    card.dispatchEvent(new MouseEvent('pointerup', { bubbles: true }));

    expect(emitted).toEqual([component.node.id]);
  });
});
