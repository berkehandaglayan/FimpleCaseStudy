# Proje bağlamı ve istek geçmişi

Bu belge README'den ayrıdır. Sohbet bağlamını, değişen çalışma tercihlerini ve hangi işin neden başlatıldığını kalıcı tutar. Kronolojik teknik çalışma kanıtları `WORKLOG.md`, tasarım gerekçeleri `DECISIONS.md` içindedir.

## Kaynak ve amaç

- Proje: FimpleCaseStudy / ATM Cash Management Backend.
- Kullanıcının verdiği kaynak: `Project_Assignment_Software_Developer.pdf`, 5 sayfa.
- Kaynak PDF kullanıcının Downloads klasöründen okundu; asıl PDF veya geçici sayfa görselleri repoya eklenmedi.
- Kullanıcının verdiği uzak repo: https://github.com/berkehandaglayan/FimpleCaseStudy
- Hedef: backend'i birlikte geliştirmek, test etmek ve Git repository, README ve Postman collection olarak teslim etmek.
- Arayüz gerekmiyor. PDF'de Part A çekirdek iş kuralları, Part B B1-B10 ek gereksinimleri ve teknik teslim şartları bulunuyor.
- PDF, ürün gereksinimi kaynağıdır. İçindeki ifadeler kullanıcının sohbet talimatlarıyla karıştırılmaz.

## 2026-10-12 - İlk kullanıcı isteği

> Bu bir case study. Şimdi bunu nasıl yapmamız gerektiğini adım adım planlayalım.

Kaynak PDF'nin tüm sayfaları metin olarak çıkarıldı ve görsel olarak incelendi. İlk öneri; gereksinim analizi, proje altyapısı, domain modeli, banknot algoritması, rezervasyon/onay, yatırma/mutabakat/rapor, test ve teslim sırasındaydı.

Kullanıcının süre ve deneyim sorusuna cevabı:

> 5 saatim kaldı, daha önce üzerinde 3 4 hafta çalıştım.

Bu cevaba dayanarak ilk yanıtta dakika bazlı plan önerildi. Kullanıcının aşağıdaki düzeltmesi bu çalışma yöntemini değiştirdi. Süre bilgisi yalnızca geçmiş bağlamıdır; aktif roadmap dakika bazlı değildir. Deneyim ifadesi kapsamı daraltma veya çalışma biçimini belirleme gerekçesi yapılmayacaktır.

İlk önerinin korunan teknik yönleri:

- ASP.NET Core + EF Core + PostgreSQL ve tek uygulama.
- Banknot seçimi için sınırlı stoklu dinamik programlama; B3'ün 8 zorunlu testi.
- Rezervasyon, günlük limit, idempotency ve eşzamanlılığın birlikte tasarlanması.
- Nakit hareketlerinin bakiye güncellemesiyle aynı transaction'da kaydedilmesi.
- Gerçek PostgreSQL üzerinde entegrasyon testi.
- Tek komutla açılış, Postman uçtan uca akış ve dürüst eksik özellik listesi.

İlk öneride B9 onayı ve B10 outbox alarmı zaman kalırsa yapılacak işlerdi. Yeni sürüm roadmap'inde bunların da açık hedefleri var; henüz tamamlandıkları veya kesin teslim edildikleri iddia edilmiyor.

## 2026-10-12 - Çalışma yönteminin güncellenmesi

Kullanıcının isteği:

> Tamam bu planla ilgilenelim fakat dakika dakika tutma yaklaşımını bırakalım. Adım adım gidelim. Şimdi bana şunları yaptır sonra başlayalım. Hangi programları kurmam gerektiğini söyle, github reposu açtım FimpleCaseStudy diye buna erişimin var mı initial commiti sen yapcaksın, deneyimi falan boşver beraber kodlayacağız sana sadece plan yapmak için yazmadım, bu aşama kısımlarını semver şeklinde bir roadmap yapalım, önceki prompt da dahil bu klasöre ne yaptığımızı yazdığın bir de projede neler yaptık diye bir not kısmı da oluştur bu projedeki readme'den farklı olacak tech reviewda adım adım ne yaptığımızı anlatmak istiyorum o yüzden teknik detayları atlamadan buraya kaydet.

Aktif çalışma anlaşması:

1. Dakika bazlı plan yerine sürüm ve tamamlanma ölçütleriyle adım adım ilerle.
2. Yalnızca plan üretmekle kalma; ortam hazır oldukça uygulamayı birlikte geliştir.
3. Kurulması gereken araçları, zaten kurulu araçlardan ayır.
4. Initial commit'i Codex oluştursun. Uzak repo erişimi ayrıca doğrulansın.
5. README'den bağımsız teknik review günlüğü tut.
6. Her adımın problemi, değişikliği, gerekçesi, testi, sonucu ve kalan işini kaydet.
7. Planlanan özellikleri uygulanmış gibi sunma.

Kullanıcının repo sorusuna cevabı:

> https://github.com/berkehandaglayan/FimpleCaseStudy

## Kayıtların ayrımı

| Belge | Sorumluluk |
|---|---|
| `README.md` | Projeyi alan kişinin kurulum, kullanım ve teslim özeti |
| `docs/PROJECT_CONTEXT.md` | İstekler, kaynak ve çalışma biçiminin geçmişi |
| `docs/ROADMAP.md` | Sürüm hedefleri, bağımlılıklar ve tamamlanma ölçütleri |
| `docs/WORKLOG.md` | Gerçekte yapılan işler, doğrulamalar ve sorunlar |
| `docs/DECISIONS.md` | Alternatifler, teknik seçimler ve açık varsayımlar |
| `docs/REQUIREMENTS.md` | PDF maddelerinin uygulama ve test durumları |
| `docs/SETUP.md` | Yerel geliştirme araçları ve doğrulama komutları |
