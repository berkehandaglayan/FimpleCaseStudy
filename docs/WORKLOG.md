# Teknik çalışma günlüğü

Bu belge teknik review'da geliştirme sırasını, seçimleri ve doğrulama kanıtlarını anlatmak için tutulur. README'nin kullanım kılavuzu rolünden ayrıdır. Tarihler Europe/Istanbul bağlamındadır. Bir özellik yalnızca planlandıysa tamamlanmış sayılmaz.

## Kayıt formatı

Her anlamlı adım şu bilgileri içerir: hedef sürüm; çözülen ihtiyaç; yapılan değişiklikler ve dosyalar; teknik gerekçe; kullanılan komut/test ve gerçek sonucu; karşılaşılan hata/düzeltme; kalan iş ve sonraki adım. Commit kimliği sonraki kayıtta veya Git geçmişinde gösterilir; kendi commit'inin hash'ini aynı commit içeriğine yazmaya çalışılmaz.

## 2026-10-12 / 001 - Gereksinimlerin okunması

Hedef: Uygulama öncesi kapsamı anlamak.

Yapılanlar:

- Kullanıcının verdiği beş sayfalık PDF okundu.
- pypdf ile metin çıkarıldı; ilk denemede Windows cp1254 stdout kodlaması PDF'deki ligatür karakterinde UnicodeEncodeError verdi. Python stdout UTF-8'e ayarlanarak çıkarım tekrarlandı ve beş sayfanın tamamı okundu.
- Poppler `pdftoppm` ile sayfalar PNG olarak render edildi; tüm sayfalar görsel olarak incelendi. B3 tablosunun ikinci ve üçüncü sayfa boyunca devam ettiği doğrulandı.
- Part A, B1-B10, teknik zorunluluklar ve teslim kalemleri ayrıştırıldı.
- Geçici sayfa görselleri `tmp/pdfs/` altında tutuldu; `.gitignore` ile Git dışında bırakıldı.

Bulgular: Backend kapsamı; 5 kaset/100 banknot sınırı; tam tutar için greedy'nin yetersizliği; 60 saniye rezervasyon; global kart günlük limiti; kalıcı idempotency; gerçek DB ile concurrency kanıtı; immutable hareket defteri ve transaction outbox.

Doğrulama: Metin çıkarımı + beş sayfanın görsel kontrolü. Henüz kod veya uygulama testi yok.

## 2026-10-12 / 002 - Çalışma yönteminin düzeltilmesi

Hedef sürüm: v0.1.0.

İlk yanıtta kullanıcının süre cevabına göre dakika bazlı plan önerilmişti. Kullanıcı bunun kaldırılmasını, birlikte kodlama ve kalıcı teknik kayıtlarla adım adım ilerlenmesini istedi. Plan süre çizelgesinden SemVer aşamalarına çevrildi; deneyim bilgisi kapsamı sınırlamak için kullanılmayacak.

Oluşturulan dosyalar:

- `docs/PROJECT_CONTEXT.md`: ilk istek, süre cevabı, güncel talep, repo bağlantısı ve çalışma anlaşması.
- `docs/ROADMAP.md`: v0.1.0-v1.0.0 hedefleri ve test edilebilir tamamlanma ölçütleri.
- `docs/DECISIONS.md`: algoritma, model, transaction, idempotency, rezervasyon alternatifleri, zaman, mutabakat ve outbox kararları.
- `docs/REQUIREMENTS.md`: A/B maddeleri, teknik teslimler, B3'ün sekiz örneği ve gerçek durum.
- `docs/SETUP.md`: mevcut araçlar, kullanıcı kurulum adımları ve doğrulama komutları.
- `docs/WORKLOG.md`: bu kronolojik teknik review kaydı.
- `README.md`: teslim dokümanı başlangıcı; henüz çalışmayan API için çalıştırma başarısı iddia etmiyor.
- `AGENTS.md`: sonraki geliştirme adımlarında kayıtların güncel tutulması.
- `.gitignore`, `.gitattributes`: build çıktıları/geçici veriler/sırlar için ignore ve metin satır sonu kuralları.

Teknik ayrım: Hareket defterinin temeli nakit işlemleriyle birlikte başlıyor; v0.7.0'da eklenmek üzere ertelenmiyor. B4/B5/B6 ortak tutarlılık sınırına sahip olduğundan aynı sürümde tasarlanıyor. B9 ve B10 için ayrı hedefler bulunuyor.

## 2026-10-12 / 003 - Ortam ve erişim incelemesi

Hedef sürüm: v0.1.0.

Gerçek komut sonuçları:

| Kontrol | Sonuç |
|---|---|
| Klasör içeriği | Önceki PDF incelemesinden `tmp/` vardı; uygulama kaynakları yoktu |
| `git status --short --branch` | Başlangıçta klasör Git repository değildi |
| `git --version` | 2.55.0.windows.5 |
| `dotnet --list-sdks` | 8.0.425 ve 10.0.400 |
| `dotnet --version` | 10.0.400 |
| Git kullanıcı adı/e-posta kontrolü | Commit kimliği tanımlı; kişisel değerler bu belgeye kopyalanmadı |
| `docker --version`, `docker compose version` | Komut bulunamadı |
| `gh auth status` | `gh` komutu bulunamadı |
| `wsl --status` | WSL kurulu olmadığını bildirdi |
| IDE/Postman standart konum ve uninstall taraması | Eşleşen kurulum görülmedi; özel/portable kurulumlar dışlanamaz |

GitHub erişimi:

- Kullanıcı repo URL'sini paylaştı: https://github.com/berkehandaglayan/FimpleCaseStudy
- `git ls-remote --symref ... HEAD` kısıtlı ortamda DNS çözümleme hatası verdi.
- Aynı salt okunur kontrol ağ izniyle tekrarlandı; bu kez kimlik doğrulama eksikliğiyle `unable to get password from user` sonucu verdi.
- Bu sonuç reponun boş, mevcut değil veya kesin private olduğunu kanıtlamaz. Remote geçmişi ve yazma yetkisi henüz doğrulanmadı.
- GitHub bağlantı seçeneği keşfedilip önerildi. Bağlantı ve yerel Git kimlik doğrulaması ayrı kontrol edilecek.

Ortam kurulum rehberi resmi Microsoft WSL, Docker, GitHub CLI ve VS Code dokümanlarıyla kontrol edildi. PostgreSQL'in Compose içinde çalıştırılması planlandı; Windows'a ayrı DB kurulumuna gerek yok.

Uygulama doğrulaması: Henüz solution/build/unit/integration test yok; başarılı uygulama testi iddia edilmiyor.

Sonraki adım: Dokümanları ve başlangıç commit'ini doğrulamak; GitHub girişini tamamlamak; Docker hazır olduğunda v0.2.0 altyapısını kurmak.

## 2026-10-12 / 004 - GitHub bağlantısının doğrulanması

Hedef sürüm: v0.1.0.

İlk yerel Git denemesinden sonra GitHub eklentisi bağlantısı kullanılabilir hale geldi. Repo metadata isteği başarılı oldu: doğru owner/repo, private görünürlük, `main` varsayılan dalı ve `pull`/`push` yetkileri doğrulandı. Branch listesi boş döndü; bu kontrolde uzak repo henüz dal/commit içermiyordu.

Yerelde `git init -b main` başarılı oldu. `git remote add origin` ilk denemede korumalı `.git/config` yazım iznine takıldı; izinli tekrar başarılı oldu. Origin kullanıcının verdiği HTTPS repo adresidir.

Güncel ayrım: GitHub eklentisi üzerinden repo erişimi doğrulandı. Yerel Git/CLI girişinin hazır olduğu henüz doğrulanmadı; eklenti yetkisi terminale otomatik aktarılmış sayılmıyor. Başlangıç commit'i önce yerelde oluşturulacak; CLI kimlik doğrulaması sonrası GitHub'a gönderilecek. Uzak repo geçmişi değiştirilmedi.

## 2026-10-12 / 005 - Doküman doğrulaması ve initial commit

Hedef sürüm: v0.1.0.

- README, AGENTS ve altı docs dosyası olmak üzere sekiz Markdown dosyası UTF-8 okuma, kapanmış kod blokları ve göreli dosya bağlantıları için kontrol edildi; kontroller geçti.
- `git check-ignore tmp/pdfs/assignment-1.png` geçici PDF görsellerinin kapsam dışında olduğunu doğruladı.
- `git diff --cached --check` başarılı oldu.
- Codex, `main` üzerinde `docs: initialize case study roadmap and technical worklog` mesajlı root commit'i oluşturdu. İlk kayıtta 10 dosya vardı; commit sonrası çalışma ağacı temizdi.
- Git'in `.gitignore` ve `.gitattributes` için gösterdiği LF/CRLF uyarısı üzerine bu iki dosya için açık LF kuralı eklendi. Commit sonucu ve bu kayıt, henüz yayımlanmamış aynı başlangıç commit'ine dahil ediliyor; başka bir kullanıcının geçmişi değiştirilmiyor.
- GitHub'a push yapılmadı; yerel CLI kimlik doğrulaması bekleniyor. Repo görünürlüğü veya ayarları değiştirilmedi; release/tag oluşturulmadı.

Uygulama testleri çalıştırılmadı: bu aşamada henüz uygulama kodu yok. Sonraki adım kullanıcı kurulumlarının ardından Docker engine ve GitHub CLI erişimini doğrulamak, başlangıç commit'ini göndermek ve v0.2.0 solution/API altyapısına geçmek.

## 2026-10-12 / 006 - Kullanıcı kurulumlarının kontrolü

Kullanıcı kurulumları yaptığını bildirdi ve Postman'ın ne zaman kurulacağını sordu. Ortamda Postman CLI kurulu olduğu bilgisi komutla doğrulandı.

- `postman --version`: 1.73.0. CLI hazır; Desktop arayüzü ayrı ve şu aşamada isteğe bağlı.
- `docker compose version`: v5.5.1.
- `docker info --format '{{.ServerVersion}}'`: sandbox içinde named pipe erişimi reddedildi; izinli tekrar 29.8.2 döndürdü. Docker engine çalışıyor. Bu ayrım kurulum hatası ile ajan erişim kısıtını karıştırmamak için kaydedildi.
- GitHub CLI executable'ı standart konumda mevcut; hem kısıtlı hem normal kullanıcı izinleriyle `auth status` oturum olmadığını bildirdi.
- `git ls-remote --symref origin HEAD`: kimlik doğrulama isteği etkileşimsiz terminalde karşılanamadı; uzak geçmiş okunamadı ve push denenmedi.
- Yerel `main` initial commit'i fa5b457, kontrole başlarken çalışma ağacı temizdi. Bu kayıt ve SETUP güncellemesi henüz yeni commit'e alınmadı.

Sonraki işlem: Kullanıcının GitHub CLI tarayıcı girişini tamamlaması ve `auth setup-git`; ardından initial commit gönderimi. Docker ve Postman CLI tarafında yeniden kurulum gerekmiyor. Uygulama kodu veya testi bu adımda eklenmedi.

## 2026-10-12 / 007 - Initial commit'in GitHub'a gönderilmesi

Hedef sürüm: v0.1.0, tamamlandı.

- Kullanıcı GitHub girişini tamamladığını bildirdi. Normal kullanıcı izinleriyle `gh auth status` aktif berkehandaglayan hesabını ve HTTPS Git protokolünü doğruladı; kimlik bilgisi değerleri dosyalara yazılmadı.
- Push öncesi `git ls-remote --symref origin HEAD` başarılı ve boş döndü; uzak repoda mevcut branch geçmişi yoktu.
- `git push -u origin main` başarılı oldu, main dalı oluşturuldu ve yerel dal origin/main'i takip edecek şekilde ayarlandı. Force push kullanılmadı.
- `git rev-parse HEAD` ve `git ls-remote origin refs/heads/main` aynı SHA'yı döndürdü: fa5b4579d260ea52f46dacce9a6175d455ca3710. Böylece initial commit'in uzak repoya ulaştığı doğrulandı.
- ROADMAP v0.1.0 tamamlandı olarak güncellendi; SETUP ve REQUIREMENTS'teki eski giriş/push bekleniyor durumları düzeltildi. Önceki başarısız denemeler tarihçe olarak korundu.
- Bu kayıtlar ayrı bir dokümantasyon commit'inde saklanacak; yayımlanmış initial commit değiştirilmeyecek. Release/tag oluşturulmadı.

Uygulama testi yok: bu adım Git erişimi ve başlangıç teslimini doğrular. Sıradaki adım v0.2.0 kapsamında solution, proje referansları ve API altyapısı.

## 2026-10-12 / 008 - v0.2.0 uygulama altyapısı

Kullanıcı uygulamaya başlanmasını istedi. Bu adımın kapsamı roadmap'teki v0.2.0 olarak seçildi; ATM ve para iş kuralları sonraki aşamada kalıyor.

Yapılanlar ve gerekçeleri:

- `FimpleCaseStudy.sln`: dört uygulama katmanı ve iki test projesi. Domain dış bağımlılık taşımıyor; Application Domain'e, Infrastructure Application'a, Api ise Application/Infrastructure'a referans veriyor.
- `global.json`, `Directory.Build.props`: .NET 10, nullable, implicit using, uyarıları hata sayma ve 0.2.0 sürüm metadatası. SDK latestFeature politikası yereldeki 10.0.400 ve Docker .NET 10 SDK ile doğrulandı.
- `Infrastructure/Persistence`: DbContext, design-time factory, üretilen migration + snapshot. İlk migration cash şemasını oluşturur; EF history tablosu otomatik oluşur. İş tabloları henüz yok. Modelde entity bulunmadığı için gereksiz configuration assembly taraması kaldırıldı.
- `Api/Program.cs`: connection string zorunlu; DI, migration-before-run, sistem bilgisi, Swagger/OpenAPI, ProblemDetails, readiness ve liveness. Application'da TimeProvider.System kayıtlı; ileride süre testlerinde değiştirilebilir.
- `Dockerfile`, `.dockerignore`, `compose.yaml`, `.env.example`: Linux API + PostgreSQL 17, non-root API, localhost portları, DB healthcheck, kalıcı volume. Yerel örnek parola configuration ile değiştirilebilir; secrets repoya alınmaz.
- `tests/FimpleCaseStudy.IntegrationTests`: Testcontainers ve WebApplicationFactory; test başına yeni PostgreSQL. Unit test projesi yalnızca altyapı olarak oluşturuldu; sahte başarılı unit test eklenmedi.
- `postman/Infrastructure.postman_collection.json`: info, readiness, liveness ve 404 için dört istek / sekiz assertion. Bu, PDF'deki ATM uçtan uca collection'ı değildir.
- README gerçek çalıştırma, geliştirme, migration ve test komutlarıyla güncellendi. ROADMAP/REQUIREMENTS/DECISIONS uygulanan kapsamı gösterecek şekilde güncellendi.

Karşılaşılan sorunlar ve düzeltmeler:

1. `dotnet new sln` sandbox'ta kullanıcı şablon önbelleğine yazamadı; izinli tekrar başarılı oldu. Kısıtlı ilk build de tamamlanmadı; normal izinlerle build başarılı oldu.
2. Testcontainers 4.8.0 transitif SSH.NET 2024.2.0 için NU1903 güvenlik uyarısı verdi. Uyarı bastırılmadı; Testcontainers 4.16.0'a yükseltildi. Restore ve build temiz geçti.
3. İlk entegrasyon çalıştırmasında 3/4 test geçti; ortak fixture'ın DB stop/start işleminden sonra diğer readiness testi 503 aldı. Mutable DB fixture paylaşımı kaldırılarak her teste ayrı container verildi. Son çalıştırmada 4/4 geçti.
4. Postman CLI ilk denemede sandbox'ın localhost kısıtı yüzünden EACCES verdi. İzinli tekrar aynı collection ile geçti. CLI oturum açılmadığına dair mesaj yazdı ama yerel çalışma exit 0 ve tüm kontroller başarılıydı; Postman hesabı gerektiren bulut paylaşımı yapılmadı (`--no-report-events`).

Doğrulama kanıtları:

| İşlem | Gerçek sonuç |
|---|---|
| `dotnet restore FimpleCaseStudy.sln`, `dotnet tool restore` | Başarılı |
| `dotnet build FimpleCaseStudy.sln --no-restore` | 0 hata, 0 uyarı |
| `dotnet ef migrations add InitializeCashSchema ...` | Migration ve snapshot üretildi |
| Gerçek DB entegrasyon testleri | 4 başarılı, 0 başarısız, 0 atlanan |
| Startup/schema testi | Migration uygulandı, pending yok, cash şeması gerçekten mevcut |
| Kesinti testi | DB kapalıyken readiness 503/DATABASE_NOT_READY, liveness 200 |
| 404/OpenAPI testleri | ProblemDetails code/traceId ve Swagger paths doğrulandı |
| `docker compose up --build -d` | İlk açılışta yeni volume, sağlıklı DB ve çalışan API; kaynak güncellemesi sonrası tekrar build/açılış da başarılı |
| Postman collection | 4 istek, 8 assertion; 0 başarısız; exit 0 |
| `docker compose ps` | API Up, DB healthy; localhost 8080/5432 |

v0.2.0 tamamlandı. API ve PostgreSQL inceleme için çalışır bırakıldı. Henüz bu aşama için Git commit/push veya release/tag yapılmadı. Sonraki aşama v0.3.0: ATM/kaset modeli, geçerli konfigürasyonlar, lifecycle, yükleme/boşaltma ve hareket kaydının temeli. B3 kabul testleri ve Part B concurrency kanıtı henüz tamamlanmış değildir.

## 2026-10-12 / 009 - Altyapı commit'i

Kullanıcı commit edip geliştirmeye devam etmemizi istedi. v0.2.0, `b29c59c` / `feat: bootstrap v0.2.0 API and PostgreSQL infrastructure` commit'i olarak kaydedildi. Staged whitespace kontrolünün bulduğu dosya sonu fazladan boş satırlar temizlendi. Uzak geçmiş değiştirilmedi; bu adımda push veya tag yapılmadı.

## 2026-10-12 / 010 - ATM yaşam döngüsü ve atomik nakit yönetimi

Hedef sürüm: v0.3.0, tamamlandı.

Değişen bileşenler ve teknik gerekçeler:

- `Domain/Cash`: Atm aggregate, beş Cassette, para/kupür bazlı CassetteBalance ve CashMovement. Konfigürasyon, kapasite, lifecycle, load/unload ve aktör doğrulamaları HTTP/EF bağımlılığı olmadan uygulanır. Bir satır hatalıysa batch'in önceki satırları da uygulanmaz.
- `Application/Atms`: IAtmStore, AtmService ve istek/yanıt DTO'ları. Domain nesneleri doğrudan HTTP sözleşmesi yapılmadı; kasetler ve bakiyeler deterministik sırada döner, listeleme sayfalanır.
- `Infrastructure/Persistence`: EF model eşlemeleri, unique/FK/CHECK kuralları ve AtmStore. Mevcut ATM mutasyonları transaction içinde parent ATM satırını FOR UPDATE ile kilitler. Stok ve hareket tek SaveChanges/commit ile yazılır.
- `20261012095350_AddAtmCashModel`: cash.atms/cassettes/balances/movements tabloları, snapshot ve hareket değişikliğini reddeden PostgreSQL trigger. EF tarafında da update/delete koruması var. İlk migration korunarak ikinci migration eklendi.
- `Api/AtmEndpoints.cs`, `DomainExceptionHandler.cs`, `Program.cs`: create/list/get/configure/load/unload/state endpoint'leri, büyük harfli metin enum'ları, 400/404/409 domain hata eşlemesi ve bozuk JSON için ProblemDetails. Sayısal enum değerleri reddedilir.
- Unit/integration testleri ve `postman/AtmSetup.postman_collection.json` eklendi. Sürüm metadatası ve altyapı Postman beklentisi 0.3.0 oldu. README, API sözleşmesi, kararlar, gereksinimler ve roadmap güncellendi.

İlk test derlemesinde xUnit2031, Assert.Single öncesindeki Where kullanımını hata saydı. Assert.Single'ın predicate overload'ına geçildi; uyarı bastırılmadı.

Doğrulamalar ve gerçek sonuçlar:

| İşlem / davranış | Sonuç |
|---|---|
| `dotnet build FimpleCaseStudy.sln --no-restore` | 0 hata, 0 uyarı |
| `dotnet test FimpleCaseStudy.sln --no-restore --logger 'console;verbosity=minimal'` | 24 unit + 12 integration başarılı; 0 başarısız, 0 atlanan |
| Domain testleri | Beş slot, geçerli/geçersiz döviz adedi, kupür/enum, kapasite, batch atomikliği, lifecycle, unload ve dolu kaseti yeniden yapılandırma kuralları doğrulandı |
| Gerçek PostgreSQL yönetim akışı | İki ATM izolasyonu, state geçişleri, fiziksel bakiye, aktör ve hareket toplamları doğrulandı |
| Paralel load | Başlangıç 50 iken iki eşzamanlı +30 isteğinden biri 200, diğeri 409; sonuç 80 ve yalnızca başarılı yüklemelerin hareketleri |
| Append-only | EF silme reddedildi; doğrudan SQL UPDATE, DELETE ve TRUNCATE PostgreSQL 55000 ile engellendi |
| Zorlanmış hareket INSERT hatası | API 500; SQL ayrıntısı dışarı verilmedi; bakiye 0, durum CONFIGURED, hareket sayısı 0: transaction bütünüyle geri alındı |
| Hatalı API girdileri | Sayısal/bilinmeyen enum ve bozuk JSON 400; kapasite çatışması 409; eksik ATM 404 ve ProblemDetails doğrulandı |
| `docker compose up --build -d` | Başarılı; mevcut geliştirme volume'ü korunarak ikinci migration uygulandı |
| Altyapı Postman collection, `--no-report-events` | 4 istek, 8 assertion, 0 başarısız |
| ATM kurulum Postman collection, `--no-report-events` | 9 istek, 18 assertion, 0 başarısız; ID response'dan otomatik taşındı |

Postman çalışmaları yereldir; CLI bulut kimlik bilgisi mesajı yazsa da iki çalıştırma exit 0 ile tamamlandı. Akış create -> configure -> load -> in service -> yasak load -> maintenance -> unload -> inspect -> in service şeklindedir. Demo ATM geliştirme DB'sinde bırakıldı. Testcontainers testleri ayrı geçici DB'ler kullandı. Geçici çalışma logları tmp altında Git dışında tutulur.

Kalan kapsam: B2'nin kaset önceliklendirmesi ve B3 optimal seçim v0.4.0'da; quote/confirm ve kart limiti v0.5.0'da. Bu sürümün parent satır kilidi testi B6'nın tamamlandığını göstermez. LOAD/UNLOAD hareketleri hazır; diğer hareket üreticileri ve consistency endpoint henüz yok. v0.3.0 ayrı geliştirme commit'i olarak kaydedilir; release/tag değildir.

## 2026-10-12 / 011 - GitHub gönderimi ve optimal banknot seçimi

Hedef sürüm: v0.4.0, tamamlandı.

Kullanıcı push yapıp devam etmemizi istedi. `git push origin main` başarılı oldu: uzak main, 5eb5ea8'den 854b7c1'e ilerledi. Böylece v0.2.0 b29c59c ve v0.3.0 854b7c1 GitHub'a gönderildi; force push veya tag kullanılmadı.

Değişen dosyalar ve teknik gerekçeler:

- `Domain/Cash/BanknoteSelector.cs`: RecyclingStock anlık görünümü, kaset bazlı DispenseLine, tutar/nota sayısı taşıyan DispensePlan ve exact/lower/upper sonucu. Saf fonksiyon mevcut/rezerve stokları değiştirmez. ATM overload'ı hizmet durumunu kontrol eder ve deposit kasetini dışlar.
- Aynı para/kupür kullanılabilir stokları gruplanır. Kupür aşamalı bounded dynamic programming, her tutarın minimum banknot sayısını ve geri kurma seçimini saklar. Ayrı aşamalar stok yeniden kullanımını engeller. Banknotların ortak böleni 10 ile tutar uzayı küçültülür; maksimum tablo genişliği 20.001'dir. Kullanıcı int.MaxValue istese bile istek büyüklüğünde bellek ayrılmaz.
- Minimum banknotlu kupür planı bulunduktan sonra kasetler fiziksel doluluk azalan, eşitlikte slot artan sırada kullanılır. Rezerve banknotlar dağıtıma dahil edilmez. Böylece minimum banknot hedefi ile aynı kupür kaset önceliği ayrı ve test edilebilir kurallardır.
- Tam ödeme mümkünse yalnızca exact; değilse pozitif en yakın alt/üst seçenekler döner. Her seçenek kalan limite uyar. İlk istek limiti aşıyorsa conflict kararı ADR-004'e işlendi. Hiç pozitif seçenek yoksa null seçenekler döner; quote katmanının hata eşlemesi sonraki sürümdedir.
- `BanknoteSelectorTests.cs`: PDF'nin sekiz kabul örneği ayrı testler olarak eklendi. İlave testler limit sınırı, tam rezerve stok, para izolasyonu, fiziksel doluluk ile kullanılabilir adet ayrımı, ters giriş sırasından bağımsız sonuç, deposit dışlama, lifecycle, boş stok, sıfır alternatifin dışlanması, 10'un katı olmayan tutar, geçersiz girdiler ve çok büyük istekleri doğrular.
- Sabit seed'li 300 senaryo, DP'den bağımsız dört iç içe döngüyle bütün banknot kombinasyonlarını sayan bir referansla karşılaştırıldı. Exact/nearest seçenekleri, minimum banknot sayısı, gerçek toplam ve kaset stok sınırları kontrol edilir. Bu 300 senaryo bir xUnit testinin içindedir; 300 ayrı test olarak sayılmaz.
- Sürüm metadatası, `/api/info` ve altyapı Postman beklentisi 0.4.0 oldu. README, ROADMAP, REQUIREMENTS ve DECISIONS güncellendi. Bu sürümde HTTP banknot seçimi endpoint'i veya migration eklenmedi.

Doğrulamalar:

| İşlem | Gerçek sonuç |
|---|---|
| Unit test projesi | 45 başarılı, 0 başarısız, 0 atlanan; 21 yeni test vakası |
| `dotnet test FimpleCaseStudy.sln --no-restore --logger 'console;verbosity=minimal'` | 45 unit + 12 gerçek PostgreSQL entegrasyon testi geçti; derleme başarılı |
| B3 kabul örnekleri | 8/8 başarılı; greedy'nin kaçırdığı 160 örneği dahil |
| Bağımsız kombinasyon karşılaştırması | 300/300 senaryoda sonuç ve minimum banknot sayısı eşleşti |
| `docker compose up --build -d` | Başarılı; mevcut DB volume'ü korundu, API yenilendi |
| Altyapı Postman collection, `--no-report-events` | 4 istek, 8 assertion, 0 başarısız; 0.4.0 ve sağlık kontrolleri geçti |
| `git diff --check` | Başarılı |

Kalan iş: v0.5.0'da bu seçici quote transaction'ına bağlanacak; kart limitinin DB hesabı, alternatif rezervasyonları, confirm/cancel/expiry, kalıcı idempotency ve paralel quote/confirm testleri eklenecek. Mevcut seçici tek başına gerçek para çekmez veya günlük kart kullanımını takip etmez. API sözleşmesi v0.3.0 yönetim endpoint'leriyle aynı kaldı. Bu aşama geliştirme sürümüdür; release/tag oluşturulmadı.

## 2026-10-12 / 012 - Güvenli para çekme ve kalıcı işlem sonucu

Hedef sürüm: v0.5.0, uygulandı ve doğrulandı. Kullanıcı GPT-6.1-Sol alt ajanla geliştirme ve ardından ana ajan incelemesi istedi. Commit/push bu aşamada yapılmadı; mevcut HEAD/uzak geçmiş değiştirilmedi. Release/tag yoktur.

Değişen bileşenler ve gerekçeleri:

- `Application/Withdrawals/WithdrawalContracts.cs`: quote/confirm DTO, seçenek/görünüm ve IWithdrawalService sınırı. API entity'leri doğrudan döndürmez.
- `Domain/Cash/Atm.cs`, `CassetteBalance.cs`, `CashMovement.cs`: fiziksel/rezerve adetlerin ayrı mutasyonu; RESERVATION, RESERVATION_RELEASE ve WITHDRAWAL hareketleri. Public domain metodu duplicate/deposit/slot/para/kupür/adet ve plan kapsamını bütünüyle doğruladıktan sonra değiştirir. Reserve/withdraw hizmette, release bakımda da mümkündür.
- `Infrastructure/Persistence/WithdrawalService.cs`, `WithdrawalRecords.cs`, `CashTransactionGate.cs`, `AtmStore.cs`, DbContext/DI: HMAC kimliği, max-option günlük rezervasyonu, quote snapshot, 60 saniye, confirm/cancel/expiry/bakım iptali ve kalıcı idempotency. Bakiye/ledger/quote/idempotency transaction atomikliği korunur. Ayrı daily usage sayacı yerine kalıcı quote toplamı kullanılır.
- Bütün nakit mutasyonlarının PostgreSQL transaction advisory gate -> ATM parent FOR UPDATE sırası, aynı kartın farklı ATM'lerdeki limitini ve worker/bakım yarışlarını korur. Case-study sadeliği için bağımsız ATM'ler de seri çalışır; üretim ölçek iddiası yoktur. Saat gate sonrası okunur. Expiry temizliği ayrı commit ve business transaction içinde yeniden kontrol edilir; daha sonra reddedilen istek temizlik commit'ini kaybettirmez.
- `20261012102731_AddWithdrawalReservations`, designer/snapshot: kalıcı quote/idempotency tabloları, FK/index ve durum/para/tutar/60 saniye/confirmation CHECK kuralları; önceki migration'lar korundu.
- `Api/WithdrawalEndpoints.cs`, `QuoteExpiryWorker.cs`, `Program.cs`: quote/get/confirm/cancel endpoint'leri ve bir saniyelik hosted expiry worker. Kalıcı hata yanıtları ilk traceId/instance alanlarını ve replay'de aynı JSON'u korur. API quote status büyük harflidir.
- `compose.yaml`, `.env.example`: açık geliştirme örneği HMAC key; gerçek secret repoya yazılmadı. API doğrudan başlatılırken en az32 UTF-8 byte key zorunludur; bütün instance/restart'lar aynı key'i kullanmalıdır. Kart girişi yalnızca12-19 ASCII rakam kabul eder.
- `WithdrawalCashTests.cs`, `WithdrawalApiTests.cs`, `ApiFixture.cs`: anlamlı domain invariantları; ayarlanabilir saat, gerçek PostgreSQL, API restart ve yakalanan uygulama logları. Mevcut test izolasyonu korunur.
- Sürüm0.5.0, README/API, ROADMAP/REQUIREMENTS/DECISIONS ve `Withdrawals.postman_collection.json` güncellendi. ADR-004/005/006/007/016 önceki önerilerin neden değiştiğini korur.

İncelemede bulunan ve giderilen noktalar: domain public metodunun dış girdilerde invariant/atomiklik koruması; kalıcı ProblemDetails traceId/instance; İstanbul gece yarısı; global gate beklerken saatin yeniden okunması; HTTP çağırmadan worker testi; zorlanmış confirm INSERT hatasında rollback ve tekrar; eksik quote404 sonucunun kalıcı replay'i. İlk sandbox build 0 hata/0 uyarı yazmasına rağmen exit1 verdi; izinli tekrar temiz derlendi. İlk test koşusu48unit+27integration, ek worker/rollback/sınır koşusu48+31; son doğrulama aşağıdadır. Başarısız test bastırılmadı.

Gerçek doğrulamalar:

| İşlem / doğrulanan davranış | Sonuç |
|---|---|
| `dotnet build FimpleCaseStudy.sln --no-restore` (izinli) | 0 hata, 0 uyarı |
| EF `AddWithdrawalReservations` | Migration ve model snapshot üretildi; gerçek DB startup'ta uygulanıp pending migration olmadığı doğrulandı |
| Son `dotnet test FimpleCaseStudy.sln --no-restore --logger 'console;verbosity=minimal'` | 48 unit +32 gerçek PostgreSQL integration geçti; 0 başarısız/atlanan; ham çıktı Git dışı `tmp/v050-tests.log` |
| Alternatif quote130 ->120/140 | 100x1 +20x2 rezerve; lower confirm100x1 +20x1 tüketti, fazla20 serbest; fiziksel/rezerve ledger toplamları eşleşti |
| 8 paralel quote | Tek201, diğer7 conflict; aynı banknot iki kez ayrılmadı |
| 8 farklı key paralel confirm /6 aynı key confirm | Tek fiziksel çekim; farklı key terminal conflict, aynı key aynı200/body |
| Gerçek API restart ve hata replay | 200 başarı ile400 invalid option ve404 missing quote aynı status/JSON; farklıbody veya quote aynıkey409 |
| TRY20.000, USD/EUR1.000 farklı ATM | Eşzamanlı tamlimit quote'lardan biri başarılı; aktif ve confirmed kullanım ikinci ATM'yi engelledi |
| İstanbul gece yarısı | Önceki gün confirmed19.500 yeni güne taşınmadı; açık500 rezervasyon kaldı; yeni gün confirmed toplam20.000'de ek çekim engellendi;60 saniye korunuyor |
| Cancel/expiry/bakım | Fiziksel stok değişmeden tüm reserved ve limit rezervasyonu geri kazanıldı; ledger release toplamları eşleşti |
| 59/60 saniye ve gate bekleyen confirm | 59 kabul, tam60 conflict; kilit beklerken60'a geçen quote reddedildi |
| Hosted worker (HTTPsiz) | Fakeclock+60 ardından bounded ayrıDBscope polling ile Expired/Reserved0 görüldü |
| Confirm/expiry/bakım yarışı | Tek terminal Expired, tek ReservationRelease, fiziksel çekim yok |
| Withdrawal ledger INSERT zorla hata | HTTP500 SQL ayrıntısı göstermedi; quoteOpen/cash/reserved korundu, idempotency kaydı yok; trigger kaldırılınca aynıkey başarılı |
| HMAC ve plaintext denetimi | Kalıcı kimlik64hex HMAC ile eşleşti; quote/ledger ve yakalanmış uygulama logları kartı içermedi; invalidcard yanıtı kartı yankılamadı |
| `docker compose up --build -d` | Mevcut volume korundu; DB healthy, yeni API ve migration çalıştı |
| Postman altyapı /ATM kurulum /çekim (`--no-report-events`) | 4istek/8assertion +9/18 +15/32; üçü exit0, başarısız0 |

Postman CLI bulut giriş mesajı yazdı; yerel collection'lar başarılıdır ve buluta yayınlama yapılmadı. Çekim Postman ATM'si bakımda, kurulum collection ATM'si hizmette bırakıldı. Testcontainers ayrı geçici DB kullanır; geliştirme volume'ü silinmedi.

Kalan iş ve sınırlar: v0.6.0 müşteri yatırması; sonraki ledger consistency/mutabakat/ikinci onay/outbox/rapor aşamaları. Global gate ve tek transaction'da bütün expiry taraması yüksek trafikte darboğazdır; batch/partition, idempotency retention, geçmiş arşivleme ve HMAC rotasyonu uygulanmadı. JSON planlara satır bazlı DB FK yoktur; domain doğrular. B4/B5/B6 tamamlandı, B8'in kalan hareket üreticileri/consistency endpoint'i tamamlandı iddiası yoktur. Ana ajan bağımsız son inceleme ve kullanıcı kararı sonrasında commit/push ele alınacak.

## 2026-10-12 / 013 - GPT-6.1-Sol çıktısının bağımsız incelemesi

Kullanıcının isteğiyle geliştirme GPT-6.1-Sol alt ajanına verildi. Kullanım limiti nedeniyle kesilen ajan, kullanıcının devam isteği üzerine mevcut dosyalar korunarak yeniden çalıştırıldı. Ana ajan uygulama sırasında ve teslim sonrasında kaynak kodu, migration, test gövdeleri ve teknik belgeleri ayrıca inceledi.

İnceleme bulguları ve düzeltmeler:

- Public rezervasyon metodu ilk halinde bütün satırları doğrulamadan mutasyon yapabiliyordu. Duplicate/deposit/slot/kupür ve çekim planının rezervasyonla kapsanması kontrolleri mutasyon öncesine taşındı; hatalı ikinci çekim satırında stok ve rezervasyonun değişmediği unit test eklendi.
- Kalıcı confirm hata yanıtları ProblemDetails middleware'inden geçmediği için traceId/instance eksikti. Alanlar ilk sonuçla birlikte saklandı; replay aynı gövdeyi koruyor. 404 başlığı da düzeltildi.
- İş günü için UTC yerine mevcut ADR'ye uygun Europe/Istanbul kullanılması istendi. Gece yarısı ve kilit beklerken sürenin dolması gerçek DB testleriyle doğrulandı.
- Otomatik worker davranışı için HTTP çağırmadan DB polling testi; confirm ledger yazımı hatasında quote/bakiye/rezervasyon/idempotency rollback testi; tam 59/60 saniye sınırı testleri eklendi.
- Eski API/ADR metinlerindeki çekim ve rezervasyonun henüz olmadığı ifadeleri güncellendi. Global kilit, JSON plan doğrulaması ve temizleme/saklama ölçek sınırları belgelerle eşleştirildi.

Ana ajan `tmp/v050-tests.log` içinden 48 unit +32 integration sonucunu doğrudan kontrol etti; aynı paketi gereksiz yere yeniden çalıştırmadı. Postman 28 istek/58 assertion başarısı alt ajanın çalıştırma raporundan alındı. Ek bağımsız HTTP kontrolünde `/api/info` sürüm0.5.0 ve `/health` Healthy döndü. Kaynak taramasında ham kartı loglayan çağrı veya EF sensitive-data logging etkinleştirmesi görülmedi; log yakalama testinin kapsamı da incelendi.

Sonuç: İncelemede tespit edilen doğruluk ve sözleşme sorunları giderildi; bu kapsamda açık engelleyici bulgu kalmadı. Bu ifade tüm olası hataların yokluğu garantisi değildir. v0.5.0 dosyaları çalışma ağacındadır; bu adımda commit/push yapılmadı. Sonraki geliştirme v0.6.0'dır.

## 2026-10-12 / 014 - v0.5.0 commit ve teslim kapsamı kontrolü

Kullanıcı yeni özelliklere geçmeden mevcut güncellemenin commit/push edilmesini ve yalnızca gönderilmiş geliştirmelerin teknik şartlar/teslim listesiyle karşılaştırılmasını istedi. v0.6.0 başlatılmadı. Önceki 48 unit +32 gerçek DB testinin ham sonucu yeniden okundu; uygulama kodu değişmediği için test paketi tekrar çalıştırılmadı. README'nin AI notuna GPT-6.1-Sol kullanımı ve incelemede yapılan düzeltmeler eklendi. Roadmap'teki geçici gönderim ifadesi kaldırıldı.

Değerlendirme ölçüsü: Automated tests başlığındaki iki davranış ayrı sayıldığında 11 teknik madde vardır. Altyapı, B3 testleri ve gerçek DB testi mevcut; tam Postman senaryosu deposit, reconcile ve report eksikliği nedeniyle tamamlanmış sayılamaz. Üç teslim başlığından repo ve beş alt bölümü içeren README hazırdır; collection dosyaları mevcut ve çalışıyor olsa da istenen tam akış için Postman teslimi kısmidir. Bu sayım bütün Part A/B işlevlerinin tamamlanma yüzdesi değildir.

Bu kayıt v0.5.0 geliştirme commit'ine dahil edilir. Commit ve push komutlarının gerçek sonucu Git çıktısından doğrulanarak kullanıcıya bildirilecek; başarısız gönderim başarılı gibi kaydedilmeyecek. Secret, yerel .env, test logları ve geçici dosyalar commit kapsamına alınmaz.
Son dosya kontrolü: `git diff --check` exit 0; fazladan EOF boş satırları temizlendi. Git yalnızca mevcut LF/CRLF dönüşüm uyarılarını gösterdi. `docker compose ps` API Up ve PostgreSQL healthy durumunu doğruladı.
