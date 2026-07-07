import { ComponentFixture, TestBed } from '@angular/core/testing';
import { SelectorPickerButtonComponent } from './selector-picker-button.component';
import { SpyService } from '../../../shared/services/spy.service';

class FakeSpyService {
  pick = vi.fn();
}

describe('SelectorPickerButtonComponent', () => {
  let fixture: ComponentFixture<SelectorPickerButtonComponent>;
  let component: SelectorPickerButtonComponent;
  let spy: FakeSpyService;

  beforeEach(async () => {
    spy = new FakeSpyService();
    await TestBed.configureTestingModule({
      imports: [SelectorPickerButtonComponent],
      providers: [{ provide: SpyService, useValue: spy }],
    }).compileComponents();

    fixture = TestBed.createComponent(SelectorPickerButtonComponent);
    component = fixture.componentInstance;
    component.pickerKind = 'sap';
  });

  it('calls SpyService.pick when clicked and emits the selected element', async () => {
    spy.pick.mockResolvedValue({
      sessionId: 's1',
      kind: 'sap',
      elementId: 'wnd[0]/usr/btn[OK]',
    });
    const emitted: string[] = [];
    component.picked.subscribe((element) => emitted.push(element.elementId));
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-testid="selector-picker"]').click();
    await fixture.whenStable();

    expect(spy.pick).toHaveBeenCalledWith('sap');
    expect(emitted).toEqual(['wnd[0]/usr/btn[OK]']);
  });

  it('shows an error state when picking fails', async () => {
    spy.pick.mockRejectedValue(new Error('timeout'));
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-testid="selector-picker"]').click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="selector-picker-error"]')).toBeTruthy();
  });
});
