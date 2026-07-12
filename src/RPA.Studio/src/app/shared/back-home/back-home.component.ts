import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';

/**
 * Ana sayfaya (Dashboard, route '/') dönüş için ortak, tutarlı bir gezinme düğmesi.
 * Ana sayfaya bağlı tüm alt ekranların başlığına yerleştirilir; böylece kullanıcı
 * her ekrandan tek tıkla dashboard'a dönebilir.
 *
 * NOT: RouterLink yerine gerçek <a href> + Router.navigate kullanılır; Router opsiyonel
 * enjekte edilir, böylece bu bileşeni render eden birim testleri router provider'ı
 * sağlamak zorunda kalmaz (sağ tık → yeni sekmede aç için href korunur).
 */
@Component({
  selector: 'app-back-home',
  standalone: true,
  template: `
    <a
      href="/"
      (click)="navigateHome($event)"
      class="inline-flex items-center gap-1.5 rounded-md border border-gray-200 bg-white px-3 py-1.5 text-sm font-medium text-gray-600 shadow-sm transition-colors hover:bg-gray-50 hover:text-gray-900"
      data-testid="back-home"
      aria-label="Ana sayfaya dön"
    >
      <svg
        xmlns="http://www.w3.org/2000/svg"
        fill="none"
        viewBox="0 0 24 24"
        stroke-width="1.8"
        stroke="currentColor"
        class="h-4 w-4"
        aria-hidden="true"
      >
        <path
          stroke-linecap="round"
          stroke-linejoin="round"
          d="m2.25 12 8.954-8.955c.44-.439 1.152-.439 1.591 0L21.75 12M4.5 9.75v10.125c0 .621.504 1.125 1.125 1.125H9.75v-4.875c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125V21h4.125c.621 0 1.125-.504 1.125-1.125V9.75M8.25 21h8.25"
        />
      </svg>
      Ana sayfa
    </a>
  `,
})
export class BackHomeComponent {
  private readonly router = inject(Router, { optional: true });

  navigateHome(event: MouseEvent): void {
    // Modifier'lı tık (yeni sekme) veya router yoksa tarayıcının varsayılan href davranışına bırak.
    if (event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0 || !this.router) {
      return;
    }
    event.preventDefault();
    void this.router.navigateByUrl('/');
  }
}
