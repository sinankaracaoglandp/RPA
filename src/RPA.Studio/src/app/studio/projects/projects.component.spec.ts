import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { ProjectsComponent } from './projects.component';

describe('ProjectsComponent', () => {
  let fixture: ComponentFixture<ProjectsComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ProjectsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(ProjectsComponent);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists project cards with workflow counts', () => {
    fixture.detectChanges();
    http.expectOne('/api/projects').flush([
      { id: 'p1', name: 'Pilot', description: 'd', workflowCount: 2 },
    ]);
    fixture.detectChanges();

    const cards = fixture.nativeElement.querySelectorAll('[data-testid="project-card"]');
    expect(cards.length).toBe(1);
    expect(cards[0].textContent).toContain('Pilot');
  });

  it('creates a project and refreshes the list', () => {
    fixture.detectChanges();
    http.expectOne('/api/projects').flush([]);
    fixture.detectChanges();

    fixture.componentInstance.newProjectName.set('Yeni');
    fixture.componentInstance.createProject();

    const post = http.expectOne(
      (r) => r.url === '/api/projects' && r.method === 'POST',
    );
    post.flush({ id: 'p2', name: 'Yeni', workflowCount: 0 });
    http.expectOne('/api/projects').flush([{ id: 'p2', name: 'Yeni', workflowCount: 0 }]);
    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelectorAll('[data-testid="project-card"]').length,
    ).toBe(1);
  });

  it('loads workflows when a project is opened', () => {
    fixture.detectChanges();
    http.expectOne('/api/projects').flush([{ id: 'p1', name: 'Pilot', workflowCount: 1 }]);
    fixture.detectChanges();

    fixture.componentInstance.openProject('p1');
    http.expectOne('/api/projects/p1/workflows').flush([
      { id: 'w1', name: 'Sipariş', updatedAt: '2026-07-07T00:00:00Z' },
    ]);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="workflow-row"]');
    expect(rows.length).toBe(1);
  });

  it('navigates to the designer when a workflow is opened', () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    fixture.detectChanges();
    http.expectOne('/api/projects').flush([]);

    fixture.componentInstance.openWorkflow('w1');

    expect(navigate).toHaveBeenCalledWith(['/designer', 'w1']);
  });
});
