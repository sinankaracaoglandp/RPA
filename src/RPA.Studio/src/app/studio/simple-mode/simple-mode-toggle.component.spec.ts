import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SimpleModeToggleComponent } from './simple-mode-toggle.component';
import { ModeService } from '../../shared/services/mode.service';

describe('SimpleModeToggleComponent', () => {
  let fixture: ComponentFixture<SimpleModeToggleComponent>;
  let component: SimpleModeToggleComponent;
  let modeService: ModeService;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [SimpleModeToggleComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(SimpleModeToggleComponent);
    component = fixture.componentInstance;
    modeService = TestBed.inject(ModeService);
    fixture.detectChanges();
  });

  it('defaults to Advanced mode active', () => {
    expect(component.isSimple()).toBe(false);
    const advancedBtn = fixture.nativeElement.querySelector('[data-testid="mode-toggle-advanced"]');
    expect(advancedBtn.getAttribute('aria-pressed')).toBe('true');
  });

  it('clicking the Simple button switches the mode', () => {
    const simpleBtn = fixture.nativeElement.querySelector('[data-testid="mode-toggle-simple"]');
    simpleBtn.click();
    fixture.detectChanges();

    expect(modeService.mode()).toBe('Simple');
    expect(component.isSimple()).toBe(true);
  });

  it('clicking Advanced after Simple switches back', () => {
    modeService.setMode('Simple');
    fixture.detectChanges();

    const advancedBtn = fixture.nativeElement.querySelector('[data-testid="mode-toggle-advanced"]');
    advancedBtn.click();
    fixture.detectChanges();

    expect(modeService.mode()).toBe('Advanced');
  });
});
