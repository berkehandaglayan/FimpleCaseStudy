# FimpleCaseStudy

ATM Cash Management Backend case study. ASP.NET Core REST API, EF Core ve PostgreSQL ile geliştirilecek.

## Güncel durum

Başlangıç kapsamı, SemVer roadmap ve teknik çalışma kayıtları hazırlandı. Henüz API, veritabanı şeması, Docker Compose veya otomatik test uygulaması bulunmuyor.

## Projeyi ve testleri çalıştırma

Geliştirme ortamı kurulumu: [SETUP](docs/SETUP.md).

API ve test komutları v0.2.0 aşamasında, çalıştırılarak doğrulandıktan sonra buraya eklenecek. Son teslimde API ve veritabanı `docker compose up` ile açılacak ve migration'lar otomatik uygulanacak.

## Mimari ve temel tasarım kararları

Tek uygulama içinde Domain, Application, Infrastructure ve API ayrımı planlandı. Banknot seçimi, transaction sınırları, idempotency ve hareket defteri kararları [DECISIONS](docs/DECISIONS.md) içinde gerekçeleriyle kayıtlıdır. Henüz uygulanmış değildir.

## Varsayımlar

Alternatif tutarların rezervasyonu, günlük limitin gün sınırı, bakım geçişi ve mutabakat farklarının değerlendirilmesi gibi noktalar [DECISIONS](docs/DECISIONS.md) içinde kaynak gereksinimlerden ayrı işaretlenmiştir.

## Tamamlanmamış işler ve bilinen sınırlamalar

Uygulama geliştirmesi başlamadı. Özellik bazlı güncel durum için [REQUIREMENTS](docs/REQUIREMENTS.md), sürüm hedefleri için [ROADMAP](docs/ROADMAP.md) kullanılmalıdır.

## AI kullanımı

Codex; ödev PDF'sinin incelenmesi, kapsamın çıkarılması, geliştirme ortamının kontrol edilmesi ve ilk dokümantasyonun hazırlanmasında kullanıldı. İlk önerideki dakika bazlı plan kullanıcı geri bildirimiyle kaldırıldı; yerine sürüm bazlı ilerleme benimsendi. Uygulama sırasında düzeltilen AI çıktıları çalışma günlüğüne somut örneklerle eklenecek.

## Geliştirme ve teknik review kayıtları

- [Proje bağlamı ve istek geçmişi](docs/PROJECT_CONTEXT.md)
- [SemVer roadmap](docs/ROADMAP.md)
- [Kronolojik çalışma günlüğü](docs/WORKLOG.md)
- [Teknik kararlar](docs/DECISIONS.md)
- [Gereksinim ve doğrulama matrisi](docs/REQUIREMENTS.md)
- [Kurulum adımları](docs/SETUP.md)
