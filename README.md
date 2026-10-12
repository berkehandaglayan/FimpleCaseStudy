# FimpleCaseStudy

ATM Cash Management Backend case study. ASP.NET Core 10, EF Core ve PostgreSQL.

## Güncel durum

v0.4.0: ATM/kaset yönetimine saf banknot seçici eklendi. Sınırlı kullanılabilir stokta en az banknotla tam tutarı veya en yakın alt/üst seçenekleri bulur; sekiz B3 kabul testi geçer. LOAD/UNLOAD hareketleri bakiye ile aynı transaction içinde kaydedilir ve değiştirilemez. Seçici henüz HTTP quote/confirm akışına bağlı değildir; müşteri çekimi, yatırması ve mutabakat uygulanmadı. Mevcut API gövdeleri: [API sözleşmesi](docs/API.md).

## Çalıştırma

Docker Desktop Linux engine açıkken repo kökünde:

```powershell
docker compose up --build
```

API başlamadan önce PostgreSQL hazır olana kadar beklenir ve EF migration uygulanır. İlk migration `cash` şemasını, ikinci migration ATM/kaset/bakiye/hareket tablolarını ve ledger koruma trigger'ını oluşturur. Veriler Docker named volume içinde saklanır.

- Swagger: http://localhost:8080/swagger
- OpenAPI: http://localhost:8080/swagger/v1/swagger.json
- Hazır olma (DB + migration): http://localhost:8080/health
- API canlılık: http://localhost:8080/health/live
- Sürüm bilgisi: http://localhost:8080/api/info

Portları değiştirmek için `.env.example` dosyasını `.env` olarak kopyala ve `API_PORT` / `DB_PORT` değerlerini değiştir. Compose varsayılan parolası yalnızca yerel geliştirme içindir. Portlar localhost'a bağlıdır. `.env` Git'e alınmaz.

Durdurma: `docker compose down` (veritabanı volume'ünü korur).

### API'yi yerelde geliştirme

.NET 10 SDK gerekir. PostgreSQL'i başlatıp PowerShell'de:

```powershell
docker compose up -d db
$env:ConnectionStrings__Database = 'Host=localhost;Database=cash_management;Username=cash_app;Password=local-development-only;Timeout=5'
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
```

Altyapı collection'ı 4 istek/8 kontrol, ATM yönetimi collection'ı 9 istek/18 kontrol içerir; hepsi geçti. İkinci collection her çalıştırmada yeni bir demo ATM oluşturur ve hizmette bırakır. Çekim/yatırma/mutabakat uçtan uca senaryosu sonraki aşamalarda eklenecek. API portu değiştiyse `--env-var baseUrl=http://localhost:YENI_PORT` kullan.

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

ATM mutasyonlarında satır kilidi ve LOAD/UNLOAD ledger temeli uygulanmıştır. Banknot seçimi önce minimum banknotlu kupür dağılımını, sonra fiziksel doluluk önceliğiyle kaset dağılımını bulur. Arama belleği istek büyüklüğüyle değil mevcut stok ve verilen kalan limitle sınırlıdır. Seçici stok değiştirmez; kalan günlük limiti hesaplamak çağıran katmanın sorumluluğudur. Quote/card-limit kilit sırası ve idempotency [DECISIONS](docs/DECISIONS.md) içinde plan olarak kayıtlıdır. Otomatik migration case study'nin şartıdır; mevcut Compose tasarımı tek API instance'ı içindir.

## Varsayımlar

Gün sınırı, alternatif rezervasyonları ve mutabakat yorumları [DECISIONS](docs/DECISIONS.md) içinde kaynak gereksinimlerden ayrı işaretlenmiştir. Docker imajlarında .NET 10 ve PostgreSQL 17 major etiketleri kullanılır; digest sabitlemesi henüz yapılmadı. NuGet paket sürümleri proje dosyalarında sabittir.

## Tamamlanmamış işler ve bilinen sınırlamalar

Part A ve B1-B10 bütünü henüz tamamlanmadı. Quote/çekim, müşteri yatırması, günlük limit takibi, ledger consistency endpoint'i, mutabakat/onay, outbox ve tam Postman akışı sıradaki aşamalardadır. Paralel load testi B6'nın quote rezervasyon testi yerine geçmez. Swagger bu yerel case study ortamında açıktır; authentication henüz yoktur ve ödevde zorunlu değildir.

## AI kullanımı

Codex; gereksinim analizi, başlangıç kayıtları, uygulama altyapısı ve testler için kullanıldı. Kullanıcı geri bildirimiyle dakika bazlı plan kaldırıldı. İlk test bağımlılığındaki NuGet güvenlik uyarısı paket güncellenerek giderildi; kesinti testindeki paylaşılan veritabanı bağımlılığı test izolasyonuyla düzeltildi. Ayrıntılı sonuçlar [WORKLOG](docs/WORKLOG.md) içinde tutulur.

## Teknik review kayıtları

- [İstek geçmişi ve proje bağlamı](docs/PROJECT_CONTEXT.md)
- [SemVer roadmap](docs/ROADMAP.md)
- [Kronolojik teknik günlük](docs/WORKLOG.md)
- [API sözleşmesi ve örnek istekler](docs/API.md)
- [Teknik kararlar](docs/DECISIONS.md)
- [Gereksinim ve doğrulama matrisi](docs/REQUIREMENTS.md)
