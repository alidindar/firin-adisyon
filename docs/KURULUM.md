# Fırın Adisyon — Kurulum ve Kullanım Rehberi

## 1. Kasa bilgisayarına kurulum

**Kısa yol (kasa PC'de .NET ya da Node gerekmez):** Geliştirme bilgisayarında proje klasöründe `scripts\publish.ps1`, ardından `scripts\paket.ps1` çalıştırın. Oluşan `paket\FirinAdisyon-Kurulum` klasörünü USB belleğe, oradan kasa PC'nin masaüstüne kopyalayın. Klasördeki **`Kur.cmd`** dosyasına sağ tıklayıp **Yönetici olarak çalıştır**'ı seçin ve Windows onayında **Evet**'e basın. Sonra aşağıdaki 3. ve 4. adımlara geçin. Güncellemede aynısını yeni paketle yapın; kaldırmak için `Kaldir.cmd` aynı şekilde çalıştırılır.

Elle kurulum:

1. Yayını üretin. Bu adım için bilgisayarda **.NET 10 SDK** ve **Node 24** kurulu olmalıdır. Kasa PC'de yoksa yayını geliştirme bilgisayarında üretip proje klasörünü `publish\FirinAdisyon` ile birlikte kasa PC'ye kopyalayın. Proje klasöründe PowerShell açın:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
   ```
   Betik web arayüzünü derler, testleri çalıştırır ve .NET kurulumu gerektirmeyen bir yayın hazırlar (`publish\FirinAdisyon`).
2. PowerShell'i **Yönetici olarak** açıp servisi kurun. Yönetici PowerShell `C:\Windows\System32` içinde açılır; önce proje klasörüne geçin (örnekteki yolu kendi proje klasörünüzün gerçek yoluyla, tırnak içinde değiştirin):
   ```powershell
   cd "C:\Users\Ali G\Desktop\firin-adisyon"
   powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
   ```
   Betik şunları yapar:
   - Programı `C:\FirinAdisyon` klasörüne kopyalar.
   - "Fırın Adisyon" servisini kurar. Servis bilgisayar açılınca kendiliğinden başlar, çökerse 5 saniye sonra yeniden başlar.
   - 5080 portu için güvenlik duvarı izni ekler (**Özel** ve **Etki alanı** ağları için).
   - Fişe takılıyken bilgisayarın uykuya ve hazırda bekletmeye geçmesini kapatır.
3. Tarayıcıda `http://localhost:5080` adresini açın. İlk açılışta sahip hesabı (ad ve 4 haneli PIN) oluşturulur. **Bunu kurulumdan hemen sonra, tabletleri bağlamadan önce yapın.** Sahip hesabı oluşana kadar kurulum ekranı aynı ağdaki herkese açıktır.
4. Bilgisayarın saatini kontrol edin: Ayarlar → Zaman ve dil → Tarih ve saat → **Saati otomatik ayarla: Açık**, **Saat dilimini otomatik ayarla: Açık** (ya da saat dilimi `(UTC+03:00) İstanbul`). Raporlar ve yedek adları bilgisayarın saatine dayanır.

### Güncelleme

Aynı iki komutu yeniden çalıştırın. Veriler korunur. Betik güncellemeden önce veritabanının bir kopyasını `C:\FirinAdisyon\data\backups` klasörüne alır (`firin-…db`) ve tam yolunu ekrana yazar. Ayarlar'da **başka bir yedek klasörü** (örn. USB) seçiliyse bu kopya yine de yedek listesinde görünür ve "son yedek" sayılır (liste `data\backups` klasörünü de gösterir). Kopya yalnızca `data\backups` klasörüne yeni bir yedek yazıldığında 30 günü geçtiyse silinir; gerekirse elle silebilirsiniz. Servis düzgün kapanmadıysa kopyanın yanında bir `-wal` dosyası da olur (bkz. Yedekten geri dönme). Yayın klasöründe (`publish\FirinAdisyon`) `data` klasörü varsa betik, verilerin üzerine yazılmasın diye durur; o klasörü silin.

### Klasörler

- Programın varsayılan veri klasörü exe'nin yanındaki `data` klasörüdür: **`C:\FirinAdisyon\data`**. Veritabanı (`firin.db`), ürün fotoğrafları (`photos`) ve yedekler (`backups`) burada durur.
- **`C:\FirinAdisyon` içine kendi dosyalarınızı koymayın.** Güncelleme sırasında `data` dışındaki her şey yayınla eşitlenir, fazlalıklar silinir.
- Servis **LocalSystem** hesabıyla çalışır (bkz. Yedekler).

## 2. Sabit IP (tavsiye edilir)

Tabletler kasa bilgisayarına IP adresiyle bağlanır. Adres değişirse tabletler bağlanamaz.
- Tabletler ve kasa bilgisayarı **aynı Wi-Fi ağında** olmalıdır. "Misafir ağı" ya da modemde "AP / istemci yalıtımı" açıksa cihazlar birbirini göremez ve tabletler bağlanamaz.
- Modemin yönetim sayfasına girin (genellikle `192.168.1.1`).
- "DHCP" ya da "LAN" ayarlarında "Adres rezervasyonu / Sabit DHCP" bölümünü bulun.
- Kasa bilgisayarını listeden seçip şu anki adresini sabitleyin.
- Kasa bilgisayarı Wi-Fi ile bağlıysa Windows'ta **Rastgele donanım adreslerini kapatın**: Ayarlar → Ağ ve İnternet → Wi-Fi → bağlı olduğunuz ağın adı → **Rastgele donanım adresleri: Kapalı**. Açıkken bilgisayar modeme farklı bir adresle görünebilir ve rezervasyon tutmaz. (Kasa bilgisayarını kabloyla bağlarsanız bu sorun olmaz.)

### Güvenlik duvarı ve ağ profili

Kurulum betiği "Firin Adisyon 5080" adlı kuralı ekler; bu kural yalnızca **Özel** ve **Etki alanı** ağlarında geçerlidir. Wi-Fi ya da Ethernet bağlantısı Windows'ta **Ortak** görünüyorsa tabletler bağlanamaz (betik bu durumda uyarı verir). Düzeltmek için: Ayarlar → Ağ ve İnternet → Wi-Fi (ya da Ethernet) → ağ adı → Ağ profili türü: **Özel**.

## 3. Tablet ve telefonu bağlama

1. Cihazı fırının Wi-Fi ağına bağlayın.
2. Kasada Yönetim → Ayarlar sayfasını açın ve QR kodu cihazın kamerasıyla okutun.
3. Kısayol ekleyin:
   - **iPhone / iPad (Safari):** Paylaş düğmesi → "Ana Ekrana Ekle". Simgeyle açılınca uygulama tam ekran, adres çubuğu olmadan açılır.
   - **Android (Chrome):** sağ üst menü (⋮) → "Ana ekrana ekle". Adres `http://` ile başladığı için Chrome bunu yüklenebilir uygulama saymaz; simge uygulamayı normal bir sekmede, **adres çubuğuyla birlikte** açar. Uygulama aynı şekilde çalışır.
4. Her garson kendi adını seçip PIN'iyle girer.

**Android'de adres çubuğunu gizlemek (isteğe bağlı, tablet başına bir kez):** Chrome'da `chrome://flags/#unsafely-treat-insecure-origin-as-secure` adresini açın, kutuya `http://<kasa-IP>:5080` yazın (örn. `http://192.168.1.20:5080`), seçeneği **Enabled** yapıp Chrome'u yeniden başlatın. Sonra kısayolu silip "Uygulamayı yükle" ile yeniden ekleyin.

## 4. Günlük kullanım

- **Masalar** ekranında dolu masalar renkli görünür. Kartta kalan tutar ve masanın açık kaldığı süre yazar.
- Masaya dokunup ürün ekleyin. Gramla satılan ürünlerde tartıdaki gramı tuş takımına yazın.
- **Ödeme al** ile ödemeyi tamamını, bir tutarı, kişi başı ya da ürün seçerek alın. Kalan sıfırlanınca masa boşalır.
- Kırmızı "bağlantı yok" bandı çıkarsa:
  - Kasa bilgisayarının açık olduğunu kontrol edin.
  - Cihazın fırının Wi-Fi ağında olduğunu kontrol edin.
  - Bağlantı gelince band kendiliğinden kaybolur.

## 5. Yedekler

- Program her gün otomatik yedek alır ve 30 gün saklar. Yönetim → Ayarlar'dan elle de yedek alabilir, indirebilirsiniz. Her yedek tek ve kendi başına yeterli bir dosyadır (`firin-yyyyMMdd-HHmmss.db`).
- **Yedek klasörü ayarını boş bırakmanızı tavsiye ederiz.** Yedekler `C:\FirinAdisyon\data\backups` içine alınır. Bunun yerine **haftada bir tüm `C:\FirinAdisyon\data` klasörünü USB belleğe kopyalayın** (fotoğraflar da böylece yedeklenir, aşağıya bakın). Yedek klasörünü USB'ye yönlendirmek yerine bu yolu seçin; çünkü USB çıkarılabilir.
- Yine de başka bir klasör seçerseniz (örn. `E:\FirinYedek`) mutlak bir yol olmalıdır. Bu klasöre ulaşılamazsa (USB çıkarıldı, sürücü harfi değişti) program yedeği **kendiliğinden `C:\FirinAdisyon\data\backups` klasörüne alır**, yedek alınmış olur ve Ayarlar'da "Yedek klasörüne ulaşılamadı; yedek varsayılan klasöre alındı." uyarısı görünür. USB'yi geri takınca yedekler yeniden ona yazılır ve uyarı kalkar; ancak bir sonraki otomatik yedek 24 saate kadar gecikebilir. USB'yi taktıktan sonra Yönetim → Ayarlar'da bir kez **Yedek al** düğmesine basın. Yedek listesi iki klasördeki yedekleri birlikte gösterir. Uygulama klasörünün (`C:\FirinAdisyon`) altındaki klasörler (`data` hariç) yedek klasörü olarak kabul edilmez; güncelleme onları silerdi.
- Servis LocalSystem hesabıyla çalıştığı için:
  - USB / harici disk ve kullanıcının OneDrive klasörü (`C:\Users\<ad>\OneDrive\FirinYedek`) çalışır.
  - Bağlı ağ sürücüleri (örn. `Z:`) ve çoğu ağ paylaşımı (`\\sunucu\...`) servise görünmez; Ayarlar "klasör yazılamıyor" hatası verir.

### Fotoğraflar yedeğe dahil değildir

Yedek dosyaları yalnızca veritabanını (ürünler, adisyonlar, ödemeler, kullanıcılar) içerir. **Ürün fotoğrafları `C:\FirinAdisyon\data\photos` klasöründedir ve yedeklerde yoktur.**
- Haftada bir tüm `C:\FirinAdisyon\data` klasörünü (veritabanı, `photos` ve `backups` birlikte) USB belleğe kopyalayın.
- Bilgisayar tamamen bozulursa: veritabanını yedek dosyadan geri yükleyin, fotoğrafları bu USB kopyasındaki `photos` klasöründen `C:\FirinAdisyon\data\photos` içine geri koyun. Kopya yoksa fotoğrafları ürün formundan yeniden yüklemeniz gerekir; ürünler fotoğrafsız çalışmaya devam eder.

### Yedekten geri dönme

1. PowerShell'i Yönetici olarak açın: `Stop-Service FirinAdisyon`
2. `C:\FirinAdisyon\data\firin.db` dosyasının yanındaki varsa `firin.db-wal` ve `firin.db-shm` dosyalarını silin.
3. Geri dönmek istediğiniz yedeği (örn. `firin-20261021-060000.db`) `C:\FirinAdisyon\data\firin.db` adıyla kopyalayın. Mevcut dosyanın üzerine yazın. Yedeğin yanında aynı adlı bir `-wal` dosyası varsa (örn. `firin-20261021-060000.db-wal`; güncelleme öncesi kopyalarda olabilir) onu da `firin.db-wal` adıyla yanına kopyalayın; yoksa son kayıtlar kaybolur.
4. `Start-Service FirinAdisyon`

Yedek dosya adındaki saat UTC'dir (Türkiye saatinden 3 saat geri). Ayarlar sayfası yedekleri Türkiye saatiyle listeler.

## 6. Hatalar nerede görülür

Program ayrı bir günlük dosyası tutmaz. Hatalar Windows **Olay Görüntüleyicisi**'ne yazılır: Başlat'ta "Olay Görüntüleyicisi" → Windows Günlükleri → **Uygulama**. Kaynağı `FirinAdisyon` ya da `.NET Runtime` olan Uyarı ve Hata kayıtlarına bakın. Destek isterken bu kayıtların metnini gönderin.

## 7. Uygulama simgelerini yeniden üretme

Simgeler hazır gelir. Değiştirmek isterseniz proje klasöründe:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/make-icons.ps1
```

Sonra `scripts\publish.ps1` ve `scripts\install-service.ps1` ile yeniden yayınlayın.

## 8. Kaldırma

`scripts\uninstall-service.ps1` betiğini Yönetici olarak çalıştırın. Servis ve güvenlik duvarı kuralı kalkar; veriler silinmez.

## 9. Kurulum provası

Dükkândaki ilk kurulumdan önce (geliştirme bilgisayarında ya da dükkânda mesai dışında) Yönetici PowerShell ile bir kez deneyin. Servis yolu yalnızca böyle doğrulanır:

1. `scripts\install-service.ps1` çalıştırılır. `Get-Service FirinAdisyon` → **Running**.
2. `http://localhost:5080` kurulum ekranını gösterir ve `C:\FirinAdisyon\data\firin.db` dosyası vardır.
3. `Firin.Api.exe` işlemi Görev Yöneticisi'nden sonlandırılır: servis yaklaşık 5 saniyede geri gelir.
4. `scripts\install-service.ps1` güncelleme olarak yeniden çalıştırılır: veriler korunur ve güncelleme öncesi kopyanın yolu ekrana yazılır.
5. `scripts\uninstall-service.ps1` çalıştırılır: servis ve güvenlik duvarı kuralı kalkar. Provayı dükkânda yaptıysanız veriler kalır; temiz başlamak istiyorsanız `C:\FirinAdisyon` klasörünü silin.

## 10. Gerçek cihaz kontrol listesi

Kurulumdan sonra kasa PC'de, bir tablette ve bir telefonda sırayla deneyin. Masaüstü öykünmesi bu maddeleri göstermez. Sorun çıkan maddeyi not edin.

**Kasa PC**
1. `Get-Service FirinAdisyon` → `Running`.
2. `http://localhost:5080` açılır; ilk kurulum ekranı görünür (geliştirme verileri gelmez, veri klasörü artık `C:\FirinAdisyon\data`).
3. `Firin.Api.exe` işlemini Görev Yöneticisi'nden sonlandırın: servis yaklaşık 5 saniyede geri gelir, tabletler yeniden bağlanır.
4. Bilgisayarı yeniden başlatın: servis kendiliğinden başlar, veriler yerinde durur, otomatik yedek Ayarlar'da görünür.

**Tablet / telefon**
1. Ayarlar'daki QR okutulur, sayfa açılır, ana ekrana eklenir. iOS: simgeyle açılınca adres çubuğu yoktur. Android: simge açılır; HTTP olduğu için adres çubuğu görünebilir.
2. Düğmelere dokununca gri parlama olmaz. Basınca düğme hafifçe küçülür, bırakınca geri gelir. Rapor sütununa dokununca renk takılı kalmaz.
3. PIN, fiyat ve gram alanlarına dokununca sayfa yakınlaşmaz. Sayısal klavye açılır.
4. Masalar ekranında aşağı çekince sayfa yenilenmez.
5. Uzun basınca düğme yazısı seçilmez.
6. Ödeme diyaloğunda "Tutar gir" alanına dokununca klavye açılır ve Nakit/Kart düğmeleri klavyenin üstünde görünür kalır (Android).
7. Tablet yatay ve dikey çevrilir. Yatayda hesap ve ürünler yan yana durur. Çentikli telefonda başlık çentiğin altında kalmaz.
8. Tablet 5 dakika uyku moduna bırakılıp açılır. Birkaç saniye içinde bağlantı bandı kaybolur; bu arada kasada eklenen kalemler görünür.
9. Kasa PC'nin Wi-Fi'ı kapatılınca tablette kırmızı band çıkar ve düğmeler kilitlenir. Açılınca düzelir.
10. Telefonda bir rapor sütununa dokunun: ipucu satırı güncellenir, renk takılı kalmaz. "Tablo olarak göster" yatay kaydırma olmadan açılır.

**Yönetim dokunma kontrolü (sahip hesabıyla, gerçek tablette)**
1. Yönetim bağlantısına dokunun; sekmeleri (Ürünler, Kategoriler, Masalar, Kullanıcılar, Ayarlar) sırayla gezin.
2. Yeni ürün ekleyin: formu doldurun, kamerayla fotoğraf çekin ya da galeriden yükleyin, kaydedin. Ürünü pasif / aktif yapın.
3. Masa ekleyin. Açılan pencereleri hem düğmesiyle hem pencere dışına dokunarak kapatın.
4. Ayarlar'da QR kodunu ve yedek indirmeyi deneyin.

**İki pencerede canlı yenileme**
1. Garson tabletinde bir masaya kalem ekleyin: sahibin bilgisayarındaki Masalar ekranında hemen görünür.
2. Tablette ödeme alın: sahibin Raporlar → "Bugün" ciro değeri yaklaşık 3 saniye içinde güncellenir.
