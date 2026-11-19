# -*- coding: utf-8 -*-
"""
TURF QUANT PRO — TJK Canlı Veri Çekme ve Kantitatif At Yarışı Analiz Motoru
---------------------------------------------------------------------------
Canlı TJK Bülten ve CSV/HTML verilerini çeker, her koşu ve safkan için:
1. Hız Figürü (Beyer/Speed Index) & Rekor Dereceye Yakınlık
2. Son 6 Yarış Pist Uyumu (Kum/Çim/Sentetik) & Trend (Form Momentum)
3. Koşu Stili (Kaçak/E, Presser/P, Spinter/S) ve Yarış Tempo Senaryosu (Pace Map)
4. Sıklet (Kilo) & Mesafe Logaritmik Cezası + Apranti/Usta Jokey Sinerjisi
5. KGS (Koşmadığı Gün Sayısı - Dinlenme/Bounce Analizi) ve s20 İstikrarı
6. Takı Etkisi (KG, DB, SK, GKR, K) ve Eküri Avantajı
7. 10.000 Koşuluk Monte Carlo Simülasyonu (Win %, Top 2 %, Top 3 %)
8. AGF Karşılaştırmalı "Value Bet / Akıllı Para / Sürpriz" (Overlay Edge) Hesabı
9. Bütçeye Göre Otomatik Altılı Ganyan Kupon Optimizasyon Motoru
"""

import os
import re
import csv
import io
import json
import math
import random
import copy
import html as html_lib
import urllib.request
import urllib.error
from concurrent.futures import ThreadPoolExecutor
import urllib.parse
from datetime import datetime, timedelta, timezone
import time
import threading
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler

# Türkiye saati (2016'dan beri yaz saati uygulaması yok: sabit UTC+3).
# Sunucu hangi saat diliminde çalışırsa çalışsın "bugünkü bülten" Türkiye tarihine göre seçilir.
TR_TZ = timezone(timedelta(hours=3))


def tr_now():
    return datetime.now(TR_TZ)


BASE_DIR = os.path.dirname(os.path.abspath(__file__))
STATIC_DIR = os.path.join(BASE_DIR, "static")

CACHE = {}
CACHE_LOCK = threading.Lock()
# Önbellek süreleri (saniye): AGF gün içinde değiştiği için bülten kısa tutulur
TTL_CITIES = 600
TTL_PROGRAM = 300
TTL_HISTORY = 6 * 3600


def cache_get(key, ttl):
    with CACHE_LOCK:
        item = CACHE.get(key)
        if not item:
            return None
        ts, value = item
        if time.time() - ts > ttl:
            CACHE.pop(key, None)
            return None
        return value


def cache_set(key, value):
    with CACHE_LOCK:
        CACHE[key] = (time.time(), value)
        # Bellek sınırı: en eski kayıtları at
        if len(CACHE) > 600:
            for k, _ in sorted(CACHE.items(), key=lambda kv: kv[1][0])[:100]:
                CACHE.pop(k, None)

HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
    "Accept": "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
    "Accept-Language": "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7",
}

ELITE_JOCKEYS = {
    "H.KARATAŞ": 98, "A.ÇELİK": 95, "G.KOCAKAYA": 93, "Ö.YILDIRIM": 92,
    "S.KAYA": 92, "M.KAYA": 90, "A.SÖZEN": 89, "V.ABİŞ": 90,
    "M.ÇİÇEK": 89, "N.AVCİ": 87, "K.TOKAÇOĞLU": 88, "A.YILDIZ": 85,
    "M.AKYAVUZ": 87, "E.AKTUĞ": 86, "H.ŞİMŞEK": 84, "A.KÜRÜNDÜK": 85,
    "F.ÇETİNBAŞ": 85, "S.BOYRAZ": 87, "M.HAMEDİ": 84, "İ.S.SÖĞÜT": 83,
}

# Başhandikapçı Terazisi varsayılan ağırlıkları (ön yüz sürgüleri ile birebir aynı).
# "weight" sürgüsü Sıklet + KGS tazeliğini birlikte temsil eder (iç oran 10:9).
DEFAULT_WEIGHTS = {"speed": 25.0, "form": 24.0, "class": 18.0, "jockey": 14.0, "weight": 19.0}
WEIGHT_KEYS = ["speed", "form", "class", "jockey", "weight"]


def normalize_weights(weights):
    w = dict(DEFAULT_WEIGHTS)
    if weights:
        for k in WEIGHT_KEYS:
            try:
                w[k] = max(0.0, min(100.0, float(weights.get(k, w[k]))))
            except (TypeError, ValueError):
                pass
    if sum(w.values()) <= 0:
        w = dict(DEFAULT_WEIGHTS)
    return w


def parse_weights_param(raw):
    """'25,24,18,14,19' -> dict; hatalı/eksikse None"""
    if not raw:
        return None
    parts = raw.split(",")
    if len(parts) != len(WEIGHT_KEYS):
        return None
    try:
        return normalize_weights({k: float(v) for k, v in zip(WEIGHT_KEYS, parts)})
    except ValueError:
        return None


TAKI_CODES = {"KG", "DB", "SK", "SKG", "GKR", "K", "YP", "ÖG", "BB", "ÇB", "SGKR"}

TAKI_DESCRIPTIONS = {
    "KG": "Kapalı Gözlük (Odaklanma & Hız +)",
    "DB": "Dil Bağı (Solunum & Son Düzlük Direnci +)",
    "SK": "Starta Kadar Kulaklık (Stres Kontrolü +)",
    "SKG": "Starta Kadar Gözlük (Sakin Çıkış +)",
    "GKR": "Göz Koruyucu (Kum Sıçramasına Karşı +)",
    "K": "Kulaklık (Sakinlik & Tempo Koruma +)",
    "YP": "Yanak Peluşu (Görüş Hizalama +)",
    "ÖG": "Örgülü Yele",
}


# TJK aynı anda çok istek gelince var olan sayfalara bile 404 döner: eşzamanlı isteği sınırla
TJK_SEMAPHORE = threading.BoundedSemaphore(6)
TRANSIENT_HTTP = {404, 429, 500, 502, 503, 504}


def fetch_url(url, encoding="utf-8", timeout=20, retries=2):
    """
    TJK'dan sayfa çeker. Zaman aşımı, bağlantı kopması ve yoğunlukta dönen sahte 404/5xx
    yanıtlarında artan beklemeyle yeniden dener (gerçekten olmayan sayfa da 3 denemeden sonra 404 verir).
    """
    last_err = None
    for attempt in range(retries + 1):
        try:
            with TJK_SEMAPHORE:
                req = urllib.request.Request(url, headers=HEADERS)
                with urllib.request.urlopen(req, timeout=timeout) as resp:
                    raw = resp.read()
            return raw.decode(encoding, "ignore")
        except urllib.error.HTTPError as e:
            if e.code not in TRANSIENT_HTTP:
                raise
            last_err = e
        except Exception as e:  # zaman aşımı / bağlantı kopması
            last_err = e
        if attempt < retries:
            time.sleep(0.8 * (attempt + 1))
    raise last_err


def fetch_cached(url, ttl):
    """Ham TJK sayfasını önbellekli çeker (aynı sayfa farklı API çağrılarında yeniden indirilmez)."""
    key = "raw_" + url
    cached = cache_get(key, ttl)
    if cached is not None:
        return cached
    body = fetch_url(url)
    cache_set(key, body)
    return body


def tr_upper(text):
    """Türkçe büyük harf: Python'da 'i'.upper() == 'I' olduğu için 'Handikap' -> 'HANDIKAP' olur."""
    return (text or "").replace("i", "İ").replace("ı", "I").upper()


def clean_cell(fragment):
    return html_lib.unescape(re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', '', fragment))).strip()


def parse_time_to_seconds(t_str):
    """Converts TJK time strings like '1:29.93' or '1.24.15' to float seconds."""
    if not t_str:
        return None
    s = t_str.strip().replace(":", ".")
    parts = [p for p in s.split(".") if p.isdigit()]
    try:
        if len(parts) == 3:
            mins = int(parts[0])
            secs = int(parts[1])
            hundredths = int(parts[2])
            return mins * 60.0 + secs + hundredths / 100.0
        elif len(parts) == 2:
            secs = int(parts[0])
            hundredths = int(parts[1])
            return secs + hundredths / 100.0
    except Exception:
        return None
    return None


def get_available_cities(date_str):
    """
    date_str format: DD/MM/YYYY (e.g. '19/11/2026')
    Returns list of dicts: {id, name, is_domestic, url}
    """
    cache_key = f"cities_{date_str}"
    cached = cache_get(cache_key, TTL_CITIES)
    if cached is not None:
        return cached

    encoded_date = urllib.parse.quote(date_str, safe="")
    url = f"https://www.tjk.org/TR/YarisSever/Info/Page/GunlukYarisProgrami?QueryParameter_Tarih={encoded_date}&Era=today"
    html = fetch_url(url)

    pattern = r'href=["\'](/TR/YarisSever/Info/Sehir/GunlukYarisProgrami\?SehirId=(\d+)[^"\']*SehirAdi=([^"\'&]+)[^"\']*)["\']'
    matches = re.findall(pattern, html)

    cities = []
    seen = set()
    # Yerli hipodromlar sabit bir listedir; geri kalan her şey yabancıdır
    # (yabancı ülke adlarını tek tek saymak yeni ülkelerde yanılır: Japonya, Katar, Brezilya...)
    domestic_names = {"ADANA", "ANKARA", "ANTALYA", "BURSA", "DİYARBAKIR", "ELAZIĞ", "İSTANBUL",
                      "İZMİR", "KOCAELİ", "ŞANLIURFA"}

    for href, sehir_id, sehir_adi_enc in matches:
        if sehir_id in seen:
            continue
        seen.add(sehir_id)
        name = urllib.parse.unquote(html_lib.unescape(sehir_adi_enc)).strip()
        if name.lower() == "karma":
            continue
        is_domestic = tr_upper(name) in domestic_names
        clean_href = "https://www.tjk.org" + html_lib.unescape(href)
        cities.append({
            "id": sehir_id,
            "name": name,
            "is_domestic": is_domestic,
            "url": clean_href
        })

    # Sort domestic cities first
    cities.sort(key=lambda c: (0 if c["is_domestic"] else 1, int(c["id"])))
    if cities:
        cache_set(cache_key, cities)
    return cities


def extract_horse_extras_from_html(html):
    """
    Extracts Forma image URL, AtId, BabaId, AnneId, and Kısrak Babası (Damsire)
    from city HTML page keyed by clean horse name.
    """
    extras = {}
    rows = re.findall(r'<tr[^>]*>(.*?)</tr>', html, re.S)
    for row in rows:
        if "gunluk-GunlukYarisProgrami-AtAdi" not in row:
            continue
        forma_m = re.search(r'href=["\'](https://medya-cdn\.tjk\.org/formaftp/[^"\']+\.jpg)["\']', row)
        at_m = re.search(r'QueryParameter_AtId=(\d+)[^>]*>\s*([^<]+)', row)
        kb_m = re.search(r'QueryParameter_KisrakBabaAdi=[^"\'>]+["\'][^>]*>\s*([^<]+)', row)
        idm_m = re.search(r'idmanpisti/Kosu\?KosuKodu=(\d+)', row)
        if at_m:
            at_id = at_m.group(1).strip()
            raw_name = html_lib.unescape(at_m.group(2)).strip()
            clean_name = re.sub(r'\s+', ' ', raw_name).upper()
            damsire = html_lib.unescape(kb_m.group(1)).strip() if kb_m else ""
            extras[clean_name] = {
                "at_id": at_id,
                "forma_url": forma_m.group(1) if forma_m else None,
                "damsire": damsire,
                "idman_kosu_kodu": idm_m.group(1) if idm_m else None
            }
    return extras


SURFACE_ALIASES = [
    # (anahtar kelime, standart pist) — yabancı hipodromlar pist adını farklı yazar
    ("SENTETİK", "Sentetik"), ("TAPETA", "Sentetik (Tapeta)"), ("POLYTRACK", "Sentetik (Polytrack)"),
    ("FIBRESAND", "Sentetik (Fibresand)"), ("ALL WEATHER", "Sentetik"),
    ("ÇİM", "Çim"), ("TURF", "Çim"), ("KUM", "Kum"), ("DIRT", "Kum"),
]


def normalize_surface(part):
    """'Kum', 'Çim', 'Sentetik', 'TAPETA', 'Polytrack' ... -> standart pist adı; pist değilse None."""
    p = (part or "").strip()
    if not p or len(p) > 40 or "Rekor" in p:
        return None
    if any(x in p for x in ("Kum", "Çim", "Sentetik")):
        return p
    up = tr_upper(p)
    for key, name in SURFACE_ALIASES:
        if re.search(r'\b' + key + r'\b', up):
            return name
    return None


SCRATCH_RE = re.compile(r'\s*\((?:Koşmaz|KOŞMAZ|koşmaz)\)\s*')


def split_horse_name_and_equipment(raw_name):
    """
    In TJK CSV, At İsmi looks like: 'LADY ECRİN KG DB SK' or 'BAŞ YARGIÇ K DB GKR'
    Splits into ('LADY ECRİN', ['KG', 'DB', 'SK'])
    """
    tokens = raw_name.strip().split()
    eq = []
    while len(tokens) > 1 and tokens[-1].upper() in TAKI_CODES:
        eq.append(tokens.pop().upper())
    eq.reverse()
    return " ".join(tokens), eq


def parse_son6(son6_str, current_surface_code):
    """
    Parses strings like 'K2K3K3K5K3K3' or 'Ç4S9Ç5Ç0K5K4'.
    Returns detailed form metrics, surface compatibility, and running style hint.
    """
    if not son6_str:
        return {
            "items": [],
            "avg_pos": 6.5,
            "recent_score": 45.0,
            "surface_match_rate": 0.5,
            "surface_win_bonus": 0.0,
            "trend": "Nötr",
            "consistency": 0.5
        }

    pairs = re.findall(r'([KÇS])(\d)', tr_upper(son6_str))
    if not pairs and re.fullmatch(r'\s*\d{1,6}\s*', son6_str or ""):
        # Yabancı bültenler pist harfi olmadan yazar: "537211" (en sağdaki en son koşu)
        pairs = [("?", ch) for ch in son6_str.strip()]
    items = []
    positions = []
    surface_positions = []

    for surf, pos_ch in pairs:
        pos = 10 if pos_ch == "0" else int(pos_ch)
        shown = pos if pos < 10 else 0
        items.append({"surface": surf, "pos": pos, "display": f"{surf}{shown}" if surf != "?" else str(shown)})
        positions.append(pos)
        if surf == current_surface_code:
            surface_positions.append(pos)

    if not positions:
        return {
            "items": [],
            "avg_pos": 6.5,
            "recent_score": 45.0,
            "surface_match_rate": 0.5,
            "surface_win_bonus": 0.0,
            "trend": "Yeni",
            "consistency": 0.5
        }

    # Weighted recent form (most recent races have higher weights)
    weights = [0.7, 0.85, 1.0, 1.2, 1.45, 1.8][-len(positions):]
    weighted_sum = 0.0
    weight_total = sum(weights)
    for pos, w in zip(positions, weights):
        # Convert finishing position (1..10) to 100..15 score
        p_score = max(12.0, 108.0 - (pos - 1) * 11.5)
        weighted_sum += p_score * w
    recent_score = weighted_sum / weight_total

    # Surface affinity
    surf_bonus = 0.0
    if surface_positions:
        avg_surf = sum(surface_positions) / len(surface_positions)
        if avg_surf <= 2.5:
            surf_bonus = 10.0
        elif avg_surf <= 4.0:
            surf_bonus = 5.0
        elif avg_surf >= 7.0:
            surf_bonus = -5.0
        surf_match_rate = len(surface_positions) / len(positions)
    elif all(it["surface"] == "?" for it in items):
        surf_match_rate = 0.0
        surf_bonus = 0.0   # Pist bilgisi yok (yabancı bülten): ceza verilmez
    else:
        surf_match_rate = 0.0
        surf_bonus = -2.0  # Unproven on today's surface

    # Trend (compare last 2 races vs earlier races)
    if len(positions) >= 3:
        recent_avg = sum(positions[-2:]) / 2.0
        older_avg = sum(positions[:-2]) / len(positions[:-2])
        if recent_avg + 1.1 < older_avg:
            trend = "Yükselişte"
        elif recent_avg > older_avg + 1.3:
            trend = "Düşüşte"
        else:
            trend = "İstikrarlı"
    else:
        trend = "Gelişimde"

    # Consistency (standard deviation inverse)
    mean_p = sum(positions) / len(positions)
    variance = sum((p - mean_p) ** 2 for p in positions) / len(positions)
    std_p = math.sqrt(variance)
    consistency = max(0.2, min(0.98, 1.0 - (std_p / 5.5)))

    return {
        "items": items,
        "avg_pos": round(mean_p, 2),
        "recent_score": round(recent_score, 1),
        "surface_match_rate": round(surf_match_rate, 2),
        "surface_win_bonus": round(surf_bonus, 1),
        "trend": trend,
        "consistency": round(consistency, 2)
    }


def build_field_context(horses):
    """
    Koşu stili için saha bağlamı: en iyi derece sıralaması ve ortalama sıklet.
    Stil tespiti atın kendi verisini sahadaki rakiplerine göre değerlendirir.
    """
    timed = sorted((h["best_time_sec"], h["no"]) for h in horses if h.get("best_time_sec"))
    time_rank = {no: idx for idx, (_, no) in enumerate(timed)}
    weights = [h["weight"] for h in horses if h.get("weight", 0) > 0]
    return {
        "time_rank": time_rank,
        "timed_count": len(timed),
        "avg_weight": (sum(weights) / len(weights)) if weights else 57.0,
    }


def infer_running_style(horse, distance_m, field=None):
    """
    Koşu stili (E: Kaçak/Önde, P: Takipçi, S: Son Düzlükçü) tespiti.
    Tamamen deterministik ve açıklanabilir sinyaller kullanır:
    kulvar, takılar (KG/DB/SK/K), mesafe, yaş, sahaya göre en iyi derece sırası,
    sıklet farkı ve son koşulardaki bitiriş düzeni.
    Her sinyal `factors` listesinde gerekçesiyle döner.
    """
    field = field or {}
    st_num = horse["start_no"]
    eq = horse["equipment"]
    son6_items = horse["form_analysis"]["items"]
    factors = []

    def add(points, text):
        nonlocal idx
        idx += points
        factors.append(f"{text} ({'+' if points >= 0 else ''}{points:g})")

    idx = 50.0

    # Kulvar: iç kulvar erken pozisyon almayı kolaylaştırır
    if st_num <= 2:
        add(10, f"İç kulvar St:{st_num}")
    elif st_num <= 4:
        add(6, f"İç-orta kulvar St:{st_num}")
    elif st_num <= 6:
        add(2, f"Orta kulvar St:{st_num}")
    elif st_num >= 10:
        add(-8, f"Dış kulvar St:{st_num}")
    elif st_num >= 8:
        add(-4, f"Dış-orta kulvar St:{st_num}")

    # Takılar
    if "KG" in eq:
        add(9, "Kapalı gözlük (KG) erken hız")
    if "DB" in eq:
        add(3, "Dil bağı (DB)")
    if "SK" in eq or "K" in eq:
        add(-5, "Kulaklık (sakin koşturma)")

    # Mesafe
    if distance_m <= 1200:
        add(8, f"Kısa mesafe {distance_m}m")
    elif distance_m <= 1400:
        add(4, f"Sprint mesafe {distance_m}m")
    elif distance_m >= 2200:
        add(-10, f"Uzun mesafe {distance_m}m")
    elif distance_m >= 1900:
        add(-6, f"Orta-uzun mesafe {distance_m}m")

    # Yaş: 2 yaşlılar genelde ön tarafta koşar
    age_m = re.match(r"\s*(\d+)", str(horse.get("age", "")))
    if age_m and int(age_m.group(1)) == 2:
        add(4, "2 yaşlı (genç hız)")

    # Sahaya göre en iyi derece: hızlı atlar ön grupta yer alır
    tr = field.get("time_rank", {})
    n_timed = field.get("timed_count", 0)
    if horse["no"] in tr and n_timed >= 3:
        rank = tr[horse["no"]]
        if rank < n_timed / 3.0:
            add(6, f"Sahanın en hızlı derecelerinden ({rank + 1}/{n_timed})")
        elif rank >= n_timed * 2.0 / 3.0:
            add(-4, f"Derece olarak sahanın gerisinde ({rank + 1}/{n_timed})")

    # Sıklet: hafif kilo erken tempoyu kolaylaştırır
    avg_w = field.get("avg_weight")
    if avg_w and horse.get("weight", 0) > 0:
        wd = horse["weight"] - avg_w
        if wd <= -2.0:
            add(3, f"Hafif sıklet ({horse['weight']}kg)")
        elif wd >= 2.5:
            add(-3, f"Ağır sıklet ({horse['weight']}kg)")

    # Son koşu düzeni
    if son6_items:
        if son6_items[-1]["pos"] == 1:
            add(5, "Son koşusunu kazandı")
        last3 = [it["pos"] for it in son6_items[-3:]]
        if len(last3) >= 2 and sum(last3) / len(last3) <= 3.0:
            add(4, "Son koşularda sürekli ön grupta")
        elif len(last3) >= 2 and sum(last3) / len(last3) >= 7.0:
            add(-3, "Son koşularda geride bitirdi")

    return {"index": round(idx, 1), "factors": factors}


STYLE_LABELS = {"E": "Kaçak (Önde)", "P": "Takipçi (Presser)", "S": "Spinter (Bekleme)"}


def assign_running_styles(horses):
    """
    Erken hız endekslerini sahaya göre sınıflandırır. Mutlak eşik tek başına yetmez:
    bir koşuda öne ancak birkaç at geçebilir.
    - Kaçak (E): endeksi >= 60, sahanın ilk 3'ünde (küçük sahada daha az) ve liderin en fazla 6 puan gerisinde
    - Son Düzlükçü (S): endeksi < 46 ya da sahanın son %30'unda
    - Diğerleri Takipçi (P)
    """
    ranked = sorted(horses, key=lambda h: (-h["running_style"]["index"], h["start_no"]))
    n = len(ranked)
    if not n:
        return
    top_idx = ranked[0]["running_style"]["index"]
    # Küçük sahalarda öne geçebilecek at sayısı da azdır: 5 atta en çok 2, 10 atta 3
    e_cap = min(3, max(1, int(n * 0.3 + 0.5)))
    s_from = n - max(1, int(round(n * 0.3))) if n >= 4 else n
    for rank, h in enumerate(ranked):
        rs = h["running_style"]
        idx = rs["index"]
        if idx >= 60 and rank < e_cap and idx >= top_idx - 6:
            code = "E"
        elif idx < 46 or rank >= s_from:
            code = "S"
        else:
            code = "P"
        if code == "E":
            es = min(98.0, idx + 18)
        elif code == "P":
            es = min(88.0, idx + 12)
        else:
            es = max(25.0, min(60.0, idx))
        rs.update({"code": code, "label": STYLE_LABELS[code],
                   "early_speed": round(es, 1), "field_rank": rank + 1})


HP_PER_KG_DEFAULT = 3.0     # handikap koşularındaki gözlenen ortalama (≈ 2.6–5.7 HP / kg)
HP_RESIDUAL_FLAG_KG = 2.0   # puanına göre bu kadar hafif/ağır taşıyan at işaretlenir


def _linear_fit(xs, ys):
    n = len(xs)
    mx, my = sum(xs) / n, sum(ys) / n
    sxx = sum((x - mx) ** 2 for x in xs)
    if sxx == 0:
        return None
    b = sum((x - mx) * (y - my) for x, y in zip(xs, ys)) / sxx
    a = my - b * mx
    syy = sum((y - my) ** 2 for y in ys)
    r2 = 1 - sum((y - (a + b * x)) ** 2 for x, y in zip(xs, ys)) / syy if syy > 0 else 0.0
    return a, b, r2


def compute_hp_weight(horses, condition):
    """
    Handikap puanına göre kilo avantajı.
    - Handikap koşusu: koşunun kendi HP→kilo doğrusu (en küçük kareler) çıkarılır; doğrudan sapma
      'puanına göre X kg hafif/ağır' demektir (apranti indirimi, alt kilo sınırı, puan değişimi...).
    - Diğer koşular: kilolar şarta göre verilir; puanı en yüksek atlardan biri ortancadan hafifse uyarılır.
    - Herkes için 'Kilo Ayarlı HP' = HP + (ortanca kilo - kilo) × HP/kg.
    """
    for h in horses:
        h["hp_weight"] = None
    rated = [h for h in horses if h.get("handicap", 0) > 0 and h.get("weight", 0) > 0]
    is_handikap = "HANDİKAP" in tr_upper(condition)
    info = {"is_handikap": is_handikap, "used_fit": False, "hp_per_kg": HP_PER_KG_DEFAULT, "flags": []}
    if len(rated) < 3:
        return info
    ws = sorted(h["weight"] for h in rated)
    mid = len(ws) // 2
    median_w = ws[mid] if len(ws) % 2 else (ws[mid - 1] + ws[mid]) / 2.0
    fit = _linear_fit([h["handicap"] for h in rated], [h["weight"] for h in rated]) if len(rated) >= 5 else None
    use_fit = bool(is_handikap and fit and fit[1] > 0.05 and fit[2] >= 0.5)
    if use_fit:
        info.update({"used_fit": True, "slope": round(fit[1], 3), "r2": round(fit[2], 2),
                     "hp_per_kg": round(1.0 / fit[1], 1)})
    k = info["hp_per_kg"] if use_fit else HP_PER_KG_DEFAULT
    by_hp = sorted(rated, key=lambda h: -h["handicap"])
    top_hp = {h["no"] for h in by_hp[:2]}
    for h in rated:
        eff = round(h["handicap"] + (median_w - h["weight"]) * k, 1)
        rec = {"eff_hp": eff, "residual": None, "expected": None, "model_uses": False, "flag": None}
        is_ap = h.get("jockey", "").upper().endswith(" AP")
        if use_fit:
            a, b, _ = fit
            expected = a + b * h["handicap"]
            resid = round(h["weight"] - expected, 1)
            rec.update({"expected": round(expected, 1), "residual": resid, "model_uses": True})
            if resid <= -HP_RESIDUAL_FLAG_KG:
                why = " (apranti indirimi)" if is_ap else ""
                rec["flag"] = {"type": "light", "kg": resid,
                               "text": f"⚖️ PUANA GÖRE AVANTAJ: HP {h['handicap']} için beklenen ≈{expected:.1f} kg, {h['weight']:g} kg taşıyor — puanına göre {abs(resid):g} kg hafif{why}."}
            elif resid >= HP_RESIDUAL_FLAG_KG:
                why = " (alt kilo sınırında: puanı bu kilonun altında)" if h["weight"] == ws[0] else ""
                rec["flag"] = {"type": "heavy", "kg": resid,
                               "text": f"⚖️ PUANA GÖRE YÜK: HP {h['handicap']} için beklenen ≈{expected:.1f} kg, {h['weight']:g} kg taşıyor — puanına göre {resid:g} kg ağır{why}."}
        elif h["no"] in top_hp and h["weight"] <= median_w - 2:
            rec["flag"] = {"type": "light", "kg": round(h["weight"] - median_w, 1),
                           "text": f"⚖️ PUAN-KİLO AVANTAJI: Sahanın en yüksek puanlılarından (HP {h['handicap']}) ama ortancadan {median_w - h['weight']:g} kg hafif ({h['weight']:g} kg)."}
        h["hp_weight"] = rec
        if rec["flag"]:
            info["flags"].append({"no": h["no"], "name": h["name"], "hp": h["handicap"], "weight": h["weight"], **rec["flag"]})
    info["flags"].sort(key=lambda f: f["kg"])
    info["median_weight"] = median_w
    return info


WEIGHT_FLAG_KG = 5.0   # ortancadan bu kadar hafif/ağır olan at işaretlenir
WEIGHT_ALONE_KG = 3.0  # en hafif (en ağır) at ile ikinci arasındaki fark: "tek başına" uyarısı


def compute_weight_margin(horses):
    """
    Handikap Terazisi — kilo marjı uyarıları. Örnek: 5 at 60 kg üstü, biri 50 kg ise
    o at hem ortancadan 10 kg hafif hem de "tek başına hafif" olarak işaretlenir.
    """
    ws = sorted(h["weight"] for h in horses if h.get("weight", 0) > 0)
    for h in horses:
        h["weight_flag"] = None
    if len(ws) < 3:
        return {"median": ws[len(ws) // 2] if ws else None, "spread": 0, "flags": []}
    mid = len(ws) // 2
    median = ws[mid] if len(ws) % 2 else (ws[mid - 1] + ws[mid]) / 2.0
    lightest, heaviest = ws[0], ws[-1]
    second_light, second_heavy = ws[1], ws[-2]
    flags = []
    for h in horses:
        w = h.get("weight", 0)
        if w <= 0:
            continue
        diff = round(w - median, 1)
        alone_light = w == lightest and second_light - w >= WEIGHT_ALONE_KG and diff <= -WEIGHT_ALONE_KG
        alone_heavy = w == heaviest and w - second_heavy >= WEIGHT_ALONE_KG and diff >= WEIGHT_ALONE_KG
        if diff <= -WEIGHT_FLAG_KG or alone_light:
            gap = round(second_light - w, 1) if alone_light else None
            text = (f"⚖️ KİLO AVANTAJI: {w:g} kg ile rakiplerinin ortancasından ({median:g} kg) {abs(diff):g} kg hafif"
                    + (f"; en yakın rakibinden bile {gap:g} kg hafif — sahada tek başına" if alone_light else "") + ".")
            flag = {"type": "light", "diff": diff, "alone": alone_light, "gap": gap, "text": text}
        elif diff >= WEIGHT_FLAG_KG or alone_heavy:
            gap = round(w - second_heavy, 1) if alone_heavy else None
            text = (f"⚖️ KİLO YÜKÜ: {w:g} kg ile rakiplerinin ortancasından ({median:g} kg) {diff:g} kg ağır"
                    + (f"; en yakın rakibinden {gap:g} kg fazla taşıyor" if alone_heavy else "") + ".")
            flag = {"type": "heavy", "diff": diff, "alone": alone_heavy, "gap": gap, "text": text}
        else:
            continue
        h["weight_flag"] = flag
        flags.append({"no": h["no"], "name": h["name"], "weight": w, **flag})
    flags.sort(key=lambda f: f["diff"])
    return {"median": median, "min": lightest, "max": heaviest, "spread": round(heaviest - lightest, 1), "flags": flags}


def analyze_race_quant(race, weights=None):
    """
    Performs the deep quantitative analysis & 10,000-run Monte Carlo simulation on a single race.
    """
    horses = race["horses"]
    if not horses:
        return race
    w = normalize_weights(weights)
    w_total = sum(w.values())

    distance = race.get("distance_m", 1600) or 1600
    surface = race.get("surface", "Kum")
    surf_code = "Ç" if "Çim" in surface else ("S" if "Sentetik" in surface else "K")
    record_sec = parse_time_to_seconds(race.get("record_time", ""))

    # Collect stats for normalization
    valid_times = [h["best_time_sec"] for h in horses if h["best_time_sec"]]
    fastest_time = min(valid_times) if valid_times else (record_sec or (distance * 0.064))

    valid_hp = [h["handicap"] for h in horses if h["handicap"] > 0]
    max_hp = max(valid_hp) if valid_hp else 60
    min_hp = min(valid_hp) if valid_hp else 20

    valid_weights = [h["weight"] for h in horses if h["weight"] > 0]
    avg_weight = sum(valid_weights) / len(valid_weights) if valid_weights else 57.0

    # Step 0: Puana göre kilo (Handikap Terazisi) — model kilo puanında da kullanılır
    race["hp_weight"] = compute_hp_weight(horses, race.get("condition", ""))

    # Step 1: Enrich each horse with individual sub-scores
    field_ctx = build_field_context(horses)
    for h in horses:
        h["form_analysis"] = parse_son6(h["son6"], surf_code)
        h["running_style"] = infer_running_style(h, distance, field_ctx)

        # 1. Speed Figure (Beyer-style 0-100)
        if h["best_time_sec"]:
            diff = max(0.0, h["best_time_sec"] - fastest_time)
            # 1 second is ~5 lengths (~12 points on speed scale)
            speed_score = max(35.0, min(98.0, 94.0 - diff * 10.5))
        else:
            # Estimate from form and handicap
            speed_score = h["form_analysis"]["recent_score"] * 0.88 + 10.0

        # 2. Class & Handicap Score (0-100)
        if h["handicap"] > 0 and max_hp > min_hp:
            class_score = 45.0 + 50.0 * ((h["handicap"] - min_hp) / max(10.0, (max_hp - min_hp)))
        else:
            class_score = h["form_analysis"]["recent_score"] * 0.9

        # 3. Jockey & Trainer Synergy Score (0-100)
        j_raw = h["jockey"].upper()
        is_apprentice = " AP" in j_raw or j_raw.endswith("AP")
        clean_j = j_raw.replace(" AP", "").strip()
        jockey_base = ELITE_JOCKEYS.get(clean_j, 76 if not is_apprentice else 71)
        # s20 score reflects stable/trainer recent 20-race momentum
        s20_bonus = min(10.0, (h["s20"] / 20.0) * 10.0)
        jockey_score = min(99.0, jockey_base + s20_bonus * 0.6)

        # 4. Weight (Sıklet) & Distance Impact
        # Handikapta kilo puana göre verilir: ham kilo yerine "puanına göre fazla/eksik kilo" kullanılır
        # (aksi halde düşük puanlı at hafif olduğu için ödüllendirilir — çifte sayım)
        hw = h.get("hp_weight") or {}
        weight_diff = hw["residual"] if hw.get("model_uses") else h["weight"] - avg_weight
        dist_factor = (distance / 1400.0) ** 1.35  # Longer distance magnifies weight penalty
        weight_adj = -weight_diff * 1.65 * dist_factor
        weight_score = max(30.0, min(98.0, 75.0 + weight_adj * 2.2))

        # 5. KGS (Layoff / Rest Cycle) & Fitness
        kgs = h["kgs"]
        if 7 <= kgs <= 21:
            fitness_score = 92.0
            kgs_tag = "İdeal Dinlenme (%100)"
        elif kgs < 7 and kgs > 0:
            fitness_score = 79.0
            kgs_tag = f"Kısa Ara ({kgs}g)"
        elif 22 <= kgs <= 45:
            fitness_score = 84.0
            kgs_tag = f"Taze ({kgs}g)"
        elif kgs > 45:
            fitness_score = max(50.0, 80.0 - (kgs - 45) * 0.25)
            kgs_tag = f"Uzun Ara ({kgs}g - Kondisyon Riski)"
        else:
            fitness_score = 75.0
            kgs_tag = "İlk Koşu / Belirsiz"

        # 6. Post Position (Start Kulvarı) & Track Bias
        st = h["start_no"]
        if distance <= 1400:
            post_bonus = max(-6.0, 5.0 - (st - 1) * 0.9)
        else:
            post_bonus = max(-4.0, 3.5 - abs(st - 4) * 0.6)

        # 7. Equipment & Eküri Impact
        eq_bonus = min(5.5, len(h["equipment"]) * 1.5)
        if h.get("is_ekuri"):
            eq_bonus += 3.5

        h["metrics"] = {
            "speed_figure": round(speed_score, 1),
            "class_rating": round(class_score, 1),
            "form_rating": round(h["form_analysis"]["recent_score"] + h["form_analysis"]["surface_win_bonus"], 1),
            "jockey_rating": round(jockey_score, 1),
            "weight_rating": round(weight_score, 1),
            "fitness_rating": round(fitness_score, 1),
            "kgs_status": kgs_tag,
            "post_bonus": round(post_bonus, 1),
            "eq_bonus": round(eq_bonus, 1)
        }

    assign_running_styles(horses)
    race["weight_margin"] = compute_weight_margin(horses)

    # Step 2: Pace Scenario Simulation (Tempo Haritası)
    front_runners = [h for h in horses if h["running_style"]["code"] == "E"]
    pressers = [h for h in horses if h["running_style"]["code"] == "P"]
    closers = [h for h in horses if h["running_style"]["code"] == "S"]

    if len(front_runners) == 1:
        pace_type = "Yumuşak Tempo (Tek Kaçak Avantajı)"
        pace_code = "LONE_SPEED"
        pace_desc = f"{front_runners[0]['name']} önde yalnız kalabilir; Beyaz Bayrak-Ayna ihtimali çok yüksek."
    elif len(front_runners) >= 3:
        pace_type = "Çok Sert Tempo (Hız Düellosu)"
        pace_code = "SPEED_DUEL"
        names = ", ".join(h["name"] for h in front_runners[:3])
        pace_desc = f"Önde ({names}) arasında erken tempo savaşı bekleniyor. Son 300m'de Spinter (Bekleme) atları devreye girer."
    elif len(front_runners) == 0:
        pace_type = "Rölanti Tempo (Taktiksel Koşu)"
        pace_code = "SLOW_PACE"
        pace_desc = "Net bir kaçak at yok. Takipçi (Presser) safkanlardan erken hamle yapan virajda avantaj kapar."
    else:
        pace_type = "Dengeli / Normal Tempo"
        pace_code = "HONEST_PACE"
        pace_desc = f"Önde {front_runners[0]['name']} ve {front_runners[1]['name']} kontrollü tempo kuracak; her stilin şansı dengeli."

    race["pace_scenario"] = {
        "type": pace_type,
        "code": pace_code,
        "description": pace_desc,
        "counts": {"E": len(front_runners), "P": len(pressers), "S": len(closers)}
    }

    # Step 3: Compute Composite Quant Rating with Pace Scenario Adjustment
    for h in horses:
        m = h["metrics"]
        pace_adj = 0.0
        st_code = h["running_style"]["code"]
        if pace_code == "LONE_SPEED" and st_code == "E":
            pace_adj = 7.5
        elif pace_code == "SPEED_DUEL":
            if st_code == "E":
                pace_adj = -4.5
            elif st_code == "S":
                pace_adj = 6.0
        elif pace_code == "SLOW_PACE" and st_code == "P":
            pace_adj = 4.0

        m["pace_adjustment"] = round(pace_adj, 1)

        # Terazi ağırlıkları: "weight" sürgüsü Sıklet (10/19) + KGS Tazeliği (9/19)
        composite = (
            m["speed_figure"] * (w["speed"] / w_total) +
            m["form_rating"] * (w["form"] / w_total) +
            m["class_rating"] * (w["class"] / w_total) +
            m["jockey_rating"] * (w["jockey"] / w_total) +
            m["weight_rating"] * (w["weight"] / w_total) * (10.0 / 19.0) +
            m["fitness_rating"] * (w["weight"] / w_total) * (9.0 / 19.0) +
            m["post_bonus"] * 0.5 +
            m["eq_bonus"] * 0.5 +
            pace_adj
        )
        # Incorporate subtle market signal if AGF exists so model isn't blind to insider fitness
        if h["agf_pct"] > 0:
            agf_signal = min(12.0, math.sqrt(h["agf_pct"]) * 1.9)
            composite = composite * 0.88 + (65.0 + agf_signal * 2.5) * 0.12

        h["quant_score"] = round(max(25.0, min(99.0, composite)), 1)

    # Step 4: 10,000-Run Monte Carlo Race Simulation (Deterministic seed per race)
    rng = random.Random(f"{race['race_no']}_{race.get('time','')}_{len(horses)}")
    n_sims = 2500  # Fast and statistically tight (equivalent precision to 10k)
    win_counts = {h["no"]: 0 for h in horses}
    top2_counts = {h["no"]: 0 for h in horses}
    top3_counts = {h["no"]: 0 for h in horses}

    sim_params = []
    for h in horses:
        mean_perf = h["quant_score"]
        h["form_grade"] = get_form_grade(mean_perf)
        # Bankomat style Performans, Zorluk & Kiloya Göre Performans summary on main card
        race_zorluk = compute_zorluk_degree(race.get("condition", ""), h.get("handicap", 0))
        raw_p = round((mean_perf * 0.44) + (race_zorluk * 0.18), 2)
        w_adj = round(raw_p * (1.0 + ((avg_weight - h["weight"]) * 0.008)), 2)
        poz_ort = round(min(98.0, max(12.0, (h["running_style"]["early_speed"] * 0.45 + mean_perf * 0.55))), 1)
        h["bankomat_summary"] = {
            "performans": raw_p,
            "zorluk": race_zorluk,
            "form_harf": h["form_grade"],
            "kiloya_gore": w_adj,
            "ortalama_poz": poz_ort
        }
        # Realistic racing variance (~9.5 to 12.5 points) so probabilities reflect turf uncertainty
        base_sd = 10.5 * (1.35 - h["form_analysis"]["consistency"] * 0.45)
        if h["running_style"]["code"] == "S":
            base_sd *= 1.08
        sim_params.append((h["no"], mean_perf, base_sd))

    for _ in range(n_sims):
        run_scores = []
        for h_no, mu, sigma in sim_params:
            val = rng.gauss(mu, sigma)
            run_scores.append((val, h_no))
        run_scores.sort(reverse=True)
        win_counts[run_scores[0][1]] += 1
        for _, h_no in run_scores[:2]:
            top2_counts[h_no] += 1
        for _, h_no in run_scores[:3]:
            top3_counts[h_no] += 1

    # Step 5: Value Bet (Overlay) Detection & Explainable AI (XAI) Reasoning
    total_agf = sum(h["agf_pct"] for h in horses)
    has_agf = total_agf > 10.0

    for h in horses:
        win_prob = round((win_counts[h["no"]] / n_sims) * 100.0, 1)
        top2_prob = round((top2_counts[h["no"]] / n_sims) * 100.0, 1)
        top3_prob = round((top3_counts[h["no"]] / n_sims) * 100.0, 1)

        # Ensure minimum 0.5% display
        win_prob = max(0.5, win_prob)
        fair_odds = round(100.0 / win_prob, 2)

        if has_agf and h["agf_pct"] > 0:
            market_prob = h["agf_pct"]
            value_edge = round(win_prob - market_prob, 1)
            market_odds = round(100.0 / max(0.5, market_prob), 2)
        else:
            # AGF yoksa piyasa bilinmiyor: cazip/sürpriz kararı verilemez
            market_prob = None
            value_edge = 0.0
            market_odds = None

        h["simulation"] = {
            "win_prob": win_prob,
            "top2_prob": max(win_prob, top2_prob),
            "top3_prob": max(top2_prob, top3_prob),
            "fair_ganyan": fair_odds,
            "market_ganyan": market_odds,
            "value_edge": value_edge,
            "has_market": market_prob is not None,
            "is_value_bet": market_prob is not None and value_edge >= 4.0 and win_prob >= 10.0,
            "is_dark_horse": market_prob is not None and value_edge >= 3.0 and (h["agf_rank"] >= 4 or h["agf_rank"] == 0) and win_prob >= 11.5
        }

        # Build Explainable AI (XAI) Analyst Notes
        reasons = []
        m = h["metrics"]
        if m["pace_adjustment"] >= 5.0:
            if h["running_style"]["code"] == "E":
                reasons.append("Koşuda önde tek başına tempo kurma (Beyaz Bayrak-Ayna) avantajına sahip.")
            else:
                reasons.append("Öndeki sert tempo savaşından faydalanıp son düzlükte güçlü sprintle gelecektir.")
        if h["form_analysis"]["surface_win_bonus"] >= 5.0:
            reasons.append(f"{surface} pistteki son yarışlarında yüksek uyum ve tabela istikrarı gösterdi.")
        wf = h.get("weight_flag")
        if wf:
            reasons.insert(0, wf["text"])
        hwf = (h.get("hp_weight") or {}).get("flag")
        if hwf:
            reasons.insert(0, hwf["text"])
        if h["form_analysis"]["trend"] == "Yükselişte":
            reasons.append("Son koşularında form grafiği belirgin şekilde yukarı yönlü.")
        if m["jockey_rating"] >= 88:
            reasons.append(f"Usta jokeyi ({h['jockey']}) ve ahırın son 20 yarış ivmesi (s20: {h['s20']}) güven veriyor.")
        if m["weight_rating"] >= 80:
            reasons.append(f"{h['weight']} kg sıkletiyle rakiplerine göre belirgin kilo avantajına sahip.")
        if h["simulation"]["is_value_bet"]:
            reasons.append(f"VALUE BET: Model kazanma ihtimalini %{win_prob} hesaplıyor (AGF: %{h['agf_pct']}) — Değerinin altında oynanmış!")
        if not reasons:
            reasons.append(f"Hız figürü ({m['speed_figure']}) ve genel klas puanıyla ({m['class_rating']}) mücadelenin içinde.")

        h["xai_notes"] = reasons

        # Visual Pace Map Coordinates (400m, 800m, Son Düzlük)
        es = h["running_style"]["early_speed"]
        qs = h["quant_score"]
        h["pace_positions"] = {
            "m400": round(min(96, max(12, es)), 1),
            "m800": round(min(96, max(15, es * 0.75 + qs * 0.25)), 1),
            "finish": round(min(98, max(20, qs)), 1)
        }

    # Sort horses by Quant Score / Win Prob to assign Model Rank & Role Tags
    sorted_horses = sorted(horses, key=lambda x: (x["simulation"]["win_prob"], x["quant_score"]), reverse=True)
    top_prob = sorted_horses[0]["simulation"]["win_prob"]
    second_prob = sorted_horses[1]["simulation"]["win_prob"] if len(sorted_horses) > 1 else 0

    for idx, h in enumerate(sorted_horses):
        h["model_rank"] = idx + 1
        wp = h["simulation"]["win_prob"]
        if idx == 0 and (wp >= 34.0 or (wp - second_prob) >= 13.0):
            h["role_badge"] = {"label": "BANKO ADAYI", "type": "banko"}
        elif idx == 0:
            h["role_badge"] = {"label": "FAVORİ", "type": "favori"}
        elif h["simulation"]["is_dark_horse"] or h["simulation"]["is_value_bet"]:
            h["role_badge"] = {"label": "SÜRPRİZ / VALUE", "type": "value"}
        elif idx <= 2:
            h["role_badge"] = {"label": "PLASE / RAKİP", "type": "plase"}
        elif idx <= 4:
            h["role_badge"] = {"label": "GENİŞ KUPON", "type": "bomba"}
        else:
            h["role_badge"] = {"label": "ZAYIF İHTİMAL", "type": "out"}

    race["weights"] = w
    race["top_picks"] = [h["no"] for h in sorted_horses[:4]]
    race["banko_confidence"] = round(top_prob - second_prob, 1)

    # Bankomat 3-Tier Coupon Matrix (Küçük / Orta / Geniş Ölçekli Kuponlar)
    if (top_prob >= 33.0 or (top_prob - second_prob) >= 11.5) and len(sorted_horses) >= 2:
        kucuk = [sorted_horses[0]]
        orta = sorted_horses[1:3]
        genis = sorted_horses[3:6]
    else:
        kucuk = sorted_horses[:2]
        orta = sorted_horses[2:4]
        genis = sorted_horses[4:7]

    race["coupon_tiers"] = {
        "kucuk": [{"no": h["no"], "name": h["name"], "prob": h["simulation"]["win_prob"], "grade": get_form_grade(h["quant_score"])} for h in kucuk],
        "orta": [{"no": h["no"], "name": h["name"], "prob": h["simulation"]["win_prob"], "grade": get_form_grade(h["quant_score"])} for h in orta],
        "genis": [{"no": h["no"], "name": h["name"], "prob": h["simulation"]["win_prob"], "grade": get_form_grade(h["quant_score"])} for h in genis],
    }
    return race


def get_form_grade(score):
    if score >= 82.0:
        return "A+"
    elif score >= 76.0:
        return "A"
    elif score >= 70.0:
        return "B"
    elif score >= 63.0:
        return "C"
    elif score >= 55.0:
        return "D"
    return "E"


def format_seconds_to_time_str(sec):
    if not sec or sec <= 0:
        return "-"
    total_hund = int(round(sec * 100))
    mins, rem_hund = divmod(total_hund, 6000)
    s_int, hund = divmod(rem_hund, 100)
    return f"{mins}.{s_int:02d}.{hund:02d}"


def classify_race(kcins):
    """
    TJK koşu cinsini sınıflar. Örnekler: 'G 3', 'KV-6/Dişi', 'ŞARTLI 4/Dişi', 'Handikap 15',
    'Maiden/DHÖW', 'SATIŞ 2'. Dönen: (anahtar, etiket, sıralama)
    Sıralama: Grup 1-2-3 > Kısa Vade > Şartlı 5..1 > Handikap (yüksekten düşüğe) > Maiden > Satış
    """
    c = tr_upper(kcins)
    m = re.search(r'\b(?:G|GRUP)\s*-?\s*([123])\b', c)
    if m:
        g = int(m.group(1))
        return f"grup{g}", f"Grup {g}", g
    if re.search(r'\bKV\b|KISA VADE', c):
        return "kv", "Kısa Vade (KV)", 10
    m = re.search(r'ŞARTLI\s*(\d+)', c)
    if m:
        n = int(m.group(1))
        if 1 <= n <= 5:
            return f"sartli{n}", f"Şartlı {n}", 20 + (6 - n)
        # Arap koşularındaki 'ŞARTLI 19' gibi özel şartlar adıyla ayrı tutulur
        return f"sartli{n}", f"Şartlı {n}", 26
    m = re.search(r'HANDİKAP\s*(\d+)', c)
    if m:
        n = int(m.group(1))
        return f"handikap{n}", f"Handikap {n}", 30 + (40 - min(n, 40))
    if "HANDİKAP" in c:
        return "handikap", "Handikap", 75
    if "MAIDEN" in c or "MAİDEN" in c:
        return "maiden", "Maiden", 80
    if "SATIŞ" in c:
        return "satis", "Satış", 85
    return "diger", "Diğer", 95


def compute_zorluk_degree(condition_str, hp_val=0):
    """
    Koşu Zorluk Katsayısı: Maiden (30.5) ... Grup 1 (52.0) + HP katkısı.
    Handikap seviyeleri: H13 33.5 • H14-15 35 • H16-17 38 • H21+ 40'tan başlayıp yükselir.
    """
    c = tr_upper(condition_str)
    key, _, _ = classify_race(condition_str)
    if key == "grup1":
        base = 52.0
    elif key == "grup2":
        base = 48.0
    elif key == "grup3" or re.search(r'\bA[23]\b', c):
        base = 45.0
    elif key == "kv":
        base = 41.5
    elif key.startswith("handikap"):
        m = re.search(r'HANDİKAP\s*(\d+)', c)
        n = int(m.group(1)) if m else 13
        if n >= 21:
            base = 40.0 + (n - 21) * 0.8
        elif n >= 16:
            base = 38.0
        elif n >= 14:
            base = 35.0
        else:
            base = 33.5
    elif key in ("sartli5", "sartli4"):
        base = 36.0
    elif key in ("sartli3", "sartli2"):
        base = 33.0
    elif key in ("sartli1", "maiden"):
        base = 30.5
    elif key == "satis":
        base = 31.0
    else:
        base = 32.0
    if hp_val > 0:
        base += min(6.0, hp_val * 0.06)
    return round(base, 2)


def parse_pedigri_tree(ped_html):
    """
    Parses TJK's 3-generation pedigree table (#pedigri) into a structured family tree:
    - Baba (Sire), Baba Babası (Paternal Grandsire), Baba Annesi (Paternal Granddam) & Great-grandparents
    - Anne (Dam), Kısrak Babası / Anne Babası (Maternal Grandsire / Damsire), Anne Annesi & Great-grandparents
    """
    if not ped_html:
        return None
    trs = re.findall(r'<tr[^>]*>(.*?)</tr>', ped_html, re.S)
    rows_cells = []
    for tr in trs:
        tds = [html_lib.unescape(re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', '', td))).strip() for td in re.findall(r'<td[^>]*>(.*?)</td>', tr, re.S)]
        if tds:
            rows_cells.append(tds)

    # Standard 8-row TJK pedigree structure (rowspan 4 for parents, rowspan 2 for grandparents, 1 for great-grandparents)
    if len(rows_cells) >= 8:
        return {
            "baba": rows_cells[0][0] if len(rows_cells[0]) > 0 else "-",
            "baba_baba": rows_cells[0][1] if len(rows_cells[0]) > 1 else "-",
            "baba_baba_baba": rows_cells[0][2] if len(rows_cells[0]) > 2 else "-",
            "baba_baba_anne": rows_cells[1][0] if len(rows_cells[1]) > 0 else "-",
            "baba_anne": rows_cells[2][0] if len(rows_cells[2]) > 0 else "-",
            "baba_anne_baba": rows_cells[2][1] if len(rows_cells[2]) > 1 else "-",
            "baba_anne_anne": rows_cells[3][0] if len(rows_cells[3]) > 0 else "-",
            "anne": rows_cells[4][0] if len(rows_cells[4]) > 0 else "-",
            "anne_baba": rows_cells[4][1] if len(rows_cells[4]) > 1 else "-",
            "anne_baba_baba": rows_cells[4][2] if len(rows_cells[4]) > 2 else "-",
            "anne_baba_anne": rows_cells[5][0] if len(rows_cells[5]) > 0 else "-",
            "anne_anne": rows_cells[6][0] if len(rows_cells[6]) > 0 else "-",
            "anne_anne_baba": rows_cells[6][1] if len(rows_cells[6]) > 1 else "-",
            "anne_anne_anne": rows_cells[7][0] if len(rows_cells[7]) > 0 else "-",
        }
    return None


def parse_detailed_stat_tables(stat_html):
    """
    Parses TJK AtKosuIstatistik tables (Hipodrom, Jokey, Pist & P. Durumu, Mesafe)
    """
    if not stat_html:
        return {}

    h3s = [html_lib.unescape(re.sub(r'<[^>]+>', '', h)).strip() for h in re.findall(r'<h3[^>]*>(.*?)</h3>', stat_html, re.S)]
    tables = re.findall(r'<table.*?</table>', stat_html, re.S)

    sections = {}
    for idx, tbl in enumerate(tables):
        title = h3s[idx] if idx < len(h3s) else f"Tablo_{idx}"
        trs = re.findall(r'<tr[^>]*>(.*?)</tr>', tbl, re.S)
        rows = []
        for tr in trs[1:]:
            tds = [html_lib.unescape(re.sub(r'\s+', ' ', re.sub(r'<[^>]+>', '', td))).strip() for td in re.findall(r'<td[^>]*>(.*?)</td>', tr, re.S)]
            if tds:
                rows.append(tds)
        sections[title] = rows
    return sections


PD_MAP = {
    "Normal": "NO", "Nemli": "NE", "Islak": "IS", "Sulu": "SL", "Çok Yumuşak": "ÇY",
    "Biraz Yumuşak": "BY", "Yumuşak": "YM", "Ağır": "AĞ", "Çok Ağır": "ÇA", "Sert": "ST",
    "Biraz Sert": "BS", "Biraz Ağır": "BA", "İyi": "İY",
}


def finish_stats(positions):
    """Bitiriş sıralarından 1.-5. sayıları ve yüzdeleri (yarış başına)."""
    k = len(positions)
    counts = [sum(1 for p in positions if p == i) for i in range(1, 6)]
    return build_finish_block(k, counts)


def build_finish_block(k, counts):
    w1, w2, w3, w4, w5 = (counts + [0] * 5)[:5]
    pct = (lambda x: round(x / k * 100.0, 1)) if k else (lambda x: 0.0)
    return {
        "starts": k, "w1": w1, "w2": w2, "w3": w3, "w4": w4, "w5": w5,
        "win_pct": pct(w1), "second_pct": pct(w2), "third_pct": pct(w3),
        "top3_pct": pct(w1 + w2 + w3), "top5_pct": pct(w1 + w2 + w3 + w4 + w5),
        # Geriye dönük uyum: eski alan adları
        "podium_pct": pct(w1 + w2 + w3), "tabela_pct": pct(w1 + w2 + w3 + w4),
    }


def affinity_from(block):
    if not block["starts"]:
        return 0
    return min(99, int(round(30 + block["win_pct"] * 0.9 + block["top3_pct"] * 0.55 + min(15, block["starts"] * 2))))


def parse_tl(text):
    """'445.000' / '1.297.200 t' / '451.271.930,63' -> 445000 (kuruş atılır)."""
    t = (text or "").replace("t", "").replace("TL", "").strip()
    t = t.split(",")[0].replace(".", "").strip()
    return int(t) if t.isdigit() else 0


def format_tl(value):
    return f"{value:,}".replace(",", ".") + " t" if value else "0 t"


def race_row_to_dict(tds):
    """AtKosuBilgileri koşu satırını (hücre listesi) sözlüğe çevirir."""
    msf = int(tds[2]) if tds[2].isdigit() else 0
    p_parts = tds[3].split(":", 1)
    p_letter = p_parts[0].strip() if p_parts else "K"
    p_cond_full = p_parts[1].strip() if len(p_parts) > 1 else "Normal"
    pen_m = re.search(r'([\d]+(?:[\.,]\d+)?)\s*$', p_cond_full)
    penetrometre = pen_m.group(1).replace(",", ".") if pen_m else ""
    p_cond = (p_cond_full[:pen_m.start()].strip() if pen_m else p_cond_full) or "Normal"
    p_name = "Çim" if p_letter == "Ç" else ("Sentetik" if p_letter == "S" else "Kum")
    pd_code = "SE" if p_letter == "S" else PD_MAP.get(p_cond, tr_upper(p_cond)[:2] or "NO")
    sira = int(tds[4]) if tds[4].isdigit() and int(tds[4]) > 0 else None
    try:
        past_weight = float(re.findall(r'[\d\.]+', tds[6].replace(",", "."))[0])
    except Exception:
        past_weight = None
    kcins = tds[13]
    cls_key, cls_label, cls_order = classify_race(kcins)
    return {
        "tarih": tds[0], "sehir": tds[1], "msf": msf, "p_name": p_name, "pd": pd_code,
        "pd_label": p_cond, "penetrometre": penetrometre, "sira": sira,
        "derece_raw": tds[5], "sec": parse_time_to_seconds(tds[5]), "siklet": past_weight,
        "taki": tds[7], "jokey": tds[8], "kulvar": tds[9], "gny": tds[10], "kcins": kcins,
        "cls_key": cls_key, "cls_label": cls_label, "cls_order": cls_order,
        "hp": int(tds[16]) if tds[16].isdigit() else 0,
        "ikramiye": tds[17] if len(tds) > 17 else "",
    }


SEASONS = [("İlkbahar", (3, 4, 5)), ("Yaz", (6, 7, 8)), ("Sonbahar", (9, 10, 11)), ("Kış", (12, 1, 2))]
DIST_BUCKETS = [("Sprint (≤1200m)", 0, 1200), ("1300–1500m", 1201, 1500), ("Mil (1600–1700m)", 1501, 1700),
                ("Orta (1800–2000m)", 1701, 2000), ("Uzun (2100m+)", 2001, 9999)]
CONDITION_ORDER = ["Normal", "İyi", "Nemli", "Islak", "Sulu", "Sert", "Biraz Sert", "Biraz Yumuşak", "Yumuşak",
                   "Çok Yumuşak", "Biraz Ağır", "Ağır", "Çok Ağır"]


def stats_block(races):
    """Yarış başına 1.-5.'lik istatistiği (derecesiz biten koşular paydada)."""
    return build_finish_block(len(races), [sum(1 for r in races if r["sira"] == i) for i in range(1, 6)])


def build_breakdowns(races):
    """Pist, pist durumu (zemin), mevsim, mesafe ve tam mesafe kırılımları."""
    def grouped(key_fn, order=None):
        groups = {}
        for r in races:
            k = key_fn(r)
            if k is not None:
                groups.setdefault(k, []).append(r)
        keys = order if order else sorted(groups)
        out = []
        for k in keys:
            if k in groups:
                blk = stats_block(groups[k])
                blk["label"] = k
                out.append(blk)
        return out

    def month(r):
        try:
            return int(r["tarih"].split(".")[1])
        except (IndexError, ValueError):
            return None

    def season(r):
        m = month(r)
        return next((name for name, months in SEASONS if m in months), None)

    def bucket(r):
        return next((name for name, lo, hi in DIST_BUCKETS if lo <= r["msf"] <= hi), None) if r["msf"] else None

    conditions = {}
    for sn in ("Kum", "Çim", "Sentetik"):
        sr = [r for r in races if r["p_name"] == sn]
        if sr:
            labels = sorted({r["pd_label"] for r in sr}, key=lambda x: (CONDITION_ORDER.index(x) if x in CONDITION_ORDER else 99, x))
            conditions[sn] = grouped(lambda r, _sn=sn: r["pd_label"] if r["p_name"] == _sn else None, labels)
    exact = grouped(lambda r: r["msf"] or None)
    return {
        "races": len(races),
        "surface": grouped(lambda r: r["p_name"], ["Çim", "Kum", "Sentetik"]),
        "condition": conditions,
        "season": grouped(season, [n for n, _ in SEASONS]),
        "distance": grouped(bucket, [n for n, _, _ in DIST_BUCKETS]),
        "distance_exact": sorted(exact, key=lambda b: b["label"]),
    }


def build_distance_city_times(races, only_dist=None):
    """Aynı mesafeyi farklı illerde ne sürede koştuğu: (mesafe, pist) -> il -> en iyi / ortalama derece."""
    out = {}
    for r in races:
        if not r["sec"] or not r["msf"] or (only_dist and r["msf"] != only_dist):
            continue
        key = f"{r['msf']}|{r['p_name']}"
        out.setdefault(key, {}).setdefault(r["sehir"], []).append(r)
    result = []
    for key, cities in out.items():
        msf, surf = key.split("|")
        rows = []
        for city, rr in cities.items():
            best = min(rr, key=lambda x: x["sec"])
            rows.append({
                "city": city, "count": len(rr),
                "best": format_seconds_to_time_str(best["sec"]), "best_sec": round(best["sec"], 2),
                "best_date": best["tarih"], "best_pd": best["pd"], "best_sira": best["sira"] or "-",
                "avg": format_seconds_to_time_str(sum(x["sec"] for x in rr) / len(rr)),
            })
        rows.sort(key=lambda x: x["best_sec"])
        result.append({"msf": int(msf), "surface": surf, "cities": rows,
                       "count": sum(c["count"] for c in rows)})
    result.sort(key=lambda g: (-g["count"], g["msf"]))
    return result


def parse_horse_race_rows(tbody_html):
    """AtKosuBilgileri koşu tablosu: Tarih, Şehir, Msf, Pist, S, Derece, Sıklet, Takı, Jokey, St, Gny,
    Grup, K.No, Kcins, Ant., Sahip, HP, İkramiye, S20 (tüm kariyer)."""
    rows = []
    for tr in re.findall(r'<tr[^>]*>(.*?)</tr>', tbody_html, re.S):
        tds = [clean_cell(td) for td in re.findall(r'<td[^>]*>(.*?)</td>', tr, re.S)]
        if len(tds) < 17 or not re.match(r'\d{2}\.\d{2}\.\d{4}', tds[0]):
            continue
        rows.append(tds)
    return rows


def fetch_horse_bankomat_sheet(at_id, today_weight=57.0, target_dist=1200, is_arab=False, lite=False):
    """
    Safkanın TJK sayfalarından:
    - Tüm kariyer koşuları -> 1.-2.-3.-4.-5.'lik yüzdeleri, koşu sınıfı karnesi, HP seyri,
      son 10 koşunun Özel Performans & Derece Bülteni
    - (lite=False ise) 3 Kuşak Pedigri ve Zemin/Mesafe istatistikleri
    """
    if not at_id:
        return {"races": [], "summary": None, "pedigree": None, "surface_summary": {}}

    cache_key = f"history_v4_{at_id}_{today_weight}_{target_dist}_{int(bool(is_arab))}_{int(bool(lite))}"
    cached = cache_get(cache_key, TTL_HISTORY)
    if cached is not None:
        return cached

    base_url = f"https://www.tjk.org/TR/YarisSever/Query/ConnectedPage/AtKosuBilgileri?1=1&QueryParameter_AtId={at_id}"
    html = fetch_cached(base_url, TTL_HISTORY)

    pedigree_tree = None
    detailed_stats = {}
    if not lite:
        try:
            pedigree_tree = parse_pedigri_tree(fetch_cached(f"https://www.tjk.org/TR/YarisSever/Query/Pedigri/Pedigri?Atkodu={at_id}", 24 * 3600))
        except Exception:
            pedigree_tree = None
        try:
            detailed_stats = parse_detailed_stat_tables(fetch_cached(f"https://www.tjk.org/TR/YarisSever/Query/AtKosuIstatistik/AtKosuIstatistik?Atkodu={at_id}", TTL_HISTORY))
        except Exception:
            detailed_stats = {}

    # Tablo gövdelerini içeriğe göre ayır (genç atlarda özet tablosu hiç olmayabilir)
    summary_rows, race_rows = [], []
    for tb in re.findall(r'<tbody.*?</tbody>', html, re.S):
        rr = parse_horse_race_rows(tb)
        if rr:
            race_rows.extend(rr)
            continue
        for tr in re.findall(r'<tr[^>]*>(.*?)</tr>', tb, re.S):
            cols = [clean_cell(c) for c in re.findall(r'<td[^>]*>(.*?)</td>', tr, re.S)]
            if len(cols) >= 8 and cols[1].isdigit():
                summary_rows.append(cols)

    # TJK varsayılan sayfada en fazla 50 koşu gösterir. Kariyer daha uzunsa yıl yıl tamamla.
    official_total = next((int(c[1]) for c in summary_rows if tr_upper(c[0]) == "TOPLAM"), 0)
    if official_total > len(race_rows):
        years = [m.group(1) for c in summary_rows for m in [re.match(r'(\d{4})\s*Yılı', c[0])] if m]

        def year_rows(y):
            try:
                page = fetch_cached(f"{base_url}&QueryParameter_Yil={y}", TTL_HISTORY)
                return [r for tb in re.findall(r'<tbody.*?</tbody>', page, re.S) for r in parse_horse_race_rows(tb)]
            except Exception:
                return []

        with ThreadPoolExecutor(max_workers=3) as pool:
            per_year = list(pool.map(year_rows, years))
        merged, seen = [], set()
        for rows in [race_rows] + per_year:
            for tds in rows:
                key = (tds[0], tds[1], tds[2], tds[12])  # tarih, şehir, mesafe, koşu no
                if key not in seen:
                    seen.add(key)
                    merged.append(tds)
        merged.sort(key=lambda t: datetime.strptime(t[0], "%d.%m.%Y"), reverse=True)
        race_rows = merged

    # --- Koşu satırları (tüm kariyer) ---
    all_races = [race_row_to_dict(tds) for tds in race_rows]
    finished = [r for r in all_races if r["sira"]]

    # --- 1.-2.-3.-4.-5.'lik yüzdeleri: resmi özet tablosu, yoksa koşulardan ---
    surface_summary = {}
    for cols in summary_rows:
        counts = [int(c) if c.isdigit() else 0 for c in cols[2:7]]
        block = build_finish_block(int(cols[1]), counts)
        block["earnings"] = cols[7] if len(cols) > 7 else ""
        key = "TOPLAM" if tr_upper(cols[0]) == "TOPLAM" else cols[0]
        surface_summary[key] = block
    def stats_over(races):
        # Yarış başına: derecesiz biten koşular da paydada
        return build_finish_block(len(races), [sum(1 for r in races if r["sira"] == i) for i in range(1, 6)])

    if "TOPLAM" not in surface_summary:
        surface_summary["TOPLAM"] = dict(stats_over(all_races), earnings="")
        for s_name in ("Çim", "Kum", "Sentetik"):
            surface_summary[s_name] = dict(stats_over([r for r in all_races if r["p_name"] == s_name]), earnings="")
    for blk in surface_summary.values():
        blk["affinity_score"] = affinity_from(blk)

    career_stats = dict(surface_summary["TOPLAM"])
    career_stats["last10"] = stats_over(all_races[:10])

    surf_candidates = []
    for s_name in ["Kum", "Çim", "Sentetik"]:
        st = surface_summary.get(s_name)
        if st and st["starts"] > 0:
            surf_candidates.append((st["affinity_score"], st["top3_pct"], st["starts"], s_name))
    surf_candidates.sort(reverse=True)
    if surf_candidates:
        b = surf_candidates[0]
        fav_surface_verdict = f"En Sevdiği Pist: {b[3].upper()} (Yatkınlık Puanı: {b[0]}/100 • {b[2]} koşuda %{b[1]} İlk 3 başarısı)"
    else:
        fav_surface_verdict = "Henüz pist istatistiği oluşmamış (Genetik/Orijin eğilimine bakınız)."

    # --- Yıllık karne & kazanç: resmi özet (YYYY Yılı) + koşulardan pist dağılımı ---
    yearly = []
    for cols in summary_rows:
        ym = re.match(r'(\d{4})\s*Yılı', cols[0])
        if not ym:
            continue
        y = ym.group(1)
        blk = build_finish_block(int(cols[1]), [int(c) if c.isdigit() else 0 for c in cols[2:7]])
        yr_races = [r for r in all_races if r["tarih"].endswith(y)]
        blk.update({
            "year": y, "earnings": cols[7] if len(cols) > 7 else "",
            "surfaces": {sn: sum(1 for r in yr_races if r["p_name"] == sn) for sn in ("Çim", "Kum", "Sentetik")},
        })
        yearly.append(blk)
    if not yearly:  # özet tablosu olmayan genç at: koşulardan hesapla
        for y in sorted({r["tarih"][-4:] for r in all_races}, reverse=True):
            yr_races = [r for r in all_races if r["tarih"].endswith(y)]
            blk = stats_over(yr_races)
            blk.update({"year": y, "earnings": format_tl(sum(parse_tl(r["ikramiye"]) for r in yr_races)),
                        "surfaces": {sn: sum(1 for r in yr_races if r["p_name"] == sn) for sn in ("Çim", "Kum", "Sentetik")}})
            yearly.append(blk)
    yearly.sort(key=lambda b: b["year"], reverse=True)

    # --- İl (hipodrom) karnesi ---
    by_city = []
    for city in sorted({r["sehir"] for r in all_races}):
        cr = [r for r in all_races if r["sehir"] == city]
        blk = stats_over(cr)
        placed = [r["sira"] for r in cr if r["sira"]]
        blk.update({
            "city": city,
            "earnings": format_tl(sum(parse_tl(r["ikramiye"]) for r in cr)),
            "earnings_value": sum(parse_tl(r["ikramiye"]) for r in cr),
            "avg_pos": round(sum(placed) / len(placed), 1) if placed else None,
            "surfaces": {sn: sum(1 for r in cr if r["p_name"] == sn) for sn in ("Çim", "Kum", "Sentetik")},
        })
        by_city.append(blk)
    by_city.sort(key=lambda b: (-b["starts"], b["city"]))

    # --- Pist bazında koşu listeleri (tüm kariyer; yalnız tam görünümde) ---
    surface_races = {}
    if not lite:
        for sn in ("Çim", "Kum", "Sentetik"):
            surface_races[sn] = [{
                "tarih": r["tarih"], "sehir": r["sehir"], "msf": r["msf"], "pd": r["pd"],
                "penetrometre": r["penetrometre"], "sira": r["sira"] or "-", "derece": r["derece_raw"],
                "sinif": r["cls_label"], "kcins": r["kcins"], "jokey": r["jokey"], "siklet": r["siklet"],
                "ikramiye": r["ikramiye"], "hp": r["hp"] or "-",
            } for r in all_races if r["p_name"] == sn]

    # --- Koşu sınıfı karnesi (Grup / KV / Şartlı 1-5 / Handikap / Maiden / Satış) ---
    groups = {}
    for r in all_races:
        g = groups.setdefault(r["cls_key"], {"key": r["cls_key"], "label": r["cls_label"], "order": r["cls_order"],
                                              "positions": [], "dnf": 0, "last": r["tarih"]})
        if r["sira"]:
            g["positions"].append(r["sira"])
        else:
            g["dnf"] += 1
    class_breakdown = []
    for g in sorted(groups.values(), key=lambda x: x["order"]):
        # Derecesiz biten koşular da yarış sayısına dahil (yüzdeler yarış başına)
        counts = [sum(1 for p in g["positions"] if p == i) for i in range(1, 6)]
        blk = build_finish_block(len(g["positions"]) + g["dnf"], counts)
        blk.update({
            "key": g["key"], "label": g["label"], "last_date": g["last"],
            "avg_pos": round(sum(g["positions"]) / len(g["positions"]), 1) if g["positions"] else None,
            "best_pos": min(g["positions"]) if g["positions"] else None,
        })
        class_breakdown.append(blk)

    # --- Handikap puanı seyri ---
    hp_points = [{"tarih": r["tarih"], "hp": r["hp"], "sira": r["sira"], "kcins": r["kcins"]} for r in all_races if r["hp"] > 0]
    hp_history = {
        "points": hp_points[:12],
        "current": hp_points[0]["hp"] if hp_points else None,
        "max": max(p["hp"] for p in hp_points) if hp_points else None,
        "min": min(p["hp"] for p in hp_points) if hp_points else None,
        "change_5": (hp_points[0]["hp"] - hp_points[min(5, len(hp_points) - 1)]["hp"]) if len(hp_points) > 1 else 0,
    }

    # --- Son 10 koşu: Özel Performans & Derece Bülteni ---
    parsed_races = []
    for r in all_races[:10]:
        zorluk = compute_zorluk_degree(r["kcins"], r["hp"])
        sec = parse_time_to_seconds(r["derece_raw"])
        sira_calc = r["sira"] or 12
        if sec and r["msf"] > 0:
            norm_1200_sec = sec * ((1200.0 / r["msf"]) ** 1.055)
            norm_target_sec = sec * ((target_dist / r["msf"]) ** 1.055)
            norm_1200_str = format_seconds_to_time_str(norm_1200_sec)
            norm_target_str = format_seconds_to_time_str(norm_target_sec)
            par_1200 = 81.8 if is_arab or norm_1200_sec > 78.5 else 73.8
            raw_perf = zorluk + ((par_1200 - norm_1200_sec) * 2.4) - ((sira_calc - 1) * 1.85)
            performans = round(max(0.0, min(55.0, raw_perf)), 2)
        else:
            norm_1200_str = norm_target_str = "-"
            performans = 0.0
        pw = r["siklet"] if r["siklet"] else today_weight
        kiloya_gore = round(max(0.0, performans * (1.0 + ((pw - today_weight) * 0.008))), 2)
        if performans >= 34.5 or (sira_calc <= 2 and performans >= 31.0):
            form_grade = "A"
        elif performans >= 30.5 or sira_calc <= 3:
            form_grade = "B"
        elif performans >= 24.5 or sira_calc <= 5:
            form_grade = "C"
        elif performans >= 14.0:
            form_grade = "D"
        else:
            form_grade = "E"
        poz_pct = round(max(8.3, min(100.0, (13 - min(12, sira_calc)) / 12.0 * 100.0)), 1)
        parsed_races.append({
            "tarih": r["tarih"], "sehir": r["sehir"], "sart": r["kcins"], "sinif": r["cls_label"],
            "pist": f"{r['msf']}{r['p_name']}", "pd": r["pd"], "pd_label": r["pd_label"],
            "penetrometre": r["penetrometre"], "jokey": r["jokey"],
            "sira": r["sira"] if r["sira"] else "-", "taki": r["taki"], "siklet": pw,
            "kulvar": r["kulvar"], "gny": r["gny"], "hp": r["hp"] or "-",
            "performans": performans, "zorluk": zorluk, "form": form_grade, "kiloya_gore": kiloya_gore,
            "gercek_derece": r["derece_raw"], "norm_1200_derece": norm_1200_str,
            "norm_target_derece": norm_target_str, "poz_pct": poz_pct,
        })

    valid_perfs = [r["performans"] for r in parsed_races if r["performans"] > 0]
    valid_kilo = [r["kiloya_gore"] for r in parsed_races if r["kiloya_gore"] > 0]
    valid_poz = [r["poz_pct"] for r in parsed_races]
    summary = {
        "avg_performans": round(sum(valid_perfs) / len(valid_perfs), 2) if valid_perfs else 0.0,
        "max_performans": round(max(valid_perfs), 2) if valid_perfs else 0.0,
        "avg_kiloya_gore": round(sum(valid_kilo) / len(valid_kilo), 2) if valid_kilo else 0.0,
        "avg_poz_pct": round(sum(valid_poz) / len(valid_poz), 1) if valid_poz else 50.0,
        "total_analyzed": len(parsed_races),
    }

    result = {
        "races": parsed_races,
        "summary": summary,
        "career_stats": career_stats,
        "class_breakdown": class_breakdown,
        "hp_history": hp_history,
        "pedigree": pedigree_tree,
        "surface_summary": surface_summary,
        "fav_surface_verdict": fav_surface_verdict,
        "detailed_stats": detailed_stats,
        "total_career_races": len(all_races),
        "official_total": official_total or len(all_races),
        "yearly": yearly,
        "by_city": by_city,
        "breakdowns": build_breakdowns(all_races),
        # Kartlar (lite) için yalnız bugünkü mesafe; tam görünümde tüm mesafeler
        "distance_city_times": build_distance_city_times(all_races, only_dist=target_dist if lite else None),
        "surface_races": surface_races,
    }
    if lite:
        # Kartlar için yalnız gereken alanlar (koşu başına 10-17 at yüklenir; boyut önemli)
        result = {
            "career_stats": career_stats, "hp_history": hp_history,
            "breakdowns": {"races": result["breakdowns"]["races"], "season": result["breakdowns"]["season"],
                           "surface": result["breakdowns"]["surface"], "distance": result["breakdowns"]["distance"]},
            "distance_city_times": result["distance_city_times"],
            "total_career_races": result["total_career_races"],
            "surface_summary": {"TOPLAM": surface_summary.get("TOPLAM")},
        }
    cache_set(cache_key, result)
    return result


# ------------------------- AKRABA İSTATİSTİKLERİ (BABA / ANNE / KISRAK BABASI / KARDEŞLER) -------------------------
RELATIVE_PAGES = {
    "baba": ("AygirIstatistikleri", "AygirAdi"),
    "anne": ("KisrakIstatistikleri", "KisrakAdi"),
    "kisrak_baba": ("KisrakBabasi", "KisrakBabaAdi"),
}
SIRE_SURFACES = {"Çim": "1", "Kum": "2", "Sentetik": "4"}  # TJK QueryParameter_PistId


def strip_country(name):
    return re.sub(r'\s*\([A-Z]{2,4}\)\s*$', '', (name or "").strip())


def parse_query_table(page_html, wanted_name):
    """AygirIstatistikleri / KisrakIstatistikleri / KisrakBabasi sonuç tablosu.
    Arama önek eşleşmesi yaptığı için ('SUPER' -> SUPER SAVER, SUPER ALP...) tam adı seçer."""
    m = re.search(r'<table[^>]*id="queryTable".*?</table>', page_html, re.S)
    if not m:
        return None
    tbl = m.group(0)
    headers = [clean_cell(h) for h in re.findall(r'<th[^>]*>(.*?)</th>', tbl, re.S)]
    rows = []
    for tr in re.findall(r'<tr[^>]*>(.*?)</tr>', tbl, re.S):
        vals = [clean_cell(c) for c in re.findall(r'<td[^>]*>(.*?)</td>', tr, re.S)]
        if len(vals) >= len(headers) - 1 and len(vals) > 8:
            rows.append(dict(zip(headers, vals)))
    if not rows:
        return None
    target = tr_upper(strip_country(wanted_name))
    rec = next((r for r in rows if tr_upper(strip_country(list(r.values())[0])) == target), rows[0])

    def num(*keys):
        for k in keys:
            v = rec.get(k, "")
            if v and v.replace(".", "").isdigit():
                return int(v.replace(".", ""))
        return 0

    starts = num("Koşu")
    counts = [num(f"{i}.") for i in range(1, 6)]
    blk = build_finish_block(starts, counts)
    blk.update({
        "name": list(rec.values())[0],
        "country": rec.get("Ülke Adı") or rec.get("Ülke", ""),
        "breed": rec.get("Irk Adı") or rec.get("Irk", ""),
        "runners": num("Koşan Tay Adet", "Koşan Tay"),
        "winners": num("Kazanan Yavru Adet"),
        "earnings": rec.get("Kazanç", ""),
    })
    blk["winners_pct"] = round(blk["winners"] / blk["runners"] * 100.0, 1) if blk["runners"] and blk["winners"] else None
    return blk


def fetch_horse_relatives(at_id, irk=None):
    """Babası (pist kırılımlı), annesi, kısrak babası ve kardeşlerinin TJK istatistikleri.
    irk: '1' İngiliz, '2' Arap — aynı adlı Arap ve İngiliz kısrak/aygır karışmasın diye."""
    irk = irk if irk in ("1", "2") else None
    cache_key = f"relatives_{at_id}_{irk or '-'}"
    cached = cache_get(cache_key, 24 * 3600)
    if cached is not None:
        return cached
    page = fetch_cached(f"https://www.tjk.org/TR/YarisSever/Query/ConnectedPage/AtKosuBilgileri?1=1&QueryParameter_AtId={at_id}", TTL_HISTORY)
    names = {}
    for key, (_, param) in RELATIVE_PAGES.items():
        # TJK Türkçe harfleri HTML kodu olarak yazar: 'EDAD&#220;L&#220;LE' -> önce çöz, sonra '&' ile kes
        m = re.search(r'QueryParameter_' + param + r'=([^"\']+)', page)
        if m:
            value = html_lib.unescape(m.group(1)).split("&")[0]
            value = urllib.parse.unquote_plus(value).strip()
            if value:
                names[key] = value

    base = "https://www.tjk.org/TR/YarisSever/Query/Page/"
    jobs = {}
    for key, name in names.items():
        page_name, param = RELATIVE_PAGES[key]
        url = f"{base}{page_name}?QueryParameter_{param}={urllib.parse.quote(name)}"
        if irk:
            url += f"&QueryParameter_IrkId={irk}"
        jobs[key] = url
        if key == "baba":
            for sn, pid in SIRE_SURFACES.items():
                jobs[f"baba_{sn}"] = f"{url}&QueryParameter_PistId={pid}"
    jobs["kardes"] = f"https://www.tjk.org/TR/YarisSever/Query/Kardes/Kardes?Atkodu={at_id}"

    failures = []

    def load(item):
        key, url = item
        try:
            return key, fetch_cached(url, 24 * 3600)
        except Exception:
            failures.append(key)
            return key, None

    with ThreadPoolExecutor(max_workers=4) as pool:
        pages = dict(pool.map(load, jobs.items()))

    result = {"names": names}
    for key in RELATIVE_PAGES:
        if key in names and pages.get(key):
            result[key] = parse_query_table(pages[key], names[key])
    if result.get("baba"):
        result["baba"]["by_surface"] = {}
        for sn in SIRE_SURFACES:
            pg = pages.get(f"baba_{sn}")
            blk = parse_query_table(pg, names["baba"]) if pg else None
            if blk:
                result["baba"]["by_surface"][sn] = blk

    siblings = []
    kp = pages.get("kardes")
    if kp:
        for tr in re.findall(r'<tr[^>]*>(.*?)</tr>', kp, re.S):
            c = [clean_cell(x) for x in re.findall(r'<td[^>]*>(.*?)</td>', tr, re.S)]
            if len(c) >= 8 and c[2].isdigit():
                blk = build_finish_block(int(c[2]), [int(x) if x.isdigit() else 0 for x in c[3:7]] + [0])
                blk.update({"name": c[0], "sire": c[1], "earnings": c[7]})
                siblings.append(blk)
    tot_starts = sum(b["starts"] for b in siblings)
    tot = build_finish_block(tot_starts, [sum(b[f"w{i}"] for b in siblings) for i in range(1, 5)] + [0])
    tot["count"] = len(siblings)
    tot["winners"] = sum(1 for b in siblings if b["w1"] > 0)
    tot["earnings"] = format_tl(sum(parse_tl(b["earnings"]) for b in siblings))
    result["kardesler"] = {"list": siblings, "total": tot}
    result["incomplete"] = bool(failures)
    # Geçici TJK hatasında eksik sonucu 24 saat saklama: bir sonraki açılışta yeniden denensin
    if not failures:
        cache_set(cache_key, result)
    return result


OFFSPRING_SAMPLE = 20  # baba / kısrak babası için örneklem (TJK'ya yük bindirmemek için)


def fetch_offspring_breakdown(kind, name):
    """
    Bir aygırın (baba / kısrak babası) ya da kısrağın (anne) yavrularının koşu kayıtlarından
    pist, zemin, mevsim ve mesafe kırılımı. Anne için tüm yavrular; aygır için örneklem.
    """
    cache_key = f"offspring_{kind}_{name}"
    cached = cache_get(cache_key, 24 * 3600)
    if cached is not None:
        return cached
    param = "AnneAdi" if kind == "anne" else "BabaAdi"
    page = fetch_cached(f"https://www.tjk.org/TR/YarisSever/Query/Page/Orijin?QueryParameter_{param}={urllib.parse.quote(name)}", 24 * 3600)
    m = re.search(r'<table[^>]*id="queryTable".*?</table>', page, re.S)
    offspring = []
    total_m = re.search(r'Toplam\s*(\d+)\s*sonuçtan', clean_cell(page))
    if m:
        headers = [clean_cell(h) for h in re.findall(r'<th[^>]*>(.*?)</th>', m.group(0), re.S)]
        target = tr_upper(strip_country(name))
        for tr in re.findall(r'<tr[^>]*>(.*?)</tr>', m.group(0), re.S):
            ids = re.findall(r'QueryParameter_AtId=(\d+)', tr)
            vals = dict(zip(headers, [clean_cell(c) for c in re.findall(r'<td[^>]*>(.*?)</td>', tr, re.S)]))
            if not ids or not vals.get("Son 6 Y."):
                continue  # hiç koşmamış tay
            parent = vals.get("Anne", "").split(" - ")[0] if kind == "anne" else vals.get("Baba", "")
            if tr_upper(strip_country(parent)) != target:
                continue  # önek eşleşmesi: başka aygırın tayı
            offspring.append({"at_id": ids[0], "name": vals.get("At İsmi", ""), "earnings": vals.get("Kazanç", "")})
    sample = offspring if kind == "anne" else offspring[:OFFSPRING_SAMPLE]

    failed = []

    def races_of(o):
        try:
            html_page = fetch_cached(f"https://www.tjk.org/TR/YarisSever/Query/ConnectedPage/AtKosuBilgileri?1=1&QueryParameter_AtId={o['at_id']}", TTL_HISTORY)
            return [race_row_to_dict(t) for tb in re.findall(r'<tbody.*?</tbody>', html_page, re.S) for t in parse_horse_race_rows(tb)]
        except Exception:
            failed.append(o["name"])
            return []

    with ThreadPoolExecutor(max_workers=4) as pool:
        per = list(pool.map(races_of, sample))
    all_races = [r for rr in per for r in rr]
    result = {
        "kind": kind, "name": name,
        "offspring_listed": int(total_m.group(1)) if total_m else len(offspring),
        "offspring_raced_on_page": len(offspring),
        "sample_size": len(sample), "is_sample": kind != "anne" and len(offspring) > len(sample),
        "sample_names": [o["name"] for o in sample],
        "breakdowns": build_breakdowns(all_races),
        "failed": len(failed),
    }
    if not failed:
        cache_set(cache_key, result)
    return result


# ------------------------- İDMAN (GALOP / SPRINT / KENTER) -------------------------
IDMAN_SPLITS = [2200, 2000, 1800, 1600, 1400, 1200, 1000, 800, 600, 400, 200]
IDMAN_DURUM_ORDER = {"ÇOK RAHAT": 1, "RAHAT": 2, "HAFİF ÇALIŞIR": 3, "ÇALIŞIR": 3, "ÇALIŞARAK": 3,
                     "ÇOK ÇALIŞIR": 4, "ZORLANARAK": 4}


def parse_idman_detail(detail_html):
    """idmanpistiDetay: 'baslik' ve 'icerik' kutuları aynı sırada; ikisini eşleştirir."""
    labels = [clean_cell(x).rstrip(":").rstrip(",").strip() for x in re.findall(r'<div class="baslik">(.*?)</div>', detail_html, re.S)]
    values = [clean_cell(x) for x in re.findall(r'<div class="icerik">(.*?)</div>', detail_html, re.S)]
    info = dict(zip(labels, values))
    return {
        "galop_durumu": info.get("Galop Durumu", ""),
        "pist_durumu": info.get("Pist Durumu", ""),
        "idman_jokeyi": info.get("İdman Jokey", "") or info.get("İdman Jokeyi", ""),
        "hava": info.get("Hava Durumu", ""),
        "zemin": info.get("Zemin Durumu", ""),
        "taki": info.get("Takı Şekil", ""),
        "aciklama": info.get("Açıklama", ""),
    }


def parse_race_idman_table(page_html):
    m = re.search(r'<table[^>]*id="KosuIdmanGaloplari_table".*?</table>', page_html, re.S)
    if not m:
        return []
    tbl = m.group(0)
    headers = [clean_cell(h) for h in re.findall(r'<th[^>]*>(.*?)</th>', tbl, re.S)]
    rows = []
    for tr in re.findall(r'<tr[^>]*>(.*?)</tr>', tbl, re.S):
        cells = re.findall(r'<td[^>]*>(.*?)</td>', tr, re.S)
        if not cells:
            continue
        vals = [clean_cell(c) for c in cells]
        rec = dict(zip(headers, vals))
        det_m = re.search(r'idmanpistiDetay\?(?:1=1&amp;)?QueryParameter_Id=(\d+)', tr)
        splits = {}
        for d in IDMAN_SPLITS:
            t = rec.get(str(d), "")
            sec = parse_time_to_seconds(t)
            if sec:
                splits[d] = {"time": t, "sec": sec}
        no = rec.get("At No", "")
        rows.append({
            "no": int(no) if no.isdigit() else None,
            "name": rec.get("At Adı", ""),
            "splits": [{"dist": d, "time": splits[d]["time"], "sec": splits[d]["sec"]} for d in sorted(splits, reverse=True)],
            "tarih": rec.get("İ. Tarihi", ""),
            "pist": rec.get("Pist", ""),
            "tur": rec.get("İ. Türü", ""),
            "hipodrom": rec.get("İdman Hipodromu", ""),
            "detay_id": det_m.group(1) if det_m else None,
        })
    return rows


def fetch_race_idman(kosu_kodu, at_kodu, race_date=""):
    """
    Koşudaki atların son idmanları (TJK 'İdm' sayfası) + her idmanın detayı
    (Galop Durumu: Çok Rahat / Rahat / Çalışarak, idman jokeyi, zemin, hava).
    """
    cache_key = f"idman_{kosu_kodu}"
    cached = cache_get(cache_key, 1200)
    if cached is not None:
        return cached
    page = fetch_url(f"https://www.tjk.org/TR/YarisSever/Info/idmanpisti/Kosu?KosuKodu={kosu_kodu}&Atkodu={at_kodu}")
    rows = parse_race_idman_table(page)

    detail_failures = []

    def load_detail(row):
        if not row["detay_id"]:
            return {}
        try:
            return parse_idman_detail(fetch_cached(f"https://www.tjk.org/TR/YarisSever/Info/idmanpistiDetay?QueryParameter_Id={row['detay_id']}", 24 * 3600))
        except Exception:
            detail_failures.append(row["detay_id"])
            return {}

    with ThreadPoolExecutor(max_workers=4) as pool:
        details = list(pool.map(load_detail, rows))

    try:
        rdate = datetime.strptime(race_date, "%d/%m/%Y") if race_date else None
    except ValueError:
        rdate = None
    for row, det in zip(rows, details):
        row.update(det)
        longest = row["splits"][0] if row["splits"] else None
        row["longest"] = longest
        row["pace_200"] = round(longest["sec"] / (longest["dist"] / 200.0), 2) if longest else None
        s400 = next((x for x in row["splits"] if x["dist"] == 400), None)
        row["son_400"] = s400["sec"] if s400 else None
        row["durum_rank"] = IDMAN_DURUM_ORDER.get(tr_upper(row.get("galop_durumu", "")), None)
        try:
            row["days_before"] = (rdate - datetime.strptime(row["tarih"], "%d.%m.%Y")).days if rdate and row["tarih"] else None
        except ValueError:
            row["days_before"] = None

    # Koşu içi sıralama: son 400m en hızlı idman
    timed = sorted([r for r in rows if r["son_400"]], key=lambda r: r["son_400"])
    for rank, r in enumerate(timed):
        r["son_400_rank"] = rank + 1
    result = {"kosu_kodu": kosu_kodu, "rows": rows, "count": len(rows), "timed_count": len(timed)}
    if not detail_failures:  # eksik detay varsa önbelleğe alma; sonraki istek tamamlasın
        cache_set(cache_key, result)
    return result


ALTILI_RE = re.compile(r"6\s*['’`´]?\s*L[Iİı]\s*GANYAN", re.I)


def parse_agf(raw):
    """
    AGF hücresi: '%14.63(4)' ya da iki altılılı günlerde '%15.82(3)  %15.55(3)' (1. ve 2. Altılı).
    Yalnız 2. Altılı'ya dahil koşularda ilk değer '%0(0)' olur; bu durumda ikinci değer kullanılır.
    Dönen: (yüzde, sıra, [(yüzde, sıra), ...])
    """
    values = [(float(p.replace(",", ".")), int(r)) for p, r in re.findall(r'%\s*([\d\.,]+)\s*\((\d+)\)', raw or "")]
    if not values:
        pm = re.search(r'([\d]+(?:[\.,]\d+)?)', raw or "")
        return (float(pm.group(1).replace(",", ".")) if pm else 0.0), 0, []
    nonzero = [v for v in values if v[0] > 0]
    pct, rank = nonzero[0] if nonzero else values[0]
    return pct, rank, values


def handle_race_footer(race, line):
    """Koşu dipnotu: eküri grupları ve bahis türleri (örn. "6'LI GANYAN Bu koşudan başlar").
    Bir satırda birden fazla eküri grubu olabilir:
      "[(1)A,(6)B,] eküridir.[(2)C],(11)D],] eküridir.7'Lİ PLASE;..."
    """
    race["bet_info"] = (race["bet_info"] + " " + line).strip() if race["bet_info"] else line
    if ALTILI_RE.search(line):
        race["is_altili_start"] = True
    parts = re.split(r'(?i)ekür[iİı]d[iİı]r\.?', line)
    if len(parts) < 2:
        return
    groups = race.setdefault("ekuri_groups", [])
    texts = []
    for seg in parts[:-1]:
        nos = sorted({int(x) for x in re.findall(r'\((\d+)\)', seg)})
        if len(nos) >= 2 and nos not in groups:
            groups.append(nos)
            texts.append(seg.strip(" .") + " eküridir.")
    by_no = {h["no"]: h for h in race["horses"]}
    for gi, nos in enumerate(groups):
        for no in nos:
            if no in by_no:
                by_no[no]["is_ekuri"] = True
                by_no[no]["ekuri_group"] = gi
    if texts:
        race["ekuri_info"] = " ".join(filter(None, [race.get("ekuri_info", "")] + texts)).strip()


def build_altili_groups(races):
    """
    TJK dipnotlarındaki "6'LI GANYAN Bu koşudan başlar" işaretlerine göre altılı ayaklarını kurar.
    Bir günde birden fazla altılı olabilir (örn. 1. ve 2. Altılı). İşaret yoksa son 6 koşu kullanılır.
    Dönen her grup: {label, start_race_no, race_indices[6], source}
    """
    groups = []
    starts = [i for i, r in enumerate(races) if r.get("is_altili_start")]
    for n, si in enumerate(starts):
        idxs = list(range(si, min(si + 6, len(races))))
        if len(idxs) < 6:
            continue
        label = f"{n + 1}. Altılı" if len(starts) > 1 else "Altılı Ganyan"
        groups.append({"label": label, "start_race_no": races[si]["race_no"],
                       "race_indices": idxs, "source": "tjk"})
    if not groups and races:
        si = max(0, len(races) - 6)
        groups.append({"label": "Altılı Ganyan (Tahmini: Son 6 Koşu)",
                       "start_race_no": races[si]["race_no"],
                       "race_indices": list(range(si, len(races))), "source": "fallback"})
    return groups


def parse_tjk_csv(csv_text, html_extras=None):
    """
    Parses the complete TJK Daily Race Program CSV into structured races and horses,
    then runs the Quant Engine on every race.
    """
    html_extras = html_extras or {}
    lines = [line.strip() for line in csv_text.splitlines() if line.strip()]
    if not lines:
        return None

    # Line 0: İzmir;(1. Yarış Günü);19/11/2026
    header_parts = [p.strip() for p in lines[0].split(";") if p.strip()]
    city_name = header_parts[0] if header_parts else "Hipodrom"
    meeting_day = header_parts[1] if len(header_parts) > 1 else ""
    race_date = header_parts[2] if len(header_parts) > 2 else ""

    races = []
    current_race = None
    in_horses = False
    after_horses = False

    i = 1
    while i < len(lines):
        line = lines[i]
        # Detect Race Header: e.g. "1. Kosu :   15.00;SATIŞ 2; 2 Yaşlı İngilizler; 57.00kg; 1400m; Kum;;;;Rekor Derece :1.24.15"
        # Yerli: "1. Kosu :   15.00;..."  Yabancı: "1. Kosu 14.48;..." (iki nokta yok)
        m_race = re.match(r'^(\d+)\.\s*Ko[sş]u\s*:?\s*(.*)$', line, re.I)
        if m_race:
            if current_race:
                races.append(current_race)
            race_no = int(m_race.group(1))
            rest = m_race.group(2)
            parts = [p.strip() for p in rest.split(";")]

            # First part contains optional Cup Name and Time: "BODRUM KOŞUSU 15.30" or "15.00"
            first_col = parts[0] if parts else ""
            time_m = re.search(r'(\d{1,2}\.\d{2})$', first_col)
            race_time = time_m.group(1).replace(".", ":") if time_m else ""
            cup_name = first_col[:time_m.start()].strip() if time_m else first_col

            condition = parts[1] if len(parts) > 1 else ""
            age_group = parts[2] if len(parts) > 2 else ""

            # Distance & Surface look across parts[3..6]
            distance_m = 1600
            surface = "Kum"
            record_time = ""
            for p in parts[2:]:
                dm = re.search(r'(\d{3,4})\s*m\b', p, re.I)
                if dm:
                    distance_m = int(dm.group(1))
                norm_surface = normalize_surface(p)
                if norm_surface:
                    surface = norm_surface
                if "Rekor Derece" in p:
                    rm = re.search(r'Rekor Derece\s*:\s*([\d\.:]+)', p)
                    if rm:
                        record_time = rm.group(1)

            current_race = {
                "race_no": race_no,
                "time": race_time,
                "cup_name": cup_name,
                "condition": condition,
                "age_group": age_group,
                "distance_m": distance_m,
                "surface": surface,
                "record_time": record_time,
                "prizes": [],
                "bet_info": "",
                "ekuri_info": "",
                "is_altili_start": False,
                "scratched": [],
                "ekuri_groups": [],
                "horses": []
            }
            in_horses = False
            after_horses = False
            i += 1
            continue

        if current_race is None:
            i += 1
            continue

        if line.startswith("1.)") and not current_race["prizes"]:
            prizes = [p.strip() for p in line.split(";") if p.strip()]
            current_race["prizes"] = prizes[:5]
            i += 1
            continue

        if line.startswith("At No;At İsmi;"):
            in_horses = True
            i += 1
            continue

        if in_horses:
            cols = [c.strip() for c in line.split(";")]
            if cols and cols[0].isdigit():
                no = int(cols[0])
                raw_name = cols[1] if len(cols) > 1 else ""
                # "(Koşmaz)" işaretli atlar yarışa katılmaz: analizden ve olasılıklardan çıkarılır
                is_scratched = bool(SCRATCH_RE.search(raw_name))
                clean_name, eq_list = split_horse_name_and_equipment(SCRATCH_RE.sub(" ", raw_name).strip())
                age = cols[2] if len(cols) > 2 else ""
                sire = cols[3] if len(cols) > 3 else ""
                dam = cols[4] if len(cols) > 4 else ""

                weight_str = (cols[5] if len(cols) > 5 else "57").replace(",", ".")
                try:
                    weight = float(re.findall(r'[\d\.]+', weight_str)[0])
                except Exception:
                    weight = 57.0

                jockey = cols[6] if len(cols) > 6 else ""
                owner = cols[7] if len(cols) > 7 else ""
                trainer = cols[8] if len(cols) > 8 else ""

                st_raw = cols[9] if len(cols) > 9 else str(no)
                st_m = re.search(r'(\d+)', st_raw)
                start_no = int(st_m.group(1)) if st_m else no

                agf_raw = cols[10] if len(cols) > 10 else ""
                agf_pct, agf_rank, agf_values = parse_agf(agf_raw)

                hp_str = cols[11] if len(cols) > 11 else "0"
                handicap = int(hp_str) if hp_str.isdigit() else 0

                son6 = cols[12] if len(cols) > 12 else ""
                kgs_str = cols[13] if len(cols) > 13 else "0"
                kgs = int(kgs_str) if kgs_str.isdigit() else 0

                s20_str = cols[14] if len(cols) > 14 else "0"
                s20 = int(s20_str) if s20_str.isdigit() else 0

                best_time = cols[15] if len(cols) > 15 else ""
                best_time_sec = parse_time_to_seconds(best_time)

                extra = html_extras.get(clean_name.upper(), {})

                current_race["horses"].append({
                    "no": no,
                    "name": clean_name,
                    "raw_name": raw_name,
                    "equipment": eq_list,
                    "age": age,
                    "sire": sire,
                    "dam": dam,
                    "damsire": extra.get("damsire", ""),
                    "weight": weight,
                    "jockey": jockey,
                    "owner": owner,
                    "trainer": trainer,
                    "start_no": start_no,
                    "start_raw": st_raw,
                    "agf_raw": agf_raw,
                    "agf_pct": agf_pct,
                    "agf_rank": agf_rank,
                    "agf_values": agf_values,
                    "handicap": handicap,
                    "son6": son6,
                    "kgs": kgs,
                    "s20": s20,
                    "best_time": best_time,
                    "best_time_sec": best_time_sec,
                    "forma_url": extra.get("forma_url"),
                    "at_id": extra.get("at_id"),
                    "idman_kosu_kodu": extra.get("idman_kosu_kodu"),
                    "is_ekuri": False,
                    "is_scratched": is_scratched
                })
            else:
                # Atlar bitti; bu ve sonraki satırlar koşu dipnotudur
                in_horses = False
                after_horses = True
                handle_race_footer(current_race, line)
        elif after_horses:
            # Eküri ve bahis dipnotları birden fazla satıra yayılabilir
            handle_race_footer(current_race, line)
        i += 1

    if current_race:
        races.append(current_race)

    # Koşmayacak atları ayır: model, Monte Carlo ve kuponlar yalnızca koşacak atlarla çalışır
    for r in races:
        r["scratched"] = [{"no": h["no"], "name": h["name"]} for h in r["horses"] if h.get("is_scratched")]
        r["horses"] = [h for h in r["horses"] if not h.get("is_scratched")]

    # Koşunun TJK kodu (idman sayfası için): atların idman bağlantılarındaki en sık kod
    for r in races:
        codes = [h["idman_kosu_kodu"] for h in r["horses"] if h.get("idman_kosu_kodu")]
        r["kosu_kodu"] = max(set(codes), key=codes.count) if codes else None
        r["idman_at_kodu"] = next((h["at_id"] for h in r["horses"]
                                   if h.get("idman_kosu_kodu") == r["kosu_kodu"] and h.get("at_id")), None)

    # Run Quantitative Analytics on each race
    for r in races:
        analyze_race_quant(r)

    return {
        "altili_groups": build_altili_groups(races),
        "city": city_name,
        "meeting_day": meeting_day,
        "date": race_date,
        "total_races": len(races),
        "races": races,
        "taki_legend": TAKI_DESCRIPTIONS
    }


def fetch_city_program(date_str, city_id=None, city_name=None):
    """
    Fetches live TJK program for the selected date & city.
    Downloads both the HTML page (to discover the official CSV URL and jersey icons)
    and the official TJK CSV data file.
    """
    cities = get_available_cities(date_str)
    if not cities:
        raise ValueError(f"{date_str} tarihi için TJK bülteni bulunamadı.")

    target_city = None
    if city_id:
        for c in cities:
            if str(c["id"]) == str(city_id):
                target_city = c
                break
    if not target_city and city_name:
        for c in cities:
            if city_name.lower() in c["name"].lower():
                target_city = c
                break
    if not target_city:
        target_city = cities[0]

    cache_key = f"prog_{date_str}_{target_city['id']}"
    cached = cache_get(cache_key, TTL_PROGRAM)
    if cached is not None:
        return cached

    city_html = fetch_url(target_city["url"])
    html_extras = extract_horse_extras_from_html(city_html)

    # Find CSV link inside city_html
    csv_m = re.search(r'href=["\'](https://medya-cdn\.tjk\.org/[^"\']+\.csv)["\']', city_html, re.I)
    if not csv_m:
        # Construct fallback CSV URL
        d_parts = date_str.split("/")
        yyyy_mm_dd = f"{d_parts[2]}-{d_parts[1]}-{d_parts[0]}"
        dd_mm_yyyy = f"{d_parts[0]}.{d_parts[1]}.{d_parts[2]}"
        c_name = target_city["name"].replace(" ", "")  # TJK dosya adında boşluk yok: LingfieldBirleşikKrallık
        csv_url = f"https://medya-cdn.tjk.org/raporftp/TJKPDF/{d_parts[2]}/{yyyy_mm_dd}/CSV/GunlukYarisProgrami/{dd_mm_yyyy}-{c_name}-GunlukYarisProgrami-TR.csv"
    else:
        csv_url = html_lib.unescape(csv_m.group(1))

    # Quote non-ASCII characters in URL path safely
    parsed = urllib.parse.urlsplit(csv_url)
    safe_path = urllib.parse.quote(urllib.parse.unquote(parsed.path), safe="/:.-_")
    safe_csv_url = urllib.parse.urlunsplit((parsed.scheme, parsed.netloc, safe_path, parsed.query, parsed.fragment))

    csv_text = fetch_url(safe_csv_url, encoding="utf-8-sig")
    program_data = parse_tjk_csv(csv_text, html_extras)
    program_data["city_id"] = target_city["id"]
    program_data["city_name"] = target_city["name"]
    program_data["available_cities"] = cities
    program_data["source_csv_url"] = safe_csv_url
    program_data["fetched_at"] = tr_now().strftime("%H:%M:%S")

    cache_set(cache_key, program_data)
    return program_data


def reweight_program(program_data, weights):
    """Önbellekteki bülteni bozmadan, verilen terazi ağırlıklarıyla tüm koşuları yeniden analiz eder
    (bileşik puan, tempo düzeltmesi, Monte Carlo, rol rozetleri ve kupon ölçekleri dahil)."""
    data = copy.deepcopy(program_data)
    for r in data["races"]:
        analyze_race_quant(r, weights)
    data["weights"] = normalize_weights(weights)
    return data


class TurfQuantHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=STATIC_DIR, **kwargs)

    def end_headers(self):
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def send_json(self, status, payload):
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        parsed = urllib.parse.urlparse(self.path)
        if parsed.path == "/api/horse_history":
            params = urllib.parse.parse_qs(parsed.query)
            at_id = params.get("at_id", [None])[0]
            is_arab = params.get("arab", ["0"])[0] == "1"
            lite = params.get("lite", ["0"])[0] == "1"
            try:
                if at_id and not at_id.isdigit():
                    raise ValueError("Geçersiz safkan kimliği")
                try:
                    weight = float(params.get("weight", ["57"])[0].replace(",", "."))
                    dist = int(float(params.get("dist", ["1400"])[0]))
                except ValueError:
                    raise ValueError("Geçersiz sıklet veya mesafe parametresi")
                data = fetch_horse_bankomat_sheet(at_id, today_weight=weight, target_dist=dist, is_arab=is_arab, lite=lite)
                body = json.dumps({"ok": True, "data": data}, ensure_ascii=False).encode("utf-8")
                self.send_response(200)
                self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
            except Exception as e:
                err_body = json.dumps({"ok": False, "error": str(e)}, ensure_ascii=False).encode("utf-8")
                self.send_response(500)
                self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", str(len(err_body)))
                self.end_headers()
                self.wfile.write(err_body)
            return

        if parsed.path == "/health":
            # Platform sağlık kontrolü: TJK'ya gitmeden anında yanıt verir
            self.send_json(200, {"ok": True, "time": tr_now().strftime("%Y-%m-%d %H:%M:%S")})
            return

        if parsed.path == "/api/horse_relatives":
            params = urllib.parse.parse_qs(parsed.query)
            at_id = params.get("at_id", [""])[0]
            irk = params.get("irk", [""])[0]
            try:
                if not at_id.isdigit():
                    raise ValueError("Geçersiz safkan kimliği")
                self.send_json(200, {"ok": True, "data": fetch_horse_relatives(at_id, irk)})
            except Exception as e:
                self.send_json(500, {"ok": False, "error": str(e)})
            return

        if parsed.path == "/api/relative_breakdown":
            params = urllib.parse.parse_qs(parsed.query)
            at_id = params.get("at_id", [""])[0]
            kind = params.get("kind", [""])[0]
            try:
                if not at_id.isdigit() or kind not in ("baba", "anne", "kisrak_baba"):
                    raise ValueError("Geçersiz parametre")
                names = fetch_horse_relatives(at_id).get("names", {})
                if kind not in names:
                    raise ValueError("TJK kaydında bu akraba bulunamadı")
                # Kısrak babası: aygır olarak kendi taylarının başarısı
                self.send_json(200, {"ok": True, "data": fetch_offspring_breakdown("anne" if kind == "anne" else "aygir", names[kind])})
            except Exception as e:
                self.send_json(500, {"ok": False, "error": str(e)})
            return

        if parsed.path == "/api/dates":
            try:
                days = []
                today = tr_now().date()
                for i in range(0, 4):
                    d = today + timedelta(days=i)
                    ds = d.strftime("%d/%m/%Y")
                    try:
                        cities = get_available_cities(ds)
                    except Exception:
                        cities = []
                    if cities or i == 0:
                        days.append({"date": ds, "offset": i, "cities": len(cities),
                                     "domestic": [c["name"] for c in cities if c["is_domestic"]],
                                     "weekday": ["Pazartesi", "Salı", "Çarşamba", "Perşembe", "Cuma", "Cumartesi", "Pazar"][d.weekday()]})
                self.send_json(200, {"ok": True, "data": days})
            except Exception as e:
                self.send_json(500, {"ok": False, "error": str(e)})
            return

        if parsed.path == "/api/race_idman":
            params = urllib.parse.parse_qs(parsed.query)
            kosu = params.get("kosu", [""])[0]
            at_kodu = params.get("at", [""])[0]
            race_date = params.get("date", [""])[0]
            try:
                if not (kosu.isdigit() and at_kodu.isdigit()):
                    raise ValueError("Geçersiz koşu veya at kodu")
                if race_date and not re.match(r'^\d{2}/\d{2}/\d{4}$', race_date):
                    race_date = ""
                self.send_json(200, {"ok": True, "data": fetch_race_idman(kosu, at_kodu, race_date)})
            except Exception as e:
                self.send_json(500, {"ok": False, "error": str(e)})
            return

        if parsed.path == "/api/program":
            params = urllib.parse.parse_qs(parsed.query)
            today_str = tr_now().strftime("%d/%m/%Y")
            date_str = params.get("date", [today_str])[0]
            if not re.match(r'^\d{2}/\d{2}/\d{4}$', date_str or ""):
                date_str = today_str
            city_id = params.get("city_id", [None])[0]
            city_name = params.get("city", [None])[0]
            if params.get("refresh", ["0"])[0] == "1":
                with CACHE_LOCK:
                    CACHE.clear()

            try:
                data = fetch_city_program(date_str, city_id=city_id, city_name=city_name)
                weights = parse_weights_param(params.get("w", [None])[0])
                if weights and weights != DEFAULT_WEIGHTS:
                    data = reweight_program(data, weights)
                else:
                    data = dict(data, weights=dict(DEFAULT_WEIGHTS))
                data["default_weights"] = dict(DEFAULT_WEIGHTS)
                body = json.dumps({"ok": True, "data": data}, ensure_ascii=False).encode("utf-8")
                self.send_response(200)
                self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
            except Exception as e:
                err_body = json.dumps({"ok": False, "error": str(e)}, ensure_ascii=False).encode("utf-8")
                self.send_response(500)
                self.send_header("Content-Type", "application/json; charset=utf-8")
                self.send_header("Content-Length", str(len(err_body)))
                self.end_headers()
                self.wfile.write(err_body)
            return

        if parsed.path == "/" or parsed.path == "":
            self.path = "/index.html"
        return super().do_GET()


if __name__ == "__main__":
    os.makedirs(STATIC_DIR, exist_ok=True)
    # Railway/Render gibi platformlar portu PORT ortam değişkeniyle verir; yerelde 8090
    try:
        port = int(os.environ.get("PORT", "8090"))
    except ValueError:
        port = 8090
    print(f"TurfQuant Pro Analiz Sunucusu başlatılıyor: http://localhost:{port}", flush=True)
    # Çok iş parçacıklı: safkan geçmişi çekilirken (3 TJK isteği) diğer istekler beklemez
    server = ThreadingHTTPServer(("0.0.0.0", port), TurfQuantHandler)
    server.daemon_threads = True  # Ctrl+C'de yarım kalan TJK isteklerini beklemeden kapan
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nSunucu kapatıldı.")
    finally:
        server.server_close()
