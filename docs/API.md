# API sözleşmesi - v0.3.0

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

Çekim, quote, müşteri yatırması, mutabakat ve rapor endpoint'leri henüz yok. `/load` müşteri yatırma işlemi değildir; operatörün elle yaptığı yüklemeyi kaydeder.
