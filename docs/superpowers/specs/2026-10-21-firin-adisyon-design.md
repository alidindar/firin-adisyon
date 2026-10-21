# Fırın Adisyon — Tasarım (Spec)

Tarih: 2026-10-21 · Durum: onaylandı, uygulama planı yazıldı (docs/superpowers/plans/)

## Bağlam
Tek bir fırın-kafe için masa hesaplarını tutan bir uygulama. Ürünleri ve fiyatları fırın sahibi girer. Ürünler adetle (poğaça, çay) ya da gramla (kuru pasta, baklava) satılır. Birden fazla masa olur, her masanın toplamı anlık hesaplanır. Öncelik kasadaki Windows bilgisayar. Tablet ve telefon (Android/iOS) da aynı veriyi canlı görür.

Kullanıcının verdiği kararlar:
- Tüm cihazlar ortak veri kullanır.
- Sunucu fırındaki kasa bilgisayarında çalışır. İnternet kesilse de iş durmaz.
- Sahip ve garsonlar PIN ile girer. Ürün, fiyat ve rapor işleri yalnızca sahipte.
- Hesap kapatma: toplamı gösterme, geçmiş ve gün sonu raporu, hesap bölme ve kısmi ödeme, nakit/kart ayrımı.
- Teknoloji: ASP.NET Core API, React PWA, SignalR, SQLite.
- Klasör: `C:\Users\Ali G\Desktop\firin-adisyon`. Klasör henüz yok. Makinede .NET SDK 10.0.401 ve Node 24 kurulu.

## Mimari
```
Kasa PC (Windows Service, port 5080, 0.0.0.0)
 └─ Firin.Api  (ASP.NET Core 10 Minimal API)
     ├─ EF Core + SQLite      → data\firin.db
     ├─ SignalR /hubs/adisyon → masa değişince tüm cihazlara haber verir
     ├─ Fotoğraflar           → data\photos\
     └─ wwwroot               → React derlemesi (aynı adresten sunulur)
Tablet / telefon / PC tarayıcı → http://<kasa-ip>:5080
```
- **Frontend:** React, TypeScript ve Vite. Sunucu verisi TanStack Query ile tutulur. `@microsoft/signalr` olay gelince ilgili sorguyu geçersiz sayar ve ekran yenilenir.
- **Ana ekrana ekleme:** manifest ve `apple-mobile-web-app-capable` ayarı olur. Yerel ağda HTTPS olmadığı için service worker çalışmaz. Sunucu zaten yerel ağda sürekli açık olduğundan çevrimdışı önbelleğe gerek yok. Bilerek atlandı.
- **Adres sabitliği:** modemde kasa PC'ye sabit IP (DHCP rezervasyonu) verilir. Ayarlar sayfasında bağlantı adresi ve QR kod gösterilir. Tablet QR'ı okutup bağlanır.
- **Para:** her tutar kuruş cinsinden `long` olarak tutulur. Ekranda `tr-TR` biçimi kullanılır, örneğin ₺1.234,50.
- **Gram hesabı:** gramla satılan ürünün fiyatı kilogram başına girilir. Satır tutarı `round(gram × kgFiyatı / 1000)` olur.
- **Fotoğraf:** tarayıcıda canvas ile 800 px'e küçültülür ve JPEG olarak yüklenir. Sunucuda görüntü kütüphanesi gerekmez.

## Veri modeli
| Tablo | Alanlar |
|---|---|
| User | Id, Name, PinHash, Role (Owner/Staff), IsActive |
| Category | Id, Name, SortOrder |
| Product | Id, CategoryId, Name, UnitType (Piece/Gram), PriceKurus (adet ya da kg fiyatı), PhotoPath, IsActive, SortOrder |
| DiningTable | Id, Name, SortOrder, IsActive. Tezgâh satışı için "Tezgâh" adlı bir masa açılabilir, ek özellik gerekmez |
| Order | Id, TableId, Status (Open/Closed/Cancelled), OpenedAt, ClosedAt, OpenedBy, ClosedBy |
| OrderItem | Id, OrderId, ProductId, **ad, birim ve fiyat kopyası**, Quantity (adet ya da gram), LineTotalKurus, AddedBy, AddedAt, VoidedAt, VoidedBy, PaymentId (ürün seçerek ödemede) |
| Payment | Id, OrderId, AmountKurus, Method (Cash/Card), PaidAt, UserId |

- Ürün fiyatı satıra kopyalanır. Böylece fiyat değişince açık ve geçmiş hesaplar etkilenmez.
- Silinen kalem veritabanından kalkmaz, iptal olarak işaretlenir. Sahip bunları raporda görür.
- Kalan tutar = iptal edilmemiş satırların toplamı − ödemeler. Kalan 0 olunca hesap kapanır ve masa boşalır.

## Netleştirilen kurallar
- Boş masaya ilk kalem eklenince adisyon açılır. Kapanmış masaya kalem eklenirse yeni adisyon açılır.
- Tüm kalemler iptal edilir ve hiç ödeme yoksa adisyon "iptal" durumuna geçer. Ciroya girmez ama raporda görünür. Bu yüzden Status alanı Open, Closed ve Cancelled değerlerini alır.
- Kısmi ödeme alınmış bir kalem iptal edilemez. Ürün seçerek ödemede bu, o kalemin kendisinin ödenmiş olmasıdır. Tutar girerek ödemede kural, iptalden sonraki toplamın ödenenin altına düşmemesidir.
- Adetli kalem tekrar eklenince yeni satır açılmaz, aynı satırın adedi artar. Bu yalnızca kalem iptal edilmemiş ve ödenmemişse geçerlidir. Gramlı ürünün her tartımı ayrı satırdır. Gramlı satıra dokununca tuş takımı açılır ve gram düzeltilir. +/− yalnızca adetli satırlarda vardır.
- Kişi sayısına bölmede kuruş artığı son kişiye eklenir. Örnek: 100,00 ₺ / 3 = 33,33 + 33,33 + 33,34.
- PIN 5 kez yanlış girilirse o kullanıcı 1 dakika kilitlenir.
- Gram alanı 1 ile 99.999 arasında bir tam sayı olur. Adet 1 ile 999 arasında olur.

## Ekranlar
1. **Giriş:** kullanıcı seçilir ve 4 haneli PIN girilir. Cihaz oturumu cookie ile hatırlanır. "Kullanıcı değiştir" düğmesi olur.
2. **Masalar:** masa kartları ızgarada durur. Boş ve dolu masalar renkle ayrılır. Kartta güncel toplam ve açık kalma süresi görünür. Hepsi canlı güncellenir.
3. **Masa detayı:** solda kalemler, toplam ve kalan tutar; sağda kategori sekmeleri ve fotoğraflı ürün ızgarası. Adetli ürüne dokunmak 1 ekler. Gramlı ürün sayı tuş takımını açar; 250 g, 500 g ve 1 kg kısayolları olur. Kalemde +/− ve sil (iptal) düğmeleri olur. Telefonda bu iki bölüm alt alta sekme olur.
4. **Ödeme:** dört seçenek var: tamamı, tutar girerek, kişi sayısına bölerek, ürün seçerek. Ardından nakit ya da kart seçilir. Nakitte verilen para girilir ve para üstü hesaplanır.
5. **Yönetim (sahip):** kategoriler, ürünler (fotoğraf, birim, fiyat, aktif/pasif, sıra), masalar, kullanıcılar, ayarlar (işletme adı, bağlantı QR'ı, yedek klasörü).
6. **Raporlar (sahip):** tarih aralığı seçilir. Ciro, nakit ve kart toplamları, adisyon sayısı, ürün bazında satış (adet ya da kg), iptal edilen kalemler, geçmiş adisyonlar ve detayları gösterilir.

## Hata ve eşzamanlılık
- Kalem eklemek bağımsız bir kayıt eklemedir. İki cihaz aynı masaya aynı anda eklerse çakışma olmaz.
- Ödeme bir transaction içinde işlenir. Tutar kalandan büyükse sunucu reddeder. Ürün seçerek ödemede seçilen kalem önceden ödenmişse yine reddeder.
- Yetki sunucuda denetlenir. Garson yönetim ve rapor uçlarına erişemez.
- SignalR bağlantısı koparsa üstte "Bağlantı yok" bandı çıkar ve işlem düğmeleri kilitlenir. Otomatik yeniden bağlanılır, bağlanınca veri tazelenir.
- **Yedek:** her gün otomatik SQLite yedeği alınır ve 30 gün saklanır. Yedek klasörü ayarlanabilir (USB ya da OneDrive). Sahibin "yedeği indir" düğmesi olur.

## Proje yapısı
```
firin-adisyon/
  server/Firin.Api/        Program.cs, Domain/, Data/, Endpoints/, Hubs/, Auth/
  server/Firin.Tests/      xUnit
  web/                     React + Vite (src/pages, components, api, lib/format.ts)
  scripts/publish.ps1      self-contained win-x64 yayın, web derlemesini wwwroot'a kopyalar
  scripts/install-service.ps1  Windows Service ve güvenlik duvarı kuralı
  docs/superpowers/specs/2026-10-21-firin-adisyon-design.md
```

## Uygulama sırası
1. İskelet: solution, EF ve SQLite migration, Vite projesi. Geliştirmede proxy kullanılır, yayında wwwroot.
2. Kullanıcılar ve PIN girişi. İlk açılışta sahip hesabı oluşturulur.
3. Katalog yönetimi: kategori, ürün, fotoğraf, masa.
4. Adisyon akışı: masalar ekranı, kalem ekleme, gram tuş takımı, iptal ve SignalR.
5. Ödeme ve hesap bölme.
6. Raporlar ve geçmiş.
7. Yedekleme, Windows Service ve kurulum betikleri.
8. Tablet ve telefon arayüz geçişi.

Her adım ayrı commit olur. Commit atmadan önce kullanıcıya sorulur.

## Doğrulama
- **xUnit:** gram ve adet tutar hesabı, kalan tutar, kısmi ödeme, fazla ödemenin reddi, ürün seçerek ödeme ve otomatik kapanma test edilir. Uç noktalar `WebApplicationFactory` ve her test için geçici bir SQLite dosyasıyla entegrasyon testinden geçer. Yetki testi: garson 403 alır.
- **Vitest:** `format.ts` biçimlendirmesi ve tuş takımı ayrıştırması test edilir.
- **Uçtan uca:** sunucu çalıştırılır. İki tarayıcı sekmesinde biri garson, diğeri sahip olarak açılır. Birine kalem eklenince diğerinde anında göründüğü doğrulanır. Gramlı ürün, bölünmüş ödeme ve gün sonu raporu denenir.
- **Ekran boyutları:** 394 px (telefon), 800 px (tablet) ve masaüstü genişliğinde kontrol edilir. Tabloların kesilmediği ayrıca ölçülür.
- Yayın betiğiyle servis kurulur. Telefondan `http://<ip>:5080` adresine bağlanılarak test edilir.

