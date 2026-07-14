# Tasarım: Job → Ajan (Tag Havuzu) İlişkilendirme + Zamanlama

**Tarih:** 2026-07-14
**Durum:** Onaylandı (tasarım) — implementasyon planı bekleniyor
**Kapsam:** Studio'da oluşturulan workflow'ların hangi ajanda (Robot) koşacağının kalıcı
olarak tanımlanması, tag havuzu üzerinden ajan seçimi ve job (Trigger) yönetim ekranı.

---

## 1. Problem

Studio'da bir proje/workflow oluşturulduğunda **hangi ajan (Robot) tarafından çalıştırılacağı
belirsiz.** Domain modelinde proje/workflow ile Robot arasında kalıcı bir ilişki yok; tek bağ
`JobRun.AssignedRobotId` (nullable) ve o da çalışma anında hiçbir yerde doldurulmuyor.

### Kök neden — 3 boşluk
1. **`Trigger`'da "hangi ajan" alanı yok** → job'un hangi tag havuzuna gideceği tanımsız.
2. **`TriggerService` ajan seçmiyor** → JobRun in-process çalışıyor, `AssignedRobotId` hep `null`.
3. **Studio'da job/zamanlama oluşturma ekranı yok** → mevcut Jobs ekranı yalnızca JobRun geçmişini
   okuyor (`jobs.component.ts`), yeni job/zamanlama tanımlanamıyor.

## 2. Konumlandırma kararı

Ajan atama bilgisi **`Project` veya `Workflow`'da değil, `Trigger` (iş tanımı) katmanında** durur.
Gerekçe: aynı workflow farklı ortam/frekans/ajan havuzunda koşabilmeli; zamanlama+sıklık+ajan seçimi
o spesifik "iş"in özelliğidir, workflow'un değil. Workflow saf tasarım olarak kalır.

Hedef mod: **yalnız Unattended** (işler PC/VM havuzunda arka planda koşar).
Ajan seçimi: **tag havuzu** (sabit tek ajan değil) — bir VM düşerse iş başka VM'e gider.

## 3. Mevcut durum (zaten var)

| Bileşen | Konum | Durum |
|---------|-------|-------|
| `Trigger` (ProjectId, WorkflowVersionId, Type, Configuration, EnvironmentId, IsActive) | `RPA.Domain/Entities/Trigger.cs` | ✅ |
| `Schedule` (cron) | `RPA.Domain/Entities/Schedule.cs` | ✅ |
| `TriggersController` (create/update/fire, workflow-version bazlı liste) | `RPA.WebAPI/Triggers/` | ✅ |
| `TriggerService.ExecuteTriggerAsync` | `RPA.Infrastructure/Scheduling/` | ✅ (ajan seçmez) |
| `SchedulerHostedService` (cron ateşleme) | `RPA.Infrastructure/Scheduling/` | ✅ |
| `Robot` (MachineName, **Tags**, Mode, Status, LastHeartbeat, Capacity) + register/heartbeat | `RPA.Domain/Entities/Robot.cs`, `RPA.WebAPI/Robots/` | ✅ |
| `TriggerType` enum (Cron, ApiWebhook, QueueThreshold, EmailWatcher, Manual) | `RPA.Domain/Enums/` | ✅ |
| Orchestrator UI: Jobs (JobRun geçmişi), Robots listesi | `RPA.Studio/src/app/orchestrator/` | ✅ |

## 4. Tasarım

### Bölüm 1 — Domain: Trigger'a ajan hedefleme

`Trigger` entity'sine eklenecek alanlar:
```csharp
public string TargetRobotTags { get; set; } = ""; // virgülle ayrık: "prod-vm,sap"
public int Priority { get; set; } = 0;             // eşit adaylar arası sıralama
```

**Eşleşme kuralı:** Bir Robot bir job'a aday olur ⟺
- `Robot.Tags` kümesi `Trigger.TargetRobotTags` kümesini **kapsar** (job'un istediği tüm etiketler
  robotta var — kısıtlayıcı, öngörülebilir), ve
- `Robot.Status == Online`, ve
- aktif iş sayısı `< Robot.Capacity`.

Tag'ler her iki tarafta da virgülle ayrık string; karşılaştırma trim + case-insensitive.
`TargetRobotTags` boşsa: yalnız etiket kısıtı yok, kapasite/online kısıtı kalır (her uygun ajan aday).

`RobotMode` zaten Unattended sabit — ayrı alan gerekmez.

### Bölüm 2 — Application: Dispatcher (ajan seçimi)

Yeni arayüz `IRobotDispatcher` (Domain veya Application katmanı, mevcut desenle uyumlu):
```csharp
Task<Robot?> SelectRobotAsync(Trigger trigger, CancellationToken ct);
```

Seçim algoritması:
1. Adayları filtrele (Bölüm 1 eşleşme kuralı).
2. Sırala: en boş kapasiteli (Capacity − aktifİş) DESC → `Trigger.Priority` DESC → en eski
   `LastHeartbeat` (adil dağıtım / round-robin benzeri).
3. İlk adayı döndür. Aday yoksa `null`.

`TriggerService` entegrasyonu:
- `NewJobRun`, dispatcher'ın seçtiği robotu `AssignedRobotId`'ye yazar.
- Aday yoksa JobRun `Status = "Pending"` (uygun ajan online olunca alınabilir) — hata değil.
  `"Pending"` mevcut JobRun status sözlüğüne eklenir.

**Kapsam sınırı (bilinçli):** Bu spec ajanı **seçip `AssignedRobotId`'yi doldurmayı** kapsar.
JobRun'ı gerçekten ajana **teslim etme / ajanın poll etmesi** (mevcut in-process
`RunAndFinalizeAsync` placeholder'ının yerini alacak agent handoff protokolü) **ayrı ve daha büyük
bir spec'e** bırakılır. Bu spec sonunda: doğru ajan seçilmiş ve JobRun'a atanmış olur.

### Bölüm 3 — API: Trigger'ı "Job" olarak yönetme

- `CreateTriggerRequest`, `UpdateTriggerRequest`, `TriggerDto`'ya `TargetRobotTags` + `Priority` eklenir.
- Yeni endpoint: `GET /api/triggers` — tüm job tanımlarını listeler (şu an yalnız
  `GET /api/workflows/{workflowVersionId}/triggers` var). Studio "Zamanlamalar" sayfasının tüm
  job'ları çekebilmesi için gerekli. Opsiyonel filtreler: `projectId`, `environmentId`, `isActive`.
- Mevcut `POST /api/triggers/{id}/fire` (manuel çalıştırma) korunur.

### Bölüm 4 — Studio: "Zamanlamalar / İşler" yönetim sayfası

Yeni component `orchestrator/schedules` (Codex'in canvas/döngü bölgesinden **tamamen ayrı**):
- **Liste:** workflow, tetikleme tipi, cron/interval özeti, hedef tag'ler, aktif/pasif, son çalışma.
- **Yeni Job formu:** Workflow versiyonu seç → Environment → Tetikleme tipi
  (Cron / Periyodik / Manuel / Webhook) → tetikleme yapılandırması → hedef robot tag'leri →
  Priority → kaydet.
  - Cron: cron ifadesi + timezone + overlap policy (skip/queue/parallel — mevcut Schedule alanları).
  - Periyodik: "her N dakika/saat" → `Configuration` JSON'a yazılır (cron'a çevrilebilir veya
    ayrı interval alanı; implementasyon planında netleşecek).
  - Manuel: yalnız "Şimdi çalıştır" ile tetiklenir.
  - Webhook: üretilen URL gösterilir.
- Her satırda: "Şimdi çalıştır" (`/fire`), aktif/pasif toggle, düzenle, sil.
- i18n anahtarları eklenir (mevcut `TranslationService` deseni).
- Yeni servis metotları `orchestrator.service.ts` içine (list/create/update/fire triggers).

Mevcut `jobs.component` (JobRun geçmişi) korunur; bu yeni sayfa **tanımları** yönetir; geçmiş ekranı
**çalışma kayıtlarını** gösterir. İki ekran birbirini tamamlar.

## 5. Kapsam dışı (bu spec değil)

- Agent handoff / poll protokolü (JobRun'ı ajana gerçekten teslim edip çalıştırma).
- Attended mod ajan seçimi.
- Sabit tek-ajan atama (yalnız tag havuzu).
- Queue-threshold / email-watcher tetikleyici tiplerinin yeni davranışı (mevcut haliyle kalır).

### 5.1 Bilinen kısıtlamalar

- **Kapasite atama yarışı (race):** `RobotDispatcher.SelectRobotAsync` içinde kapasite okuma
  (`GetActiveJobCountsByRobotAsync`) ile `TriggerService`'in `AssignedRobotId` yazması ayrı adımlar;
  aralarında lock/transaction yok. İki eşzamanlı `/fire` çağrısı aynı robotu Capacity'nin üzerinde
  atayabilir. Kabul edilebilir — gerçek ajan handoff/poll protokolü (bkz. Kapsam dışı) henüz
  yapılmadığından şu an pratik etkisi yok. Handoff protokolü inşa edilirken transaction/row-lock
  (örn. `SELECT ... FOR UPDATE` ya da EF Core optimistic concurrency) altında yeniden doğrulanmalı.
- **`Trigger.Priority` kullanılmıyor:** Alan persist ediliyor ancak `RobotDispatcher` robot
  seçiminde şu an kullanmıyor (tekil bir Trigger alanı olduğundan aday robotlar arası sıralamaya
  uygun değil). İleride Pending JobRun kuyruğunun işlenme sırasını belirlemek için kullanılması
  planlanıyor.

## 6. Test stratejisi

- **Domain/Application:** `IRobotDispatcher` seçim algoritması unit testleri — tag kapsama, kapasite
  dolu, hiç aday yok (Pending), sıralama (adil dağıtım), eşit adaylarda Priority.
- **Infrastructure:** `TriggerService` artık `AssignedRobotId` set ediyor mu; aday yoksa Pending.
- **WebAPI:** `GET /api/triggers` liste + filtreler; create/update `TargetRobotTags`/`Priority`
  round-trip; integration testleri (mevcut `TriggersControllerIntegrationTests` desenine ek).
- **Studio:** schedules component spec — liste render, yeni job formu validasyonu, fire çağrısı.
- Migration: `Trigger` yeni kolonları için EF migration.

## 7. Etkilenen kontratlar

`Trigger` entity + `CreateTriggerRequest`/`UpdateTriggerRequest`/`TriggerDto` genişler; yeni
`IRobotDispatcher` arayüzü eklenir; yeni `GET /api/triggers` endpoint. CLAUDE.md'ye Kontrat
Değişikliği notu eklenecek (implementasyon sırasında). Codex'in canvas/döngü bölgesine dokunulmaz.
