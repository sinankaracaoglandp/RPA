# Common Loop Node Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Studio ve runner'da While, For ve ForEach için ortak `body/exit/loop-back` akışı ve ayrı `Logic.For` node'u oluşturmak.

**Architecture:** Workflow bağlantıları hedef portu taşıyacak; loop node'ları ortak port/validasyon yardımcılarını kullanacak. Runner bağlantılardan loop gövdesi ve çıkışını çıkaracak, eski `bodyStartNodeId/bodyEndNodeId` workflow'larını geriye uyumlu çalıştıracak.

**Tech Stack:** .NET 10, C#/xUnit/Newtonsoft.Json; Angular/TypeScript/Rete/Karma.

## Global Constraints

- Failing test → minimal implementation → passing test → commit.
- `Logic.For`: `end` dahildir; `step=0` geçersizdir; negatif step desteklenir.
- Normal graph cycle'ları yasak kalır; yalnızca doğrulanmış `loop-back` kenarı cycle kontrolünden muaftır.
- Mevcut `bodyStartNodeId/bodyEndNodeId` workflow'ları geriye uyumlu kalır.
- Kontrat değişikliği AGENTS.md prosedürüne kaydedilir.

---

### Task 1: Workflow kontratı ve modeller

**Files:**
- Modify: `AGENTS.md`
- Modify: `src/RPA.Domain/WorkflowSchema.json`
- Modify: `src/RPA.Infrastructure/Workflow/Model/WorkflowNode.cs`
- Modify: `src/RPA.Infrastructure/Workflow/Model/WorkflowConnection.cs`
- Modify: `src/RPA.Studio/src/app/shared/models/workflow.model.ts`
- Test: `tests/RPA.Infrastructure.Tests/WorkflowSchemaTests.cs`

**Interfaces:**
- Produces: node tipi `for`; `ConnectionPort = out|success|failure|true|false|body|exit`; `ConnectionTargetPort = in|loop-back`; `WorkflowConnection.ToPort`; `WorkflowNode.Start/End/Step/IndexVariable`.

- [ ] **Step 1: Failing kontrat testi yaz.** JSON schema'nın `for`, `body`, `exit`, `toPort=loop-back`, `start/end/step/indexVariable` kabul ettiğini; bilinmeyen hedef portu reddettiğini doğrula.
- [ ] **Step 2: Testi çalıştır.** `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter WorkflowSchema`; beklenen: yeni `for` şeması bulunmadığı için FAIL.
- [ ] **Step 3: Minimal kontratı uygula.** Schema enum/alanlarını, C# ve TypeScript modellerini ekle; `toPort` varsayılanı `in` olsun. AGENTS.md'ye `2026-07-14 (Common Loop Nodes)` gerekçesi ve etkilenen paketleri ekle.
- [ ] **Step 4: Testi tekrar çalıştır.** Aynı komut; beklenen PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(contract): ortak loop portlari ve Logic.For"`.

### Task 2: Runner ortak loop yürütmesi

**Files:**
- Modify: `src/RPA.Infrastructure/Workflow/BaseRunner.cs`
- Modify: `src/RPA.Infrastructure/Workflow/Model/WorkflowDefinition.cs`
- Test: `tests/RPA.Infrastructure.Tests/BaseRunnerTests.cs`

**Interfaces:**
- Consumes: `WorkflowConnection.ToPort`, loop `body/exit` source portları ve For alanları.
- Produces: While/For/ForEach için ortak gövde çözümleme; eski body ID fallback'i.

- [ ] **Step 1: Failing runner testleri yaz.** Bağlantı tabanlı While; dahilî artan For `1,2,3`; azalan For `3,2,1`; yönü uyumsuz For için sıfır iterasyon; `step=0` SystemException; bağlantı tabanlı ForEach; loop-back dışı cycle reddi senaryolarını ekle.
- [ ] **Step 2: Testleri çalıştır.** `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "BaseRunnerTests"`; beklenen: For tanınmadığı ve loop portları çözülmediği için FAIL.
- [ ] **Step 3: Minimal runner uygulamasını yaz.** `ResolveLoopFlow(node,state)` ile body start/end ve exit hedefini belirle; `ExecuteForAsync` içinde `long` aralık ve dahilî end uygula; cycle analizinde yalnızca `toPort=loop-back` kenarlarını yok say; eski ID alanlarına fallback yap.
- [ ] **Step 4: Testleri tekrar çalıştır.** Aynı komut; beklenen PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(workflow): ortak loop yurutmesi ve For semantigi"`.

### Task 3: Aktivite kataloğu ve Studio property modeli

**Files:**
- Modify: `src/RPA.Infrastructure/Workflow/ActivityRegistry.cs`
- Modify: `src/RPA.Studio/src/app/shared/models/activity.model.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.ts`
- Test: `tests/RPA.Infrastructure.Tests/Workflow/ActivityRegistryCoverageTests.cs`
- Test: `src/RPA.Studio/src/app/studio/designer/properties/generic-property.component.spec.ts`

**Interfaces:**
- Produces: `Logic.For` metadata (`start`, `end`, `step`, `indexVariable`) ve property editörü desteği.

- [ ] **Step 1: Failing katalog/property testleri yaz.** Katalogda `Logic.For` ve dört alanını; Studio formunda sayısal start/end/step ve değişken index alanını doğrula.
- [ ] **Step 2: Testleri çalıştır.** `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter ActivityRegistryCoverageTests` ve `npm test -- --watch=false --include=**/generic-property.component.spec.ts`; beklenen FAIL.
- [ ] **Step 3: Minimal metadata ve form eşlemesini ekle.** `Logic.For` görünen adı `Sayaç Döngüsü`, kategori `Logic`; step varsayılanı `1`.
- [ ] **Step 4: İki test komutunu tekrar çalıştır.** Beklenen PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(studio): Logic.For katalog ve ozellikleri"`.

### Task 4: Canvas ortak loop portları ve validasyonu

**Files:**
- Modify: `src/RPA.Studio/src/app/studio/designer/canvas/canvas.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/canvas/node.component.ts`
- Modify: `src/RPA.Studio/src/app/studio/designer/canvas/node.component.html`
- Modify: `src/RPA.Studio/src/app/studio/designer/canvas/node.component.scss`
- Test: `src/RPA.Studio/src/app/studio/designer/canvas/canvas.component.spec.ts`
- Test: `src/RPA.Studio/src/app/studio/designer/canvas/node.component.spec.ts`

**Interfaces:**
- Consumes: `ConnectionTargetPort`, `for` node tipi.
- Produces: ortak `isLoopNodeType`, `body/exit` output ve `in/loop-back` input soketleri; serileştirilmiş `toPort`.

- [ ] **Step 1: Failing canvas testleri yaz.** Üç loop tipinin aynı portları verdiğini; `Logic.For` drop'unun `for` node'u ürettiğini; loop-back'in yalnızca gövde içinden sahip loop'a bağlandığını; tek body/exit kuralını ve serialize/load round-trip'i doğrula.
- [ ] **Step 2: Testleri çalıştır.** `npm test -- --watch=false --include=**/{canvas,node}.component.spec.ts`; beklenen FAIL.
- [ ] **Step 3: Minimal canvas uygulamasını yaz.** Tek `LOOP_NODE_TYPES` sabiti kullan; input socket view modeline `loop-back` ekle; bağlantı başlatma/tamamlama olayında hedef portu taşı; Rete connection'a `targetInput` kaydet; validasyon ve serileştirmeyi ortaklaştır.
- [ ] **Step 4: Testleri tekrar çalıştır.** Aynı komut; beklenen PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(studio): ortak loop baglanti portlari"`.

### Task 5: Entegrasyon ve regresyon doğrulaması

**Files:**
- Modify only if a failing regression exposes an in-scope defect.

**Interfaces:**
- Consumes: Tasks 1–4 deliverables.
- Produces: verified end-to-end loop workflow compatibility.

- [ ] **Step 1: Odaklı backend testlerini çalıştır.** `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter "BaseRunnerTests|WorkflowSchema|ActivityRegistryCoverageTests"`; beklenen PASS.
- [ ] **Step 2: Studio testlerini çalıştır.** `npm test -- --watch=false --include=**/{canvas,node,generic-property}.component.spec.ts`; beklenen PASS.
- [ ] **Step 3: Studio build al.** `npm run build`; beklenen exit code 0.
- [ ] **Step 4: Diff ve kontrat etkisini denetle.** `git diff --check` temiz; plaintext credential veya ilgisiz dosya değişikliği yok.
- [ ] **Step 5: Son düzeltmeler varsa testle ve commit et.** `git commit -m "fix(loop): entegrasyon regresyonlari"`; düzeltme yoksa commit oluşturma.
