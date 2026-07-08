# Codex Handoff - Agent Run Flow

Date: 2026-07-08
Workspace: `C:\Source\RPA`

## Scope

Studio designer'da kaydetme/yukleme ve Agent ile calistirma hattindaki eksikler kapatiliyor.

## Completed

- Canvas, workflow input'u component initialize olduktan sonra gelirse artik yukluyor.
  - `canvas.component.ts`: `OnChanges`, `workflowLoaded`, `loadedWorkflowKey`, `loadWorkflowInput()` eklendi.
  - `canvas.component.spec.ts`: gec gelen workflow input testlendi.
- Designer Run akisi eklendi.
  - Run butonu once draft'i kaydediyor, sonra `/api/workflows/{workflowId}/run` endpoint'ine POST ediyor.
  - Run sonucu `QueueItemId` tutuluyor ve ekranda kisa id olarak gosteriliyor.
- Frontend draft service Run endpoint'ini biliyor.
  - `WorkflowDraftService.run()` eklendi.
  - TR/EN ceviri anahtarlari eklendi.
- Backend Run endpoint'i eklendi.
  - `WorkflowRunController`: `POST /api/workflows/{workflowId}/run`.
  - `WorkflowRunService`: Draft `WorkflowVersion` icin Agent payload formatinda `QueueItem` olusturuyor.
- Agent queue selection duzeltildi.
  - `AgentOptions.QueueName` eklendi, varsayilan `StudioRun`.
  - `QueueAgentJobSource`, `QueueId` bos ise `QueueName` ile kuyrugu cozumleyebiliyor.
  - `src/RPA.Agent/appsettings.json` icine `QueueName: "StudioRun"` eklendi.
- Studio Run kuyrugu tekillestirildi.
  - `WorkflowRunService`, `StudioRun` kuyrugunu proje bazinda cogaltmak yerine ada gore reuse ediyor.
- Agent polling zinciri icin integration seviyesine yakin test eklendi.
  - `HostedServiceTests.Poll_QueueName_Ile_Cozulen_StudioRun_Isini_Runnera_Tasir`
  - Gercek `QueueAgentJobSource + QueuePollingBackgroundService + JobExecutor` zinciri ile `StudioRun` queue-name cozumleme ve runner'a payload tasima dogrulaniyor.
- Bozuk draft JSON hata senaryosu kontrollu hale getirildi.
  - `WorkflowRunService`, draft `JsonDefinition` parse edilemezse `BusinessException` firlatiyor.
  - QueueItem olusturulmadan hata donmesi icin test eklendi.
- Studio Run QueueItem durum gecisi testlendi.
  - `WorkflowRunServiceTests.EnqueuedStudioRunItem_CanBeClaimedAndCompletedByQueueService`
  - `WorkflowRunService.EnqueueDraftAsync` ile olusan item `QueueService.GetNextItemAsync` tarafindan `InProgress` yapilir, sonra `CompleteAsync` ile `Successful` olur.
- Run sonrasi QueueItem durum sorgulama eklendi.
  - Backend: `GET /api/queues/{queueId}/items/{itemId}`.
  - `IQueueService.GetItemAsync` ve `QueueService.GetItemAsync`.
  - Frontend: `OrchestratorService.getQueueItem()`.
  - Designer: Run sonrasi `queueId`, `queueItemId`, `status` saklanir; "Durumu yenile" ile tek item status'u guncellenir.
  - WebAPI controller testleri eklendi: dogru queue altinda 200, farkli queue altinda 404.
- Queue API yetkilendirme altina alindi.
  - `QueuesController` icin `[Authorize]` eklendi.
  - WebAPI queue integration testleri test-auth handler ile guncellendi.
- Designer Run status otomatik polling'e alindi.
  - Run kuyruğa alindiktan sonra 3 saniyede bir `GET /api/queues/{queueId}/items/{itemId}` cagrilir.
  - `Successful`, `Failed`, `BusinessException`, `Abandoned` terminal durumlarinda polling durur.
  - Component destroy edilirken polling subscription temizlenir.
- Designer polling testleri temizlendi.
  - Fake timer icinde acik kalan polling timer'i fixture destroy ile temizlenir.
  - Component destroy edilince polling'in durdugu testlendi.
  - `Abandoned` terminal status olarak eklendi ve case-insensitive terminal kontrolu yapildi.
- NuGet audit uyarilarini kapatmadan paket yukseltme denendi.
  - `MailKit`: 4.7.1 -> 4.17.0.
  - `ClosedXML`: 0.102.0 -> 0.105.0.
  - `System.IO.Packaging`: explicit 10.0.9 eklendi.
  - Test projesine `SQLitePCLRaw.lib.e_sqlite3` 3.53.3 ve `System.IO.Packaging` 10.0.9 eklendi.
- `global.json` SDK surumu duzeltildi.
  - Gecersiz `10.0.0` yerine `10.0.300` kullaniliyor; makinedeki `10.0.301` roll-forward ile seciliyor.

## Verification

- `npx tsc -p tsconfig.spec.json --noEmit` passed.
- Run status refresh degisikliklerinden sonra `npx tsc -p tsconfig.spec.json --noEmit` tekrar passed.
- Otomatik polling degisikliginden sonra `npx tsc -p tsconfig.spec.json --noEmit` tekrar passed.
- Polling cleanup ve `Abandoned` terminal status degisikliginden sonra `npx tsc -p tsconfig.spec.json --noEmit` tekrar passed.
- Angular test runner bu ortamda `esbuild spawn EPERM` nedeniyle tam kosamadi.
- .NET test/build komutlari yer yer ortam/izin ve NuGet vulnerability policy nedeniyle tamamlanamadi.
  - Agent build ciktisi kod hatasi gostermedi: `0 Hata`, ancak `MailKit`/`MimeKit` NU1902 uyarilari nedeniyle exit code `1` dondu.
  - Agent focused test komutu 15 dk civari bekleyip test sonucuna ulasmadan sadece NU1902/NU1903 vulnerability uyarilari dondurdu.
  - Paket yukseltme sonrasi yerel restore, cache'te olmayan paketler ve/veya ag kisiti nedeniyle `0 Hata` ile erken cikiyor. `ClosedXML 0.105.0` cache'te var; `MailKit 4.17.0`, `System.IO.Packaging 10.0.9`, `SQLitePCLRaw.lib.e_sqlite3 3.53.3` cache'te yok.

## Known Risks

- `src/RPA.Agent/appsettings.json` icinde mevcut connection string gercek parola iceriyor; bu calisma kapsaminda degistirilmedi.
- .NET tarafinda paket restore internet erisimi olan ortamda kosulmali; ardindan testler yeniden calistirilmali.
- Studio Run su anda Draft versiyonu kuyruga aliyor; publish/governance ayrimi daha sonra netlestirilmeli.

## Next Items

- Online restore sonrasi `dotnet test` ile Agent/Infrastructure testlerini kos.
- QueueItem `New -> InProgress -> Successful` gecisi DB uzerinden testle sabitlendi; online restore sonrasi kosulmali.
- Run status icin terminal durumlari backend enum degisirse frontend listesiyle senkron tutulmali.
