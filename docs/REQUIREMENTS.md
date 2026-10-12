# Gereksinim ve doğrulama matrisi

Kaynak: Kullanıcının sağladığı 5 sayfalık Project Assignment - Software Developer / ATM Cash Management Backend PDF'si. Bu özet kaynağın yerini almaz. Durumlar gerçek uygulama/test kanıtına göre güncellenir.

Git teslim sınırı: v0.9.0 commit b403528 GitHub'a gönderildi; Part A/B1-B10 işlevsel kapsamı repodadır. v1.0.0 final teslim incelemesi yerelde tamamlandı: temiz kaynak/boş DB açılışı, otomatik migration, Release testleri ve tüm Postman collection'ları doğrulandı. v1.0.0 sürüm metadata'sı ve final belgeleri 1121c3c ile GitHub main dalına gönderildi. Tag/release oluşturulmadı.

## Part A

| ID | Gereksinim | Hedef | Durum | Doğrulama kanıtı |
|---|---|---|---|---|
| A1-A2 | TRY/USD/EUR; yalnızca 20/50/100/500, bozuk para yok | v0.3.0 | Mevcut işlemlerde tamamlandı | Geçerli/geçersiz giriş unit testleri ve JSON boundary entegrasyon testleri |
| A3-A5 | 5 kaset, her biri 100; 1 karışık deposit-only, 4 tek para/kupür recycling | v0.3.0-v0.6.0 | Tamamlandı; model, operatör yükleme, çekim ve müşteri yatırması hazır | Konfigürasyon, karma deposit kapasitesi, yönlendirme, taşma ve paralel yatırma testleri |
| A6 | 1-2 recycling kaseti döviz için | v0.3.0 | Tamamlandı | 0/3/4 red, 1/2 kabul unit testleri |
| A7 | Hizmete alınmadan konfigürasyon ve elle yüklenen nakit kaydı | v0.3.0 | Tamamlandı | Unit, gerçek DB lifecycle ve Postman setup akışı |
| A8 | Fiziksel sayım ve sistem farkının kaydı | v0.7.0 | Tamamlandı | Tam envanter, karma deposit/eksik kupür sıfır, signed adjustment, kalıcı sayım geçmişi ve rollback |
| A9 | Çekim banknot kırılımı; tam ödenemiyorsa yakın tutar | v0.4.0-v0.5.0 | Tamamlandı; HTTP quote alternatifleri ve confirm bağlı | B3 unit + gerçek DB alternatif/rezervasyon testi |
| A10 | Müşteri onayından sonra çekim | v0.5.0 | Tamamlandı | Quote fiziksel bakiyeyi değiştirmiyor; lower confirm yalnızca seçilen planı tüketiyor |
| A11 | Yatırmada eşleşen recycling, diğerleri deposit | v0.6.0 | Tamamlandı | Unit, PostgreSQL ve Postman yönlendirme/taşma/iade testleri |
| A12 | ATM banknot adedi ve toplam bakiye görüntüleme | v0.7.0-v0.9.0 | Tamamlandı; para birimi ve genel TRY toplamları | Kaset/ATM kupür kırılımı, fiziksel/rezerve/kullanılabilir tutarlar; kur/stock snapshot testi |

## Part B

| ID | Gereksinim | Hedef | Durum | Doğrulama kanıtı |
|---|---|---|---|---|
| B1 | Birden çok ATM; her ATM'de tüm kurallar | v0.3.0 | Mevcut işlemlerde tamamlandı | Bir ATM'de işlem yapılırken diğer ATM ve ledger değişmiyor |
| B2 | Yaşam döngüsü, işlem kısıtları, aynı kupürde doluluk önceliği | v0.3.0-v0.5.0 | Lifecycle, seçici doluluk önceliği ve müşteri çekimine bağlama tamamlandı | State testleri, fiziksel doluluk/rezerve stok ve eşitlik sırası testleri geçti |
| B3 | Sınırlı stokta en az banknot; en yakın alt/üst, üstte limit kontrolü | v0.4.0 | Saf algoritma tamamlandı | Sekiz kabul testi + sınır testleri + 300 bağımsız exhaustive karşılaştırma |
| B4 | 60 saniye rezervasyon, cancel/confirm, expiry, Idempotency-Key | v0.5.0 | Tamamlandı | 59/60 sınırı, hosted worker, cancel, paralel confirm, restart sonrası byte replay ve rollback |
| B5 | Kart/para bazında günlük limit; aktif rezervasyon dahil; plaintext kart yok | v0.5.0 | Tamamlandı | TRY/USD/EUR farklı ATM paralel limit; İstanbul gece yarısı; HMAC DB ve yakalanmış log denetimi |
| B6 | Paralel quote'larda çifte rezervasyon yok | v0.5.0 | Tamamlandı | Gerçek PostgreSQL'de 8 paralel quote: tek başarılı rezervasyon; paralel confirm tek nakit çıkışı |
| B7 | Tanınmayanları iade; recycling/deposit taşması; ayrıntılı yanıt | v0.6.0 | Tamamlandı | 12 yeni unit vakası +6 gerçek DB yatırma testi; Postman 8/19 |
| B8 | Immutable hareketler, yeniden üretilebilir bakiye, consistency endpoint | v0.7.0 | Tamamlandı; yedi hareket türü ve aynı snapshot'ta fiziksel/rezerve replay | Tüm türler tek akışta; kontrollü physical/reserved drift ve eksik bakiye satırı, araya deposit commit girmesi, rollback, mevcut UPDATE/DELETE/TRUNCATE red testleri |
| B9 | TRY >1.000 farkta başka X-User onayı, hizmete dönüş engeli, kur tablosu | v0.8.0 | Tamamlandı, c87f61e gönderildi | 1000/1100 ve fractional eşik, mutlak FX farkı/snapshot, canonical farklı kullanıcı, pending state/count, concurrency/replay/restart, rollback ve legacy migration |
| B10 | Ayrıntılı operasyon raporu, düşük nakit alarmı, transaction outbox, worker | v0.9.0 | Tamamlandı, b403528 gönderildi | 11 unit +9 gerçek DB: FX snapshot raporu, tüm cash yolları, insert rollback, retry/restart/hosted log/sent, parallel claims, cancellation ve publish-sent crash penceresi; v1.0.0 tam koşuda tekrar geçti |

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
| ASP.NET Core REST API, DI | v0.2.0 | Tamamlandı; ATM yönetimi ve çekim endpointleri DI ile çalışıyor |
| EF Core code-first ve migration | v0.2.0-v0.9.0 | Altı migration: şema, ATM nakit modeli, quote/idempotency, mutabakat geçmişi, onay/kur, outbox; otomatik uygulama ve model/snapshot eşleşmesi doğrulandı |
| SQL Server veya PostgreSQL | v0.2.0 | PostgreSQL 17 Compose ve Testcontainers üzerinde çalışıyor |
| API + DB Docker Compose, otomatik başlangıç migration | v0.2.0/v1.0.0 | Final temiz kaynak/boş volume açılışı geçti; altı migration otomatik, health hazır. Container down/up sonrası sayımlar aynı ve migration tekrar çalışmadı |
| Swagger/OpenAPI, ProblemDetails/hata kodu, health | v0.2.0 | Tamamlandı; mevcut ATM/çekim işlemlerinde domain hata kodları ve health testleri hazır |
| B3 unit testleri | v0.4.0/v1.0.0 | 8/8 geçti; v1.0.0 Release koşusunda 106/106 unit başarılı, sıfır atlanan |
| En az bir gerçek DB entegrasyon testi | v0.2.0-v1.0.0 | v1.0.0 Release koşusunda 63/63 PostgreSQL testi başarılı, sıfır atlanan; ayrıntı WORKLOG |
| Sıralı uçtan uca Postman collection ve gerekiyorsa environment | v0.7.0-v1.0.0 | Final temiz kurulumda beşi de geçti: EndToEnd 23/60, Infrastructure 4/8, AtmSetup 9/18, Withdrawals 16/37, Deposits 8/19. baseUrl collection değişkeni; ayrı environment gerekmez |
| Git repo bağlantısı veya ZIP | v0.1.0/v1.0.0 | https://github.com/berkehandaglayan/FimpleCaseStudy — işlevsel kapsam b403528 ile gönderildi; v1.0.0 teslimi 1121c3c ile gönderildi |
| README: çalıştırma/test, mimari/gerekçe, varsayım, eksikler, AI notu | v1.0.0 | Beş zorunlu bölüm incelendi ve final kanıtlarıyla güncellendi; final belgeler 1121c3c ile gönderildi |

## Kullanıcının ek istekleri

- SemVer roadmap: oluşturuldu; `ROADMAP.md`.
- README'den ayrı teknik review günlüğü: oluşturuldu; `WORKLOG.md`.
- Teknik kararların gerekçesi: oluşturuldu; `DECISIONS.md`.
- Initial commit'in Codex tarafından oluşturulması: tamamlandı; fa5b457 GitHub main dalında doğrulandı.
