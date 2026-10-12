# Teknik kararlar ve varsayımlar

Bu kayıt mimari seçimleri teknik review'da gerekçeleriyle anlatmak içindir. **Uygulanmış** açıkça yazılmadıkça aşağıdaki maddeler tasarım planıdır. PDF'nin kesin kuralları `REQUIREMENTS.md` içindedir; burada eklenen yorumlar kaynak gereksinim gibi sunulmaz.

## ADR-001 - Tek uygulama, dört sorumluluk alanı

Durum: v0.2.0 katman ve DI altyapısı; v0.3.0 ATM yönetimi kullanım senaryoları uygulandı.

ASP.NET Core REST API, EF Core code-first ve PostgreSQL. Domain iş kurallarını ve saf algoritmaları, Application kullanım senaryolarını, Infrastructure veritabanı ve dış teknik bağımlılıkları, Api HTTP sözleşmesini içerir. DI uygulama başlangıcında yapılır. Aynı deployment içinde arka plan işleri çalışır.

Gerekçe: Rezervasyon, limit, hareket ve outbox değişikliklerini tek ilişkisel veritabanı transaction'ında tutmak; iş kurallarını HTTP/EF'den bağımsız test edebilmek. Bu kapsam için mikroservis, mesaj broker'ı veya dağıtık transaction eklenmeyecek. Bir generic repository/CQRS kütüphanesi yalnızca somut ihtiyaç çıkarsa değerlendirilir.

Makinede .NET SDK 10.0.400 doğrulandı. global.json minimum 10.0.100 + latestFeature ile aynı major/minor içindeki yüklü feature band kullanımını sağlar. EF Core 10.0.4 / Npgsql sağlayıcısı 10.0.3, Swashbuckle 10.3.0 ve Testcontainers 4.16.0 restore/build ile doğrulandı. Paket sürümleri csproj, dotnet-ef 10.0.4 tool manifest içinde sabittir.

## ADR-002 - Nakit modeli ve stok boyutları

Durum: v0.3.0 Atm, Cassette, CassetteBalance ve CashMovement; v0.5.0 WithdrawalQuote ve IdempotencyRecord uygulandı.

Model adayları: Atm, Cassette, CassetteBalance, CashMovement, WithdrawalQuote, QuoteOption, ReservationLine, CardDailyUsage, IdempotencyRecord, Reconciliation, ReconciliationLine, ExchangeRate, OutboxMessage. Bunlar kesin migration/tablo listesi değildir.

Recycling kaseti tek para/kupür taşır. Deposit kaseti birden çok para/kupür için bakiye satırları taşır; kapasite satır başına değil kasetteki banknotların toplamı için 100'dür.

Her stok satırında fiziksel adet (`OnHand`) ve rezerve adet (`Reserved`) ayrılır; kullanılabilir adet `OnHand - Reserved` olur. Depozit kasetinden çekim yapılamaz. Rezervasyon fiziksel nakdi azaltmaz. Sayılar negatif olamaz ve rezervasyon fiziksel adedi aşamaz; uygulama doğrulaması ile uygun DB constraint'leri birlikte kullanılacak.

Banknot tutarları integer/minor unit veya kesin ondalık temsil ile işlenir; kur hesapları `decimal` olur. Float/double para hesabında kullanılmaz.

## ADR-003 - Optimal banknot seçimi

Durum: v0.4.0 saf Domain seçicisi ve sekiz B3 kabul testi; v0.5.0 HTTP quote/confirm akışına bağlama uygulandı.

Sınırlı stoklu dinamik programlama ile ulaşılabilir tutarların minimum banknot sayısı ve çözümün geri kurulması hesaplanır. Greedy yaklaşım B3 test 1 gibi örnekleri kaçırır. Problem boyutu toplam kullanılabilir stok ve geçerli limitlerle sınırlandırılacak; kullanıcının istediği sınırsız büyüklükte bir tutara göre bellek ayrılmayacak.

Önce para birimi bazında kupür kombinasyonu, sonra kaset bazında dağıtım yapılabilir. Aynı para/kupüre sahip kasetlerde fiziksel doluluk oranı yüksek olana kullanılabilir stok ölçüsünde öncelik verilir. Eşit dolulukta kaset sırası/ID deterministik bağlayıcıdır. Alternatiflerde de minimum banknot hedeflenecek; bu son tercih PDF'deki exact minimum kuralını genişleten tasarım varsayımıdır.

Kaynak test 1: 50 x 3 ve 20 x 10 varken 160, 50 x 2 + 20 x 3 ile ödenir. İlk iş B3'ün 8 örneğini veritabanından bağımsız unit test olarak kodlamak.

Uygulama ayrıntısı: BanknoteSelector aynı para birimindeki recycling kullanılabilir stoklarını kupüre göre toplar. 10 ortak böleniyle ölçeklenmiş, kupür aşamalı bounded dynamic programming tablosu ve ayrı seçim tablosu kullanılır. Bir aşama yalnızca önceki aşamadan okur; aynı stoğun tekrar kullanılması veya geri kurma zincirinin sonradan bozulması engellenir. Arama sınırı min(toplam kullanılabilir değer, kalan limit) / 10'dur. En çok dört kaset * 100 banknot * 500 değer ile aşama başına en fazla 20.001 hücre bulunur. Bellek O(D*S), süre O(D*S*C); D kupür grubu, S ölçekli sınır, C grup stok üst sınırıdır. İstek int.MaxValue olsa da ona göre tablo ayrılmaz.

Tam tutar varsa yalnızca exact döner. Yoksa pozitif erişilebilir tutarlardan en yakın lower/upper seçilir; upper dahil her seçenek verilen kalan limite uyar. Hiç seçenek yoksa üç alan da null olur; bunu HTTP iş hatasına çevirmek quote kullanım senaryosunun sorumluluğudur. Tutarlar tam para birimi cinsinden int'tir. 10'un katı olmayan istek yuvarlanmaz, alternatif aranır. Minimum banknot sayısı eşitliğinde büyük kupür tercihi deterministiktir.

Kaset dağıtımı aynı kupürde fiziksel OnHand azalan, eşitlikte slot artan sırayla yapılır; her kasetten en fazla Available alınır. Tüm kapasiteler 100 olduğundan fiziksel adet sırası doluluk oranı sırasıdır. Atm overload'ı IN_SERVICE ister ve deposit kasetini dışarıda bırakır. Saf stok overload'ı izole algoritma testleri içindir; tam ATM konfigürasyonu kurallarını tekrar uygulamaz. Hesaplama bakiyeyi veya rezervasyonu değiştirmez.

## ADR-004 - Alt/üst seçenekler için rezervasyon

Durum: v0.5.0 uygulandı; alt/üst kaset maksimumu ve seçilmeyen stoğun serbest bırakılması gerçek DB testleriyle doğrulandı. Alternatiflerin paylaşımı kaynakta açıklanmadığı için aşağıdaki kural tasarım varsayımıdır.

PDF iki seçeneği de sunmayı ve sunulan banknotları rezerve etmeyi ister; alternatifler arasında ortak banknotun nasıl sayılacağını belirtmez.

Uygulama: İki alternatifin kaset bazlı planı üretilir; her kasette alternatiflerin gerektirdiği adetlerin maksimumu ayrılır. Aynı quote içindeki ortak banknotlar paylaşılabilir, çünkü yalnızca bir alternatif onaylanabilir. Başka bir quote bu ayrılmış stoğu kullanamaz. Örneğin alt seçenek 100 x 1, üst seçenek 100 x 1 + 20 x 1 ise toplamda 100 x 1 ve 20 x 1 rezerve edilir.

Bir seçenekte olup diğerinde olmayan kupürler birlikte tutulduğundan fiziksel rezervasyonun toplam değeri en yüksek seçenek tutarından büyük olabilir. Günlük limit ise gerçekleşebilecek en yüksek seçenek tutarını sayar. Onay seçilen planı tüketir ve quote'un bütün rezervasyonunu bırakır; iptal/expiry hiçbir nakit tüketmeden rezervasyonu bırakır.

Alt seçenek 0 ise müşteriye 0 tutarlı çekim önerilmez. Ödenebilir pozitif seçenek yoksa quote katmanı INSUFFICIENT_CASH döndürür. v0.4.0'da limit üstü ilk isteğin DAILY_LIMIT_EXCEEDED ile reddedilmesi seçildi; sessizce limit içine küçültülmez. Bu ek tasarım varsayımıdır. Seçici kalan limiti parametre alır; v0.5.0 servis kartın günlük kullanımı ve aktif quote yükümlülüğünü DB'den hesaplar. Seçici tek başına B5'i uygulamaz.

## ADR-005 - Transaction ve eşzamanlılık

Durum: v0.5.0 ortak transaction advisory gate ve ATM satır kilidi uygulandı; paralel quote/confirm, farklı ATM kart limitleri, lock beklerken expiry ve rollback gerçek PostgreSQL üzerinde doğrulandı.

v0.3.0 AtmStore.UpdateAsync transaction başlatıp ATM satırını SELECT FOR UPDATE ile kilitler, kaset/bakiyeleri yükler, domain işlemini çalıştırır ve bakiye ile hareketleri birlikte kaydederdi. O aşamada paralel load testi kapasiteyi doğruluyordu; B6 quote/confirm kanıtı değildi. v0.5.0 aşağıdaki ortak gate ve ayrıca gerçek DB müşteri işlem testlerini ekledi.

Quote/confirm/cancel/expiry ve nakit değiştiren operasyonlar kısa DB transaction'ları kullanır. Bakiye, rezervasyon, günlük limit, hareket, idempotency sonucu ve gerektiğinde outbox aynı transaction sınırında tutarlı değişir.

Önceki öneri kart/para/gün limit satırları ve birden çok satır kilidiydi. v0.5.0'da case-study kapsamı için daha basit ve denetlenebilir tercih yapıldı: bütün nakit mutasyonları önce `pg_advisory_xact_lock(507050)`, sonra ATM parent satırı `FOR UPDATE` alır. Worker, quote, confirm, cancel ve bakım aynı sırayı izler. ATM create nakit değiştirmediği için gate gerekmez. Aynı kart farklı ATM'lerde işlem yapsa da tek gate altında limit toplamı hesaplanır; ayrı CardDailyUsage sayacı ve ilk insert/upsert yarışı yoktur. Çok-instance güvenliği process lock'a dayanmaz. Başka bir uygulama veya SQL yazarı bu protokolü izlemiyorsa advisory kilit onu engellemez.

Bu tercih bağımsız ATM mutasyonlarını da seri çalıştırır; yüksek trafik için kart bazlı kilit ve deterministik çoklu ATM sırasına geçmek gerekir. PostgreSQL komut timeout'u 15 saniyedir; aşırı yük altında timeout ve retry gerekebilir. Bütün expired quote'lar tek transaction'da yüklenip serbest bırakılır; batch/partition veya süre bütçesi uygulanmadı. Günlük kullanım aggregate SUM ile hesaplanır; büyük geçmişte indeksler yanında arşivleme veya atomik sayaç tasarımı gerekir. Bunlar uygulanmış ölçek optimizasyonları değildir.

Kullanıcı onayı beklenirken transaction/kilit açık tutulmaz. Quote ve rezervasyon veritabanında kalıcıdır. Süre dolumu hem worker'da hem işlem anında kontrol edilir. Worker gecikmesi süresi dolmuş quote'u onaylanabilir yapmamalı; yeni quote kontrollerinde ilgili süresi dolmuş rezervasyonlar da temizlenmeli. Çoklu API instance'ında yalnızca process içi `lock` yeterli sayılmaz.

Saat global gate alındıktan sonra TimeProvider'dan okunur; lock bekleyerek süresi dolan quote onaylanamaz. Bu an transaction kararlarının zaman referansıdır, HTTP yanıtının gönderildiği an değildir. İşlem önce ayrı commitli expiry temizliği yapar, ardından business transaction gate'ini alıp expiry'yi yeniden kontrol eder. İlk cleanup sonrası iki gate arasında yeni expiry olursa business validation conflict'inde de cleanup commit korunur; yeni nakit/quote mutasyonlarının domain doğrulaması tamamlanmadan değişiklik yapılmaz. Beklenmeyen DB hatası transaction'ı bütünüyle geri alır. ReservationRelease ve Withdrawal hareketleri ayrı yazılır; balance satırı EF tarafından son fiziksel/rezerve haliyle tek UPDATE alır, geçici constraint ihlali oluşmaz.

Kaynaklar: [PostgreSQL explicit locking](https://www.postgresql.org/docs/17/explicit-locking.html), [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

## ADR-006 - Idempotency

Durum: v0.5.0 kalıcı confirm sonuçları uygulandı; aynı key paralel replay ve gerçek API restart sonrası byte eşitliği doğrulandı.

Confirm'in `Idempotency-Key` değeri, istek parmak izi, HTTP sonucu ve yanıtı kalıcı kayıtta tutulur. Aynı anahtar/aynı istek aynı sonucu alır; farklı payload conflict olur. DB unique constraint paralel aynı anahtar yarışını korur. Quote durum geçişi, nakit hareketi ve tamamlanmış sonuç aynı transaction'da kaydedilir.

Quote daha önce onaylanmış olsa bile başarılı aynı anahtar tekrarında saklanan sonuç döndürülür. Farklı anahtarla aynı quote'u tekrar onaylamak nakit çıkışına yol açamaz. Key bütün confirm endpoint'lerinde globaldir; fingerprint canonical DTO üzerinden ATM/quote/option SHA-256'sıdır. Key DB primary key'i ve ortak gate paralel yarışı korur. Quote/cancel için key zorunluluğu eklenmedi; bu kapsam confirm içindir.

Geçerli key ile tamamlanan 200 ve işlenen 400/404/409 sonuçları JSON metni, HTTP status ve ilk traceId/instance ile süresiz saklanır. Yanlış option ve bulunamayan quote hataları da kalıcıdır; başka payload için yeni key gerekir. Hatalı JSON/key, çelişen mevcut key ve rollback edilen 500 ilk sonucu değiştirmez veya yeni kalıcı sonuç oluşturmaz. Transaction rollback sonrası aynı key tekrar denenebilir. HTTP tarih header'ının byte eşitliği iddia edilmez; status/body eşitliği doğrulanmıştır. Retention/temizlik politikası uygulanmadı.

## ADR-007 - Kart kimliği, zaman ve günlük limit

Durum: v0.5.0 HMAC, kart/para günlük limit ve İstanbul gün sınırı uygulandı; gün/gece yarısı yorumu tasarım varsayımıdır.

Kart numarası yalnızca 12-19 ASCII rakam olarak kabul edilir; boşluk/ayraç reddedilerek tek canonical girdi sağlanır. Uygulama sırrıyla HMAC-SHA256 kimliğine çevrilir. Veritabanına ve uygulama loglarına plaintext kart numarası yazılmaz. HMAC anahtarı en az 32 UTF-8 byte configuration/secret olarak sağlanır; uygulama yoksa/kısaysa başlangıçta hata verir. Compose ve `.env.example` içindeki değer açıkça geliştirme örneğidir. Tüm instance/restart'larda aynı sır gerekir; rotasyon kimlikleri değiştireceği için güvenli rotasyon/migration ayrı iştir. Bu case study'de gerçek kart verisi yerine sentetik test girdileri kullanılır.

Limit bütün ATM ağı için kart + para birimi + gün bazındadır: TRY 20.000; USD/EUR 1.000. Günlük tüketim ayrı mutable sayaç yerine aynı transaction'daki Confirmed quote'ların gün içindeki ConfirmedAmount toplamından türetilir. Kart/para için halen açık ve süresi dolmamış quote'ların en yüksek seçenek tutarı buna eklenir; fiziksel banknot rezervasyonu toplam değeri kullanılmaz. Zaman damgaları UTC, iş günü `Europe/Istanbul` kabul edilir. Test edilebilir TimeProvider ve worker'da bir saniyelik PeriodicTimer kullanılır; expiry testleri saati ilerletir, gerçekten 60 saniye beklemez.

Önceki açık gece yarısı sorusu v0.5.0'da çözüldü: quote gece yarısında iptal edilmez; createdAt + tam 60 saniye korunur. Geçerli açık quote eski gün oluşturulsa da yeni günün limitinden yer ayırır; confirm anında yeni iş günü ve diğer rezervasyonlar yeniden kontrol edilir. Çekim confirm gününe yazılır. Böylece önceki günkü tamamlanmış tüketim yeni güne taşınmaz, açık rezervasyon da limit hesabından düşmez. İstanbul 23:59:40'ta 19.500 confirm +500 açık quote; ertesi gün +19.500 quote ve iki confirm toplam20.000 senaryosu doğrulandı. `now >= ExpiresAt` süresi dolmuştur; tam60 saniye reddedilir.

## ADR-008 - Değiştirilemez hareket defteri

Durum: v0.3.0 LOAD/UNLOAD ve append-only koruma; v0.5.0 RESERVATION/RESERVATION_RELEASE/WITHDRAWAL uygulandı. Deposit/mutabakat hareket üreticileri ve consistency endpoint planlandı.

Her hareket kaset, para birimi, kupür, fiziksel adet değişimi, rezervasyon adet değişimi, işlem tipi, ilişkilendirme kimliği ve zamanı taşır. Örneğin rezervasyonda fiziksel delta 0, rezerve delta +n; serbest bırakmada 0 ve -n. Çekimde fiziksel delta -n ve ayrılmış miktarın release hareketi ayrı izlenir; ikili sayım yapılmaz.

Bakiye satırları hızlı okuma için tutulur; hareketlerin toplamı aynı fiziksel/rezerve bakiyeyi üretmeli. Mevcut hareketler update/delete edilmez; hata düzeltme ters/düzeltici yeni hareketle yapılır. v0.3.0 migration'ında PostgreSQL trigger UPDATE/DELETE/TRUNCATE işlemlerini reddeder; EF SaveChanges koruması da hareket değişikliği/silinmesini engeller. DB yöneticisinin trigger kaldırma yetkisine karşı bir güvenlik garantisi değildir. Hareket INSERT'i bilerek başarısız kılınarak bakiye ve durum değişikliğinin de rollback olduğu doğrulandı.

Consistency endpoint aynı tutarlı okuma görünümünde kayıtlı ve hesaplanmış adetleri karşılaştırır; normal eşzamanlı işlem sahte fark üretmemeli. Ledger altyapısı v0.3.0'da başlar, tüm hareket türleri ve denetimi v0.7.0'da tamamlanır.

## ADR-009 - Yaşam döngüsü ve bakım

Durum: v0.3.0 temel yaşam döngüsü/dolu kaset koruması; v0.5.0 bakımda açık quote iptali uygulandı. Bekleyen mutabakat onayı koruması planlandı.

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

Durum: v0.3.0 domain/gerçek DB/yönetim Postman, v0.4.0 B3 ve v0.5.0 müşteri çekimi/concurrency/replay/expiry testleri uygulandı. Güncel kanıt WORKLOG içindedir.

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

İstek/yanıt örnekleri ve endpoint listesi [API sözleşmesinde](API.md) bulunur. v0.3.0'da yalnızca hazır olan rezervasyon alanları v0.5.0 quote/confirm/cancel/expiry kullanım senaryolarıyla doldurulur.

## ADR-016 - Quote planı ve limitin kalıcı temsili

Durum: v0.5.0 uygulandı; DTO/domain sınırı ve restart replay testleriyle doğrulandı.

v0.3.0 aday model listesinde ayrı QuoteOption, ReservationLine ve CardDailyUsage vardı. v0.5.0'da seçenekler ve kaset bazlı rezervasyon satırları immutable plan snapshot'ı olarak WithdrawalQuote üzerinde JSON text tutuldu. Bu seçenekler sonradan yeniden hesaplanmaz; confirm teklif edilen planı uygular. Süre/durum/tutar alanları sorgulanabilir ilişkisel kolonlardır. Kart/gün tüketimi ConfirmedAmount toplamı ve halen açık LimitReservation toplamından türetilir; ikinci bir mutable günlük sayaç ile quote verisi arasındaki tutarlılık riski azaltıldı.

Gerekçe: küçük case-study kapsamındaki sabit dört recycling kaseti için daha az entity/join, anlaşılır migration ve replay'de değişmeyen snapshot. Tradeoff: JSON plan satırlarına DB foreign key/check uygulanmaz; doğru kaset/slot/para/kupür/adet ve planın rezervasyonla kapsanması domain'de iki aşamalı doğrulanır. Doğrudan DB JSON değişiklikleri güvenli yönetim arayüzü sayılmaz. Büyük tarihçede aggregate limit hesabı ve plan raporlama için ilişkisel satırlara/sayaçlara geçiş yeniden değerlendirilebilir. Bu bir performans benchmark sonucu değildir.
