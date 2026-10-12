# FimpleCaseStudy

## Genel Bakış

ATM Cash Management Backend, ATM ağındaki nakit envanterini ve finansal işlem yaşam döngüsünü yöneten bir REST API'dir. ASP.NET Core 10, EF Core ve PostgreSQL üzerinde çalışır; ATM/kaset yönetimi, banknot seçimi, rezervasyonlu para çekme, günlük limit, kalıcı idempotency, para yatırma, mutabakat ve ikinci kullanıcı onayı, hareket defteri, TRY bazlı raporlama ve transactional outbox işlevlerini kapsar.

Tasarımın temel hedefi, fiziksel nakit, rezervasyonlar ve işlem kayıtları arasındaki tutarlılığı korumaktır. Nakit mutasyonları ilişkisel transaction sınırında yürütülür; eşzamanlı işlemler veritabanı kilitleriyle koordine edilir ve hareket defteri append-only olarak korunur.

Her ATM'de dört recycling ve bir deposit-only kaseti bulunur; kaset kapasitesi toplam 100 banknottur. Recycling tek para/kupür, deposit karışık para/kupür taşır. TRY/USD/EUR ve 20/50/100/500 kupürleri desteklenir. Recycling kasetlerinden bir veya ikisi USD/EUR için konfigüre edilir.

v1.0.0 doğrulamasında temiz kaynak kopyası ve boş PostgreSQL ile Compose açılışı, otomatik migration ve yeniden başlatma sonrası veri kalıcılığı kontrol edildi. 106 unit test, 63 gerçek veritabanı entegrasyon testi ve beş Postman collection'ında 60 istek / 142 kontrol başarılı oldu. İstek ve yanıt yapıları [API sözleşmesi](docs/API.md) içindedir. Sürüm ve doğrulama kayıtları [ROADMAP](docs/ROADMAP.md) ve [WORKLOG](docs/WORKLOG.md) üzerinden izlenebilir.

## Çalıştırma

Uygulamayı çalıştırmak için Docker Desktop'ın Linux engine'i yeterlidir. Host makinede .NET SDK veya PostgreSQL kurulumu gerekmez. Kaynağı alıp repo kökünde başlatın:

```powershell
git clone https://github.com/berkehandaglayan/FimpleCaseStudy.git
cd FimpleCaseStudy
docker compose up
```

API, PostgreSQL sağlık kontrolü başarılı olduktan sonra başlar ve bekleyen EF Core migration'larını otomatik uygular. Altı migration; `cash` şemasını, ATM/kaset/bakiye/hareket tablolarını, ledger koruma trigger'ını, quote/idempotency kayıtlarını, mutabakat geçmişi ve onay alanlarını, kur ve outbox tablolarını oluşturur. Veriler Docker named volume içinde saklanır.

Başlangıç kur verileri `TRY=1`, `USD=40`, `EUR=45` olarak tanımlanmıştır. Bu değerler sabit demo verisidir; güncel piyasa kuru sağlamaz. Kurlar `/api/exchange-rates` üzerinden görüntülenebilir.

- Swagger: http://localhost:8080/swagger
- OpenAPI: http://localhost:8080/swagger/v1/swagger.json
- Hazır olma (DB + migration): http://localhost:8080/health
- API canlılık: http://localhost:8080/health/live
- Sürüm bilgisi: http://localhost:8080/api/info

Portları değiştirmek için `.env.example` dosyasını `.env` olarak kopyalayın ve `API_PORT` / `DB_PORT` değerlerini düzenleyin. Compose içindeki varsayılan DB parolası ve HMAC anahtarı yerel geliştirme örnekleridir. Dağıtımda `WITHDRAWAL_HMAC_KEY`, en az 32 UTF-8 byte içeren bir uygulama sırrı olarak sağlanmalıdır; tüm instance'lar ve yeniden başlatmalar aynı anahtarı kullanmalıdır. Anahtarın değiştirilmesi aynı kart için üretilen kimliği değiştirir. Otomatik anahtar rotasyonu uygulanmamıştır. Portlar `localhost` arayüzüne bağlıdır; `.env` Git'e alınmaz.

İlk açılışta uygulama image'ı derlenir. Kaynak kod güncellendiğinde `docker compose up --build` kullanın. `docker compose down` servisleri durdurur ve veritabanı volume'ünü korur. Sonraki `docker compose up` aynı verileri kullanır; daha önce uygulanan migration'lar yeniden çalıştırılmaz.

### API'yi yerelde geliştirme

Yerel geliştirme için .NET 10 SDK gerekir. PostgreSQL'i başlatıp PowerShell'de aşağıdaki komutları çalıştırın:

```powershell
docker compose up -d db
$env:ConnectionStrings__Database = 'Host=localhost;Database=cash_management;Username=cash_app;Password=local-development-only;Timeout=5'
$env:Withdrawals__HmacKey = 'example-development-only-hmac-key-change-me'
dotnet run --project src/FimpleCaseStudy.Api -- --urls http://localhost:8080
```

Compose API servisi çalışıyorsa önce `docker compose stop api` komutunu kullanın veya yerel API için farklı bir port seçin. `.env` üzerinden DB portu ya da parolası değiştirildiyse bağlantı dizesini de güncelleyin.

## Testler

.NET 10 SDK kurulu ve Docker engine açıkken repo kökünde çalıştırın:

```powershell
dotnet restore
dotnet build FimpleCaseStudy.sln -c Release --no-restore
dotnet test FimpleCaseStudy.sln -c Release --no-build
```

Testcontainers her entegrasyon testi için ayrı PostgreSQL container'ı ve dinamik port oluşturur. Testler mevcut Compose veritabanından bağımsızdır; container'lar test sonunda temizlenir. Kilit, transaction, migration ve constraint davranışları gerçek PostgreSQL üzerinde doğrulanır.

Banknot seçimi testleri sekiz kabul senaryosunu; rezervasyon ve limit sınırlarını, fiziksel doluluk önceliğini, deposit kasetinin çekimden dışlanmasını ve 300 sabit seed'li senaryoda bağımsız exhaustive enumeration karşılaştırmasını kapsar. Entegrasyon testleri yaşam döngüsü, kaset kapasitesi, ATM izolasyonu, paralel nakit işlemleri, günlük limit, idempotent replay, süre dolumu, mutabakat onayı, kur snapshot'ı ve outbox teslim davranışlarını doğrular. Kasıtlı kayıt hatalarında rollback ve EF/SQL seviyesinde ledger değişmezliği de test kapsamındadır. Son çalıştırma sonuçları [WORKLOG](docs/WORKLOG.md) içindedir.

Postman CLI ile çalışan API üzerinde aşağıdaki collection'ları çalıştırın. Postman Desktop üzerinden JSON dosyaları import edilerek Collection Runner da kullanılabilir:

```powershell
postman collection run postman/Infrastructure.postman_collection.json --no-report-events
postman collection run postman/AtmSetup.postman_collection.json --no-report-events
postman collection run postman/Withdrawals.postman_collection.json --no-report-events
postman collection run postman/Deposits.postman_collection.json --no-report-events
postman collection run postman/EndToEnd.postman_collection.json --no-report-events
```

`EndToEnd` collection'ı create ATM -> configure -> load -> in service -> quote -> confirm -> deposit -> maintenance -> reconcile -> report sırasını izler. Ek kontroller; sayım geçmişi, ledger tutarlılığı, kur snapshot'ı, onaysız hizmete dönüş ve yeni sayım engeli, farklı kullanıcı onayı, replay, genel TRY toplamı ve düşük stok olaylarını kapsar.

Ayrı environment dosyası gerekmez: `baseUrl` collection değişkenidir ve işlem kimlikleri yanıtlardan alınır. Her uçtan uca koşu yeni ATM, sentetik kart ve idempotency anahtarı kullanır. Son test ve collection sonuçları [WORKLOG](docs/WORKLOG.md) içindedir.

v1.0.0 temiz kurulumunda tüm collection'lar yeniden doğrulandı:

| Collection | İstek | Başarılı kontrol |
|---|---:|---:|
| Infrastructure | 4 | 8 |
| AtmSetup | 9 | 18 |
| Withdrawals | 16 | 37 |
| Deposits | 8 | 19 |
| EndToEnd | 23 | 60 |

API portu değiştiyse CLI komutlarına `--env-var baseUrl=http://localhost:YENI_PORT` ekleyin. Yerel collection koşuları için Postman hesabı gerekmez; cloud'a sonuç yayınlama işlemi ayrıca kimlik doğrulama gerektirir.

## Migration geliştirme

```powershell
dotnet tool restore
dotnet ef migrations add MigrationName --project src/FimpleCaseStudy.Infrastructure --startup-project src/FimpleCaseStudy.Api --output-dir Persistence/Migrations
```

`dotnet-ef` sürümü repo içindeki tool manifest'inde sabittir. Design-time factory, model üretirken DB bağlantısı açmaz. Başlangıç migration'ı başarısız olduğunda uygulama açılışı durur; servis hazır duruma geçmez. Compose tek API instance'ı çalıştırır.

## Mimari ve temel tasarım kararları

- **Domain:** ATM aggregate, kasetler, kupür bakiyeleri, yaşam döngüsü, nakit doğrulamaları ve sınırlı stoklu dynamic programming banknot seçicisi.
- **Application:** `AtmService`, `IAtmStore`, kullanım senaryosu sözleşmeleri, istek/yanıt DTO'ları ve test edilebilir `TimeProvider` üzerinden zaman bağımlılığı.
- **Infrastructure:** EF Core eşlemeleri ve constraint'ler, PostgreSQL kilitleri, transaction yönetimi, migration'lar, ledger trigger'ı, sağlık kontrolü ve outbox oluşturma/dispatch.
- **Api:** HTTP endpoint'leri, dependency injection composition root, Swagger/OpenAPI ve hata yanıtları.

### Eşzamanlılık ve transaction sınırları

Nakit mutasyonları önce PostgreSQL transaction advisory lock (`pg_advisory_xact_lock`), ardından ATM satırında pessimistic row lock (`FOR UPDATE`) alır. Ortak kilit sırası, stok yarışlarını ve aynı kartın farklı ATM'lerde günlük limit aşmasını önler; rezervasyon temizliği ile bakım geçişlerinin aynı protokolü izlemesini sağlar.

Bakiye, rezervasyon, hareket defteri, quote ve confirm sonucu ilgili mutasyon transaction'ında birlikte kaydedilir. Gerekli düşük stok olayları da aynı transaction'a dahil edilir. Günlük limit hesabı ile stok doğrulaması kilit altında yapılır; doğrulama ve yazım arasına başka bir nakit mutasyonu giremez.

Global advisory lock, bağımsız ATM mutasyonlarını da seri çalıştırır. Bu tercih, tutarlılık kurallarını tek bir kilit protokolüyle denetlenebilir kılarken işlem paralelliğini sınırlar. Yüksek trafik altında kart bazlı kilitler ve deterministik çoklu ATM kilit sırası yeniden değerlendirilmelidir. Kararların gerekçeleri ve ölçek sınırları [DECISIONS](docs/DECISIONS.md) içindedir.

### Banknot seçimi ve rezervasyon

Banknot seçimi, sınırlı stok üzerinde minimum banknot sayısını bulan bounded dynamic programming algoritması kullanır. Tam tutar ödenemediğinde en yakın ödenebilir alt ve üst seçenekler hesaplanır; üst seçenek kalan günlük limite uyar. Aynı para/kupürde kaset dağılımı fiziksel doluluk önceliğine göre yapılır ve yalnız kullanılabilir stok tüketilir.

Fiziksel adet `OnHand`, rezerve adet `Reserved`, kullanılabilir adet `Available = OnHand - Reserved` olarak ayrılır. Alt/üst seçenekler için her kasette gereken maksimum banknot adedi rezerve edilir. Müşteri confirm işlemiyle bir seçeneği kabul ettiğinde seçilen plan uygulanır ve quote'un bütün rezervasyonu serbest bırakılır. Cancel ve süre dolumu fiziksel nakit tüketmeden rezervasyonu bırakır. Quote geçerliliği 60 saniyedir; süre kontrolü hem işlem anında hem arka plan temizliğinde uygulanır.

### Günlük limit, kart kimliği ve idempotency

Kart ve para birimi bazında, İstanbul iş gününde tamamlanan çekimler ile açık quote'ların en yüksek seçenek tutarı günlük limitten düşülür: TRY 20.000, USD/EUR 1.000. Fiziksel rezervasyonun parasal değeri, günlük limit için ayrılan tutardan farklı olabilir. Quote gece yarısında iptal edilmez; 60 saniyelik geçerliliği boyunca yeni günün limitinden de yer ayırır. Gerçekleşen çekim confirm gününe yazılır.

Kart girdisi 12-19 ASCII rakamdan oluşur; boşluk veya ayraç kabul edilmez. Kalıcı kayıtlarda HMAC-SHA256 kimliği kullanılır; plaintext kart numarası veritabanına ve uygulama loglarına yazılmaz.

Confirm sonucunun HTTP status ve response body bilgileri `Idempotency-Key` ile kalıcı olarak saklanır. Aynı anahtar ve aynı istek ilk sonucu döndürür; farklı istekle anahtarın yeniden kullanılması conflict üretir. İşlenen 200/400/404/409 sonuçları replay kapsamındadır; rollback edilen 500 sonucu kalıcı başarı kaydı oluşturmaz. Böylece yeniden başlatma ve istemci tekrarlarında ikinci bir nakit çıkışı engellenir.

### Para yatırma ve kapasite

Müşteri yatırması yalnızca hizmetteki ATM'de yapılır. Eşleşen recycling kasetleri fiziksel doluluk artan, eşitlikte slot artan sırada kullanılır. Taşma deposit kasetine yönlendirilir; kapasiteye sığmayan veya tanınmayan banknotlar iade edilir. İstek grupları gönderim sırasıyla kapasiteyi paylaşır.

Her girdi grubunda kabul edilen ve iade edilen adetlerin toplamı girdi adedine eşittir. Rezerve banknotlar fiziksel kasette bulunduğundan boş kapasite yaratmaz. Bakiye değişiklikleri ile yalnızca kabul edilen banknotların `DEPOSIT` hareketleri atomik olarak kaydedilir.

### Append-only hareket defteri ve mutabakat

Hareket defteri, fiziksel ve rezerve adet değişimlerini ayrı deltalarla saklar. EF Core kayıt korumasına ek olarak PostgreSQL trigger'ı mevcut hareketlerin `UPDATE`, `DELETE` ve `TRUNCATE` işlemlerini reddeder. Nakit düzeltmeleri yeni hareketlerle temsil edilir; geçmiş kayıtlar değiştirilmez.

Mutabakat yalnızca bakım durumunda, sıfır rezervasyonla ve beş kasetin tam fiziksel envanteri üzerinden yapılır. `counted - stored` farkı yeni `RECONCILIATION_ADJUSTMENT` hareketiyle uygulanır. Sayım snapshot'ı, stok ve hareketler aynı transaction'da kaydedilir; fark sıfır olsa da sayım geçmişi korunur.

Consistency endpoint'i fiziksel/rezerve hareket deltalarını toplayıp saklanan adetlerle PostgreSQL `REPEATABLE READ` snapshot'ında karşılaştırır. Sorgular arasına giren nakit işlemi sahte tutarsızlık üretmez. Bu endpoint salt okunur bir denetimdir; tespit edilen farkı otomatik onarmaz.

### Raporlama ve transactional outbox

Rapor; para/kupür ve kaset kırılımında fiziksel, rezerve ve kullanılabilir adetleri, TRY/USD/EUR tutarlarını, ATM seviyesinde `overallTry`, kaset seviyesinde `totalTry` ve kullanılan `exchangeRates` listesini döndürür. Genel fiziksel bakiye `overallTry.onHandAmount` alanıdır. Envanter ve kurlar aynı `REPEATABLE READ` snapshot'ından okunur. Deposit kasetinin kullanılabilir muhasebe bakiyesi, para çekiminde kullanılabilecek stok anlamına gelmez.

Kasetteki `lowCash` alanı anlık durumdur; `/api/atms/{id}/alerts` olay geçmişini sunar. Düşük stok olayları EF Core `SaveChanges` sınırında önceki ve son `Available` değerleri karşılaştırılarak nakit mutasyonuyla aynı transaction'da outbox'a yazılır. Bu ortak sınır quote, confirm ve süre dolumu yollarını da kapsar. Confirm'in ara rezervasyon bırakma ve para çekme adımları yerine net değişim değerlendirilir.

Arka plan worker'ı her saniye en fazla 20 işlenmeye hazır olayı, olay başına transaction ve `FOR UPDATE SKIP LOCKED` ile işler. Row lock, bir olayın paralel worker'lar tarafından aynı anda yayınlanmasını önler. Varsayılan sink yapılandırılmış log üretir; başarılı işlemde `SentAt` kaydedilir, yayın hatasında olay 5 saniye sonra yeniden denenir.

Yayın başarılı olup `SentAt` commit'i başarısız kalırsa aynı olay tekrar yayınlanabilir. Teslim semantiği en az bir kezdir; `eventId` tekrarların korelasyonunu sağlar. Bu hata penceresi gerçek veritabanı testinde aynı `eventId` ile ikinci yayın gözlenerek doğrulanmıştır.

### HTTP sözleşmesi ve sağlık kontrolleri

HTTP hataları `ProblemDetails` modeliyle `code`, `traceId` ve `instance` alanlarını taşır. Domain doğrulama hataları 400, bulunamayan ATM 404, durum ve stok çelişkileri 409 döndürür. Beklenmeyen çalışma zamanı hataları merkezi exception handler üzerinden işlenir.

`/health`, DB bağlantısını ve bekleyen migration bulunmadığını kontrol eder; başarısızlıkta 503 / `DATABASE_NOT_READY` döner. `/health/live` veritabanından bağımsız process canlılığını gösterir.

## Varsayımlar

İş günü sınırı, alternatif rezervasyonları, mutabakat ve alarm politikaları [DECISIONS](docs/DECISIONS.md) içinde gerekçeleriyle belgelenmiştir.

- **Tam sayım:** Recycling kasetinde konfigüre kupürün sıfır dahil açık sayımı gerekir. Deposit `notes` listesi boş olabilir; belirtilmeyen para/kupürler sıfır kabul edilir. Onay beklemeyen sayımın tekrar gönderilmesi yeni geçmiş oluşturur. Mutabakat ATM'yi bakımda bırakır; onay sonrası hizmete dönüş ayrı state isteğidir.
- **Onay eşiği:** Toplam fark `sum(abs(delta * denomination * rateToTry))` ile hesaplanır; artı ve eksi farklar birbirini gizlemez. Tam 1.000 TRY onay gerektirmez; üzeri farklı operatör onayı gerektirir. Kullanılan kurlar ve fark değeri sayımda sabitlenir. Bekleyen onay varken yeni sayım ve hizmete dönüş engellenir.
- **Operatör beyanı:** Sayımda `X-User` yoksa `system` kullanılır; onayda `X-User` zorunludur. Aktör trim/lowercase ile normalize edilir ve yalnız ASCII harf/rakam ile `.`, `_`, `-`, `@` kabul edilir. `ALICE` ve `alice` aynı operatördür. Onay nakdi yeniden değiştirmez; aynı onaylayan tekrarında ilk timestamp korunur.
- **Tarihsel sayımlar:** Kur snapshot'ı bulunmayan eski sayımlar `LEGACY_COMPLETED` olarak korunur. Bugünkü demo kurla tarihsel fark yeniden değerlendirilmez ve geriye dönük onay engeli üretilmez.
- **Alarm politikası:** Recycling `Available` değeri 10 veya üzerindeyken 10'un altına düştüğünde ve ilk `LOADED -> IN_SERVICE` geçişinde düşük stok varsa olay üretilir. Aynı düşük durumda tekrar alarm oluşmaz; stok 10 veya üzerine yükseldiğinde yeni düşüş tetiklenebilir. Deposit-only kaseti kapsam dışıdır. Migration geçmiş düşük stoklar için olay oluşturmaz; anlık rapor bayrağı bu durumu gösterir.
- **Bağımlılık sürümleri:** Docker image'larında .NET 10 ve PostgreSQL 17 major etiketleri kullanılır; digest sabitlemesi uygulanmamıştır. NuGet paket sürümleri proje dosyalarında sabittir.

## Tamamlanmamış işler ve bilinen sınırlamalar

Tanımlanan işlevsel kapsamın geliştirme ve teslim doğrulaması tamamlanmıştır. Mevcut uygulamanın dağıtım ve ölçek sınırları aşağıdadır:

- **Kimlik doğrulama:** Sistem sınırları ve kapsamı gereği kimlik doğrulama mekanizması trusted service context varsayımıyla ele alınmış; odak doğrudan çekirdek nakit mutasyonları ve finansal işlem tutarlılığına verilmiştir. API içinde authentication/authorization uygulanmamıştır. `X-User` operatör beyanıdır ve doğrulanmış kimlik sağlamaz. Swagger erişime açıktır.
- **Ölçek:** Global transaction kilidi nakit mutasyonlarını seri çalıştırır. Süresi dolmuş quote'ların toplu taranması ve günlük limitin quote toplamları üzerinden hesaplanması yüksek hacim için optimizasyon gerektirir.
- **Saklama ve sır yönetimi:** Quote, idempotency, sayım ve outbox kayıtları süresiz saklanır. Arşivleme, retention politikası ve HMAC anahtar rotasyonu uygulanmamıştır.
- **Outbox teslimi:** Varsayılan hedef log sink'idir; harici bildirim sistemi ve dead-letter mekanizması yoktur. Hatalı yayınlar 5 saniye aralıkla yeniden denenir. Yayın ile `SentAt` commit'i arasındaki kesinti tekrar log üretebilir.
- **Yazım protokolü:** Alarm hook'u tam aggregate'i yükleyen uygulama protokolüne dayanır; dış SQL yazımlarını izlemez. JSON olarak saklanan plan ve sayım satırlarına DB seviyesinde kaset başına foreign key doğrulaması uygulanmaz; bu kurallar domain katmanında doğrulanır.
- **Tekrar gönderim:** Deposit ve mutabakat endpoint'lerinde idempotency yoktur. İstemci tekrarları yeni işlem olarak değerlendirilir; confirm için kalıcı idempotency sağlanır.
- **Dış sistemler:** Kurlar sabit başlangıç verisidir; canlı kur ve kur yönetimi endpoint'i yoktur. Banknot tanıma girdisi simüledir; fiziksel okuyucu entegrasyonu uygulanmamıştır.
- **Denetim kapsamı:** Consistency kontrolü bakiye/ledger farkını tespit eder. Otomatik veri onarımı yapmaz ve DB kapasite constraint'lerinin yerini almaz.

## Yapay Zekâ Kullanımı ve Doğrulama

Codex uygulamasında orkestratör olarak GPT-6 Astra kullanılmış sub-agent olarak GPT-6.1 Sol High kullanılmıştır. Gereksinim analizi, uygulama ve test geliştirme ile teknik dokümantasyonda geliştirme hızlandırıcısı olarak kullanılmıştır. Tasarım ve doğrulama sahipliği mühendislik sorumluluğunun parçası olarak ele alınmış; eşzamanlılık sınırları, boundary ve edge-case davranışları, platform uyuşmazlıkları ve finansal veri tutarlılığı kuralları bağımsız mühendislik testleriyle doğrulanmıştır. Algoritma sonuçları bağımsız exhaustive enumeration ile karşılaştırılmış; kilit, rollback, idempotent replay ve outbox hata pencereleri gerçek PostgreSQL üzerinde test edilmiş; PostgreSQL ile .NET arasındaki zaman hassasiyeti farkı mikrosaniye normalizasyonuyla giderilerek replay tutarlılığı doğrulanmıştır. Tespit edilen sorunlar, uygulanan düzeltmeler ve gerçek test sonuçları [WORKLOG](docs/WORKLOG.md) altında şeffaf biçimde belgelenmiştir.

## Teknik inceleme kayıtları

- [SemVer roadmap](docs/ROADMAP.md)
- [Kronolojik teknik günlük](docs/WORKLOG.md)
- [API sözleşmesi ve örnek istekler](docs/API.md)
- [Teknik kararlar](docs/DECISIONS.md)
- [Gereksinim ve doğrulama matrisi](docs/REQUIREMENTS.md)
