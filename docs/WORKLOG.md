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
