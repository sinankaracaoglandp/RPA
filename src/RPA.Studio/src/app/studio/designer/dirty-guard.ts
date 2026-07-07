import { inject } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { TranslationService } from '../../core/translation.service';
import { DesignerComponent } from './designer.component';

/** Kaydedilmemiş değişiklik varsa ayrılmadan önce onay ister (Paket B). */
export const dirtyGuard: CanDeactivateFn<DesignerComponent> = (component) => {
  if (!component.dirty()) {
    return true;
  }
  const translation = inject(TranslationService);
  return window.confirm(translation.translate('designer.unsavedConfirm'));
};
