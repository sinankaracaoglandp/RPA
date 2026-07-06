import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RobotsComponent } from './robots.component';
import { Robot } from '../orchestrator.models';

describe('RobotsComponent', () => {
  let fixture: ComponentFixture<RobotsComponent>;
  let httpMock: HttpTestingController;

  const robots: Robot[] = [
    { id: 'r1', machineName: 'RPA-01', mode: 'Unattended', status: 'Online', agentVersion: '1.0.0' },
    { id: 'r2', machineName: 'RPA-02', mode: 'Attended', status: 'Offline' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RobotsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(RobotsComponent);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('renders one row per robot', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/robots').flush(robots);
    fixture.detectChanges();

    const rows = fixture.nativeElement.querySelectorAll('[data-testid="robot-row"]');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('RPA-01');
    expect(rows[1].textContent).toContain('Offline');
  });

  it('shows an error when robots fail to load', () => {
    fixture.detectChanges();
    httpMock.expectOne('/api/robots').error(new ProgressEvent('fail'));
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeTruthy();
  });
});
