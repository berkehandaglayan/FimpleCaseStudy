# Geliştirme ortamı kurulumu

## 2026-10-12 kontrolü

| Araç | Gözlenen durum | Yapılacak işlem |
|---|---|---|
| Git | `2.55.0.windows.5` kurulu; commit kimliği tanımlı | Yeniden kurulum gerekmiyor |
| .NET SDK | `8.0.425` ve `10.0.400` kurulu; seçilen SDK `10.0.400` | .NET 10 ile ilerle |
| WSL | `wsl --status` kurulu olmadığını bildirdi | WSL 2 kur |
| Docker | CLI PATH'te bulunamadı; standart kurulum kontrollerinde görülmedi | Docker Desktop kur ve çalıştır |
| GitHub CLI | `gh` PATH'te ve standart kurulum yerinde bulunamadı | GitHub CLI kur ve tarayıcıyla giriş yap |
| IDE | VS Code PATH/standart kullanıcı konumunda bulunamadı; standart uninstall taramasında IDE görünmedi | Başka konumda kurulu IDE varsa onu kullan; yoksa VS Code + C# Dev Kit |
| Postman | Standart kullanıcı konumunda ve uninstall taramasında bulunamadı | Postman Desktop kur |

Bu tablo belirli kontrol noktalarının sonucudur; özel klasörde/portable kurulum olmadığı iddia edilmez.

## 1. WSL 2

Yönetici PowerShell'de:

```powershell
wsl --install
```

Windows'un istediği yeniden başlatmayı yap. Varsayılan Ubuntu ilk açılışta Linux kullanıcı adı/parolası isteyebilir. Sonra:

```powershell
wsl --update
wsl --version
wsl --list --verbose
```

Docker'ın WSL 2 arka ucu kullanılacak. Kaynak: [Microsoft WSL kurulumu](https://learn.microsoft.com/en-us/windows/wsl/install).

## 2. Docker Desktop

[Docker Desktop Windows kurulum sayfasından](https://docs.docker.com/desktop/setup/install/windows-install/) makinenin mimarisine uygun yükleyiciyi indir. WSL 2 arka ucunu seç, Docker Desktop'ı aç ve engine hazır olana kadar bekle. Proje Linux container kullanacak.

Yeni bir terminalde:

```powershell
docker --version
docker compose version
docker info
```

İlk iki komut CLI kurulumunu, son komut engine erişimini doğrular. CLI'nin sürüm yazması tek başına container çalıştırabildiğimizi kanıtlamaz.

PostgreSQL'i Windows'a ayrıca kurmak gerekmiyor; proje veritabanı Compose servisinde çalışacak. Docker Compose ayrı bir kurulum adımı olmayacak.

## 3. GitHub CLI ve repo erişimi

[GitHub CLI](https://cli.github.com/) yüklenebilir veya PowerShell'de:

```powershell
winget install --id GitHub.cli --exact --source winget
```

Yeni terminal açıp aşağıdakileri çalıştır:

```powershell
gh auth login --hostname github.com --git-protocol https --web
gh auth setup-git
gh auth status
```

Girişi kendi tarayıcında tamamla. Erişim tokenını sohbet veya proje dosyalarına yapıştırma. Sonra Codex repo geçmişini inceleyip remote bağlantısını ve ilk gönderimi tamamlayabilir.

Kaynak: [GitHub CLI giriş dokümanı](https://cli.github.com/manual/gh_auth_login).

GitHub eklentisi üzerinden bu repoya okuma ve yazma yetkisi doğrulandı. Ancak eklenti bağlantısı yerel Git kimlik doğrulaması sağlamış sayılmaz. Yerel commit için GitHub'a giriş şart değildir; oluşturduğumuz yerel commit'i normal `git push` ile göndermek için yukarıdaki CLI girişini tamamlayacağız.

## 4. Kod editörü

Kurulu ve tercih ettiğin bir .NET IDE'si varsa onu kullan. Yeni kurulum için [Visual Studio Code](https://code.visualstudio.com/) ve Microsoft'un **C# Dev Kit** eklentisi yeterli bir başlangıçtır. SDK zaten kurulu.

Kaynak: [VS Code C# başlangıç rehberi](https://code.visualstudio.com/docs/csharp/get-started).

## 5. Postman

[Postman indirme sayfasından](https://www.postman.com/downloads/) Desktop uygulamasını kur. Projenin collection ve environment dosyalarını biz oluşturacağız. İlk API geliştirmesinde Swagger da kullanılacak.

## Kurulumdan sonra ortak kontrol

```powershell
git --version
dotnet --version
docker compose version
docker info
gh auth status
```

Kurulumdan sonra mevcut Codex süreci yeni PATH'i görmezse terminal/uygulamayı yeniden aç veya kurulu executable'ın tam yoluyla doğrula. PostgreSQL, EF Core sağlayıcısı ve test bağımlılıkları proje aşamasında tanımlanacak; rastgele global paket kurulumu yapmaya gerek yok.
