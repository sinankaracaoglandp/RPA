import { provideRouter } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DashboardComponent } from './dashboard.component';
import { AuthService } from '../auth/auth.service';
import { TranslationService } from '../core/translation.service';

describe('DashboardComponent', () => {
  let fixture: ComponentFixture<DashboardComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            getUsername: () => 'sinan',
            getRoles: () => ['Admin'],
            logout: () => undefined,
          },
        },
        {
          provide: TranslationService,
          useValue: {
            currentLang: () => 'tr',
            translate: (key: string) =>
              key === 'dashboard.einvoiceAddressingTitle'
                ? 'E-Fatura Adresleme'
                : key === 'dashboard.einvoiceAddressingDesc'
                  ? 'XML alanlarını profil değişkenlerine bağlayın'
                  : key,
            use: () => Promise.resolve(),
          },
        },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
  });

  it('shows e-invoice addressing as a separate studio entry from the home page', () => {
    const component = fixture.componentInstance;

    const addressingCard = component.studioCards.find((card) => card.route === '/einvoice-addressing');

    expect(addressingCard?.titleKey).toBe('dashboard.einvoiceAddressingTitle');
    expect(addressingCard?.descKey).toBe('dashboard.einvoiceAddressingDesc');
    expect(fixture.nativeElement.textContent).toContain('E-Fatura Adresleme');
  });
});
