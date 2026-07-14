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

  it('save() form verisiyle createTrigger çağırır, listeyi yeniler ve formu kapatır', () => {
    const createTrigger = vi.fn().mockReturnValue(of({}));
    const listTriggers = vi.fn().mockReturnValue(of([]));
    const svc = {
      listTriggers,
      listRobots: () => of([]),
      createTrigger,
      updateTrigger: () => of({}),
      fireTrigger: () => of({}),
    };
    TestBed.configureTestingModule({
      imports: [SchedulesComponent],
      providers: [{ provide: OrchestratorService, useValue: svc }],
    });
    const fixture = TestBed.createComponent(SchedulesComponent);
    fixture.detectChanges();
    const cmp = fixture.componentInstance;
    cmp.showForm.set(true);
    listTriggers.mockClear();

    cmp.save();

    expect(createTrigger).toHaveBeenCalledWith(cmp.form);
    expect(listTriggers).toHaveBeenCalled();
    expect(cmp.showForm()).toBe(false);
  });

  it('setActive(t, false) updateTrigger\'ı trigger id ve isActive:false ile çağırır', () => {
    const updateTrigger = vi.fn().mockReturnValue(of({}));
    const svc = {
      listTriggers: () => of([]),
      listRobots: () => of([]),
      createTrigger: () => of({}),
      updateTrigger,
      fireTrigger: () => of({}),
    };
    TestBed.configureTestingModule({
      imports: [SchedulesComponent],
      providers: [{ provide: OrchestratorService, useValue: svc }],
    });
    const fixture = TestBed.createComponent(SchedulesComponent);
    fixture.detectChanges();
    const cmp = fixture.componentInstance;
    const trigger = { id: 't1', type: 'Cron', targetRobotTags: 'prod-vm', isActive: true, priority: 0, configuration: '{}' } as any;

    cmp.setActive(trigger, false);

    expect(updateTrigger).toHaveBeenCalledWith('t1', { isActive: false });
  });

  it('runNow(t) fireTrigger\'ı trigger id ile çağırır', () => {
    const fireTrigger = vi.fn().mockReturnValue(of({}));
    const svc = {
      listTriggers: () => of([]),
      listRobots: () => of([]),
      createTrigger: () => of({}),
      updateTrigger: () => of({}),
      fireTrigger,
    };
    TestBed.configureTestingModule({
      imports: [SchedulesComponent],
      providers: [{ provide: OrchestratorService, useValue: svc }],
    });
    const fixture = TestBed.createComponent(SchedulesComponent);
    fixture.detectChanges();
    const cmp = fixture.componentInstance;
    const trigger = { id: 't1', type: 'Cron', targetRobotTags: 'prod-vm', isActive: true, priority: 0, configuration: '{}' } as any;

    cmp.runNow(trigger);

    expect(fireTrigger).toHaveBeenCalledWith('t1');
  });
});
