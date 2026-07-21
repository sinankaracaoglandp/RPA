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

    // SAP seçimi onay tuşuyla verilir (tıklama SAP'ta alanı/butonu tetiklerdi) → tuş
    // seçenekleri gönderilir. Ekran dondurma SAP'ta yok; captureMode varsayılan kalır.
    expect(spy.pick).toHaveBeenCalledWith('sap', {
      captureMode: 'f2',
      delaySeconds: 5,
      hotKey: 'T',
      ctrl: true,
      shift: false,
      alt: false,
    });
    expect(emitted).toEqual(['wnd[0]/usr/btn[OK]']);
  });

  it('passes image capture options (mode + delay) to SpyService.pick', async () => {
    component.pickerKind = 'image';
    spy.pick.mockResolvedValue({ sessionId: 's1', kind: 'image', elementId: 'image', imageBase64: 'B64' });
    component.onModeChange('timer');
    component.onDelayChange(10);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-testid="selector-picker"]').click();
    await fixture.whenStable();

    expect(spy.pick).toHaveBeenCalledWith('image', {
      captureMode: 'timer',
      delaySeconds: 10,
      hotKey: 'F2',
      ctrl: false,
      shift: false,
      alt: false,
    });
  });

  it('passes a custom freeze hotkey with modifiers to SpyService.pick', async () => {
    component.pickerKind = 'image';
    spy.pick.mockResolvedValue({ sessionId: 's1', kind: 'image', elementId: 'image', imageBase64: 'B64' });
    component.onModeChange('f2');
    component.onHotKeyChange('F9');
    component.ctrl.set(true);
    fixture.detectChanges();

    fixture.nativeElement.querySelector('[data-testid="selector-picker"]').click();
    await fixture.whenStable();

    expect(spy.pick).toHaveBeenCalledWith('image', {
      captureMode: 'f2',
      delaySeconds: 5,
      hotKey: 'F9',
      ctrl: true,
      shift: false,
      alt: false,
    });
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
