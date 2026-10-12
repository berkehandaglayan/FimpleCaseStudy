# SemVer roadmap

## Sürüm yaklaşımı

Sürümler süre tahmini değildir. Her aşama kendi tamamlanma ölçütleriyle ilerler. Bu belge bir geliştirme hedefidir; satırdaki sürümün varlığı Git tag veya GitHub release oluşturulduğu anlamına gelmez.

- `0.MINOR.PATCH`: ilk geliştirme dönemi. Yeni tamamlanmış kabiliyet bir minor aşamadır; o aşamadaki düzeltmeler patch artışıdır.
- `1.0.0`: belgelenmiş API sözleşmesi ve kabul edilen teslim kapsamı doğrulandığında.
- `1.0.0` sonrasında geriye uyumsuz API değişikliği major, uyumlu özellik minor, uyumlu hata düzeltmesi patch olur.
- Sürüm/tag yalnızca ilgili kod ve doğrulama hazır olduğunda oluşturulur; roadmap maddeleri tamamlanmadan release yayımlanmaz.
- Kaynak: [Semantic Versioning 2.0.0](https://semver.org/).

## Aşamalar

| Sürüm | Hedef | Kapsam | Durum |
|---|---|---|---|
| v0.1.0 | Başlangıç ve izlenebilirlik | Ortam incelemesi, Git başlangıcı, roadmap, bağlam ve teknik günlük | Devam ediyor |
| v0.2.0 | Çalışan uygulama altyapısı | Solution, katmanlar, PostgreSQL, EF migration, Compose, Swagger, ProblemDetails, health | Planlandı |
| v0.3.0 | ATM ve kaset yaşam döngüsü | Part A temel kuralları, B1, B2, yükleme/boşaltma ve hareket kaydı temeli | Planlandı |
| v0.4.0 | Optimal banknot seçimi | B3, tam/alt/üst tutar ve 8 kabul testi | Planlandı |
| v0.5.0 | Güvenli para çekme | B4, B5, B6: quote, confirm, cancel, expiry, limit ve idempotency | Planlandı |
| v0.6.0 | Para yatırma | B7: yönlendirme, taşma, tanınmayan/iade edilen banknotlar | Planlandı |
| v0.7.0 | Nakit denetimi | Part A mutabakat/rapor, B8 tam hareket defteri ve consistency endpoint | Planlandı |
| v0.8.0 | İkinci kullanıcı onayı | B9, kur tablosu, eşik ve bakımda kalma kuralı | Planlandı |
| v0.9.0 | Operasyon raporu ve alarm | B10, TRY toplamı, outbox ve hosted service | Planlandı |
| v1.0.0 | Doğrulanmış teslim | Uçtan uca Postman, gerçek DB testleri, temiz Compose açılışı, README ve son inceleme | Planlandı |

## v0.1.0 - Başlangıç ve izlenebilirlik

- [x] PDF'nin tüm sayfalarını incele.
- [x] Önceki istekleri ve değişen çalışma yöntemini kaydet.
- [x] Kurulu geliştirme araçlarını kontrol et.
- [x] SemVer roadmap, gereksinim matrisi ve teknik kayıtları oluştur.
- [x] Yerel initial commit'i oluştur ve doğrula.
- [x] Uzak repo erişimini ve mevcut geçmişini doğrula (eklenti yetkisi var, branch listesi boş).
- [ ] Yerel Git kimlik doğrulamasını tamamla; başlangıç commit'ini güvenli şekilde gönder.

Kurulum kullanıcının makinesinde devam edebilir. GitHub kimlik doğrulaması uygulama kodunu yerel geliştirmeyi engellemez; remote teslim adımı ayrıca açık kalır.

## v0.2.0 - Çalışan uygulama altyapısı

- [ ] `src/` altında Domain, Application, Infrastructure ve Api projelerini oluştur.
- [ ] `tests/` altında unit ve integration test projelerini oluştur.
- [ ] .NET SDK / paket sürümlerini uyumlu seç ve sabitle; NuGet restore doğrula.
- [ ] DI kayıtları, EF Core DbContext, Npgsql sağlayıcısı ve ilk migration.
- [ ] Dockerfile, Compose API + PostgreSQL servisleri; veritabanı hazır olma kontrolü.
- [ ] API başlangıcında migration uygulaması.
- [ ] Swagger/OpenAPI, ProblemDetails ve anlamlı hata kodları.
- [ ] Health endpoint; veritabanına bağımlı hazır olma kontrolü.
- [ ] Boş veritabanıyla açılış, build ve sağlık kontrolü çalışır.

## v0.3.0 - ATM ve kaset yaşam döngüsü

- [ ] ATM oluşturma ve çoklu ATM izolasyonu.
- [ ] Tam 5 kaset; 1 deposit-only, 4 recycling; kaset başına toplam 100 banknot.
- [ ] TRY/USD/EUR ve 20/50/100/500 doğrulaması; recycling için 1-2 döviz kaseti.
- [ ] Konfigürasyon, yükleme ve boşaltma işlemleri; başlangıç nakdinin kaydı.
- [ ] Yaşam döngüsü geçişleri ve işlem/durum matrisi.
- [ ] Karışık deposit bakiyeleri için para birimi + kupür kırılımı.
- [ ] Bakiye ve değiştirilemez hareket kaydının aynı transaction içinde yazılması.
- [ ] Geçersiz konfigürasyon, kapasite ve durum testleri.

## v0.4.0 - Optimal banknot seçimi

- [ ] Veritabanından bağımsız, sınırlı stoklu en az banknot algoritması.
- [ ] Tam tutar veya en yakın ödenebilir alt/üst alternatif.
- [ ] Üst alternatifte kalan günlük limit filtresi; limitsiz arama yapmayan sınırlandırılmış problem boyutu.
- [ ] Aynı para/kupür için yüksek doluluk oranlı kasete öncelik ve deterministik eşitlik bozma.
- [ ] PDF B3 testlerinin sekizi de geçer; mevcut/rezerve/kullanılabilir stok ayrımı korunur.

## v0.5.0 - Güvenli para çekme

- [ ] Quote durumları, alternatifler, kaset bazlı rezervasyon ve `ExpiresAt`.
- [ ] Confirm ve cancel; 60 saniye sonunda arka plan işiyle otomatik serbest bırakma.
- [ ] İşlem anında expiry kontrolü; worker gecikse bile süresi dolmuş quote onaylanamaz.
- [ ] Kalıcı idempotency kaydı; aynı anahtar/aynı istek aynı yanıt, farklı istek conflict.
- [ ] TRY 20.000, USD/EUR 1.000 günlük kart limitleri; aktif rezervasyonlar dahil.
- [ ] Kart için HMAC kimliği; plaintext kart bilgisi DB/loglarda yok.
- [ ] Aynı ATM ve aynı kartın farklı ATM'lerdeki işlemlerinde transaction/lock stratejisi.
- [ ] Gerçek DB üzerinde paralel quote ve paralel confirm testleri.
- [ ] Confirm-expiry yarışı ve iptal sonrası stok/limit geri kazanımı.

## v0.6.0 - Para yatırma

- [ ] Uygun recycling kasetine yönlendirme; eşleşen birden fazla kasette belirlenmiş sıra.
- [ ] Recycling taşması deposit kasetine; toplam kapasite dolunca iade.
- [ ] Tanınmayan banknotlar işlem dışında ve yanıtta ayrı.
- [ ] Para/kupür/kaset/adet kırılımında yerleşim ve iade yanıtı.
- [ ] Bakiye/hareket atomikliği ve kısmi kabul testleri.

## v0.7.0 - Nakit denetimi

- [ ] Bakımda fiziksel sayım; deposit için para/kupür kırılımı.
- [ ] Sistem-fiziksel farkı ve reconciliation adjustment hareketi.
- [ ] LOAD, UNLOAD, WITHDRAWAL, DEPOSIT, RESERVATION, RESERVATION_RELEASE, RECONCILIATION_ADJUSTMENT kayıtlarının tamamı.
- [ ] Hem fiziksel hem rezerve adetlerin hareketlerden yeniden üretilebilmesi.
- [ ] Consistency endpoint ve kontrollü tutarsızlık testi.
- [ ] ATM/kaset temel bakiye raporu; mevcut/rezerve/kullanılabilir ve para birimi toplamları.

## v0.8.0 - İkinci kullanıcı onayı

- [ ] Seed döviz kurları ve mutabakatta kullanılan kurun sabitlenmesi.
- [ ] TRY fark değeri > 1.000 ise awaiting approval durumu.
- [ ] `X-User` üzerinden işlemi girenden farklı kullanıcının onayı.
- [ ] Bekleyen onay varken hizmete dönüş engeli; eşik sınır testleri.

## v0.9.0 - Operasyon raporu ve alarm

- [ ] B10 raporunun bütün alanları ve genel TRY toplamı.
- [ ] Recycling kullanılabilir adedi %10'un altına düştüğünde alarm.
- [ ] İşlem ve outbox kaydı aynı transaction içinde.
- [ ] Hosted service: olayları oku, logla, sent işaretle; hata halinde yeniden dene.
- [ ] Eşik geçişi, rollback ve yeniden işleme davranışı testleri.

## v1.0.0 - Doğrulanmış teslim

- [ ] B3 kabul testleri ve gerçek PostgreSQL entegrasyon testleri geçer.
- [ ] Postman: create ATM -> configure -> load -> in service -> quote -> confirm -> deposit -> maintenance -> reconcile -> report.
- [ ] Postman response'larından ATM/quote gibi kimlikler otomatik taşınır.
- [ ] Temiz veritabanıyla `docker compose up` çalışır ve migration otomatik uygulanır.
- [ ] README zorunlu bölümleri, varsayımlar, AI kullanımı ve bilinen eksikler gerçeği yansıtır.
- [ ] Teknik günlük ve gereksinim matrisi test kanıtlarıyla günceldir.
- [ ] Kaynaklar, migration'lar, testler, Compose ve Postman teslim edilen commit'te bulunur.

## Her aşamanın kapanış kuralı

Değişiklik açıklanır, ilgili test/doğrulama çalıştırılır, gerçek sonucu günlüğe yazılır ve sonraki adım belirlenir. Kapsam değişirse sebebi kaydedilir. Part B maddelerinin kısmi uygulanması tamamlama işareti için yeterli değildir.
