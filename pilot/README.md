# Pilot Senaryosu — Müşteri Portalından Veri ile SAP MM01 Malzeme Açma (WP-6.5)

RPA Platform v3'ün uçtan uca doğrulama kilometre taşı (Spec Bölüm 15, Doğrulama Checklist).

## Senaryo

> Müşteri portalına **OTP'li giriş** yapılır, malzeme verisi çekilir ve **SAP MM01**
> transaksiyonunda (BAPI) malzeme açılır. 100 kayıtlık batch işlenir.

**Hedef:** ≥ %95 straight-through başarı; `BusinessException`'lar (ör. "malzeme zaten
mevcut") Action Center'a düşer ve insan tarafından çözülür.

## Workflow

`mm01-material-creation.workflow.json` — 4 node'lu doğrusal akış:

| Node | Aktivite | Açıklama |
|------|----------|----------|
| `login` | `Web.Portal.Login` | Portala OTP ile giriş |
| `fetch` | `Web.Portal.FetchMaterial` | Malzeme verisini çeker |
| `create` | `Sap.Nco.CreateMaterial` | SAP MM01 BAPI ile malzeme açar |
| `done` | `assign` | Sonuç mesajını yazar |

## Doğrulama koşumu

`tests/RPA.Infrastructure.Tests/PilotScenarioTests.cs` gerçek üretim bileşenlerini
(BaseRunner state machine, RetryHandler + ExceptionClassifier) kullanarak 100 kayıtlık
batch'i çalıştırır. Portal/OTP/SAP kanalları deterministik sahtelerle temsil edilir:

- **BusinessException:** `recordId % 33 == 0` (33, 66, 99) → "malzeme zaten mevcut" →
  Action Center'a yönlendirilir (retry edilmez).
- **Geçici SystemException:** seçili kayıtlar portal girişinde timeout yaşar → retry
  politikasıyla (MaxRetries+1) toparlanır ve başarıya sayılır.

**Sonuç:** 100 kayıtta 3 iş istisnası → **%97 başarı** (≥%95 hedefi karşılar), 0 hard
failure (geçici hatalar retry ile toparlandı). Test suite'de doğrulanır:

- `Pilot_100RecordBatch_MeetsSuccessTarget` — başarı oranı ≥%95, hard failure = 0
- `Pilot_BusinessExceptions_RoutedToActionCenter` — 3 iş istisnası, Action Center kaydı
- `Pilot_TransientSystemErrors_RecoveredByRetry` — geçici hata retry ile başarıya döner

## Canlı pilot notu

Bu simülasyon, kanal sahtelerini gerçek implementasyonlarla değiştirerek canlı ortama
taşınır: `OtpEmailChannel`/`OtpTotpChannel` (WP-4.3), `SapNcoCallBapiActivity` (WP-4.2),
Web aktiviteleri (WP-5.6). Workflow JSON ve batch orkestrasyonu aynen kalır.
