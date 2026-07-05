import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ConnectionComponent } from './connection.component';

describe('ConnectionComponent', () => {
  let fixture: ComponentFixture<ConnectionComponent>;
  let component: ConnectionComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ConnectionComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ConnectionComponent);
    component = fixture.componentInstance;
  });

  it('builds a cubic Bézier path between two points', () => {
    const path = ConnectionComponent.buildPath({ x: 0, y: 0 }, { x: 100, y: 200 });
    expect(path.startsWith('M 0 0 C')).toBe(true);
    expect(path.endsWith('100 200')).toBe(true);
  });

  it('renders the path from start/end inputs', () => {
    component.id = 'c1';
    component.start = { x: 10, y: 10 };
    component.end = { x: 50, y: 90 };
    fixture.detectChanges();
    const path = fixture.nativeElement.querySelector('[data-testid="canvas-connection-path"]');
    expect(path.getAttribute('d')).toContain('M 10 10 C');
  });
});
