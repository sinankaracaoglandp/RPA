import { DropZoneRegistry } from './drop-zone';
import { step } from '../structured-model';
import { WorkflowNode } from '../../../../shared/models/workflow.model';

const n = (id: string): WorkflowNode => ({ id, type: 'activity', activity: 'X' });

/** `top`/`height` dışındaki alanlar çözümlemede kullanılmaz. */
function card(top: number, height: number): HTMLElement {
  const el = document.createElement('app-structured-item');
  el.getBoundingClientRect = () => ({ top, height, bottom: top + height, left: 0, right: 0, width: 0, x: 0, y: top, toJSON: () => ({}) });
  return el;
}

describe('DropZoneRegistry', () => {
  let hit: HTMLElement | null = null;

  beforeEach(() => {
    hit = null;
    // jsdom'da `elementFromPoint` yoktur (layout motoru yok) → doğrudan tanımlanır.
    (document as unknown as { elementFromPoint: () => Element | null }).elementFromPoint = () => hit;
  });

  function zone(cards: HTMLElement[]): HTMLElement {
    const el = document.createElement('section');
    el.setAttribute('data-drop-zone', '');
    cards.forEach((c) => el.appendChild(c));
    document.body.appendChild(el);
    return el;
  }

  it('resolves the zone under the point even when it is a nested lane', () => {
    const reg = new DropZoneRegistry();
    const seq = [step(n('a'))];
    const lane = zone([card(100, 40)]);
    reg.set(lane, seq);
    hit = lane.firstElementChild as HTMLElement; // imlecin altındaki derin eleman

    const t = reg.resolve(10, 105, []);

    expect(t?.seq).toBe(seq);
  });

  it('inserts before the card whose midpoint is below the point', () => {
    const reg = new DropZoneRegistry();
    const a = step(n('a')); const b = step(n('b')); const c = step(n('c'));
    const el = zone([card(0, 40), card(40, 40), card(80, 40)]);
    reg.set(el, [a, b, c]);
    hit = el;

    expect(reg.resolve(0, 10, [])!.anchor).toBe(a);   // 1. kartın üst yarısı
    expect(reg.resolve(0, 50, [])!.anchor).toBe(b);   // 2. kartın üst yarısı
    expect(reg.resolve(0, 130, [])!.anchor).toBeNull(); // hepsinin altı → sona
    expect(reg.resolve(0, 130, [])!.index).toBe(3);
  });

  it('skips the dragged items when picking the anchor', () => {
    const reg = new DropZoneRegistry();
    const a = step(n('a')); const b = step(n('b'));
    const el = zone([card(0, 40), card(40, 40)]);
    reg.set(el, [a, b]);
    hit = el;

    // `a`nın üstüne bırakıldı ama taşınan zaten `a` → çapa bir SONRAKİ öğedir.
    expect(reg.resolve(0, 10, [a])!.anchor).toBe(b);
  });

  it('returns null when the point is not over any registered zone', () => {
    const reg = new DropZoneRegistry();
    hit = document.createElement('div');
    expect(reg.resolve(0, 0, [])).toBeNull();
  });
});
