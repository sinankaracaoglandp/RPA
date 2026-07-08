# Playwright Web Activities Design

## Goal

Studio'da eklenen Web node'lari gercek bir kullanici gibi gorunur browser uzerinde calissin: tarayici acilsin, URL'ye gidilsin, input doldurulsun, tiklama yapilsin ve text okunabilsin.

## Scope

Ilk kapsam attended calisma icindir. `Web.Open`, `Web.Goto`, `Web.Fill`, `Web.Click`, `Web.GetText` ve `Web.WaitFor` gercek Playwright session'i kullanir. `Web.Download`, `Web.Upload`, `Web.Screenshot`, `Web.FrameSwitch` mevcut kayitli aktivite olarak kalir; bu aktiviteler daha sonra ayni session manager uzerinden genisletilir.

## Architecture

`Web.Open` bir `IWebAutomationSessionManager` uzerinden Playwright browser/page session'i baslatir ve session id'yi workflow degiskenlerine yazar. `Web.Goto`, `Web.Fill`, `Web.Click`, `Web.GetText` ve `Web.WaitFor` ayni session id ile manager'dan page alir. `browser` alani `chromium`, `chrome` ve `edge` degerlerini destekler; `headless=false` durumunda kullanici tarayiciyi ekranda gorur.

## Error Handling

Bos zorunlu parametreler mevcut `BusinessException` davranisini korur. Desteklenmeyen browser adi `BusinessException` uretir. Playwright runtime hatalari teknik hata olarak runner tarafindan system exception'a sarilir.

## Testing

TDD ile once manager arayuzu uzerinden unit testler yazilir. Testlerde gercek browser acmak yerine fake manager kullanilir; bu sayede `Web.Open/Goto/Fill/Click/GetText/WaitFor` aktivitelerinin dogru session ve selector/value bilgilerini manager'a ilettigi kanitlanir. Paket ve build dogrulamasi sonunda `dotnet test tests/RPA.Infrastructure.Tests --no-restore --filter WebActivityTests` ve `dotnet build src/RPA.Infrastructure/RPA.Infrastructure.csproj --no-restore` calistirilir.
