# Teknik kararlar ve varsayımlar

Bu kayıt mimari seçimleri teknik review'da gerekçeleriyle anlatmak içindir. **Uygulanmış** açıkça yazılmadıkça aşağıdaki maddeler tasarım planıdır. PDF'nin kesin kuralları `REQUIREMENTS.md` içindedir; burada eklenen yorumlar kaynak gereksinim gibi sunulmaz.

## ADR-001 - Tek uygulama, dört sorumluluk alanı

Durum: v0.2.0 katman ve DI altyapısı; v0.3.0 ATM yönetimi kullanım senaryoları uygulandı.

ASP.NET Core REST API, EF Core code-first ve PostgreSQL. Domain iş kurallarını ve saf algoritmaları, Application kullanım senaryolarını, Infrastructure veritabanı ve dış teknik bağımlılıkları, Api HTTP sözleşmesini içerir. DI uygulama başlangıcında yapılır. Aynı deployment içinde arka plan işleri çalışır.

Gerekçe: Rezervasyon, limit, hareket ve outbox değişikliklerini tek ilişkisel veritabanı transaction'ında tutmak; iş kurallarını HTTP/EF'den bağımsız test edebilmek. Bu kapsam için mikroservis, mesaj broker'ı veya dağıtık transaction eklenmeyecek. Bir generic repository/CQRS kütüphanesi yalnızca somut ihtiyaç çıkarsa değerlendirilir.

Makinede .NET SDK 10.0.400 doğrulandı. global.json minimum 10.0.100 + latestFeature ile aynı major/minor içindeki yüklü feature band kullanımını sağlar. EF Core 10.0.4 / Npgsql sağlayıcısı 10.0.3, Swashbuckle 10.3.0 ve Testcontainers 4.16.0 restore/build ile doğrulandı. Paket sürümleri csproj, dotnet-ef 10.0.4 tool manifest içinde sabittir.

## ADR-002 - Nakit modeli ve stok boyutları

Durum: v0.3.0 Atm, Cassette, CassetteBalance ve CashMovement uygulandı; diğer modeller planlandı.

Model adayları: Atm, Cassette, CassetteBalance, CashMovement, WithdrawalQuote, QuoteOption, ReservationLine, CardDailyUsage, IdempotencyRecord, Reconciliation, ReconciliationLine, ExchangeRate, OutboxMessage. Bunlar kesin migration/tablo listesi değildir.

Recycling kaseti tek para/kupür taşır. Deposit kaseti birden çok para/kupür için bakiye satırları taşır; kapasite satır başına değil kasetteki banknotların toplamı için 100'dür.

Her stok satırında fiziksel adet (`OnHand`) ve rezerve adet (`Reserved`) ayrılır; kullanılabilir adet `OnHand - Reserved` olur. Depozit kasetinden çekim yapılamaz. Rezervasyon fiziksel nakdi azaltmaz. Sayılar negatif olamaz ve rezervasyon fiziksel adedi aşamaz; uygulama doğrulaması ile uygun DB constraint'leri birlikte kullanılacak.

Banknot tutarları integer/minor unit veya kesin ondalık temsil ile işlenir; kur hesapları `decimal` olur. Float/double para hesabında kullanılmaz.

## ADR-003 - Optimal banknot seçimi

Durum: Planlandı.

Sınırlı stoklu dinamik programlama ile ulaşılabilir tutarların minimum banknot sayısı ve çözümün geri kurulması hesaplanır. Greedy yaklaşım B3 test 1 gibi örnekleri kaçırır. Problem boyutu toplam kullanılabilir stok ve geçerli limitlerle sınırlandırılacak; kullanıcının istediği sınırsız büyüklükte bir tutara göre bellek ayrılmayacak.

Önce para birimi bazında kupür kombinasyonu, sonra kaset bazında dağıtım yapılabilir. Aynı para/kupüre sahip kasetlerde fiziksel doluluk oranı yüksek olana kullanılabilir stok ölçüsünde öncelik verilir. Eşit dolulukta kaset sırası/ID deterministik bağlayıcıdır. Alternatiflerde de minimum banknot hedeflenecek; bu son tercih PDF'deki exact minimum kuralını genişleten tasarım varsayımıdır.

Kaynak test 1: 50 x 3 ve 20 x 10 varken 160, 50 x 2 + 20 x 3 ile ödenir. İlk iş B3'ün 8 örneğini veritabanından bağımsız unit test olarak kodlamak.

## ADR-004 - Alt/üst seçenekler için rezervasyon

Durum: Önerilen varsayım; kodlanmadan örnek ve testlerle kesinleştirilecek.

PDF iki seçeneği de sunmayı ve sunulan banknotları rezerve etmeyi ister; alternatifler arasında ortak banknotun nasıl sayılacağını belirtmez.

Öneri: İki alternatifin kaset bazlı planını üret; her kasette alternatiflerin gerektirdiği adetlerin maksimumunu ayır. Aynı quote içindeki ortak banknotlar paylaşılabilir, çünkü yalnızca bir alternatif onaylanabilir. Başka bir quote bu ayrılmış stoğu kullanamaz. Örneğin alt seçenek 100 x 1, üst seçenek 100 x 1 + 20 x 1 ise toplamda 100 x 1 ve 20 x 1 rezerve edilir.

Bir seçenekte olup diğerinde olmayan kupürler birlikte tutulduğundan fiziksel rezervasyonun toplam değeri en yüksek seçenek tutarından büyük olabilir. Günlük limit ise gerçekleşebilecek en yüksek seçenek tutarını sayar. Onay seçilen planı tüketir ve quote'un bütün rezervasyonunu bırakır; iptal/expiry hiçbir nakit tüketmeden rezervasyonu bırakır.

Alt seçenek 0 ise müşteriye 0 tutarlı çekim önerilmez. Ödenebilir pozitif seçenek yoksa hata döner. Limit üstü ilk istek için hata mı, limit içi alternatif mi sunulacağı API sözleşmesi aşamasında kesinleştirilecek; henüz karara bağlanmış gibi davranılmayacak.

## ADR-005 - Transaction ve eşzamanlılık

Durum: v0.3.0 ATM yönetiminde transaction ve ATM satır kilidi uygulandı. Kart, quote ve idempotency kapsamı planlandı.

Mevcut AtmStore.UpdateAsync transaction başlatır, ATM satırını SELECT FOR UPDATE ile kilitler, kaset/bakiyeleri yükler, domain işlemini çalıştırır ve bakiye ile hareketleri birlikte kaydeder. Paralel yüklemelerde kapasitenin aşılmadığı gerçek PostgreSQL ile doğrulandı. Bu sonuç B6 quote/confirm eşzamanlılığı tamamlandı anlamına gelmez.

Quote/confirm/cancel/expiry ve nakit değiştiren operasyonlar kısa DB transaction'ları kullanır. Bakiye, rezervasyon, günlük limit, hareket, idempotency sonucu ve gerektiğinde outbox aynı transaction sınırında tutarlı değişir.

PostgreSQL satır kilitleri ile ATM işlemlerini sıralamak ve kart/para/gün limit satırını korumak planlandı. Yalnızca ATM kilidi yeterli değildir: aynı kart farklı ATM'lerde de limiti aşabilir. Tüm mutasyonlar ortak kilit sırası izlemeli. Yeni günlük limit satırının ilk kez oluşturulmasındaki yarış unique constraint/upsert ile ele alınmalı. İlgili işlemde kart/ATM/quote kilitleri ve worker sırası birlikte tasarlanıp gerçek DB testleriyle doğrulanacak.

Kullanıcı onayı beklenirken transaction/kilit açık tutulmaz. Quote ve rezervasyon veritabanında kalıcıdır. Süre dolumu hem worker'da hem işlem anında kontrol edilir. Worker gecikmesi süresi dolmuş quote'u onaylanabilir yapmamalı; yeni quote kontrollerinde ilgili süresi dolmuş rezervasyonlar da temizlenmeli. Çoklu API instance'ında yalnızca process içi `lock` yeterli sayılmaz.

Kaynaklar: [PostgreSQL explicit locking](https://www.postgresql.org/docs/17/explicit-locking.html), [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

## ADR-006 - Idempotency

Durum: Planlandı.

Confirm'in `Idempotency-Key` değeri, istek parmak izi, HTTP sonucu ve yanıtı kalıcı kayıtta tutulur. Aynı anahtar/aynı istek aynı sonucu alır; farklı payload conflict olur. DB unique constraint paralel aynı anahtar yarışını korur. Quote durum geçişi, nakit hareketi ve tamamlanmış sonuç aynı transaction'da kaydedilir.

Quote daha önce onaylanmış olsa bile başarılı aynı anahtar tekrarında saklanan sonuç döndürülür. Farklı anahtarla aynı quote'u tekrar onaylamak nakit çıkışına yol açamaz. Anahtar kapsamı ve hata yanıtlarının tekrar davranışı API sözleşmesinde netleştirilecek.

## ADR-007 - Kart kimliği, zaman ve günlük limit

Durum: Planlandı; gün sınırı bir varsayımdır.

Kart numarası normalize edilip uygulama sırrıyla HMAC kimliğine çevrilir. Veritabanına ve request loglarına plaintext kart numarası yazılmaz. HMAC anahtarı kaynak koddan ayrı configuration/secret olarak sağlanır. Bu case study'de gerçek kart verisi yerine sentetik test girdileri kullanılır.

Limit bütün ATM ağı için kart + para birimi + gün bazında tutulur. Onaylanmış tutar + aktif quote yükümlülüğü limiti geçemez. Zaman damgaları UTC, iş günü `Europe/Istanbul` kabul edilir. Test edilebilir bir saat soyutlaması kullanılır; expiry testi için gerçekten 60 saniye beklenmez.

Gece yarısını geçen quote'un hangi güne yazılacağı açık karar konusudur: onay gününün limitini ayrıca kontrol etmek ile günü değişince quote'u geçersiz kılmak farklı sonuçlar verir. B4'ün 60 saniye geçerliliği korunarak kural kesinleştirilmeli ve README'ye eklenmeli.

## ADR-008 - Değiştirilemez hareket defteri

Durum: v0.3.0 LOAD/UNLOAD kayıtları ve append-only koruma uygulandı; diğer hareket üreticileri ve consistency endpoint planlandı.

Her hareket kaset, para birimi, kupür, fiziksel adet değişimi, rezervasyon adet değişimi, işlem tipi, ilişkilendirme kimliği ve zamanı taşır. Örneğin rezervasyonda fiziksel delta 0, rezerve delta +n; serbest bırakmada 0 ve -n. Çekimde fiziksel delta -n ve ayrılmış miktarın release hareketi ayrı izlenir; ikili sayım yapılmaz.

Bakiye satırları hızlı okuma için tutulur; hareketlerin toplamı aynı fiziksel/rezerve bakiyeyi üretmeli. Mevcut hareketler update/delete edilmez; hata düzeltme ters/düzeltici yeni hareketle yapılır. v0.3.0 migration'ında PostgreSQL trigger UPDATE/DELETE/TRUNCATE işlemlerini reddeder; EF SaveChanges koruması da hareket değişikliği/silinmesini engeller. DB yöneticisinin trigger kaldırma yetkisine karşı bir güvenlik garantisi değildir. Hareket INSERT'i bilerek başarısız kılınarak bakiye ve durum değişikliğinin de rollback olduğu doğrulandı.

Consistency endpoint aynı tutarlı okuma görünümünde kayıtlı ve hesaplanmış adetleri karşılaştırır; normal eşzamanlı işlem sahte fark üretmemeli. Ledger altyapısı v0.3.0'da başlar, tüm hareket türleri ve denetimi v0.7.0'da tamamlanır.

## ADR-009 - Yaşam döngüsü ve bakım

Durum: v0.3.0 temel yaşam döngüsü ve dolu kaseti yeniden yapılandırma engeli uygulandı. Quote iptali ve bekleyen mutabakat onayı korumaları planlandı.

PDF akışı: UNCONFIGURED -> CONFIGURED -> LOADED -> IN_SERVICE <-> MAINTENANCE. Configure/load/reconcile yalnızca izin verilen kurulum/bakım durumlarında; deposit/withdrawal yalnızca IN_SERVICE.

Varsayımlar: Bakım geçişinde açık quote'lar güvenli şekilde iptal edilir; ilgili kart rezervasyonları ve stok birlikte serbest bırakılır. Kaset para/kupür değişiminden önce fiziksel ve rezerve stok sıfır olmalı; unload hareketiyle nakit çıkarılır. İlk LOADED geçişinin minimum yük koşulu v0.3.0'da ADR-015 ile kesinleştirildi. Bekleyen mutabakat onayı hizmete dönüşü engeller.

## ADR-010 - Mutabakat ve dört göz onayı

Durum: Temel mutabakat planlandı; B9 yorumları önerilen varsayım.

Recycling için tek kupür sayımı, deposit için para/kupür kırılımı alınır. Fark counted - stored olarak kaydedilir; deposit için yalnızca toplam adet almak parasal farkı açıklamaya yetmez.

PDF, TRY farkların toplamının 1.000'i aşmasını ikinci kullanıcı onayına bağlar. Artı ve eksi farkların birbirini gizlememesi için her farkın mutlak TRY değerlerinin toplamını kullanmak öneriliyor. Tam 1.000 eşik aşımı değildir. Mutabakatta kullanılan kur snapshot'ı saklanır; sonradan kur değişmesi eski onay gereğini değiştirmez.

PDF'nin adjustment oluşturma sırasıyla uyumlu öneri: fiziksel sayım kabul edilince adjustment ve awaiting approval birlikte kaydedilir; ikinci kullanıcı onayı hizmete dönüş engelini kaldırır, adjustment'ı yeniden uygulamaz. Aynı `X-User` onay veremez. Authentication istenmiyor. Bu yorum kod öncesi test örnekleriyle kesinleştirilecek.

## ADR-011 - Yatırma ve kapasite

Durum: Planlandı; eşleşen kasetlerin yatırma sırası varsayımdır.

Eşleşen recycling kasetlerinde boş yer ölçüsünde dağıtım; önerilen sıra düşük doluluk oranı ve eşitlikte kaset sırasıdır. Sonraki taşma deposit kasetine, sığmayan/tanınmayan banknotlar müşteriye döner. Tanınmayan banknotlara bir kupür/değer uydurulmaz. Response bilinen banknotların yerleşimini ve iadeyi ayrı listeler.

Rezerve banknotlar fiziksel kasette durduğundan rezervasyon kaset boşluğu yaratmaz. Deposit kapasitesi fiziksel adet üzerinden hesaplanır.

## ADR-012 - Alarm ve outbox

Durum: Planlandı.

Recycling kullanılabilir adet 10'un altına indiğinde alarm üretilir. Varsayım: aynı düşük stok durumunda her istekte tekrar alarm üretmek yerine >=10'dan <10'a geçiş alarm üretir; tekrar >=10 olduğunda yeni düşüşe hazır olur. Başlangıçta düşük stoklu ATM'nin hizmete alınması için alarm davranışı ayrıca testle tanımlanacak.

Outbox kaydı stok işlemiyle aynı DB transaction'ında oluşturulur. Hosted service loglar ve sent işaretler. Log yazımı ile sent güncellemesi arasında çökme olabileceği için en az bir kez işleme ve olası tekrar logu belgelenir; event ID korelasyon için kullanılır. Exactly-once dış etki iddia edilmez.

## ADR-013 - Test ve teslim kanıtı

Durum: v0.3.0 saf domain testleri, Testcontainers PostgreSQL entegrasyonu ve Postman yönetim akışı uygulandı. B3 ve müşteri işlemleri testleri sonraki sürümlerde.

Saf kurallar ve B3 unit testlerle; kilit/transaction/idempotency gerçek PostgreSQL entegrasyon testleriyle; kullanıcı akışı Postman collection ile doğrulanır. EF InMemory concurrency kanıtı olarak kullanılmaz. Otomatik entegrasyon testleri için test başına ayrı Testcontainers PostgreSQL, yerel API/Postman için Compose seçildi.

Her testte hangi davranışın doğrulandığı ve sonuç `WORKLOG.md` içine yazılır. Testler, uygulanan özellikler ve README aynı gerçek durumu anlatır.


## ADR-014 - Altyapı başlangıcı ve test izolasyonu

Durum: v0.2.0 uygulandı ve doğrulandı.

İlk migration yalnızca cash şemasını oluşturur. Henüz domain modeli olmayan aşamada örnek/yanıltıcı ATM tabloları yaratılmadı; bunlar v0.3.0 iş kurallarıyla birlikte tasarlanacak. Boş EF modelinde üretilen migration gövdesine EnsureSchema/DropSchema eklendi. Migration snapshot araç tarafından üretildi. API her başlangıçta MigrateAsync çağırır; başarısız migration ile HTTP servisi açılmaz. Tasarım zamanı factory EF CLI'nın model üretmek için API'yi veya PostgreSQL'i başlatmasını gerektirmez.

/health/live sadece process canlılığını; /health DB bağlantısını ve bekleyen migration olmamasını kontrol eder. DB kesintisinde readiness 503/DATABASE_NOT_READY döner, canlılık 200 kalır. HTTP hataları ProblemDetails, code ve traceId içerir. Sağlık yanıtı DB exception ayrıntısını dışarıya vermez.

Testcontainers her test için ayrı PostgreSQL container'ı kullanır. İlk tasarımda ortak fixture'daki stop/start işlemi sonraki readiness testini etkiledi. Testlerin mutable altyapı durumunu paylaşması kaldırıldı; kesinti testi kendi container'ını durdurur, fixture her durumda dispose edilir. API WebApplicationFactory ile test edilir; EF InMemory kullanılmaz.

Docker Compose db healthcheck koşulunu bekler. PostgreSQL kalıcı volume kullanır; testler bu volume'e bağlanmaz. API non-root container kullanıcısıyla çalışır ve portlar localhost'a yayınlanır. Yerel geliştirme varsayılan parolası örnektir; gerçek ortam secret yönetimi henüz kapsamda değildir.

Referanslar: [ASP.NET Core ProblemDetails](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0), [Npgsql 10](https://www.npgsql.org/efcore/release-notes/10.0.html).

## ADR-015 - İlk kurulum ve yönetim API sözleşmesi

Durum: v0.3.0 uygulandı ve test edildi. Aşağıdaki slot ve ilk yükleme yorumları PDF'ye ek tasarım varsayımlarıdır.

- ATM oluşturulurken 1-4 recycling, 5 deposit olmak üzere beş sabit slot yaratılır. Configure dört recycling slotunu tek istekte tanımlar; aynı para/kupür birden fazla kasette bulunabilir. Döviz kaseti sayısı bir veya ikidir.
- İlk yükleme CONFIGURED durumunda tek atomik batch olarak yapılır ve en az bir recycling banknotu içermelidir. Başarı LOADED durumuna geçirir; sonraki yükleme için hizmete alıp bakıma geçmek gerekir. Durum endpoint'i kurulum adımlarını atlayamaz. Bakımdan hizmete dönüş için ek minimum nakit koşulu konmadı.
- Manuel load/unload CONFIGURED veya MAINTENANCE durumunda yapılır. Bunlar müşteri deposit/withdrawal işlemleri değildir. Bakımda konfigürasyonu değişen kaset boş ve rezervasyonsuz olmalıdır; değişmeyen dolu kaset korunabilir. Eski para/kupürün sıfır bakiyeli satırı geçmişin okunabilirliği için tutulur.
- Bütün batch doğrulanmadan aggregate değiştirilmez. Aynı slot/para/kupür tekrarları reddedilir. Karma deposit kapasitesi tüm bakiye satırlarının fiziksel toplamıdır. Bu toplam ve döviz kaseti adedi domain kurallarıyla, eşzamanlı işlemlerde ATM kilidi altında korunur; satırlar arası toplam için DB CHECK varmış gibi bir iddia yoktur.
- DB CHECK kuralları geçerli para/kupür, slot/tür, durum ve 0 <= Reserved <= OnHand <= 100 sınırlarını korur. Hareketin composite foreign key'i başka ATM'ye ait kasetle eşleştirilmesini engeller. Aynı batch hareketleri OperationId paylaşır.
- HTTP enum değerleri büyük harfli metindir; sayısal enum kabul edilmez. Hatalar ProblemDetails ve code/traceId içerir: doğrulama 400, bulunamayan ATM 404, iş kuralı çatışması 409. Beklenmeyen 500 hataları SQL ayrıntısını döndürmez.
- X-User manuel nakit hareketinin aktörünü belirler; yoksa system kullanılır. Bu alan authentication sağlamaz. İşlem zamanları TimeProvider üzerinden UTC alınır. Listeleme limit/offset ile sınırlıdır.

İstek/yanıt örnekleri ve endpoint listesi [API sözleşmesinde](API.md) bulunur. Rezervasyon alanları modelde hazır olsa da henüz rezervasyon üreten kullanım senaryosu yoktur.
