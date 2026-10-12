# FimpleCaseStudy

ATM Cash Management Backend case study. ASP.NET Core 10, EF Core ve PostgreSQL.

## Güncel durum

v0.5.0: ATM/kaset yönetimi ve optimal banknot seçiminin ardından güvenli para çekme uygulanmıştır. Quote 60 saniye stok ve günlük limit rezervasyonu tutar; confirm, cancel, otomatik expiry ve bakımda iptal kalıcıdır. Confirm'in Idempotency-Key sonucu PostgreSQL'de saklanır ve API yeniden başladıktan sonra da aynı yanıt döner. Müşteri yatırması ve mutabakat sonraki sürümlerdedir. İstek gövdeleri: [API sözleşmesi](docs/API.md).

## Çalıştırma

Docker Desktop Linux engine açıkken repo kökünde:

```powershell
docker compose up --build
```

API başlamadan önce PostgreSQL hazır olana kadar beklenir ve EF migration uygulanır. Migration'lar `cash` şemasını, ATM/kaset/bakiye/hareket tablolarını, ledger koruma trigger'ını ve quote/idempotency tablolarını oluşturur. Veriler Docker named volume içinde saklanır.

- Swagger: http://localhost:8080/swagger
- OpenAPI: http://localhost:8080/swagger/v1/swagger.json
- Hazır olma (DB + migration): http://localhost:8080/health
- API canlılık: http://localhost:8080/health/live
- Sürüm bilgisi: http://localhost:8080/api/info

Portları değiştirmek için `.env.example` dosyasını `.env` olarak kopyala ve `API_PORT` / `DB_PORT` değerlerini değiştir. Compose varsayılan DB parolası ve HMAC anahtarı açıkça yerel geliştirme örneğidir. Gerçek kullanımda `WITHDRAWAL_HMAC_KEY` en az 32 UTF-8 byte içeren uygulama sırrı olmalıdır; tüm instance ve yeniden başlatmalar aynı anahtarı kullanmalıdır. Anahtarı değiştirmek aynı kartın kimliğini değiştirir; otomatik anahtar rotasyonu uygulanmadı. Portlar localhost'a bağlıdır. `.env` Git'e alınmaz.

Durdurma: `docker compose down` (veritabanı volume'ünü korur).

### API'yi yerelde geliştirme

.NET 10 SDK gerekir. PostgreSQL'i başlatıp PowerShell'de:

```powershell
docker compose up -d db
$env:ConnectionStrings__Database = 'Host=localhost;Database=cash_management;Username=cash_app;Password=local-development-only;Timeout=5'
$env:Withdrawals__HmacKey = 'example-development-only-hmac-key-change-me'
dotnet run --project src/FimpleCaseStudy.Api -- --urls http://localhost:8080
```

Compose API servisi de açıksa önce `docker compose stop api` çalıştır veya farklı API portu seç. `.env` içinde DB port/parolası değiştirildiyse bağlantı dizesini de buna göre değiştir. Tam kurulum rehberi: [SETUP](docs/SETUP.md).

## Testler

Docker engine açıkken:

```powershell
dotnet restore
dotnet build --no-restore
dotnet test FimpleCaseStudy.sln --no-restore
```

Testcontainers her test için ayrı PostgreSQL container'ı ve dinamik port oluşturur; mevcut Compose veritabanını kullanmaz. Test sonunda container'lar temizlenir. Banknot testleri sekiz B3 örneğini, rezervasyon/limit sınırlarını, doluluk önceliğini, deposit dışlamasını ve 300 sabit seed'li senaryoda bağımsız exhaustive enumeration karşılaştırmasını içerir. Diğer testler kaset kuralları, lifecycle, karma deposit kapasitesi, atomik yükleme/boşaltma, ATM izolasyonu, paralel yükleme, ledger yazım hatasında rollback, EF/SQL ledger değişmezliği ve altyapıyı doğrular. Son çalıştırma sonuçları [WORKLOG](docs/WORKLOG.md) içindedir.

Çalışan API üzerinde Postman altyapı kontrolleri:

```powershell
postman collection run postman/Infrastructure.postman_collection.json --no-report-events
postman collection run postman/AtmSetup.postman_collection.json --no-report-events
postman collection run postman/Withdrawals.postman_collection.json --no-report-events
```

Altyapı collection'ı 4 istek/8 kontrol, ATM yönetimi 9 istek/18 kontrol, çekim collection'ı 15 istek/32 kontrol içerir; üçü de geçti. Çekim akışı yeni ATM oluşturur, alternatifleri rezerve eder, lower seçeneğini onaylar, byte olarak aynı replay'i ve farklı gövde conflict'ini doğrular, cancel ve bakım iptalini kontrol eder. ID'ler yanıtlarla taşınır; yalnızca sentetik kart girdisi kullanılır. Expiry, eşzamanlılık ve yeniden başlatma gerçek DB entegrasyon testlerindedir. Yatırma/mutabakat tam akışı sonraki aşamalarda eklenecek. API portu değiştiyse `--env-var baseUrl=http://localhost:YENI_PORT` kullan.

## Migration geliştirme

```powershell
dotnet tool restore
dotnet ef migrations add MigrationName --project src/FimpleCaseStudy.Infrastructure --startup-project src/FimpleCaseStudy.Api --output-dir Persistence/Migrations
```

`dotnet-ef` sürümü repo içindeki tool manifest'inde sabittir. Design-time factory model üretirken DB bağlantısı açmaz. Uygulama başlangıcında migration başarısızsa servis hazır gibi davranmak yerine açılışı durdurur.

## Mimari ve temel tasarım kararları

- Domain: ATM aggregate, kasetler, kupür bakiyeleri, yaşam döngüsü, nakit doğrulamaları ve bounded dynamic programming banknot seçicisi.
- Application: AtmService, IAtmStore sınırı, istek/yanıt DTO'ları ve test edilebilir TimeProvider.
- Infrastructure: EF eşlemeleri/constraint'ler, ATM satır kilidi, transaction, migration, ledger trigger'ı ve sağlık kontrolü.
- Api: HTTP endpoint'leri, DI composition root, Swagger ve hata yanıtları.

Bilinmeyen endpoint gibi HTTP hataları ProblemDetails ile `code`, `traceId` ve `instance` alanları taşır. `/health` başarısızlığında 503 / `DATABASE_NOT_READY`, `/health/live` ise DB'den bağımsızdır. Beklenmeyen çalışma zamanı hataları exception handler üzerinden işlenir; domain doğrulama hataları 400, bulunamayan ATM 404, durum/stok çelişkileri 409 döndürür.

Nakit mutasyonları PostgreSQL transaction advisory kilidi, ardından ATM satır kilidi alır. Bu case-study tercihi aynı kartın farklı ATM'lerde limit aşmasını, stok yarışlarını ve worker/bakım kilit tersliğini önler; bağımsız ATM mutasyonlarını da seri çalıştırdığı için yüksek trafik için ölçeklenmez. Bakiye, rezervasyon, ledger, quote ve confirm sonucu aynı transaction'dadır. Banknot seçimi önce minimum banknotlu kupür dağılımını, sonra fiziksel doluluk önceliğiyle kaset dağılımını bulur. Ayrıntılar ve ölçek sınırları [DECISIONS](docs/DECISIONS.md) içindedir. Otomatik migration case study'nin şartıdır; Compose tek API instance'ı çalıştırır.

Kart + para birimi için İstanbul iş gününde tamamlanan çekimler ve halen açık quote'ların en yüksek seçenek tutarı limitten düşülür: TRY 20.000, USD/EUR 1.000. Quote gece yarısında iptal edilmez; 60 saniyesi boyunca yeni günün limitinden de yer ayırır ve çekim confirm gününe yazılır. Kart girdisi 12-19 ASCII rakam olmalıdır; boşluk veya ayraç kabul edilmez. Yalnızca HMAC-SHA256 kimliği kalıcıdır. Alt/üst planlar kaset başına maksimum banknot adediyle rezerve edilir; fiziksel rezervasyon değeri günlük limit rezervasyonuyla aynı olmak zorunda değildir.

## Varsayımlar

Gün sınırı, alternatif rezervasyonları ve mutabakat yorumları [DECISIONS](docs/DECISIONS.md) içinde kaynak gereksinimlerden ayrı işaretlenmiştir. Docker imajlarında .NET 10 ve PostgreSQL 17 major etiketleri kullanılır; digest sabitlemesi henüz yapılmadı. NuGet paket sürümleri proje dosyalarında sabittir.

## Tamamlanmamış işler ve bilinen sınırlamalar

Part A ve B1-B10 bütünü henüz tamamlanmadı. Müşteri yatırması, ledger consistency endpoint'i, mutabakat/onay, outbox ve tam teslim Postman akışı sıradaki aşamalardadır. Global transaction kilidi, bütün expired quote'ları tarayan worker/işlem temizliği ve quote toplamlarıyla hesaplanan limit bu küçük kapsamın ölçek sınırlamalarıdır. Quote/idempotency verileri süresiz saklanır; arşivleme, idempotency saklama süresi ve HMAC anahtar rotasyonu uygulanmadı. JSON olarak saklanan planların DB içinde kaset başına FK doğrulaması yoktur; uygulama domain doğrulaması yapar. Swagger açıktır; authentication ödevde zorunlu değildir ve uygulanmadı.

## AI kullanımı

Codex; gereksinim analizi, başlangıç kayıtları, uygulama altyapısı ve testler için kullanıldı. Kullanıcı geri bildirimiyle dakika bazlı plan kaldırıldı. İlk test bağımlılığındaki NuGet güvenlik uyarısı paket güncellenerek giderildi; kesinti testindeki paylaşılan veritabanı bağımlılığı test izolasyonuyla düzeltildi. Ayrıntılı sonuçlar [WORKLOG](docs/WORKLOG.md) içinde tutulur.

v0.5.0 çekim akışı GPT-6.1-Sol alt ajanıyla geliştirildi ve ana ajan tarafından incelendi. İnceleme sonucunda rezervasyonun tüm girdileri mutasyon öncesinde doğrulandı, kalıcı hata yanıtlarına traceId/instance eklendi ve İstanbul iş günü hesabı korundu. Worker, rollback ve süre sınırı testleriyle bu noktalar doğrulandı.

## Teknik review kayıtları

- [İstek geçmişi ve proje bağlamı](docs/PROJECT_CONTEXT.md)
- [SemVer roadmap](docs/ROADMAP.md)
- [Kronolojik teknik günlük](docs/WORKLOG.md)
- [API sözleşmesi ve örnek istekler](docs/API.md)
- [Teknik kararlar](docs/DECISIONS.md)
- [Gereksinim ve doğrulama matrisi](docs/REQUIREMENTS.md)
