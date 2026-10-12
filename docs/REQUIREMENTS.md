# Gereksinim ve doğrulama matrisi

Kaynak: Kullanıcının sağladığı 5 sayfalık Project Assignment - Software Developer / ATM Cash Management Backend PDF'si. Bu özet kaynağın yerini almaz. Durumlar gerçek uygulama/test kanıtına göre güncellenir.

## Part A

| ID | Gereksinim | Hedef | Durum | Planlanan kanıt |
|---|---|---|---|---|
| A1-A2 | TRY/USD/EUR; yalnızca 20/50/100/500, bozuk para yok | v0.3.0 | Planlandı | Geçerli/geçersiz giriş testleri |
| A3-A5 | 5 kaset, her biri 100; 1 karışık deposit-only, 4 tek para/kupür recycling | v0.3.0 | Planlandı | Konfigürasyon ve kapasite testleri |
| A6 | 1-2 recycling kaseti döviz için | v0.3.0 | Planlandı | Alt/üst sınır testleri |
| A7 | Hizmete alınmadan konfigürasyon ve elle yüklenen nakit kaydı | v0.3.0 | Planlandı | Setup + service akışı |
| A8 | Fiziksel sayım ve sistem farkının kaydı | v0.7.0 | Planlandı | Mutabakat hareketi testi |
| A9 | Çekim banknot kırılımı; tam ödenemiyorsa yakın tutar | v0.4.0 | Planlandı | B3 kabul testleri |
| A10 | Müşteri onayından sonra çekim | v0.5.0 | Planlandı | Quote fiziksel bakiyeyi değiştirmez, confirm değiştirir |
| A11 | Yatırmada eşleşen recycling, diğerleri deposit | v0.6.0 | Planlandı | Yönlendirme testleri |
| A12 | ATM banknot adedi ve toplam bakiye görüntüleme | v0.7.0 | Planlandı | Rapor doğrulaması |

## Part B

| ID | Gereksinim | Hedef | Durum | Planlanan kanıt |
|---|---|---|---|---|
| B1 | Birden çok ATM; her ATM'de tüm kurallar | v0.3.0 | Planlandı | ATM izolasyonu |
| B2 | Yaşam döngüsü, işlem kısıtları, aynı kupürde doluluk önceliği | v0.3.0-v0.4.0 | Planlandı | Durum matrisi ve kaset seçimi testleri |
| B3 | Sınırlı stokta en az banknot; en yakın alt/üst, üstte limit kontrolü | v0.4.0 | Planlandı | Aşağıdaki sekiz unit test |
| B4 | 60 saniye rezervasyon, cancel/confirm, expiry, Idempotency-Key | v0.5.0 | Planlandı | Tekrar, iptal, süre ve yarış testleri |
| B5 | Kart/para bazında günlük limit; aktif rezervasyon dahil; plaintext kart yok | v0.5.0 | Planlandı | Limit ve kayıt/log denetimi |
| B6 | Paralel quote'larda çifte rezervasyon yok | v0.5.0 | Planlandı | Gerçek DB ile paralel HTTP istekleri |
| B7 | Tanınmayanları iade; recycling/deposit taşması; ayrıntılı yanıt | v0.6.0 | Planlandı | Kısmi kabul, taşma ve iade testleri |
| B8 | Immutable hareketler, yeniden üretilebilir bakiye, consistency endpoint | v0.7.0 | Planlandı | Ledger replay ve sapma testi |
| B9 | TRY >1.000 farkta başka X-User onayı, hizmete dönüş engeli, kur tablosu | v0.8.0 | Planlandı | Eşik ve ikinci kullanıcı testleri |
| B10 | Ayrıntılı operasyon raporu, düşük nakit alarmı, transaction outbox, worker | v0.9.0 | Planlandı | Rapor, eşik geçişi, rollback, outbox işleme |

## B3 zorunlu kabul testleri

| No | Para | Mevcut banknot | İstek | Beklenen |
|---|---|---|---|---|
| 1 | TRY | 100 x 0, 50 x 3, 20 x 10 | 160 | Tam: 50 x 2 + 20 x 3 |
| 2 | TRY | 50 x 1, 20 x 2 | 80 | Alt 70: 50 x 1 + 20 x 1; üst 90: 50 x 1 + 20 x 2 |
| 3 | TRY | 100 x 5, 20 x 10 | 130 | Alt 120: 100 x 1 + 20 x 1; üst 140: 100 x 1 + 20 x 2 |
| 4 | USD | 100 x 2 | 250 | Alt 200: 100 x 2; üst yok |
| 5 | TRY | 500 x 1, 100 x 0, 20 x 5 | 600 | Tam: 500 x 1 + 20 x 5 |
| 6 | TRY | 100 x 1, 50 x 4, 20 x 5 | 200 | Tam: 100 x 1 + 50 x 2 |
| 7 | TRY | 500 x 1, 100 x 3, 20 x 20 | 1000 | Tam: 500 x 1 + 100 x 3 + 20 x 10 |
| 8 | TRY | 100 x 3, 50 x 1, 20 x 1 | 110 | Alt 100: 100 x 1; üst 120: 100 x 1 + 20 x 1 |

Bu örnekler banknot seçiciyi izole test eder; testte listelenmeyen kasetlerin ATM konfigürasyonu ihlali sayılması gerekmez. API entegrasyon fixture'ları ayrıca geçerli beş kasetli ATM oluşturmalıdır.

## Teknik şartlar ve teslim

| Gereksinim | Hedef | Durum |
|---|---|---|
| ASP.NET Core REST API, DI | v0.2.0 | Planlandı |
| EF Core code-first ve migration | v0.2.0 | Planlandı |
| SQL Server veya PostgreSQL | v0.2.0 | PostgreSQL seçildi; kurulmadı |
| API + DB Docker Compose, otomatik başlangıç migration | v0.2.0/v1.0.0 | Planlandı |
| Swagger/OpenAPI, ProblemDetails/hata kodu, health | v0.2.0 | Planlandı |
| B3 unit testleri | v0.4.0 | Planlandı |
| En az bir gerçek DB entegrasyon testi | v0.5.0 | Planlandı |
| Sıralı uçtan uca Postman collection ve gerekiyorsa environment | v1.0.0 | Planlandı |
| Git repo bağlantısı veya ZIP | v0.1.0/v1.0.0 | Initial commit GitHub main dalına gönderildi; uygulama teslimi henüz tamamlanmadı |
| README: çalıştırma/test, mimari/gerekçe, varsayım, eksikler, AI notu | v1.0.0 | Başlıklar hazır; uygulama sonrası doğrulanacak |

## Kullanıcının ek istekleri

- SemVer roadmap: oluşturuldu; `ROADMAP.md`.
- Önceki istek ve çalışma bağlamı: oluşturuldu; `PROJECT_CONTEXT.md`.
- README'den ayrı teknik review günlüğü: oluşturuldu; `WORKLOG.md`.
- Teknik kararların gerekçesi: oluşturuldu; `DECISIONS.md`.
- Kurulum yönergeleri: oluşturuldu; `SETUP.md`.
- Initial commit'in Codex tarafından oluşturulması: tamamlandı; fa5b457 GitHub main dalında doğrulandı.
