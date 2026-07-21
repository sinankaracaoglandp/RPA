import { Injectable, signal } from '@angular/core';

/** Yapısal görünümde "Kontrol" tiplerini temsil eden sözde-kategori adı. */
export const CONTROL_CATEGORY = 'Kontrol';

/**
 * Yapısal palet ile ekleme menüleri arasında paylaşılan kategori filtresi. Palet bir kategoriye
 * tıklayınca yazar; ekleme menüsündeki aktivite dropdown'ı ve palet çipleri buna göre filtrelenir.
 * structured-view seviyesinde sağlanır → o görünümdeki tüm menüler aynı örneği paylaşır.
 */
@Injectable()
export class StructuredPaletteFilter {
  /** Seçili kategori; null = filtre yok (hepsi). */
  readonly category = signal<string | null>(null);

  /** Aynı kategoriye tekrar tıklama filtreyi kaldırır. */
  toggle(category: string): void {
    this.category.update((current) => (current === category ? null : category));
  }

  /** Dışarıdan (soldaki toolbox) doğrudan atama; toggle semantiği yok. */
  set(category: string | null): void {
    this.category.set(category);
  }

  clear(): void {
    this.category.set(null);
  }
}
