# E-Fatura Adresleme Çalışma Alanı Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ana sayfada ayrı “E-Fatura Adresleme” çalışma alanı oluşturmak ve yayınlanan profil seçildiğinde designer değişken kataloğuna profil alanlarını otomatik getirmek.

**Architecture:** Backend kontratları korunur. Mevcut `EInvoiceProfilesComponent` adresleme merkezi olarak yeniden konumlandırılır; `EInvoiceMappingEditorComponent` gerçek form tabanlı kök alan/koleksiyon alanı editörü olur. Designer’daki mevcut schema-aware variable kaydı, profil seçimiyle gelen schema bilgisini kullanmaya devam eder.

**Tech Stack:** Angular standalone components, Vitest/Angular test runner, mevcut WebAPI e-fatura profil uçları, mevcut workflow profile activities.

## Global Constraints

- Backend public kontrat değişikliği yapılmayacak.
- Profil adresleme ana sayfadan ayrı kart/rota ile erişilecek.
- Designer sadece hazır profili seçecek ve çıkan değişkenlerle RPA akışı kuracak.
- TDD: önce failing test, sonra minimal implementasyon.

---

### Task 1: Dashboard’dan E-Fatura Adresleme Merkezine Giriş

**Files:**
- Modify: `src/RPA.Studio/src/app/dashboard/dashboard.component.ts`
- Modify: `src/RPA.Studio/src/app/app.routes.ts`
- Test: dashboard component testi yoksa `src/RPA.Studio/src/app/dashboard/dashboard.component.spec.ts` oluştur.

**Interfaces:**
- Route: `/einvoice-addressing`
- Component: mevcut `EInvoiceProfilesComponent`

- [ ] **Step 1: Write failing dashboard route/card test**

Beklenen: Studio kartları içinde `/einvoice-addressing` rotalı “E-Fatura Adresleme” kartı bulunur.

- [ ] **Step 2: Run test and observe failure**

Run: `npm test -- --watch=false --include src/app/dashboard/dashboard.component.spec.ts`

- [ ] **Step 3: Add route and dashboard card**

`app.routes.ts` içine `/einvoice-addressing` rotası ekle; dashboard kartına başlık/description ekle.

- [ ] **Step 4: Run test**

Run: `npm test -- --watch=false --include src/app/dashboard/dashboard.component.spec.ts`

- [ ] **Step 5: Commit**

Commit: `feat(studio): e-fatura adresleme ana sayfa girisi`

### Task 2: Mapping Editor’ü Gerçek Adresleme Formuna Tamamla

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.html`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.scss`
- Test: `src/RPA.Studio/src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts`

**Interfaces:**
- Input `value` profil JSON string veya mapping rules kabul eder.
- Output `profileDefinitionChange` profil definition JSON üretir.
- Methods: `addCollectionFromDraft()`, `addDraftAsCollectionField()`, `emitProfileDefinition()`

- [ ] **Step 1: Write failing tests**

Testler:
- Mevcut profil JSON’u verildiğinde kök alanlar ve `satirlar` koleksiyonu editörde görünür.
- XML tag seçilip “Kök alan ekle” ile field definition’a yazılır.
- `satirlar` koleksiyonu oluşturulup “Satır alanı ekle” ile `MalzemeKodu` eklenir.

- [ ] **Step 2: Run tests and observe failure**

Run: `npm test -- --watch=false --include src/app/studio/designer/properties/einvoice-mapping-editor.component.spec.ts`

- [ ] **Step 3: Implement minimal UI**

Template’e görünür bölümler ekle:
- Örnek XML seç
- Kök alan formu
- Satır koleksiyonu formu
- Satır alanları listesi
- Önizleme

- [ ] **Step 4: Run tests**

Run: aynı spec.

- [ ] **Step 5: Commit**

Commit: `feat(studio): e-fatura adresleme editorunu tamamla`

### Task 3: Adresleme Merkezi Sayfasını Kullanılabilir Hale Getir

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.html`
- Modify: `src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.scss`
- Test: `src/RPA.Studio/src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.spec.ts`

**Interfaces:**
- Route `/einvoice-addressing` proje seçmeden açıldığında kullanıcıya proje seçimi/uyarı gösterir.
- Route `/projects/:projectId/einvoice-profiles` geriye uyum için çalışır.
- Editor `value` input’una `draftJson()` verilir.

- [ ] **Step 1: Write failing tests**

Testler:
- Sayfa başlığı “E-Fatura Adresleme” gösterir.
- Editor’e mevcut `draftJson` value olarak aktarılır.
- Kaydet/Yayınla akışı definition JSON’u API’ye gönderir.

- [ ] **Step 2: Run tests and observe failure**

Run: `npm test -- --watch=false --include src/app/studio/projects/einvoice-profiles/einvoice-profiles.component.spec.ts`

- [ ] **Step 3: Implement page updates**

Başlık/kopya değiştir, editor binding ekle, JSON textarea’yı teknik detay olarak konumlandır.

- [ ] **Step 4: Run tests**

Run: aynı spec.

- [ ] **Step 5: Commit**

Commit: `feat(studio): e-fatura adresleme merkezini ayir`

### Task 4: Designer Profil Seçimi ve Değişken Kataloğu Akışı

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts/html`
- Modify: `src/RPA.Studio/src/app/studio/designer/designer.component.spec.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/variables/variables-panel.component.spec.ts`

**Interfaces:**
- `profileId`, `profileVersion`, `outputSchemaJson`, `outputVariable`
- Existing `onProfileActivityPropertiesChange(activityType, properties)` registers schema-aware variable.

- [ ] **Step 1: Write failing designer test**

Test: Profile activity properties include output schema; designer registers `fatura.faturaNo` and `fatura.satirlar.MalzemeKodu` variable paths.

- [ ] **Step 2: Run failing tests**

Run: `npm test -- --watch=false --include src/app/studio/designer/designer.component.spec.ts --include src/app/studio/designer/variables/variables-panel.component.spec.ts`

- [ ] **Step 3: Implement minimal property/profile schema wiring**

Property panel profile picker should keep selected profile schema on node properties so designer variable catalog sees it.

- [ ] **Step 4: Run tests**

Run: same specs.

- [ ] **Step 5: Commit**

Commit: `feat(studio): profil secimini designer degiskenlerine bagla`

### Task 5: Full Verification

**Files:**
- No source edits unless tests reveal regression.

- [ ] **Step 1: Run Studio tests**

Run: `npm test -- --watch=false` in `src/RPA.Studio`

- [ ] **Step 2: Run Studio build**

Run: `npm run build` in `src/RPA.Studio`

- [ ] **Step 3: Run backend smoke if schema touched**

Run: `dotnet test RPA.sln -m:1 -nodeReuse:false --no-restore`

- [ ] **Step 4: Commit any verification fixes**

Commit only if source/test fixes were required.

## Self-Review

- Spec coverage: dashboard entry, addressing workspace, visible field/collection mapping, designer variable catalog covered.
- Placeholder scan: no TBD/TODO remains.
- Type consistency: route `/einvoice-addressing`, component `EInvoiceProfilesComponent`, profile definition `{ fields, collections }` matches current code.
