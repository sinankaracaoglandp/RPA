import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EnvironmentsComponent } from './environments.component';
import { Environment } from '../orchestrator.models';

describe('EnvironmentsComponent', () => {
  let fixture: ComponentFixture<EnvironmentsComponent>;
  let httpMock: HttpTestingController;

  const envs: Environment[] = [
    { id: 'e1', name: 'Dev', description: '' },
    { id: 'e2', name: 'Prod', description: 'Canlı' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EnvironmentsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(EnvironmentsComponent);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders one row per environment', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/environments').flush(envs);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="env-row"]');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Dev');
    expect(rows[1].textContent).toContain('Canlı');
  });

  it('posts a new environment then reloads', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/environments').flush(envs);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.newName = 'Staging';
    component.create();

    const post = httpMock.expectOne(
      (r) => r.method === 'POST' && r.url === '/api/environments',
    );
    expect(post.request.body).toEqual({ name: 'Staging', description: undefined });
    post.flush({ id: 'e3', name: 'Staging', description: '' });

    // reload after create
    httpMock.expectOne('/api/environments').flush([...envs, { id: 'e3', name: 'Staging', description: '' }]);
  });

  it('shows an error when load fails', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/environments').error(new ProgressEvent('fail'));
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeTruthy();
  });
});
