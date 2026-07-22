import { Directive, ElementRef, Injectable, Input, OnDestroy, inject } from '@angular/core';
import { StructuredItem, StructuredSequence } from '../structured-model';

/** Bırakma noktasının çözümü: hedef dizi + "hangi öğenin ÖNÜNE" (null → sona). */
export interface DropTarget {
  seq: StructuredSequence;
  anchor: StructuredItem | null;
  index: number;
}

/**
 * Bırakma hedefini CDK yerine DOM'dan çözer.
 *
 * **Neden:** CDK her drop-list'in ekran dikdörtgenini sürükleme BAŞINDA önbelleğe alır
 * (`DropListRef._domRect`) ve yalnız kaydırma olaylarında tazeler. Kök liste sıralama yaparken
 * konteyner kartını transform ile kaydırdığı an, o konteynerin lane'lerinin önbellekli
 * dikdörtgenleri gerçek konumdan kayar; `_canReceive` içindeki `isInsideClientRect` kapısı
 * bu yüzden lane'i asla kabul etmez → node bir `if`/`while`/`tryCatch` içine bırakılamaz.
 * Burada geometri her zaman CANLI okunur, dolayısıyla bayatlama mümkün değildir.
 */
@Injectable({ providedIn: 'root' })
export class DropZoneRegistry {
  private readonly zones = new Map<HTMLElement, StructuredSequence>();

  set(el: HTMLElement, seq: StructuredSequence): void { this.zones.set(el, seq); }
  delete(el: HTMLElement): void { this.zones.delete(el); }
  dataOf(el: HTMLElement): StructuredSequence | undefined { return this.zones.get(el); }

  /**
   * `x/y` noktasındaki bırakma hedefini bulur. `moving` sürüklenen (yer değiştirecek) öğelerdir;
   * çapa seçilirken atlanırlar — kendi eski yerlerini çapa göstermek taşımayı iptal ederdi.
   */
  resolve(x: number, y: number, moving: readonly StructuredItem[] = []): DropTarget | null {
    const hit = this.doc().elementFromPoint(x, y) as HTMLElement | null;
    const zoneEl = hit?.closest?.('[data-drop-zone]') as HTMLElement | null;
    if (!zoneEl) { return null; }
    const seq = this.zones.get(zoneEl);
    if (!seq) { return null; }

    // Kart elemanları dizideki sırayla birebir hizalıdır (CDK placeholder'ı sürüklenen öğenin
    // yerinde durur, sayıyı değiştirmez). Noktanın üstünde kalan ilk kart ekleme konumudur.
    const cards = Array.from(zoneEl.querySelectorAll(':scope > app-structured-item'));
    let index = cards.length;
    for (let i = 0; i < cards.length; i++) {
      const r = cards[i].getBoundingClientRect();
      if (y < r.top + r.height / 2) { index = i; break; }
    }
    while (index < seq.length && moving.includes(seq[index])) { index++; }
    return { seq, anchor: seq[index] ?? null, index };
  }

  /** Test edilebilirlik için ayrıldı (jsdom'da `document` global'dir). */
  protected doc(): Document { return document; }
}

/** Bir `cdkDropList`'i bırakma-hedefi kütüğüne kaydeder ve DOM'da işaretler. */
@Directive({
  selector: '[appDropZone]',
  standalone: true,
  host: { 'data-drop-zone': '' },
})
export class DropZoneDirective implements OnDestroy {
  private readonly el = inject(ElementRef) as ElementRef<HTMLElement>;
  private readonly registry = inject(DropZoneRegistry);

  @Input('appDropZone') set zoneData(value: StructuredSequence) {
    this.registry.set(this.el.nativeElement, value ?? []);
  }

  ngOnDestroy(): void { this.registry.delete(this.el.nativeElement); }
}
