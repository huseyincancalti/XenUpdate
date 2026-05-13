# ⚡ XenUpdate (eski adıyla ZenUpdate) - Proje Durum ve Devir Dokümanı

Bu doküman, projeye yeni katılacak veya kaldığı yerden devam edecek geliştiricilerin projeyi hızlıca anlayabilmesi ve geliştirmeye devam edebilmesi için hazırlanmıştır.

## 📌 Projenin Amacı
XenUpdate, kullanıcıların sistemlerindeki yazılımları, Windows güncellemelerini ve sürücüleri tek bir modern arayüz üzerinden yönetmelerini sağlayan bir Windows masaüstü uygulamasıdır.

## 🏗️ Mimari ve Proje Yapısı
Proje, bağımlılıkların yönetilebilir olması ve kodun test edilebilir olması için **Clean Architecture (Temiz Mimari)** ve **MVVM (Model-View-ViewModel)** tasarım desenleri kullanılarak katmanlara ayrılmıştır.

Çözüm (Solution) yapısı şu şekildedir:

1. **ZenUpdate.Core (`XenUpdate.Core`)**
   - Projenin en alt katmanıdır. Dışa bağımlılığı yoktur.
   - Uygulamanın temel veri modellerini (Models), arayüzlerini (Interfaces) ve sabitlerini/enumlarını barındırır.
   - Diğer tüm katmanlar bu katmanı referans alır.

2. **ZenUpdate.Infrastructure (`XenUpdate.Infrastructure`)**
   - Dış sistemlerle (İşletim sistemi, Winget vb.) iletişimi sağlayan katmandır.
   - **Winget:** Program tarama ve güncelleme işlemleri için Winget CLI entegrasyonu. (Arka planda asenkron olarak process çalıştırılıp çıktıları parse edilir.)
   - **WindowsUpdate:** Windows Update ve Sürücü güncellemeleri için `WUApiLib` (Windows Update Agent API) kullanımı.
   - **Storage:** Kullanıcı ayarları, loglar ve kara liste (Blacklist) gibi verilerin `%APPDATA%` altında JSON formatında kalıcı olarak saklanması.
   - **Logging:** Uygulama içi loglama mekanizmasının implementasyonu.

3. **ZenUpdate.App (`XenUpdate.App`)**
   - Kullanıcı arayüzünü barındıran **WPF (Windows Presentation Foundation)** projesidir.
   - **MVVM Pattern:** Arayüz (View) ve iş mantığı (ViewModel) birbirinden `CommunityToolkit.Mvvm` kullanılarak ayrılmıştır.
   - **Temalar (Themes):** Karanlık/Aydınlık tema desteği, `MaterialDesignInXamlToolkit` kütüphanesi ile modern UI bileşenleri.
   - Özel Window Chrome kullanımı ve marka/logo entegrasyonları burada bulunur.

4. **ZenUpdate.Tests (`XenUpdate.Tests`)**
   - Birim testlerinin (Unit Tests) bulunduğu katmandır. Özellikle Winget çıktılarını parse eden sistemlerin ve core logic'in testleri buradadır.

## 🛠️ Kullanılan Teknolojiler ve Araçlar

- **Framework:** .NET 8.0
- **UI Framework:** WPF (Windows Presentation Foundation)
- **Mimari Desen:** MVVM (CommunityToolkit.Mvvm kütüphanesi ile)
- **UI Kütüphanesi:** Material Design In XAML Toolkit
- **Paket Yöneticisi Entegrasyonu:** Windows Package Manager (Winget) CLI
- **İşletim Sistemi Güncellemeleri:** COM tabanlı WUApiLib
- **Veri Saklama:** JSON (System.Text.Json)

## 🚀 Son Yapılan Geliştirmeler (Güncel Durum)

1. **Marka Değişimi (Rebranding):** Uygulama adı "ZenUpdate" yerine "XenUpdate" olarak değiştirildi. Uygulama içi metinler, logolar, ikon (.ico) dosyaları, başlatma mantığı ve görev çubuğu entegrasyonları bu yeni markaya göre uyarlandı (Eski `%APPDATA%` verileri için uyumluluk kodları eklendi).
2. **Hata Giderme ve Güvenilirlik (Reliability):** Başlangıç (startup) süreçleri, ayar (settings) dosyalarının bozulması veya olmaması durumunda çökmemesi için güvenlik çemberine alındı (Robust startup logic).
3. **Kullanıcı Arayüzü İyileştirmeleri (UI Polish):** Standart Windows title bar yerine özel, temaya uyumlu pencere çerçevesi (window chrome) eklendi. Ayarlar sayfasındaki aralıklar ve ComboBox tasarımları modernize edildi.
4. **Kara Liste (Blacklist) Yönetimi:** Winget üzerinden taranmasını/güncellenmesini istemediğimiz uygulamaları dışarıda bırakmak için altyapı ve arayüz tamamlandı.

## 📋 Geliştirmeye Devam Edecekler İçin Notlar

- **Winget Asenkron İşlemleri:** Uygulama Winget CLI üzerinden asenkron veri çeker. `Infrastructure` katmanındaki Winget parser'larını değiştirirken `Tests` projesindeki testlerin kırılmadığından emin olun.
- **Yönetici İzni (Admin Rights):** Bazı güncellemeler (özellikle Windows Update ve bazı Winget paketleri) yönetici yetkisi gerektirebilir. Uygulamanın Administrator olarak başlatılmasına yönelik manifest veya mantık kontrollerine dikkat edin.
- **Proje İsimlendirmeleri:** Görünür marka `XenUpdate` olsa da, büyük bir refactoring riskine girmemek için namespace ve proje/klasör isimleri şu an büyük oranda `ZenUpdate.*` olarak kalmıştır. Yeni dosya eklerken mevcut namespace yapısına (`ZenUpdate.Core` vb.) uymaya dikkat edin.
- **Tema Yönetimi:** Yeni bir UI elemanı eklerken `MaterialDesignInXamlToolkit`'in DynamicResource'larını ve projenin `Themes/` klasöründeki stilleri kullanın, sabit (hardcoded) renk vermekten kaçının.

## 🏃‍♂️ Projeyi Çalıştırma

1. `ZenUpdate.sln` dosyasını Visual Studio 2022 ile açın.
2. `ZenUpdate.App` projesini **Startup Project** (Başlangıç Projesi) olarak ayarlayın.
3. Çalıştırın. (Not: Tam deneyim için uygulamanın Yönetici olarak çalıştırılması önerilir).
