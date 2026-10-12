# FimpleCaseStudy

ATM Cash Management Backend case study. ASP.NET Core 10, EF Core ve PostgreSQL.

## Güncel durum

v1.0.0: Part A ve B1-B10 uygulandı ve teslim doğrulaması tamamlandı. ATM/kaset yönetimi, banknot seçimi, rezervasyonlu çekim, günlük limit, kalıcı idempotency, yatırma, mutabakat ve ikinci kullanıcı onayı, hareket defteri, TRY raporu ve transactional outbox hazırdır. Temiz kaynak kopyası ve boş PostgreSQL ile Compose açılışı doğrulandı; 106 unit ve 63 gerçek DB testi, beş Postman collection'ında 60 istek / 142 kontrol geçti. İstek gövdeleri: [API sözleşmesi](docs/API.md). Git gönderim durumu ve doğrulama kayıtları [ROADMAP](docs/ROADMAP.md) ve [WORKLOG](docs/WORKLOG.md) içindedir.

## Çalıştırma

Uygulamayı çalıştırmak için Docker Desktop'ın Linux engine'i yeterlidir; host makineye .NET SDK veya PostgreSQL kurmak gerekmez. Kaynağı alıp başlat:

```powershell
git clone https://github.com/berkehandaglayan/FimpleCaseStudy.git
cd FimpleCaseStudy
docker compose up
```

API başlamadan önce PostgreSQL hazır olana kadar beklenir ve EF migration uygulanır. Altı migration `cash` şemasını, ATM/kaset/bakiye/hareket tablolarını, ledger koruma trigger'ını, quote/idempotency, mutabakat geçmişi/onay, kur ve outbox tablolarını oluşturur. Veriler Docker named volume içinde saklanır. Kur seed'i TRY=1, USD=40, EUR=45 sabit demo verisidir; güncel piyasa kuru değildir. `/api/exchange-rates` ile görüntülenir.

- Swagger: http://localhost:8080/swagger
- OpenAPI: http://localhost:8080/swagger/v1/swagger.json
- Hazır olma (DB + migration): http://localhost:8080/health
- API canlılık: http://localhost:8080/health/live
- Sürüm bilgisi: http://localhost:8080/api/info

Portları değiştirmek için `.env.example` dosyasını `.env` olarak kopyala ve `API_PORT` / `DB_PORT` değerlerini değiştir. Compose varsayılan DB parolası ve HMAC anahtarı açıkça yerel geliştirme örneğidir. Gerçek kullanımda `WITHDRAWAL_HMAC_KEY` en az 32 UTF-8 byte içeren uygulama sırrı olmalıdır; tüm instance ve yeniden başlatmalar aynı anahtarı kullanmalıdır. Anahtarı değiştirmek aynı kartın kimliğini değiştirir; otomatik anahtar rotasyonu uygulanmadı. Portlar localhost'a bağlıdır. `.env` Git'e alınmaz.

İlk açılış image'ı derler. Kaynak kodu güncelledikten sonra `docker compose up --build` kullan. Durdurma: `docker compose down` (veritabanı volume'ünü korur). Yeniden `docker compose up` mevcut verileri açar; daha önce uygulanan migration'lar tekrar çalıştırılmaz.

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

.NET 10 SDK ve Docker engine açıkken repo kökünde:

```powershell
dotnet restore
dotnet build FimpleCaseStudy.sln -c Release --no-restore
dotnet test FimpleCaseStudy.sln -c Release --no-build
```

Testcontainers her test için ayrı PostgreSQL container'ı ve dinamik port oluşturur; mevcut Compose veritabanını kullanmaz. Test sonunda container'lar temizlenir. Banknot testleri sekiz B3 örneğini, rezervasyon/limit sınırlarını, doluluk önceliğini, deposit dışlamasını ve 300 sabit seed'li senaryoda bağımsız exhaustive enumeration karşılaştırmasını içerir. Diğer testler kaset kuralları, lifecycle, karma deposit kapasitesi, atomik yükleme/boşaltma, ATM izolasyonu, paralel yükleme, ledger yazım hatasında rollback, EF/SQL ledger değişmezliği ve altyapıyı doğrular. Son çalıştırma sonuçları [WORKLOG](docs/WORKLOG.md) içindedir.

Postman CLI kuruluysa çalışan API üzerinde aşağıdaki collection'ları çalıştır. Postman Desktop kullanıyorsan JSON dosyalarını import edip Collection Runner ile sırayla çalıştırabilirsin:

```powershell
postman collection run postman/Infrastructure.postman_collection.json --no-report-events
postman collection run postman/AtmSetup.postman_collection.json --no-report-events
postman collection run postman/Withdrawals.postman_collection.json --no-report-events
postman collection run postman/Deposits.postman_collection.json --no-report-events
postman collection run postman/EndToEnd.postman_collection.json --no-report-events
```

`EndToEnd` collection'ı istenen sırada create ATM -> configure -> load -> in service -> quote -> confirm -> deposit -> maintenance -> reconcile -> report işlemlerini ve ayrıca sayım geçmişi/ledger replay kontrolünü çalıştırır. Kur snapshot'ı, onaysız hizmete dönüş/yeni sayım engeli, farklı kullanıcı onayı, replay, genel TRY toplamı ve kaydedilmiş low-cash olaylarını doğrular. Ayrı environment dosyası gerekmez: baseUrl collection değişkenidir, işlem kimlikleri yanıtlardan alınır. Her E2E koşusu yeni ATM, sentetik kart ve idempotency anahtarı kullanır. Son gerçek test/collection sonuçları [WORKLOG](docs/WORKLOG.md) içindedir.

v1.0.0 temiz kurulumunda tüm collection'lar yeniden doğrulandı:

| Collection | İstek | Başarılı kontrol |
|---|---:|---:|
| Infrastructure | 4 | 8 |
| AtmSetup | 9 | 18 |
| Withdrawals | 16 | 37 |
| Deposits | 8 | 19 |
| EndToEnd | 23 | 60 |

Çekim akışı replay/conflict, cancel ve bakım iptalini; yatırma akışı taşma ve iadeleri kontrol eder. Expiry, eşzamanlılık, restart ve kayıt hatasında rollback 63 gerçek DB testinin içindedir. API portu değiştiyse `--env-var baseUrl=http://localhost:YENI_PORT` kullan. CLI'nın cloud'a sonuç yayınlamak için giriş isteyen mesajı yerel test sonucunu etkilemez; bu doğrulamalar için Postman hesabı gerekmedi.

## Migration geliştirme

```powershell
dotnet tool restore
dotnet ef migrations add MigrationName --project src/FimpleCaseStudy.Infrastructure --startup-project src/FimpleCaseStudy.Api --output-dir Persistence/Migrations
```

`dotnet-ef` sürümü repo içindeki tool manifest'inde sabittir. Design-time factory model üretirken DB bağlantısı açmaz. Uygulama başlangıcında migration başarısızsa servis hazır gibi davranmak yerine açılışı durdurur.

## Mimari ve temel tasarım kararları

- Domain: ATM aggregate, kasetler, kupür bakiyeleri, yaşam döngüsü, nakit doğrulamaları ve bounded dynamic programming banknot seçicisi.
- Application: AtmService, IAtmStore sınırı, istek/yanıt DTO'ları ve test edilebilir TimeProvider.
- Infrastructure: EF eşlemeleri/constraint'ler, ATM satır kilidi, transaction, migration, ledger trigger'ı, sağlık kontrolü ve outbox oluşturma/dispatch.
- Api: HTTP endpoint'leri, DI composition root, Swagger ve hata yanıtları.

Bilinmeyen endpoint gibi HTTP hataları ProblemDetails ile `code`, `traceId` ve `instance` alanları taşır. `/health` başarısızlığında 503 / `DATABASE_NOT_READY`, `/health/live` ise DB'den bağımsızdır. Beklenmeyen çalışma zamanı hataları exception handler üzerinden işlenir; domain doğrulama hataları 400, bulunamayan ATM 404, durum/stok çelişkileri 409 döndürür.

Nakit mutasyonları PostgreSQL transaction advisory kilidi, ardından ATM satır kilidi alır. Bu case-study tercihi aynı kartın farklı ATM'lerde limit aşmasını, stok yarışlarını ve worker/bakım kilit tersliğini önler; bağımsız ATM mutasyonlarını da seri çalıştırdığı için yüksek trafik için ölçeklenmez. Bakiye, rezervasyon, ledger, quote ve confirm sonucu aynı transaction'dadır. Banknot seçimi önce minimum banknotlu kupür dağılımını, sonra fiziksel doluluk önceliğiyle kaset dağılımını bulur. Ayrıntılar ve ölçek sınırları [DECISIONS](docs/DECISIONS.md) içindedir. Otomatik migration case study'nin şartıdır; Compose tek API instance'ı çalıştırır.

Kart + para birimi için İstanbul iş gününde tamamlanan çekimler ve halen açık quote'ların en yüksek seçenek tutarı limitten düşülür: TRY 20.000, USD/EUR 1.000. Quote gece yarısında iptal edilmez; 60 saniyesi boyunca yeni günün limitinden de yer ayırır ve çekim confirm gününe yazılır. Kart girdisi 12-19 ASCII rakam olmalıdır; boşluk veya ayraç kabul edilmez. Yalnızca HMAC-SHA256 kimliği kalıcıdır. Alt/üst planlar kaset başına maksimum banknot adediyle rezerve edilir; fiziksel rezervasyon değeri günlük limit rezervasyonuyla aynı olmak zorunda değildir.

Müşteri yatırması yalnızca hizmette yapılır. Eşleşen recycling kasetleri fiziksel doluluk artan, eşitlikte slot artan sırada kullanılır; taşma deposit kasetine, sığmayan veya tanınmayan banknotlar iadeye gider. İstek sırasındaki gruplar kapasiteyi paylaşır. Her grubun kabul + iade adedi girdi adedine eşittir. Rezerve banknotlar boş kapasite yaratmaz. Bakiye ve yalnızca kabul edilen banknotların DEPOSIT hareketleri atomiktir. Endpoint yeni tablo gerektirmedi.

Mutabakat yalnızca bakımda, sıfır rezervasyonla ve beş kasetin tam fiziksel envanteriyle yapılır. Counted-stored farkı yalnızca yeni RECONCILIATION_ADJUSTMENT hareketiyle uygulanır; eski ledger kayıtları değişmez. Sayım snapshot'ı (fark sıfır olsa bile), stok ve hareketler tek transaction'dadır. Consistency endpoint fiziksel/rezerve deltaları toplayıp saklanan adetlerle PostgreSQL REPEATABLE READ snapshot'ında karşılaştırır; sorgular arasına giren nakit işlemi sahte fark oluşturmaz. Endpoint fark bulursa veri onarmaz. Temel rapor para/kupür ve kaset kırılımında fiziksel/rezerve/kullanılabilir adet ve TRY/USD/EUR ayrı tutarları verir; deposit kullanılabilir muhasebe bakiyesi çekilebilir değildir.

Rapor ayrıca `overallTry` (ATM), `totalTry` (kaset) fiziksel/rezerve/kullanılabilir TRY tutarlarını ve kullanılan `exchangeRates` listesini verir. Genel toplam `overallTry.onHandAmount` alanıdır; envanter ve kurlar aynı REPEATABLE READ snapshot'ından okunur. Kaset `lowCash` anlık durumdur; `/api/atms/{id}/alerts` olay geçmişidir.

Low-cash olayları EF SaveChanges hook'unda original ve final Available karşılaştırılarak aynı cash transaction'ında kaydedilir. Böylece AtmStore dışındaki quote/confirm ve expiry yolları da kapsanır; confirm'in ara release/withdrawal adımları sahte alarm yaratmaz. Worker her saniye20due olayı, olay başına transaction ve FOR UPDATE SKIP LOCKED row lock ile işler. Sink yapılandırılmış log yazar; başarı SentAt, hata5saniye retry. DB commit başarısızlığında log tekrarı olabilir; eventId korelasyon sağlar. Gerçek test log sonrası sent failure'da aynı eventId'nin tekrar yayınlandığını gösterir; teslim en az bir kezdir.

## Varsayımlar

Gün sınırı, alternatif rezervasyonları ve mutabakat yorumları [DECISIONS](docs/DECISIONS.md) içinde kaynak gereksinimlerden ayrı işaretlenmiştir. Docker imajlarında .NET 10 ve PostgreSQL 17 major etiketleri kullanılır; digest sabitlemesi henüz yapılmadı. NuGet paket sürümleri proje dosyalarında sabittir.

Tam sayımda recycling için konfigüre kupürün sıfır dahil açık sayımı gerekir. Deposit notes boş olabilir; belirtilmeyen para/kupürler sıfır kabul edilir. Onay beklemeyen aynı sayımı tekrar göndermek yeni geçmiş oluşturur; mutabakat idempotency desteği yoktur. Sayımda X-User yoksa system kullanılır ve authentication sağlanmaz. Farklar ATM'yi bakımda bırakır; onay sonrası hizmete dönüş ayrı state isteğiyle yapılır.

v0.8.0'da toplam fark `sum(abs(delta * denomination * rateToTry))` olarak değerlendirilir; artı/eksi farklar birbirini gizlemez. Tam 1.000 TRY onay istemez. Kullanılan kurlar ve fark değeri sayımda sabitlenir. Approval için X-User zorunludur; aktör trim/lowercase edilir ve yalnız ASCII harf/rakam ile . _ - @ kabul edilir. ALICE ve alice aynı kullanıcıdır. Onaylama nakdi yeniden değiştirmez; aynı onaylayan tekrarında ilk timestamp korunur. Pending sayım varken yeni sayım yapılamaz. Eski v0.7.0 sayımlar LEGACY_COMPLETED olarak korunur; tarihsel kur snapshot'ı bilinmediğinden geriye dönük onay engeli üretilmez. Bunlar [DECISIONS](docs/DECISIONS.md) içinde kaynak kurallardan ayrı açıklanır.

Low-cash alarmı >=10'dan<10'a geçişte ve ilk LOADED->IN_SERVICE sırasında düşük stok için üretilir. Aynı düşük durumda tekrar alarm yoktur; >=10'a yükselince yeniden düşüş tetiklenebilir. Deposit-only hariçtir. Eski DB'deki düşük stoklar için migration geçmiş olay uydurmaz; report bayrağı görünürdür, sonraki geçiş yeni alarm üretir. Bu tekrar/ilk hizmet politikası kaynakta belirtilmediği için varsayım olarak belgelenir.

## Tamamlanmamış işler ve bilinen sınırlamalar

Kaynak gereksinimleri bakımından bilinen tamamlanmamış geliştirme yoktur. Kurlar sabit seed verisidir; canlı kur ve kur yönetimi endpoint'i yoktur. Global transaction kilidi, bütün expired quote'ları tarayan worker/işlem temizliği ve quote toplamlarıyla hesaplanan limit bu küçük kapsamın ölçek sınırlamalarıdır. Quote/idempotency/sayım/outbox verileri süresiz saklanır; arşivleme, idempotency saklama süresi ve HMAC anahtar rotasyonu uygulanmadı. Outbox log sink'i kullanır; harici bildirim sistemi/dead-letter yoktur, başarısız sink 5 saniye aralıkla tekrar denenir. Log/sent arasındaki crash tekrar log üretebilir. SaveChanges alarm hook'u tam aggregate'i yükleyen uygulama protokolünü izler; dış SQL yazımlarını izleyen trigger değildir. JSON olarak saklanan plan/sayım satırlarının DB içinde kaset başına FK doğrulaması yoktur; uygulama domain doğrulaması yapar. Deposit ve mutabakat endpoint'lerinde idempotency yoktur. Banknot tanıma girdisi simüledir; fiziksel okuyucu entegrasyonu yapılmadı. Consistency kontrolü bakiye/ledger drift'ini tespit eder; otomatik veri onarımı veya DB kapasite constraint'lerinin yerini alma amacı taşımaz. Swagger açıktır; authentication ödevde zorunlu değildir ve uygulanmadı.

## AI kullanımı

Codex; gereksinim analizi, başlangıç kayıtları, uygulama altyapısı ve testler için kullanıldı. Kullanıcı geri bildirimiyle dakika bazlı plan kaldırıldı. İlk test bağımlılığındaki NuGet güvenlik uyarısı paket güncellenerek giderildi; kesinti testindeki paylaşılan veritabanı bağımlılığı test izolasyonuyla düzeltildi. Ayrıntılı sonuçlar [WORKLOG](docs/WORKLOG.md) içinde tutulur.

v0.5.0 çekim akışı GPT-6.1-Sol alt ajanıyla geliştirildi ve ana ajan tarafından incelendi. İnceleme sonucunda rezervasyonun tüm girdileri mutasyon öncesinde doğrulandı, kalıcı hata yanıtlarına traceId/instance eklendi ve İstanbul iş günü hesabı korundu. Worker, rollback ve süre sınırı testleriyle bu noktalar doğrulandı.

v0.8.0 kur/onay akışı Codex ile geliştirildi. Testler PostgreSQL'in enum metni sıralamasının beklenen TRY/USD/EUR sırasından farklı olduğunu ve .NET timestamp hassasiyetinin DB'den yüksek olması nedeniyle approval replay farkını ortaya çıkardı. API sıralaması ve kayıt öncesi mikrosaniye normalizasyonu düzeltilerek doğrulandı; eski sayımların migration'ında tarihsel kur uydurulmaması açıkça belgelendi.

v0.9.0 rapor/alarm/outbox da Codex ile geliştirildi. İncelemede quote/expiry'nin AtmStore dışında da nakit değiştirdiği dikkate alınıp olay üretimi ortak SaveChanges sınırına bağlandı. Confirm'in ara release/withdrawal adımları yerine net değişim kullanıldı; log ve sent commit'i arasındaki tekrar olasılığı testle gösterilerek belgeye işlendi.

v1.0.0 kapanışında Codex temiz kaynak/boş DB doğrulaması, Release testleri, tüm Postman collection'ları ve belge incelemesi için kullanıldı. Uygulama iş kuralları değiştirilmedi; sürüm alanları ve altyapı collection beklentisi 1.0.0'a taşındı. Test sonuçları ve karşılaşılan araç sorunları teknik günlükte tutuldu.

## Teknik review kayıtları

- [İstek geçmişi ve proje bağlamı](docs/PROJECT_CONTEXT.md)
- [SemVer roadmap](docs/ROADMAP.md)
- [Kronolojik teknik günlük](docs/WORKLOG.md)
- [API sözleşmesi ve örnek istekler](docs/API.md)
- [Teknik kararlar](docs/DECISIONS.md)
- [Gereksinim ve doğrulama matrisi](docs/REQUIREMENTS.md)
