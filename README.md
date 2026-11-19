# 🏇 VELİEFENDİ & PADOK — Elit At Yarışı Analiz, Derece ve Soyağacı Bülteni

Bu proje, Türkiye Jokey Kulübü (TJK) canlı verilerini gerçek zamanlı çeken, safkanların geçmiş koşu derecelerini **Özel Performans & Derece Bülteni (Bankomat)** metodolojisiyle normalize eden, **3 Kuşak Soyağacı (Pedigri)** ve **Çim/Kum/Sentetik pist sevgisini** çıkaran, **10.000 koşuluk Monte Carlo simülasyonu** ve **Tempo (Gidişat) Haritası** ile çalışan mobil PWA uyumlu bir at yarışı analiz uygulamasıdır.

---

## 📂 Klasör ve Dosya Yapısı

Proje dizini: `C:\Users\hakta\.gemini\antigravity\scratch\turf-quant-pro`

```text
turf-quant-pro/
├── server.py               # Python (Sıfır bağımlılık) Backend API + TJK Canlı Scraper + Analiz Motoru
├── README.md               # Proje mimarisi, algoritma formülleri ve devir teslim dokümanı
├── tests/
│   └── test_server.py      # İnternetsiz analiz motoru testleri (sentetik TJK CSV)
└── static/
    ├── index.html          # Mobil PWA arayüz iskeleti (Veliefendi & Royal Ascot Elit Kulüp Teması)
    ├── app.js              # Tüm ön yüz (Frontend) mantığı, interaktif kupon kurucu, modal ve tablolar
    ├── sw.js               # Servis çalışanı: çevrimdışı açılış + son bülteni saklama
    ├── manifest.json       # Telefona kurulabilir uygulama (PWA) tanımı
    └── icon*.svg/png       # Uygulama ikonları (yarış yeşili zemin, pirinç nal)
```

### 🚀 Nasıl Çalıştırılır?
Hiçbir harici kütüphane (`pip install` veya `npm install`) gerektirmez. Standart Python 3 ile doğrudan çalışır:
```bash
python server.py
```
Sunucu `http://localhost:8090` adresinde başlar.

**Testler** (internet gerekmez):
```bash
python -m unittest discover -s tests -v
```

**Telefona kurulum (PWA):** Bilgisayarda `localhost` üzerinden açıldığında tarayıcı menüsünden "Uygulamayı yükle" ile kurulabilir; çevrimdışı açılır ve son çekilen bülteni gösterir. Telefondan aynı ağdaki `http://192.168.x.x:8090` adresiyle girildiğinde uygulama çalışır, ancak tarayıcılar servis çalışanını yalnızca HTTPS'te etkinleştirdiği için çevrimdışı mod ve "yükle" seçeneği orada kapalı kalır.


---

## 🎯 Kullanıcının İstekleri ve Projede Gerçekleştirilen Tüm Detaylar

### 1. Canlı TJK Veri Çekme Motoru (`server.py`)
* **Çoklu Hipodrom & Günlük Bülten:** `https://www.tjk.org/TR/YarisSever/Info/Page/GunlukYarisProgrami` adresinden o günkü tüm yerli ve yabancı hipodromları (`city_id`, `city_name`, `is_domestic`) otomatik keşfeder.
* **Resmi CSV + HTML Hibrit Ayrıştırıcı:**
  * TJK Medya CDN (`medya-cdn.tjk.org/.../CSV/GunlukYarisProgrami/...csv`) üzerindeki resmi günlük CSV dosyasını indirir ve koşu saatlerini, koşu şartlarını, mesafeyi, pisti, rekor dereceyi, ikramiyeleri, safkan bilgilerini, AGF yüzdelerini, handikap puanlarını, son 6 yarış dizilimini (`K2K3Ç1...`), KGS, s20 ve en iyi dereceyi ayrıştırır.
  * Aynı anda şehir HTML sayfasını tarayarak CSV'de bulunmayan **Eküri Forma Görsellerini (`forma_url`)**, **Safkan TJK ID'sini (`at_id`)** ve **Kısrak Babası (`damsire`)** bilgisini eşleştirir.

### 2. Özel Performans ve Derece Bülteni (Bankomat Metodolojisi)
* **Koşu Zorluk Katsayısı (`compute_zorluk_degree`):** Maiden/Şartlı 1 (`30.5`) koşulardan G1 (`52.0`) koşulara ve Handikap puanına göre her yarışın zorluk derecesini hesaplar.
* **1200m Normalize Derece (`norm_1200_derece`):** Farklı mesafelerde (1000m, 1400m, 1600m, 2000m) koşulmuş dereceleri Riegel mesafe yorgunluk üssü (`(1200 / msf) ** 1.055`) ile **1200 metre eşdeğer derecesine** (`1.13.85` - `1.16.50` bandına) normalize eder. Böylece farklı mesafeden gelen atların hızları doğrudan kıyaslanır.
* **Performans Puanı & Harf Form Notu (`A+, A, B, C, D, E`):** Koşu zorluğu, 1200m standart par süresine göre hız farkı ve bitiriş sırasını birleştirerek her geçmiş yarış için bir `Performans` puanı ve `A-E` arası harf notu üretir.
* **Bugünkü Kiloya Göre Performans (`kiloya_gore`):** Atın geçmiş yarışta taşıdığı sıklet ile bugün taşıyacağı sıklet arasındaki farkı (`±%0.8 / kg`) performans puanına yansıtır.
* **Pist Durumu Kodu (`P.D`) ve Yarış İçi Pozisyon (`Poz%`):** `NO` (Normal), `NE` (Nemli), `IS` (Islak), `ÇY` (Çok Yumuşak), `SE` (Sentetik) kodlarını ve yarış içi pozisyon yüzdesini hesaplar.
* **3 Kademeli Kupon Şablonu (`coupon_tiers`):** Her koşuyu otomatik olarak **Küçük Ölçekli Kuponlar**, **Orta Ölçekli Kuponlar** ve **Geniş Kuponlar** olarak 3 kademeye ayırır.

### 3. Soyağacı (3 Kuşak Pedigri) ve Pist Sevgisi / Yatkınlığı (`/api/horse_history`)
Kullanıcı bir ata tıkladığında arka planda TJK'nın 3 ayrı uç noktasına canlı bağlanır:
1. **`AtKosuBilgileri?QueryParameter_AtId={at_id}`:**
   * Atın kariyerindeki **Çim, Kum ve Sentetik** pist koşu adetlerini, `1.-2.-3.-4.` sayılarını, kazanma/podyum/tabela yüzdelerini ve kazançlarını çeker.
   * Her pist için `0-100` arası **Pist Sevgi / Uyum Puanı (`affinity_score`)** hesaplar ve **"En Sevdiği Pist: KUM / ÇİM / SENTETİK"** kararını üretir.
   * Ayrıca son 10 yarışının tam **Özel Performans ve Derece Bülteni** tablosunu oluşturur.
2. **`Pedigri/Pedigri?Atkodu={at_id}` (`parse_pedigri_tree`):**
   * TJK'nın `#pedigri` tablosunu ayrıştırarak **3 Kuşak (14 Ata) Soyağacı Şemasını** çıkarır:
     * **Baba Kan Hattı (♂):** Baba ➔ Baba Babası & Baba Annesi ➔ 4 Büyük-Büyükbaba/Anne.
     * **Anne Kan Hattı (♀):** Anne ➔ **Kısrak Babası (Anne Babası)** & Anne Annesi ➔ 4 Büyük-Büyükbaba/Anne.
3. **`AtKosuIstatistik/AtKosuIstatistik?Atkodu={at_id}` (`parse_detailed_stat_tables`):**
   * Safkanın **Zemin Durumu (Normal, Nemli, Islak)** ve **Mesafe (1100m, 1200m, 1400m, 1600m vb.)** bazlı detaylı performans karnesini çeker.

### 4. Kantitatif Analiz, Tempo Haritası ve Monte Carlo Simülasyonu (`analyze_race_quant`)
* **6 Boyutlu Puanlama:**
  1. `speed_figure`: En iyi derece ve rekor yakınlığı (Beyer tarzı hız figürü)
  2. `form_rating`: Son 6 yarış ağırlıklı form + bugünkü pist uyumu bonusu
  3. `class_rating`: Handikap puanı ve grup klası
  4. `jockey_rating`: Elit jokey katsayısı + ahırın son 20 yarış (`s20`) ivmesi
  5. `weight_rating`: Mesafe uzadıkça logaritmik artan sıklet (kilo) avantaj/dezavantajı
  6. `fitness_rating`: KGS (Koşmadığı Gün Sayısı) dinlenme, tazelik ve kondisyon analizi
* **Koşu Gidişatı ve Tempo Senaryosu (`infer_running_style` & `pace_scenario`):**
  * Her ata deterministik ve açıklanabilir sinyallerle bir **Erken Hız Endeksi** hesaplar: kulvar, takı (`KG`, `DB`, `SK`/`K`), mesafe, yaş (2 yaşlı), sahaya göre en iyi derece sırası, sıklet farkı ve son koşulardaki bitiriş düzeni. Her sinyal gerekçesiyle safkan penceresinde gösterilir.
  * Stil sahaya göre göreceli atanır (`assign_running_styles`): **Kaçak (`E`)** = endeks ≥ 60, sahanın ilk 3'ünde ve liderin en fazla 6 puan gerisinde; **Son Düzlükçü / Spinter (`S`)** = endeks < 46 ya da sahanın son %30'unda; diğerleri **Takipçi (`P`)**.
  * Koşuda tek kaçak varsa **`LONE_SPEED` (Tek Kaçak / Beyaz Bayrak-Ayna Avantajı)**, 3 veya daha fazla kaçak varsa **`SPEED_DUEL` (Çok Sert Tempo / Spinter Avantajı)** senaryosunu kurar ve atların puanlarını gidişata göre günceller.
* **Monte Carlo Simülasyonu & Cazip Safkan (Value) Tespiti:**
  * Her atın ortalama puanı ve istikrarına (standart sapma) göre koşuyu olasılıksal simüle ederek **Kazanma %**, **İlk 2 %**, **İlk 3 (Tabela) %** ve **Eder Ganyan** üretir.
  * Bülten kazanma yüzdesi ile halkın oynadığı **AGF %** arasındaki farkı (`value_edge`) hesaplayarak ihmal edilmiş **Cazip Sürpriz Safkanları** yakalar.

### 5. Görsel Tasarım: "Veliefendi & Padok" Elit Kulüp Teması (`index.html` & `app.js`)
* Standart karanlık/neon "AI" şablonlarından tamamen arındırılmış, **Royal Ascot / Racing Post / Veliefendi Kulüp Locası** estetiğinde tasarlanmıştır.
* **Renk Paleti:** Sıcak Fildişi Bülten Kağıdı (`#F6F3EC`), İngiliz Yarış Yeşili (`#0F291E` / `#1B4332`), Pirinç Altını (`#A67C37` / `#D4AF37`), Eyer Derisi (`#8C5A3C`) ve Bordo (`#7A1C1C`).
* **Tipografi:** Başlıklarda `Cormorant Garamond` (Klasik Serif), metinlerde `Plus Jakarta Sans`, derece ve oranlarda `JetBrains Mono`.
* **5 Alt Sekmeli Mobil PWA Deneyimi:**
  1. **📖 Bülten:** Koşu özeti, Öne Çıkan İlk 4, Küçük/Orta/Geniş Kupon Şablonu, Safkan Kartları ve Başhandikapçı Notları.
  2. **⏱️ Tempo:** Koşu gidişatı analizi, İlk 600m piste yayılış şeması ve İhtimal/Eder Ganyan tablosu.
  3. **🏛️ Cazip Atlar:** Tüm koşulardaki AGF oranına göre en yüksek şans farkı taşıyan sürpriz adayları.
  4. **🎫 6'lı Kurgu:** Ayaklar TJK dipnotundaki *"6'LI GANYAN Bu koşudan başlar"* işaretinden kurulur (`build_altili_groups`); günde iki altılı varsa seçici çıkar. İşaret yoksa son 6 koşu kullanılır ve bu açıkça belirtilir. Küçük, Orta veya Geniş ölçekli stratejiye göre otomatik 6'lı Ganyan kupon kurucu ve interaktif kombinasyon/tutar hesaplayıcı.
  5. **⚖️ Kıyasla:** İki safkanı 8 kriterde başa baş terazi barlarıyla kıyaslama + Sağ üstte **Başhandikapçı Terazi Ayarı** (kriter ağırlıklarını canlı değiştirme).

### 6. Başhandikapçı Terazisi (Tek Doğruluk Kaynağı)
* Varsayılan ağırlıklar `server.py` içindeki `DEFAULT_WEIGHTS` ile sürgüler birebir aynıdır: Kronometre **25**, Form **24**, Klas **18**, Jokey **14**, Sıklet + KGS **19** (iç oran 10:9).
* Terazi değiştirildiğinde ön yüz `/api/program?w=25,24,18,14,19` biçiminde sunucuya gönderir; sunucu önbellekteki bülteni bozmadan **tüm analizi** (bileşik puan, tempo düzeltmesi, Monte Carlo İlk 2 / Tabela %, rol rozetleri, kupon ölçekleri) yeniden yapar. Ayar tarayıcıda hatırlanır.

### 7. Güvenlik ve Dayanıklılık
* TJK'dan gelen tüm metinler ön yüze yüklenirken tek noktada HTML kaçışından geçer (`deepEscape`).
* Önbellek süreli: bülten 5 dk (AGF güncel kalsın diye), hipodrom listesi 10 dk, safkan geçmişi 6 sa. ↻ düğmesi hepsini temizler.
* Sunucu çok iş parçacıklıdır (`ThreadingHTTPServer`); bir safkanın TJK sayfaları çekilirken diğer istekler beklemez.
* AGF verisi olmayan koşularda "Cazip Safkan / Sürpriz" kararı verilmez (piyasa bilinmeden değer hesaplanamaz).

### 8. Derece Karnesi, Koşu Sınıfı Karnesi, Handikap Seyri ve İdman
Bülten kartlarında (koşu açılınca arka planda yüklenir) ve safkan penceresinde:
* **Derece Karnesi (yarış başına):** Birincilik, İkincilik, Üçüncülük yüzdeleri ile **İlk 3** ve **İlk 5** yüzdeleri — Kariyer, Son 10 koşu, Çim / Kum / Sentetik kırılımı. Kaynak: TJK özet tablosu (1.–5.'lik sayıları); genç atlarda özet tablo yoksa tüm koşulardan hesaplanır. Derecesiz biten koşular da yarış sayısına dahildir.
* **Koşu Sınıfı Karnesi (`classify_race`):** Tüm kariyer koşuları Grup 1/2/3 → Kısa Vade (KV) → Şartlı 5…1 → Handikap (yüksekten düşüğe) → Maiden → Satış sırasıyla gruplanır; her sınıfta K, 1-2-3, 1.%, İlk 3 %, İlk 5 %, ortalama ve en iyi sıra.
* **Handikap Puanı Seyri:** Son 12 koşudaki HP çubuk grafiği, güncel / en yüksek / en düşük HP ve son 5 koşudaki değişim (kartta `HP 70 ▲17`). Geçmiş koşu tablosuna HP sütunu eklendi.
* **Son İdman (Galop • Sprint • Kenter):** TJK bültenindeki "İdm" bağlantısından koşunun idman tablosu (`/api/race_idman`) ve her idmanın detayı çekilir: ara dereceler (2200m…200m), idman türü, **Galop Durumu (Çok Rahat / Rahat / Çalışır)**, idman jokeyi, pist/zemin/hava, kaç gün önce yapıldığı, 200m başına ortalama tempo ve koşu içinde **son 400m sıralaması** ("EN HIZLI SON 400").
* **Pist durumu:** Çim pistlerdeki penetrometre değeri ayrılır (`Ç:Yumuşak 3.7` → `YM 3.7`); Biraz Yumuşak (BY), Çok Ağır (ÇA) gibi durumlar doğru kodlanır.
* **Zorluk katsayısı düzeltmesi:** Python'da `"Handikap".upper()` noktasız `HANDIKAP` verdiği için tüm handikap koşuları Maiden seviyesinde (32.0) puanlanıyordu; Türkçe büyük harf (`tr_upper`) ile düzeltildi. `G 3` yazımı da artık Grup 3 olarak tanınır.
