# 🏇 VELİEFENDİ & PADOK — Elit At Yarışı Analiz, Derece ve Soyağacı Bülteni

Bu proje, Türkiye Jokey Kulübü (TJK) canlı verilerini gerçek zamanlı çeken, safkanların geçmiş koşu derecelerini **Özel Performans & Derece Bülteni (Bankomat)** metodolojisiyle normalize eden, **3 Kuşak Soyağacı (Pedigri)** ve **Çim/Kum/Sentetik pist sevgisini** çıkaran, **10.000 koşuluk Monte Carlo simülasyonu** ve **Tempo (Gidişat) Haritası** ile çalışan mobil PWA uyumlu bir at yarışı analiz uygulamasıdır.

---

## 📂 Klasör ve Dosya Yapısı

Proje dizini: `C:\Users\hakta\.gemini\antigravity\scratch\turf-quant-pro`

```text
turf-quant-pro/
├── server.py               # Python (Sıfır bağımlılık) Backend API + TJK Canlı Scraper + Analiz Motoru
├── README.md               # Proje mimarisi, algoritma formülleri ve devir teslim dokümanı
└── static/
    ├── index.html          # Mobil PWA arayüz iskeleti (Veliefendi & Royal Ascot Elit Kulüp Teması)
    ├── app.js              # Tüm ön yüz (Frontend) mantığı, interaktif kupon kurucu, modal ve tablolar
    └── manifest.json       # Telefondan girildiğinde tam ekran mobil uygulama (PWA) olmasını sağlayan manifest
```

### 🚀 Nasıl Çalıştırılır?
Hiçbir harici kütüphane (`pip install` veya `npm install`) gerektirmez. Standart Python 3 ile doğrudan çalışır:
```bash
python server.py
```
Sunucu `http://localhost:8090` adresinde başlar.

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
  * Her ata kulvar, takı (`KG`, `DB`, `SK`), mesafe ve form yapısına göre koşu stili atar: **Kaçak (`E`)**, **Takipçi (`P`)**, **Son Düzlükçü / Spinter (`S`)**.
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
  4. **🎫 6'lı Kurgu:** Küçük, Orta veya Geniş ölçekli stratejiye göre otomatik 6'lı Ganyan kupon kurucu ve interaktif kombinasyon/tutar hesaplayıcı.
  5. **⚖️ Kıyasla:** İki safkanı 8 kriterde başa baş terazi barlarıyla kıyaslama + Sağ üstte **Başhandikapçı Terazi Ayarı** (kriter ağırlıklarını canlı değiştirme).
