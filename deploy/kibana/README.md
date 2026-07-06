# Kibana Dashboard Şablonları (WP-6.3)

RPA Platform v3 Serilog → Elasticsearch log akışı için hazır Kibana panoları.
Loglar `rpa-logs-*` index pattern'ine yazılır; korelasyon anahtarı `correlation_id` (JobRun GUID).

## İçe Aktarma

Kibana → **Stack Management → Saved Objects → Import** ile `rpa-dashboards.ndjson`
dosyasını yükleyin (veya API):

```bash
curl -X POST "http://<kibana>:5601/api/saved_objects/_import?overwrite=true" \
  -H "kbn-xsrf: true" \
  --form file=@rpa-dashboards.ndjson
```

## Panolar

| Pano | İçerik | Alan |
|------|--------|------|
| **İş Hacmi** | Zaman içinde JobRun sayısı | `@timestamp`, `job_status` |
| **Hata Oranı** | Failed / BusinessException oranı | `job_status` |
| **Süre Dağılımı** | JobRun süresi histogramı | `duration_ms` |
| **Robot Doluluk** | Robot bazında aktif iş yüzdesi | `robot_id`, `job_status` |

Alan adları uygulama log şablonuyla (`RPA.Infrastructure.Logging`) hizalıdır. Ortam
farklıysa index pattern'i `deploy` sırasında güncelleyin.
