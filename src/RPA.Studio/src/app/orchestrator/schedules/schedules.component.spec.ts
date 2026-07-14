import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { SchedulesComponent } from './schedules.component';
import { OrchestratorService } from '../orchestrator.service';

describe('SchedulesComponent', () => {
  function setup(triggers: any[]) {
    const svc = {
      listTriggers: () => of(triggers),
      listRobots: () => of([]),
      createTrigger: () => of({}),
      updateTrigger: () => of({}),
      fireTrigger: () => of({}),
    };
    TestBed.configureTestingModule({
      imports: [SchedulesComponent],
      providers: [{ provide: OrchestratorService, useValue: svc }],
    });
    return TestBed.createComponent(SchedulesComponent);
  }

  it('yüklenince trigger listesini gösterir', () => {
    const fixture = setup([
      { id: 't1', type: 'Cron', targetRobotTags: 'prod-vm', isActive: true, priority: 0, configuration: '{}' },
    ]);
    fixture.detectChanges();
    const cmp = fixture.componentInstance;
    expect(cmp.triggers().length).toBe(1);
    expect(cmp.triggers()[0].targetRobotTags).toBe('prod-vm');
  });
});
