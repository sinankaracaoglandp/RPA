# RPA Platform v3 — Kurulum ve Operasyon Kılavuzu (WP-6.6)

Bu doküman RPA Platform v3'ün kurulumu, yapılandırması ve günlük operasyonunu kapsar.
Mimari ve gereksinim detayları için `docs/specs/2026-07-04-rpa-platform-v3-design.md`.

---

## 1. Bileşenler

| Bileşen | Proje | Açıklama |
|---------|-------|----------|
| **Orchestrator API** | `src/RPA.WebAPI` | REST API + SignalR hub; işler, kuyruklar, robotlar, Action Center, alarmlar |
| **Studio (Web UI)** | `src/RPA.Studio` | Angular tasarımcı + orchestrator ekranları |
| **Robot Agent** | `src/RPA.Agent` | İş istasyonunda çalışan ajan; kuyruktan iş çeker, workflow yürütür |
| **Çekirdek/Altyapı** | `RPA.Domain`, `RPA.Application`, `RPA.Infrastructure` | Motor, EF Core, SAP/OTP kanalları, Vault |

### Harici bağımlılıklar

| Servis | Amaç | Zorunlu mu? |
|--------|------|-------------|
| **SQL Server** (2019+/LocalDB) | Ana veri deposu | Evet |
| **Elasticsearch 7.x** + **Kibana** | Log toplama + panolar | Üretimde evet |
| **HashiCorp Vault** *(veya DPAPI)* | Credential saklama | Evet (DPAPI tek-makine fallback) |
| **AD / LDAP** | SSO kimlik doğrulama | Üretimde evet |
| **SMTP sunucusu** | E-posta alarmları + OTP e-posta kanalı | Opsiyonel |
| **SAP** (NCo + SAP GUI) | SAP entegrasyon aktiviteleri | Senaryoya bağlı |

---

## 2. Önkoşullar

- **.NET SDK 10.0** (`global.json` → `rollForward: latestFeature`)
- **Node.js 20+** ve **npm 11+** (Studio)
- **SQL Server** veya LocalDB (`sqllocaldb create mssqllocaldb`)
- Robot Agent makinelerinde: SAP GUI (SAP GUI aktiviteleri için), SAP NCo runtime

---

## 3. Kurulum

### 3.1 Veritabanı

```bash
# LocalDB (geliştirme)
sqllocaldb create mssqllocaldb

# Şemayı migration'larla oluştur
dotnet ef database update --project src/RPA.Infrastructure --startup-project src/RPA.WebAPI
```

Bağlantı dizesi `appsettings.json` → `ConnectionStrings:DefaultConnection`. Varsayılan:
`Server=(localdb)\mssqllocaldb;Database=RPA_Dev;Trusted_Connection=true;`

### 3.2 Orchestrator API

```bash
dotnet restore
dotnet build -c Release
dotnet run --project src/RPA.WebAPI -c Release
```

Varsayılan adres `https://localhost:5001`. Yapılandırma: bkz. Kısım 4.

### 3.3 Studio (Web UI)

```bash
cd src/RPA.Studio
npm install
npm start          # geliştirme: http://localhost:4200
# üretim derlemesi:
npm run build      # dist/ altına statik dosyalar
```

API adresi CORS izin listesinde olmalı (`Cors:AllowedOrigins`, varsayılan
`http://localhost:4200`). Üretimde `dist/` bir web sunucusundan (IIS/nginx) yayınlanır.

### 3.4 Robot Agent

```bash
dotnet run --project src/RPA.Agent -c Release
```

`src/RPA.Agent/appsettings.json` → `Agent:OrchestratorUrl`, `MachineName`, `Mode`
(Unattended/Attended), `QueueId`, poll/heartbeat aralıkları.

### 3.5 Kibana panoları

`deploy/kibana/rpa-dashboards.ndjson` içe aktarılır (bkz. `deploy/kibana/README.md`).

---

## 4. Yapılandırma (`src/RPA.WebAPI/appsettings.json`)

> **Güvenlik:** Üretimde `CHANGE_ME` değerleri **mutlaka** değiştirilir. Sırlar
> `appsettings.json` yerine ortam değişkenleri veya secret store ile verilmelidir.

### 4.1 Kimlik doğrulama

```jsonc
"Authentication": {
  "Ldap": {
    "ServerUrl": "ldap://your-ad-server:389",
    "BaseDn": "dc=example,dc=com",
    "SearchFilter": "(&(objectClass=user)(sAMAccountName={0}))",
    "Domain": "EXAMPLE"
  },
  "Jwt": {
    "Secret": "<en az 32 byte base64 sır — ZORUNLU değiştir>",
    "Issuer": "RPA.Platform",
    "Audience": "RPA.Clients",
    "ExpirationMinutes": 60
  }
}
```

Roller: `Developer` (publish), `Approver` (approve + ortam oluşturma). Deployment
governance akışı bu rollerle korunur (bkz. WP-6.4).

### 4.2 Credential Vault

```jsonc
"Vault": {
  "Type": "Dpapi",              // "Dpapi" (tek makine) veya "HashiCorp" (üretim)
  "HashiCorp": {
    "Url": "https://your-vault-server:8200",
    "Token": "<vault token>",
    "Mount": "secret",
    "MaxRetries": 3,
    "RetryBaseDelayMs": 200
  },
  "Dpapi": { "StorePath": "" }
}
```

Database'de **asla plaintext credential tutulmaz** — yalnızca Vault key referansı.

### 4.3 Loglama (Serilog → Elasticsearch)

```jsonc
"Serilog": {
  "WriteTo": [
    { "Name": "Console" },
    { "Name": "Elasticsearch",
      "Args": { "nodeUris": "http://localhost:9200",
                "indexFormat": "rpa-logs-{0:yyyy.MM.dd}" } }
  ]
}
```

Her log kaydına korelasyon anahtarı (`JobRun` GUID) eklenir → Kibana'da
`correlation_id` ile filtrelenir.

### 4.4 Alarm motoru (WP-6.3)

```jsonc
"Alerting": {
  "IntervalSeconds": 60,        // değerlendirme periyodu
  "WindowMinutes": 60,          // metrik penceresi
  "Smtp": {
    "Host": "smtp.example.com", // boş ise e-posta gönderimi atlanır
    "Port": 587,
    "FromAddress": "rpa@example.com",
    "Username": "",
    "Password": ""
  }
}
```

Teams kanalı için alarm kuralında webhook URL'si alıcı olarak verilir.

---

## 5. Operasyon

### 5.1 Sağlık kontrolü

- **API:** çalışıyor mu → `https://localhost:5001` yanıtı; loglarda başlangıç kaydı.
- **Robot:** heartbeat aralığında (`HeartbeatInterval`) orchestrator'a bildirir; heartbeat
  gecikirse robot **Offline** işaretlenir ve alarm tetiklenebilir.
- **Elasticsearch:** `rpa-logs-*` index'ine güncel kayıt akışı.

### 5.2 İzleme (Orchestrator UI)

| Ekran | Yol | İşlev |
|-------|-----|-------|
| Dashboard | `/orchestrator` | Günün iş özeti + başarı oranı |
| İşler | `/orchestrator/jobs` | JobRun listesi (durum filtresi) |
| Kuyruklar | `/orchestrator/queues` | Kuyruk + kalem durumları, SLA |
| Robotlar | `/orchestrator/robots` | Robot sağlık/mod |
| Action Center | `/orchestrator/action-center` | BusinessException/OTP/onay kayıtları |
| Alarm Kuralları | `/orchestrator/alert-rules` | Kural tanımı + aktif/pasif |
| Ortamlar | `/orchestrator/environments` | Dev/Test/Prod yönetimi |

### 5.3 İstisna yönetimi

- **BusinessException** (iş kuralı, ör. "malzeme zaten mevcut") → Action Center'a düşer,
  insan çözümler. Retry **edilmez**.
- **SystemException** (teknik, ör. bağlantı timeout) → kuyruk retry politikası uygulanır
  (`MaxRetries+1` deneme); tükenirse iş başarısız işaretlenir.

### 5.4 Deployment governance (Dev → Test → Prod)

1. Geliştirici workflow versiyonunu **Test**'e yayınlar (`Developer` rolü).
2. Onaylayan Test'ten geçen versiyonu **Prod**'a terfi ettirir (`Approver` rolü).
3. Draft doğrudan Prod'a onaylanamaz — önce Test'ten geçmelidir.

### 5.5 Yedekleme

- **SQL Server:** düzenli tam + log yedeği (JobRun, kuyruk, audit, deployment durumu).
- **Vault:** kendi yedekleme prosedürü (HashiCorp); DPAPI için makine anahtarı korunur.
- **Elasticsearch:** snapshot politikası (log saklama süresine göre).

---

## 6. Test ve doğrulama

```bash
# Backend (tüm katmanlar)
dotnet test

# Studio
cd src/RPA.Studio && npm test

# Pilot senaryosu (uçtan uca doğrulama)
dotnet test tests/RPA.Infrastructure.Tests --filter "FullyQualifiedName~PilotScenario"
```

Pilot kabul kriteri: portal OTP girişi + SAP MM01 senaryosu, 100 kayıtlık batch, **≥%95
başarı** (bkz. `pilot/README.md`).

---

## 7. Sorun giderme

| Belirti | Olası neden | Çözüm |
|---------|-------------|-------|
| API başlarken DB hatası | Migration uygulanmamış | `dotnet ef database update` |
| Studio API'ye erişemiyor (CORS) | Origin izin listesinde değil | `Cors:AllowedOrigins`'e ekle |
| Loglar Kibana'da yok | Elasticsearch erişilemiyor | `nodeUris` + ES sağlığı |
| Robot Offline görünüyor | Heartbeat ulaşmıyor | Ağ/`OrchestratorUrl`; agent logları |
| Credential çözülemiyor | Vault yapılandırması hatalı | `Vault:Type` + erişim; DPAPI aynı makine mi? |
| E-posta alarmı gitmiyor | SMTP `Host` boş/hatalı | `Alerting:Smtp` doldur |
| JWT doğrulama hatası | `Jwt:Secret` kısa/eksik | En az 32 byte base64 sır |
