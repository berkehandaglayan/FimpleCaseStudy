# Gereksinim ve doğrulama matrisi

Kaynak: Kullanıcının sağladığı 5 sayfalık Project Assignment - Software Developer / ATM Cash Management Backend PDF'si. Bu özet kaynağın yerini almaz. Durumlar gerçek uygulama/test kanıtına göre güncellenir.

## Part A

| ID | Gereksinim | Hedef | Durum | Planlanan kanıt |
|---|---|---|---|---|
| A1-A2 | TRY/USD/EUR; yalnızca 20/50/100/500, bozuk para yok | v0.3.0 | Mevcut işlemlerde tamamlandı | Geçerli/geçersiz giriş unit testleri ve JSON boundary entegrasyon testleri |
| A3-A5 | 5 kaset, her biri 100; 1 karışık deposit-only, 4 tek para/kupür recycling | v0.3.0 | Model/elle nakit yönetimi tamam; müşteri işlemleri sonraki aşamada | Konfigürasyon, toplam deposit kapasitesi, paralel yükleme testleri |
| A6 | 1-2 recycling kaseti döviz için | v0.3.0 | Tamamlandı | 0/3/4 red, 1/2 kabul unit testleri |
| A7 | Hizmete alınmadan konfigürasyon ve elle yüklenen nakit kaydı | v0.3.0 | Tamamlandı | Unit, gerçek DB lifecycle ve Postman setup akışı |
| A8 | Fiziksel sayım ve sistem farkının kaydı | v0.7.0 | Planlandı | Mutabakat hareketi testi |
| A9 | Çekim banknot kırılımı; tam ödenemiyorsa yakın tutar | v0.4.0 | Domain hesabı tamam; HTTP quote v0.5.0 | B3 kabul testleri geçti |
| A10 | Müşteri onayından sonra çekim | v0.5.0 | Planlandı | Quote fiziksel bakiyeyi değiştirmez, confirm değiştirir |
| A11 | Yatırmada eşleşen recycling, diğerleri deposit | v0.6.0 | Planlandı | Yönlendirme testleri |
| A12 | ATM banknot adedi ve toplam bakiye görüntüleme | v0.7.0 | Planlandı | Rapor doğrulaması |

## Part B

| ID | Gereksinim | Hedef | Durum | Planlanan kanıt |
|---|---|---|---|---|
| B1 | Birden çok ATM; her ATM'de tüm kurallar | v0.3.0 | Mevcut işlemlerde tamamlandı | Bir ATM'de işlem yapılırken diğer ATM ve ledger değişmiyor |
| B2 | Yaşam döngüsü, işlem kısıtları, aynı kupürde doluluk önceliği | v0.3.0-v0.4.0 | Lifecycle ve seçicide doluluk önceliği tamam; müşteri işlemlerine bağlama v0.5.0 | State testleri, fiziksel doluluk/rezerve stok ve eşitlik sırası testleri geçti |
| B3 | Sınırlı stokta en az banknot; en yakın alt/üst, üstte limit kontrolü | v0.4.0 | Saf algoritma tamamlandı | Sekiz kabul testi + sınır testleri + 300 bağımsız exhaustive karşılaştırma |
| B4 | 60 saniye rezervasyon, cancel/confirm, expiry, Idempotency-Key | v0.5.0 | Planlandı | Tekrar, iptal, süre ve yarış testleri |
| B5 | Kart/para bazında günlük limit; aktif rezervasyon dahil; plaintext kart yok | v0.5.0 | Planlandı | Limit ve kayıt/log denetimi |
| B6 | Paralel quote'larda çifte rezervasyon yok | v0.5.0 | Planlandı | Gerçek DB ile paralel HTTP istekleri |
| B7 | Tanınmayanları iade; recycling/deposit taşması; ayrıntılı yanıt | v0.6.0 | Planlandı | Kısmi kabul, taşma ve iade testleri |
| B8 | Immutable hareketler, yeniden üretilebilir bakiye, consistency endpoint | v0.7.0 | LOAD/UNLOAD temeli, EF/DB append-only koruması hazır; diğer türler ve endpoint bekliyor | Ledger toplamı, rollback, UPDATE/DELETE/TRUNCATE red testleri |
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
| ASP.NET Core REST API, DI | v0.2.0 | Altyapı tamamlandı; iş endpointleri sonraki aşamada |
| EF Core code-first ve migration | v0.2.0 | DbContext + ilk schema migration ve otomatik uygulama doğrulandı |
| SQL Server veya PostgreSQL | v0.2.0 | PostgreSQL 17 Compose ve Testcontainers üzerinde çalışıyor |
| API + DB Docker Compose, otomatik başlangıç migration | v0.2.0/v1.0.0 | Altyapı için temiz açılış ve tekrar açılış doğrulandı; final kapsamı yeniden doğrulanacak |
| Swagger/OpenAPI, ProblemDetails/hata kodu, health | v0.2.0 | Tamamlandı; domain hata kodları sonraki aşamalarda eklenecek |
| B3 unit testleri | v0.4.0 | 8/8 geçti; toplam 45 unit test başarılı |
| En az bir gerçek DB entegrasyon testi | v0.2.0/v0.5.0 | 12 entegrasyon testi geçti; paralel load var, quote concurrency kanıtı B6 ile eklenecek |
| Sıralı uçtan uca Postman collection ve gerekiyorsa environment | v1.0.0 | Altyapı (4/8) ve ATM yönetimi (9/18) geçti; tam çekim/yatırma/mutabakat akışı henüz yok |
| Git repo bağlantısı veya ZIP | v0.1.0/v1.0.0 | Initial commit GitHub main dalına gönderildi; uygulama teslimi henüz tamamlanmadı |
| README: çalıştırma/test, mimari/gerekçe, varsayım, eksikler, AI notu | v1.0.0 | v0.4.0 için güncel; final kapsamda tekrar güncellenecek |

## Kullanıcının ek istekleri

- SemVer roadmap: oluşturuldu; `ROADMAP.md`.
- Önceki istek ve çalışma bağlamı: oluşturuldu; `PROJECT_CONTEXT.md`.
- README'den ayrı teknik review günlüğü: oluşturuldu; `WORKLOG.md`.
- Teknik kararların gerekçesi: oluşturuldu; `DECISIONS.md`.
- Kurulum yönergeleri: oluşturuldu; `SETUP.md`.
- Initial commit'in Codex tarafından oluşturulması: tamamlandı; fa5b457 GitHub main dalında doğrulandı.
