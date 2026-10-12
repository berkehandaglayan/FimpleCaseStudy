# API sözleşmesi - v0.5.0

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
| POST | `/api/atms/{id}/state` | İzin verilen operasyonel durum geçişini yap |

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

Quote ve çekim endpoint'leri aşağıdaki bölümde yer alır. Müşteri yatırması, mutabakat ve rapor endpoint'leri henüz yok. `/load` operatörün elle yaptığı yüklemeyi kaydeder.

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
