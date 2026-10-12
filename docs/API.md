# API sözleşmesi - v0.9.0

Base URL: `http://localhost:8080`. Swagger: `/swagger`. JSON enum değerleri string ve büyük harflidir; durumlarda alt çizgi kullanılır (`IN_SERVICE`). Numeric enum, tanımsız enum ve bozuk JSON 400 ProblemDetails döndürür.

## ATM yönetimi

| Metot | Yol | Amaç |
|---|---|---|
| POST | `/api/atms` | ATM ve beş sabit kasetini oluştur; 201 ve Location döndür |
| GET | `/api/atms?limit=50&offset=0` | ATM özetlerini listele; limit 1-100 |
| GET | `/api/atms/{id}` | ATM, kaset ve para/kupür bazında mevcut/rezerve/kullanılabilir adetleri getir |
| PUT | `/api/atms/{id}/configuration` | Dört recycling slotunun tam konfigürasyonunu ver |
| POST | `/api/atms/{id}/load` | Elle konulan nakdi kaydet |
| POST | `/api/atms/{id}/unload` | Elle çıkarılan nakdi kaydet |
| POST | `/api/atms/{id}/deposits` | Müşteri banknotlarını yönlendir; kabul ve iadeleri döndür |
| POST | `/api/atms/{id}/state` | İzin verilen operasyonel durum geçişini yap |
| POST | `/api/atms/{id}/reconciliations` | Bakımda tam fiziksel sayım ve fark düzeltmesini kaydet |
| GET | `/api/atms/{id}/reconciliations/{operationId}` | Sayım snapshot'ını getir |
| POST | `/api/atms/{id}/reconciliations/{operationId}/approve` | Farklı operatörle pending sayımı onayla |
| GET | `/api/atms/{id}/report` | ATM/kaset kupürleri ve para birimi toplamlarını getir |
| GET | `/api/atms/{id}/consistency` | Fiziksel/rezerve bakiyeyi hareket toplamlarıyla karşılaştır |
| GET | `/api/atms/{id}/alerts?limit=50&offset=0` | Düşük stok alarm geçmişini getir |

Oluşturma:

```json
{ "name": "Kadıköy Şubesi ATM" }
```

Konfigürasyon: slot 1-4 recycling, slot 5 deposit-only olarak sabittir. Aynı para/kupür birden çok recycling slotuna atanabilir. Döviz kaseti sayısı para birimi çeşidi değil, USD/EUR atanmış slot sayısıdır.

```json
{
  "cassettes": [
    { "slot": 1, "currency": "TRY", "denomination": 100 },
    { "slot": 2, "currency": "TRY", "denomination": 50 },
    { "slot": 3, "currency": "TRY", "denomination": 20 },
    { "slot": 4, "currency": "USD", "denomination": 100 }
  ]
}
```

Load ve unload aynı gövde biçimini kullanır. Yalnızca pozitif adet gönderilir; işleme göre hareketin işareti sistem tarafından belirlenir. Slot/para/kupür kombinasyonu istekte bir kez geçebilir. `X-User: operator-a` isteğe bağlıdır; yoksa hareketin aktörü `system` olur. Authentication uygulanmadı.

```json
{
  "lines": [
    { "slot": 1, "currency": "TRY", "denomination": 100, "count": 20 },
    { "slot": 5, "currency": "EUR", "denomination": 50, "count": 2 }
  ]
}
```

İlk yükleme tek batch olarak gönderilir ve en az bir recycling banknotu içerir. Başarılı işlem CONFIGURED -> LOADED geçişini otomatik yapar. Sonraki elle yükleme/boşaltma için ATM'nin MAINTENANCE durumuna gelmesi gerekir. İlk kurulumda CONFIGURED durumunda da nakit işlemlerine izin verilir; UNCONFIGURED durumunda önce konfigürasyon zorunludur. Bu yorum ADR-015'te varsayım olarak kayıtlıdır.

Durum isteği:

```json
{ "state": "IN_SERVICE" }
```

Durum endpoint'inin izin verdiği geçişler LOADED -> IN_SERVICE, IN_SERVICE -> MAINTENANCE ve MAINTENANCE -> IN_SERVICE'dir. CONFIGURED ve LOADED durumlarına konfigürasyon/yükleme operasyonları üzerinden geçilir; doğrudan durum atamasıyla kurallar atlanamaz. Aynı duruma tekrar geçiş geçersizdir.

## Atomiklik ve nakit sınırları

- Her kasetin kapasitesi toplam 100 fiziksel banknottur. Deposit içindeki farklı kupürler aynı kapasiteyi paylaşır.
- Rezerve banknotlar fiziksel kasette kalır; kapasite hesaplamasında boş yer yaratmaz.
- Load/unload isteğindeki tek bir hatalı satır bütün isteği reddeder.
- Aynı ATM'nin konfigürasyon/durum/nakit değişiklikleri parent ATM satırındaki FOR UPDATE kilidiyle sıraya girer.
- Nakit ve hareket kayıtları aynı transaction'da yazılır. Her batch tek OperationId; her satır ayrı CashMovement üretir.
- Dolu kasetin para/kupür ataması değiştirilemez. Önce boşaltılır. Boşalmış eski kupür bakiye satırları sıfır adetle korunur; tarihsel hareketler yeniden yazılmaz.

## Hata sözleşmesi

ProblemDetails içinde `status`, `title`, `detail`, `instance`, `code`, `traceId` bulunur. 400 geçersiz giriş, 404 olmayan ATM, 409 mevcut durum/stokla çelişen işlem anlamına gelir. Beklenmeyen sunucu hataları 500'dür.

Örnek kodlar: `ATM_NOT_FOUND`, `INVALID_ATM_NAME`, `INVALID_CASSETTE_CONFIGURATION`, `INVALID_FOREIGN_CASSETTE_COUNT`, `UNSUPPORTED_CURRENCY`, `UNSUPPORTED_DENOMINATION`, `INVALID_ATM_STATE`, `INVALID_STATE_TRANSITION`, `CASSETTE_NOT_EMPTY`, `CASSETTE_NOTE_MISMATCH`, `CASSETTE_CAPACITY_EXCEEDED`, `INSUFFICIENT_CASH`, `DUPLICATE_CASH_LINE`, `INITIAL_RECYCLING_CASH_REQUIRED`.

Quote, çekim ve müşteri yatırma endpoint'leri aşağıdaki bölümlerde yer alır. Mutabakat ve rapor endpoint'leri henüz yok. `/load` operatörün elle yaptığı yüklemeyi kaydeder.

## Para çekme

Yol öneki `/api/atms/{atmId}/withdrawals`:

| Metot | Yol sonu | Sonuç |
|---|---|---|
| POST | `/quotes` | 201; fiziksel nakdi değiştirmeden 60 saniye rezervasyon |
| GET | `/quotes/{quoteId}` | 200; expiry temizliği sonrası kalıcı quote görünümü |
| POST | `/quotes/{quoteId}/confirm` | 200; seçilen planı tüketir ve bütün rezervasyonu bırakır |
| POST | `/quotes/{quoteId}/cancel` | 200; nakit tüketmeden stok ve limit rezervasyonunu bırakır |

Quote örneği (yalnızca sentetik kart):

```json
{"cardNumber":"9900000000000050","currency":"TRY","amount":130}
```

Kart 12-19 ASCII rakam olmalıdır; boşluk/ayraç kabul edilmez. Tutar pozitif integer tam para birimi cinsindendir. İstenen tutar kalan günlük limiti aşıyorsa 409 `DAILY_LIMIT_EXCEEDED`; pozitif ödenebilir seçenek yoksa 409 `INSUFFICIENT_CASH` döner. ATM `IN_SERVICE` olmalıdır. Quote yanıtı `id`, `atmId`, `currency`, `status`, `createdAt`, `expiresAt`, `options`, `selectedOption` içerir. `options` elemanında `name` (`exact`, `lower`, `upper`) ve `plan` (`amount`, `noteCount`, `lines`) bulunur. Her satır `cassetteId`, `slot`, `denomination`, `count` taşır. Tam tutar varsa yalnızca exact sunulur. Quote durumları string olarak `OPEN`, `CONFIRMED`, `CANCELLED`, `EXPIRED` döner; seçenek isimleri küçük harfe duyarlıdır.

Confirm gövdesi ve zorunlu header:

```http
Idempotency-Key: client-operation-unique-key
```

```json
{"option":"lower"}
```

Key 1-128 ASCII görünür karakter olmalıdır; boşluk kabul edilmez. Kapsam bütün API'nin confirm işlemleridir: ATM, quote ve option birlikte istek parmak izini belirler. JSON property sırası/boşlukları anlamı değiştirmez; deserialize edilen option değeri karşılaştırılır. Aynı key ve aynı istek saklanan status/body'yi byte olarak aynen döndürür. Aynı key başka ATM/quote/option için 409 `IDEMPOTENCY_CONFLICT` verir. Başarılı replay, expiry/cancel/bakım veya API restart sonrasında da orijinal sonucu döndürür; ikinci kez nakit çıkarmaz. Farklı key ile aynı confirmed quote 409 `QUOTE_CONFIRMED` döner.

Geçerli key ile işlenen confirm'in 200, 400 `INVALID_QUOTE_OPTION`, 404 `QUOTE_NOT_FOUND`, 409 terminal durum ve limit hataları kalıcıdır. Retry'da hata sonucu da aynı kalır; düzeltilecek istek için yeni key kullanılmalıdır. Hatalı JSON/eksik veya geçersiz key gibi işlem öncesi doğrulama hataları ve transaction'ı geri alınan beklenmeyen 500 saklanmaz. Böyle bir 500 sonrasında aynı key ile yeniden denemek mümkündür. Idempotency kayıtlarının otomatik silinmesi yoktur. Kalıcı ProblemDetails `code`, `instance`, `traceId` içerir; replay ilk işleme ait traceId'yi korur.

Confirm geçerliliği `now < expiresAt` şeklindedir. Tam 60 saniye sonunda worker gecikse bile confirm 409 `QUOTE_EXPIRED` alır. Cancel tekrarında CANCELLED/EXPIRED görünümü 200 döner; CONFIRMED için 409 alınır. Bakım geçişi açık quote'ları ve rezervasyonlarını aynı transaction'da iptal eder. İş günü Europe/Istanbul, zaman damgaları UTC'dir. Açık quote gece yarısından sonra da yeni günün limitinden yer ayırır; confirm gününün limiti yeniden kontrol edilir.

## Müşteri yatırması

`POST /api/atms/{id}/deposits` yalnızca IN_SERVICE durumunda çalışır ve 200 döndürür. Fiziksel banknot okuyucusunun bildirdiği gruplar alınır; okuyucu/hardware entegrasyonu bu case study'de yoktur.

```json
{
  "notes": [
    { "currency": "TRY", "denomination": 100, "count": 5 },
    { "currency": "EUR", "denomination": 500, "count": 2 },
    { "recognized": false, "count": 2 }
  ]
}
```

`recognized` varsayılanı true'dur. Currency bu girişte ham metindir; TRY/USD/EUR için harf büyüklüğü ve çevre boşlukları normalize edilir. Tanınmayan para birimi, desteklenmeyen/eksik kupür veya recognized=false banknot kabul edilmez; UNRECOGNIZED olarak iade edilir. Tanınmayan girdiye parasal değer atanmaz. Pozitif adet zorunludur. İstek en fazla 100 grup/10.000 banknot ve currency alanı en fazla 32 karakter içerir; null/boş liste, null grup, geçersiz adet veya boyut sınırı ihlali bütün isteği 400 INVALID_DEPOSIT_NOTES ile reddeder. ATM yoksa 404, hizmette değilse 409 döner.

Her girdi grubunda eşleşen recycling kasetleri fiziksel doluluk artan, eşitlikte slot artan sırada kullanılır. Sıradaki kasetin boş kapasitesi doldurulur; taşan miktar diğer eşleşenlere, ardından deposit kasetine gider. Başka para/kupür taşıyan recycling kaseti kullanılmaz. Kapasite bütün fiziksel banknotları içerir; rezerve banknotlar boş yer yaratmaz. Deposit kasetinin kapasitesi bütün para/kupürler için ortak 100'dür. Gruplar istek sırasıyla işlenir; duplicate gruplar aynı kalan kapasiteyi paylaşır.

Yanıt `operationId`, `acceptedCount`, `returnedCount`, `placements`, `returned` içerir. Placement: sıfırdan başlayan `inputIndex`, `cassetteId`, `slot`, normalize `currency`, `denomination`, `count`. Returned: `inputIndex`, orijinal `currency`/`denomination`, `count`, `reason` (UNRECOGNIZED veya CAPACITY_EXCEEDED). Her girdinin kabul + iade adedi gönderilen adede eşittir. Tamamen iade edilen geçerli istek de 200'dür; yeni nakit hareketi oluşmaz.

Kabul edilen stok ve DEPOSIT hareketleri aynı transaction'dadır; yalnızca kabul edilen banknotlar ledger'a girer. Bir işlemde aynı kaset/para/kupür hareketleri birleştirilir, OperationId paylaşılır. Reserve/confirm/deposit aynı PostgreSQL kilit sırasına uyar. Veritabanı hatasında hiçbir kabul kalıcı olmaz. Deposit idempotency desteği bu sürümde yoktur; aynı isteği tekrar göndermek yeni bir yatırma işlemidir. Operasyon kimliği tek başına tekrar engellemez.

## Fiziksel sayım ve mutabakat (v0.8.0)

`POST /api/atms/{id}/reconciliations`, MAINTENANCE ve sıfır rezervasyon ister. Header: `X-User: alice` (yoksa system). Tam fiziksel envanter gönderilir; beş slot birer kez bulunmalıdır.

```json
{
  "cassettes": [
    { "slot": 1, "notes": [{ "currency": "TRY", "denomination": 100, "count": 9 }] },
    { "slot": 2, "notes": [{ "currency": "TRY", "denomination": 50, "count": 1 }] },
    { "slot": 3, "notes": [{ "currency": "TRY", "denomination": 20, "count": 9 }] },
    { "slot": 4, "notes": [{ "currency": "USD", "denomination": 100, "count": 10 }] },
    { "slot": 5, "notes": [{ "currency": "EUR", "denomination": 500, "count": 1 }] }
  ]
}
```

Recycling notes tam bir konfigüre kupür satırıdır; adet sıfır olsa da yazılır. Deposit için notes boş liste olabilir; belirtilmeyen para/kupürler sıfır sayılır. Null notes, duplicate para/kupür, geçersiz kupür/para ve0-100 dışındaki adet400; toplam kaset kapasitesi100 üstü409. Her kaset önce doğrulanır; geçersiz son kaset ilkini değiştirmez.

201 yanıtı ve Location header'ı oluşur. Gövde `operationId`, `atmId`, `actor`, `occurredAt` (UTC), `lines`, `status`, `totalDifferenceTry`, `exchangeRates`, `approvedBy`, `approvedAt` içerir. Her line: `cassetteId`, `slot`, `currency`, `denomination`, `storedCount`, `countedCount`, `delta` (counted-stored). Geçmişte kalan sıfır bakiyeli kupürler korunabilir. Fark sıfır olsa da sayım kaydı oluşur; yalnızca sıfır olmayan farklar fiziksel bakiye ve RECONCILIATION_ADJUSTMENT hareketi yaratır. Kayıt/hareket/bakiye/onay gereği tek transaction'dadır. ATM bakımda kalır. Aynı envanteri yeniden gönderme ayrı sayımdır; idempotency desteği yoktur, fakat pending onay varken yeni sayım engellenir.

`GET /api/atms/{id}/reconciliations/{operationId}` kaydedilmiş snapshot'ı 200 döndürür; başka ATM'ye ait veya olmayan sayım 404 RECONCILIATION_NOT_FOUND. ATM bulunamayan POST 404 ATM_NOT_FOUND, hizmette sayım 409 INVALID_ATM_STATE, rezervasyon varsa 409 ACTIVE_RESERVATIONS.

### Kur snapshot'ı ve ikinci kullanıcı onayı

`GET /api/exchange-rates` TRY/USD/EUR sırasıyla `currency`, `rateToTry` listesi döndürür. Migration seed oranları TRY=1, USD=40, EUR=45 yalnız örnek/demo değeridir; güncel piyasa kuru değildir. Oranlar pozitif ve en fazla altı decimal basamaktır, TRY daima 1. Kur güncelleme API'si eklenmedi. Sayım anındaki üç oran `exchangeRates` alanına kopyalanır; sonraki kur değişikliği eski sayım tutarını değiştirmez.

`totalDifferenceTry = sum(abs(delta * denomination * rateToTry))`. Pozitif/negatif farklar birbirini götürmez. Eşik kesin decimal toplamla karşılaştırılır; toplam <=1000 COMPLETED, >1000 AWAITING_APPROVAL. Yeni sayım snapshot'ı/tutarı doludur; v0.8.0 öncesi geçmiş LEGACY_COMPLETED ve null totalDifferenceTry/exchangeRates taşır. Legacy için güncel kurla geçmiş fark/onay gereği uydurulmaz.

Mutabakat aktörü trim + lowercase ile saklanır; 1-128 ASCII harf/rakam ve `.`, `_`, `-`, `@` kabul edilir. Çoklu/virgüllü X-User veya geçersiz karakter 400 INVALID_ACTOR. Sayımda header yoksa system; approval'da yoksa 400 ACTOR_REQUIRED.

`POST /api/atms/{id}/reconciliations/{operationId}/approve` boş gövdeyle, `X-User: bob` header'ıyla çağrılır. Alice'in sayımını ALICE onaylayamaz: 409 SAME_USER_APPROVAL. Farklı operatör pending kaydı APPROVED yapar, approvedBy/approvedAt(UTC) yazar, 200 verir. Yeni adjustment/bakiye değişimi yoktur. Aynı onaylayan tekrarında ilk onay sonucu/timestamp korunur; başka onaylayan 409 RECONCILIATION_ALREADY_APPROVED. Onay gerektirmeyen sayım 409 APPROVAL_NOT_REQUIRED. Onay ATM'yi otomatik hizmete almaz.

Pending kayıt varken MAINTENANCE -> IN_SERVICE ve yeni reconciliation 409 RECONCILIATION_APPROVAL_PENDING alır. Approval/state/sayım aynı PostgreSQL lock sırasıyla çalışır; başka ATM'ye ait sayım onaylanamaz. Onay sonrası normal state isteğiyle hizmete dönüş mümkündür. Authentication uygulanmadı; X-User operatör kimlik beyanıdır. Eksik/geçersiz kurda sayım 409 INVALID_EXCHANGE_RATES ve rollback; geçersiz DB onay kaydı da transaction'ı geri alır.

## Nakit raporu ve ledger denetimi (v0.9.0)

`GET /api/atms/{id}/report`:200, `atmId`, `name`, `state`, `cassettes`, `notes`, `totals`, `overallTry`, `exchangeRates`. CassetteReport içindeki `cassette` önceki GET ATM kaset sözleşmesidir; `totals` kasetin para birimi toplamları, `totalTry` kasetin kur çevrimli toplamları, `lowCash` anlık düşük stok bayrağıdır. ATM `notes` alanı para/kupür bazında bütün kasetlerin birleşimidir. Totals TRY/USD/EUR için ayrı `onHandNotes`, `reservedNotes`, `availableNotes`, `onHandAmount`, `reservedAmount`, `availableAmount` alanları taşır. Tutarlar tam para birimi cinsinden kesin decimal toplamdır. Available=OnHand-Reserved; deposit Available muhasebe bakiyesidir ve çekilemez.

`overallTry` ve `totalTry` üç alan taşır: `onHandAmount`, `reservedAmount`, `availableAmount`. Genel TRY fiziksel bakiye `overallTry.onHandAmount` alanıdır; her para birimi tutarı `exchangeRates` içindeki `rateToTry` ile çarpılıp toplanır. Kur güncel tablodan, inventory ve kurlar aynı REPEATABLE READ snapshot'ından okunur. Kullanılan oranlar response'tadır. Örneğin TRY1130/USD1000/EUR500 ve seed1/40/45 için toplam63630 TRY olur. Bunlar sabit demo kurlarıdır; canlı kur değildir. Eksik/geçersiz kur409 INVALID_EXCHANGE_RATES, olmayan ATM404 ATM_NOT_FOUND.

`GET /api/atms/{id}/consistency`:200, `atmId`, `isConsistent`, `lines`. Her satır `cassetteId`, `slot`, `currency`, `denomination`, `storedOnHand`, `storedReserved`, `ledgerOnHand`, `ledgerReserved`, `isConsistent` taşır. Hareketlerin fiziksel/rezerve deltaları ayrı toplanır; silinmiş bakiye satırı da anahtar birleşimiyle karşılaştırılır. Bütün satırlar aynı REPEATABLE READ snapshot'ını kullanır. Fark bulunması HTTP hatası değildir:200vefalse döner. Endpoint veri onarmaz. Bu kontrol DBCHECK/kaset kapasitesi denetiminin yerine geçmez; kayıtlı ve ledger'dan türetilmiş adet eşitliğini ölçer. Report/consistency için olmayan ATM404 ATM_NOT_FOUND.

## Düşük stok alarmı ve teslim (v0.9.0)

Konfigüre recycling kasetinde Available>=10'dan<10'a düşüş alarm yaratır. 10 düşük stok değildir;9ve0düşüktür. İlk LOADED->IN_SERVICE geçişinde düşük stoklu recycling kasetleri de alarm verir. Bakımdan tekrar hizmete girişte aynı düşük stok yeniden alarm üretmez; stok >=10'a yükselip yeniden düşerse yeni alarm oluşur. Deposit-only alarm dışıdır. Bu tekrar/ilk hizmet politikası bizim varsayımımızdır; kaynak yalnız%10altı kuralı verir.

Alarm cash.outbox'a nakit/rezervasyon işlemiyle aynı DB transaction'ında eklenir. Hata durumunda nakit ve olay birlikte rollback olur. Quote rezervasyonu alarmı tetikleyebilir; release fiziksel adedi değiştirmeden Available yükseltir. Confirm'in release+withdrawal ara adımları yerine net değişim alınır; replay alarmı tekrarlamaz.

`GET /api/atms/{id}/alerts?limit=50&offset=0` 200 liste döndürür. limit1-100/offset>=0; aksi400 INVALID_PAGINATION, ATM yoksa404 ATM_NOT_FOUND. Her kayıtta `id`, `atmId`, `cassetteId`, `slot`, `currency`, `denomination`, `beforeAvailable`, `available`, `threshold`, `operationId`, `reason`, `occurredAt`, `attempts`, `sentAt` bulunur. Reason INITIAL_SERVICE_LOW_CASH veya AVAILABLE_COUNT_FELL_BELOW_THRESHOLD. SentAt null henüz teslim edilmedi; Attempts kaç deneme yapıldığını gösterir. Report lowCash anlık stoktur; alerts geçmiş olay listesidir.

Hosted service her saniye en fazla20 due event dener. Her olay row lock ile seçilir; paralel worker aynı olayı eşzamanlı yayınlamaz. Sink low-cash warning logu yazıp sent işaretler. Publisher hatasında5saniye sonra retry; iptal veya DB hata rollback'i kilidi bırakır. Log başarılı/sent commit başarısız penceresi aynı eventId ile tekrar loga yol açabilir: en az bir kez teslim, exactly-once değil. Kart/sır/publisher exception metni olay payload'ına yazılmaz. Log dışında harici bir bildirim gönderilmez.
