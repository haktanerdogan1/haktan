# -*- coding: utf-8 -*-
"""
SÜRPRİZ TERCİHLERİ — Geçmiş veri toplama ve işaret ölçümü (backtest)

Her geçmiş yarış günü için:
  1) O günün resmi bülteni (yarış öncesi bilgi) modelimizden geçirilir,
  2) her safkanın TJK geçmişi YALNIZCA o tarihten önceki koşularla değerlendirilir
     (gelecek bilgisi sızmaz),
  3) sonuç CSV'sinden bitiş sırası ve kapanış ganyanı eşleştirilir.

Kullanım:
  python backtest.py collect --days 60      # veriyi topla (data/records/YYYY-MM-DD.jsonl.gz)
  python backtest.py analyze                # işaretleri ölç, static/data/surprise_stats.json üret

Yalnızca Python standart kütüphanesi kullanır.
"""
import argparse
import gzip
import json
import os
import re
import sys
import time
import urllib.parse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timedelta

import server as S

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DATA_DIR = os.path.join(BASE_DIR, "data")
RECORDS_DIR = os.path.join(DATA_DIR, "records")          # arşiv: gün başına bir dosya, hiç yeniden yazılmaz
RECORDS_PATH = os.path.join(DATA_DIR, "backtest_records.jsonl.gz")  # eski tek dosya (taşıma için)
STATS_PATH = os.path.join(BASE_DIR, "static", "data", "surprise_stats.json")
RAW_CACHE_DIR = os.environ.get("BACKTEST_CACHE", os.path.join(BASE_DIR, ".backtest_cache"))

# İşaretlerin tanımları (hem ölçümde hem canlı puanlamada aynı kurallar kullanılır)
SIGNAL_LABELS = {
    "model_value": "Model şansı halkın 1,5 katı",
    "hp_light": "Puanına göre 2 kg+ hafif",
    "apprentice": "Apranti jokey",
    "class_drop": "Sınıf düşüşü (son koşusu daha zor)",
    "surface_fit": "Bugünkü pistte İlk 3 ≥ %40 (3+ koşu)",
    "distance_fit": "Bu mesafe grubunda İlk 3 ≥ %40 (3+ koşu)",
    "season_fit": "Bu mevsimde İlk 3 diğerlerinden 15+ puan iyi",
    "recent_top3": "Son koşusunda ilk 3",
    "improving": "Form grafiği yükselişte",
    "lone_speed": "Koşunun tek kaçağı",
    "first_blinkers": "İlk kez kapalı gözlük (KG)",
    "fresh": "Dinlenmiş (14–45 gün)",
    "long_layoff": "Uzun ara (120+ gün)",
    "season_weak": "Bu mevsimde zayıf",
}
TAKI_TOKENS = sorted(["SGKR", "SKG", "GKR", "KG", "DB", "SK", "YP", "ÖG", "BB", "ÇB", "K"], key=len, reverse=True)


# ------------------------------------------------------------------ yardımcılar
def log(msg):
    print(time.strftime("%H:%M:%S"), msg, flush=True)


def raw_cache_get(url):
    path = os.path.join(RAW_CACHE_DIR, urllib.parse.quote(url, safe="")[:240])
    if os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            return f.read()
    return None


def raw_cache_put(url, text):
    os.makedirs(RAW_CACHE_DIR, exist_ok=True)
    path = os.path.join(RAW_CACHE_DIR, urllib.parse.quote(url, safe="")[:240])
    with open(path, "w", encoding="utf-8") as f:
        f.write(text)


def fetch_disk(url, encoding="utf-8"):
    hit = raw_cache_get(url)
    if hit is not None:
        return hit
    text = S.fetch_url(url, encoding=encoding)
    raw_cache_put(url, text)
    return text


def safe_csv_url(url):
    p = urllib.parse.urlsplit(url)
    return urllib.parse.urlunsplit((p.scheme, p.netloc, urllib.parse.quote(urllib.parse.unquote(p.path), safe="/:.-_"), "", ""))


def norm_name(raw):
    name = S.SCRATCH_RE.sub(" ", raw or "").strip()
    name, _ = S.split_horse_name_and_equipment(name)
    return re.sub(r"\s+", " ", name).upper()


def taki_tokens(taki):
    out, t = [], (taki or "").upper().replace(" ", "")
    while t:
        tok = next((k for k in TAKI_TOKENS if t.startswith(k)), None)
        if not tok:
            t = t[1:]
            continue
        out.append(tok)
        t = t[len(tok):]
    return out


def parse_float(text):
    m = re.search(r"\d+(?:[\.,]\d+)?", text or "")
    return float(m.group(0).replace(",", ".")) if m else None


# ------------------------------------------------------------------ sonuçlar
def results_cities(date_str):
    url = f"https://www.tjk.org/TR/YarisSever/Info/Page/GunlukYarisSonuclari?QueryParameter_Tarih={urllib.parse.quote(date_str, safe='')}"
    page = fetch_disk(url)
    out = []
    for href, sid, name in re.findall(r'href="(/TR/YarisSever/Info/Sehir/GunlukYarisSonuclari\?SehirId=(\d+)[^"]*SehirAdi=([^"&]+)[^"]*)"', page):
        nm = urllib.parse.unquote(S.html_lib.unescape(name)).strip()
        if S.tr_upper(nm) in {"ADANA", "ANKARA", "ANTALYA", "BURSA", "DİYARBAKIR", "ELAZIĞ", "İSTANBUL", "İZMİR", "KOCAELİ", "ŞANLIURFA"}:
            out.append({"id": sid, "name": nm, "url": "https://www.tjk.org" + S.html_lib.unescape(href)})
    seen, uniq = set(), []
    for c in out:
        if c["id"] not in seen:
            seen.add(c["id"])
            uniq.append(c)
    return uniq


def parse_results_csv(text):
    """Sonuç CSV'si: satırlar bitiş sırasında; 'Ganyan' sütunu her atın kapanış ganyanı."""
    races, cur, header = {}, None, None
    for line in text.splitlines():
        line = line.strip()
        m = re.match(r"^(\d+)\.\s*Ko[sş]u\s*:?\s*(.*)$", line, re.I)
        if m:
            cur = int(m.group(1))
            races[cur] = []
            header = None
            continue
        if cur is None:
            continue
        if line.startswith("At No;"):
            header = [h.strip() for h in line.split(";")]
            continue
        cols = [c.strip() for c in line.split(";")]
        if header and cols and cols[0].isdigit() and len(cols) >= len(header) - 1:
            rec = dict(zip(header, cols))
            raw = rec.get("At İsmi", "")
            der = rec.get("Derece", "")
            races[cur].append({
                "pos": int(cols[0]),
                "name": norm_name(raw),
                "scratched": "koşmaz" in (raw + der).lower(),
                "finished": bool(S.parse_time_to_seconds(der)) or der.lower() == "derecesiz",
                "derece": der,
                "ganyan": parse_float(rec.get("Ganyan", "")),
                "agf_final": parse_float(rec.get("AGF", "")),
            })
    return races


# ------------------------------------------------------------------ atın geçmişi (tarihten önce)
def horse_history_before(at_id, race_date):
    url = f"https://www.tjk.org/TR/YarisSever/Query/ConnectedPage/AtKosuBilgileri?1=1&QueryParameter_AtId={at_id}"
    page = fetch_disk(url)
    rows = [S.race_row_to_dict(t) for tb in re.findall(r"<tbody.*?</tbody>", page, re.S) for t in S.parse_horse_race_rows(tb)]
    out = []
    for r in rows:
        try:
            d = datetime.strptime(r["tarih"], "%d.%m.%Y")
        except ValueError:
            continue
        if d < race_date:
            r["_date"] = d
            out.append(r)
    out.sort(key=lambda r: r["_date"], reverse=True)
    return out


def season_of(month):
    return next(n for n, ms in S.SEASONS if month in ms)


def bucket_of(msf):
    return next((n for n, lo, hi in S.DIST_BUCKETS if lo <= msf <= hi), None)


def history_features(hist, race, horse, race_date):
    today_surf = "Çim" if "Çim" in race["surface"] else ("Sentetik" if "Sentetik" in race["surface"] else "Kum")
    f = {"h_starts": len(hist)}
    if not hist:
        return f
    def top3(rs):
        return round(sum(1 for r in rs if r["sira"] and r["sira"] <= 3) / len(rs) * 100, 1) if rs else None
    f["h_top3"] = top3(hist)
    f["h_wins"] = sum(1 for r in hist if r["sira"] == 1)
    last = hist[0]
    f["last_pos"] = last["sira"]
    f["days_since"] = (race_date - last["_date"]).days
    f["last_zorluk"] = S.compute_zorluk_degree(last["kcins"], last["hp"])
    f["today_zorluk"] = S.compute_zorluk_degree(race.get("condition", ""), horse.get("handicap", 0))
    sr = [r for r in hist if r["p_name"] == today_surf]
    f["surf_starts"], f["surf_top3"] = len(sr), top3(sr)
    bk = bucket_of(race["distance_m"])
    dr = [r for r in hist if r["msf"] and bucket_of(r["msf"]) == bk]
    f["dist_starts"], f["dist_top3"] = len(dr), top3(dr)
    cur_season = season_of(race_date.month)
    se = [r for r in hist if season_of(r["_date"].month) == cur_season]
    ot = [r for r in hist if season_of(r["_date"].month) != cur_season]
    f["season_starts"], f["season_top3"] = len(se), top3(se)
    f["other_season_starts"], f["other_season_top3"] = len(ot), top3(ot)
    f["last_taki"] = taki_tokens(last["taki"])
    f["ever_kg"] = any("KG" in taki_tokens(r["taki"]) for r in hist)
    return f


# ------------------------------------------------------------------ işaretler (ölçümde ve canlıda ortak)
def compute_signals(rec):
    """Bir koşucu kaydından (yarış öncesi bilgiler) işaret kümesi."""
    s = set()
    agf = rec.get("agf_pct") or 0
    model_p = rec.get("win_prob_raw", rec.get("win_prob", 0))  # canlıda ham şans ayrı alanda
    if agf > 0 and model_p >= 1.5 * agf:
        s.add("model_value")
    if (rec.get("hp_resid") or 0) <= -2:
        s.add("hp_light")
    if rec.get("apprentice"):
        s.add("apprentice")
    if rec.get("last_zorluk") is not None and rec.get("today_zorluk") is not None and rec["last_zorluk"] - rec["today_zorluk"] >= 2:
        s.add("class_drop")
    if (rec.get("surf_starts") or 0) >= 3 and (rec.get("surf_top3") or 0) >= 40:
        s.add("surface_fit")
    if (rec.get("dist_starts") or 0) >= 3 and (rec.get("dist_top3") or 0) >= 40:
        s.add("distance_fit")
    if (rec.get("season_starts") or 0) >= 3 and (rec.get("other_season_starts") or 0) >= 4:
        diff = (rec.get("season_top3") or 0) - (rec.get("other_season_top3") or 0)
        if diff >= 15:
            s.add("season_fit")
        elif diff <= -20:
            s.add("season_weak")
    if rec.get("last_pos") and rec["last_pos"] <= 3:
        s.add("recent_top3")
    if rec.get("trend") == "Yükselişte":
        s.add("improving")
    if rec.get("style") == "E" and rec.get("pace") == "LONE_SPEED":
        s.add("lone_speed")
    if "KG" in (rec.get("equipment") or []) and rec.get("h_starts") and not rec.get("ever_kg"):
        s.add("first_blinkers")
    ds = rec.get("days_since")
    if ds is not None and 14 <= ds <= 45:
        s.add("fresh")
    if ds is not None and ds >= 120:
        s.add("long_layoff")
    return s


def is_overlooked(rec):
    """Halkın ihmal ettiği at: AGF sıralamasında 4. ve gerisi (ya da AGF %8 altı)."""
    return (rec.get("agf_rank") or 0) >= 4 or 0 < (rec.get("agf_pct") or 0) < 8


# ------------------------------------------------------------------ toplama
class IncompleteDay(Exception):
    """Bir hipodromun verisi alınamadı: gün eksik kaydedilmesin, sonraki çalıştırmada yeniden denensin."""


def collect_day(date_str):
    race_date = datetime.strptime(date_str, "%d/%m/%Y")
    records = []
    failed = []
    for city in results_cities(date_str):
        try:
            res_page = fetch_disk(city["url"])
            m = re.search(r'https://medya-cdn\.tjk\.org/[^"\']+GunlukYarisSonuclari[^"\']+\.csv', res_page)
            if not m:
                continue
            results = parse_results_csv(fetch_disk(safe_csv_url(S.html_lib.unescape(m.group(0))), encoding="utf-8-sig"))
            prog = S.fetch_city_program(date_str, city_id=city["id"], with_jockeys=False)
        except Exception as e:
            log(f"  ! {date_str} {city['name']}: {e}")
            failed.append(city["name"])
            continue
        jobs = []
        for race in prog["races"]:
            res = {r["name"]: r for r in results.get(race["race_no"], [])}
            winners = [r for r in res.values() if r["pos"] == 1 and not r["scratched"]]
            if not res or not winners:
                continue
            for h in race["horses"]:
                r = res.get(h["name"].upper())
                if not r or r["scratched"]:
                    continue
                hw = h.get("hp_weight") or {}
                rec = {
                    "date": date_str, "city": city["name"], "race_no": race["race_no"],
                    "condition": race.get("condition", ""), "cls": S.classify_race(race.get("condition", ""))[0],
                    "surface": race["surface"], "distance": race["distance_m"], "field": len(race["horses"]),
                    "no": h["no"], "name": h["name"], "at_id": h.get("at_id"),
                    "agf_pct": h["agf_pct"], "agf_rank": h["agf_rank"],
                    # Ölçümde hep HAM model şansı kullanılır (kalibrasyon bu veriden öğrenilir);
                    # uygulamada gösterilen yumuşatılmış şans ayrıca saklanır.
                    "win_prob": h["simulation"].get("raw_win_prob", h["simulation"]["win_prob"]),
                    "win_prob_cal": h["simulation"]["win_prob"], "model_rank": h.get("model_rank"),
                    "quant": h["quant_score"], "style": h["running_style"]["code"],
                    "pace": race["pace_scenario"]["code"], "trend": h["form_analysis"]["trend"],
                    "hp": h.get("handicap"), "hp_resid": hw.get("residual"), "eff_hp": hw.get("eff_hp"),
                    "weight": h["weight"], "apprentice": h.get("jockey", "").upper().endswith(" AP"),
                    # Not: geçmiş günler için jokeyin 'bu yıl' istatistiği o günden sonraki koşuları da içerir
                    "jockey": h.get("jockey"), "jokey_id": h.get("jokey_id"),
                    "jockey_win_adj": (h.get("jockey_stats") or {}).get("win_adj"),
                    "kgs": h.get("kgs"), "equipment": h.get("equipment", []),
                    "pos": r["pos"], "won": r["pos"] == 1, "ganyan": r["ganyan"], "agf_final": r["agf_final"],
                }
                jobs.append((rec, race, h))

        def enrich(item):
            rec, race, h = item
            if h.get("at_id"):
                try:
                    rec.update(history_features(horse_history_before(h["at_id"], race_date), race, h, race_date))
                except Exception:
                    rec["hist_error"] = True
            return rec

        with ThreadPoolExecutor(max_workers=4) as pool:
            records.extend(pool.map(enrich, jobs))
        log(f"  {date_str} {city['name']}: {len(jobs)} koşucu")
    if failed:
        raise IncompleteDay(f"{date_str}: alınamayan hipodrom(lar): {', '.join(failed)}")
    return records


def day_path(date_str):
    d = datetime.strptime(date_str, "%d/%m/%Y")
    return os.path.join(RECORDS_DIR, d.strftime("%Y-%m-%d") + ".jsonl.gz")


def write_day(date_str, records):
    os.makedirs(RECORDS_DIR, exist_ok=True)
    tmp = day_path(date_str) + ".tmp"
    with gzip.open(tmp, "wt", encoding="utf-8") as f:
        for r in records:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
    os.replace(tmp, day_path(date_str))


def migrate_single_file():
    """Eski tek dosyalık arşivi gün başına dosyalara böler."""
    if not os.path.exists(RECORDS_PATH):
        return
    by_day = {}
    with gzip.open(RECORDS_PATH, "rt", encoding="utf-8") as f:
        for line in f:
            r = json.loads(line)
            by_day.setdefault(r["date"], []).append(r)
    for d, recs in by_day.items():
        if not os.path.exists(day_path(d)):
            write_day(d, recs)
    os.remove(RECORDS_PATH)
    log(f"{len(by_day)} gün gün başına dosyalara taşındı")


def load_records():
    migrate_single_file()
    recs = []
    if os.path.isdir(RECORDS_DIR):
        for name in sorted(os.listdir(RECORDS_DIR)):
            if name.endswith(".jsonl.gz"):
                with gzip.open(os.path.join(RECORDS_DIR, name), "rt", encoding="utf-8") as f:
                    recs.extend(json.loads(l) for l in f)
    return recs


def collect(days, end=None):
    """Son `days` günü toplar; arşivde olan günleri atlar (yarış olmayan günler boş dosya olarak işaretlenir)."""
    migrate_single_file()
    end_date = datetime.strptime(end, "%d/%m/%Y") if end else S.tr_now().replace(tzinfo=None) - timedelta(days=1)
    added = 0
    for i in range(days):
        d = (end_date - timedelta(days=i)).strftime("%d/%m/%Y")
        if os.path.exists(day_path(d)):
            continue
        try:
            recs = collect_day(d)
        except Exception as e:
            log(f"! {d}: {e}")
            continue
        write_day(d, recs)
        added += len(recs)
        log(f"{d} tamam • {len(recs)} kayıt")
    log(f"bitti • yeni kayıt {added}")
    return added


# ------------------------------------------------------------------ piyasa ve kalibrasyon
def _races(recs):
    import collections
    races = collections.defaultdict(list)
    for r in recs:
        races[(r["date"], r["city"], r["race_no"])].append(r)
    return {k: v for k, v in races.items() if sum(x["won"] for x in v) == 1}


def market_probs(race, agf_exp, model_exp=0.0):
    """Benter tarzı birleşim: p ∝ AGF^b × model^a (AGF yoksa yalnız model^a)."""
    import math
    has_agf = sum(x["agf_pct"] for x in race) > 10
    w = []
    for x in race:
        lp = model_exp * math.log(max(0.3, x["win_prob"]) / 100)
        if has_agf:
            lp += agf_exp * math.log(max(0.3, x["agf_pct"]) / 100)
        w.append(math.exp(lp))
    s = sum(w)
    return [v / s for v in w]


def mean_ll(races, agf_exp, model_exp):
    import math
    return sum(math.log(market_probs(r, agf_exp, model_exp)[[x["won"] for x in r].index(True)]) for r in races) / len(races)


def market_analysis(recs, split):
    races = _races(recs)
    tr = {k: v for k, v in races.items() if datetime.strptime(k[0], "%d/%m/%Y") < split}
    te = {k: v for k, v in races.items() if datetime.strptime(k[0], "%d/%m/%Y") >= split}
    agf_tr = [v for v in tr.values() if sum(x["agf_pct"] for x in v) > 10]
    agf_te = [v for v in te.values() if sum(x["agf_pct"] for x in v) > 10]
    noagf_tr = list(tr.values())
    grid = [x / 10 for x in range(0, 16)]
    # AGF'li koşular: AGF ve model üsleri
    best = max(((mean_ll(agf_tr, b, a), a, b) for a in grid for b in grid))
    _, model_w, agf_w = best
    # Model tek başına (AGF yokken kullanılacak) sıcaklık üssü
    temp = max(((mean_ll([[dict(x, agf_pct=0) for x in r] for r in noagf_tr], 0, a), a) for a in grid if a > 0))[1]
    ll = {
        "model_only": round(mean_ll(agf_te, 0, 1.0), 4),
        "model_calibrated": round(mean_ll(agf_te, 0, temp), 4),
        "agf_only": round(mean_ll(agf_te, 1.0, 0), 4),
        "combined": round(mean_ll(agf_te, agf_w, model_w), 4),
    }

    def ev_rule(rs, thr, minp):
        bets = [(x, p) for r in rs for x, p in zip(r, market_probs(r, agf_w, model_w)) if p >= minp and p * (x["ganyan"] or 0) >= thr]
        return roi_block([x for x, _ in bets])

    ev = [{"threshold": thr, "train": ev_rule(agf_tr, thr, 0.05), "test": ev_rule(agf_te, thr, 0.05)} for thr in (1.0, 1.1, 1.2, 1.3, 1.5)]
    eligible = [e for e in ev if e["train"]["n"] >= 80 and e["train"]["roi_pct"] is not None]  # az kazananlı eşiğe aşırı uymamak için
    ev_thr = max(eligible, key=lambda e: e["train"]["roi_pct"])["threshold"] if eligible else 1.2
    return {
        "model_weight": model_w, "agf_weight": agf_w, "model_temperature": temp,
        "log_likelihood_test": ll, "races_with_agf": {"train": len(agf_tr), "test": len(agf_te)},
        "ev_rule": ev, "ev_threshold": ev_thr, "ev_min_prob": 0.05,
    }


# ------------------------------------------------------------------ ölçüm
def roi_block(recs):
    n = len(recs)
    wins = [r for r in recs if r["won"]]
    ret = sum(r["ganyan"] or 0 for r in wins)
    return {
        "n": n, "wins": len(wins),
        "win_pct": round(len(wins) / n * 100, 1) if n else 0,
        "avg_win_ganyan": round(ret / len(wins), 2) if wins else None,
        "roi_pct": round((ret - n) / n * 100, 1) if n else None,
    }


def analyze():
    recs = [r for r in load_records() if r.get("ganyan")]
    for r in recs:
        r["_sig"] = sorted(compute_signals(r))
    dates = sorted({datetime.strptime(r["date"], "%d/%m/%Y") for r in recs})
    split = dates[int(len(dates) * 0.67)] if dates else None
    train = [r for r in recs if datetime.strptime(r["date"], "%d/%m/%Y") < split]
    test = [r for r in recs if datetime.strptime(r["date"], "%d/%m/%Y") >= split]
    over_all, over_train, over_test = ([r for r in g if is_overlooked(r)] for g in (recs, train, test))

    signals = {}
    for key, label in SIGNAL_LABELS.items():
        signals[key] = {
            "label": label,
            "all": roi_block([r for r in over_all if key in r["_sig"]]),
            "train": roi_block([r for r in over_train if key in r["_sig"]]),
            "test": roi_block([r for r in over_test if key in r["_sig"]]),
        }

    # Ağırlıklar: eğitim döneminde, ihmal edilmiş atlar arasında kazanma oranının tabana oranı (shrinkage ile)
    base_train = roi_block(over_train)["win_pct"] or 1
    weights = {}
    for key, s in signals.items():
        b = s["train"]
        prior = 30.0  # küçük örneklemde ağırlığı 1'e çeker
        lift = ((b["wins"] + prior * base_train / 100) / (b["n"] + prior) * 100) / base_train if b["n"] else 1.0
        weights[key] = round(max(-1.5, min(2.0, (lift - 1.0) * 3)), 2)

    def score(r):
        return round(sum(weights.get(k, 0) for k in r["_sig"]), 2)

    # Skor eşiğine göre test dönemi performansı
    thresholds = []
    for th in (0.5, 1.0, 1.5, 2.0, 2.5, 3.0):
        thresholds.append({"threshold": th,
                           "train": roi_block([r for r in over_train if score(r) >= th]),
                           "test": roi_block([r for r in over_test if score(r) >= th])})

    # Önerilen eşik: eğitim döneminde en az 40 at içeren eşikler arasında getirisi en iyi olan
    # (test dönemi seçimde kullanılmaz; yalnızca raporlanır)
    eligible = [t for t in thresholds if t["train"]["n"] >= 40 and t["train"]["roi_pct"] is not None]
    recommended = max(eligible, key=lambda t: t["train"]["roi_pct"])["threshold"] if eligible else 1.0

    # Model kalibrasyonu
    calib = []
    for lo, hi in [(0, 5), (5, 10), (10, 20), (20, 35), (35, 101)]:
        b = [r for r in recs if lo <= r["win_prob"] < hi]
        calib.append({"bucket": f"%{lo}–{hi if hi <= 100 else 100}", "n": len(b),
                      "model_avg": round(sum(r["win_prob"] for r in b) / len(b), 1) if b else None,
                      "actual": round(sum(1 for r in b if r["won"]) / len(b) * 100, 1) if b else None})

    # En yüksek ganyanlı kazananlar ve taşıdıkları işaretler
    big = sorted([r for r in recs if r["won"] and (r["ganyan"] or 0) >= 10], key=lambda r: -r["ganyan"])[:25]
    stats = {
        "generated_at": S.tr_now().strftime("%d/%m/%Y %H:%M"),
        "period": {"from": dates[0].strftime("%d/%m/%Y"), "to": dates[-1].strftime("%d/%m/%Y"),
                   "test_from": split.strftime("%d/%m/%Y"), "days": len(dates)} if dates else {},
        "counts": {"runners": len(recs), "races": len({(r["date"], r["city"], r["race_no"]) for r in recs}),
                   "overlooked": len(over_all)},
        "baseline": {"all_runners": roi_block(recs), "overlooked": roi_block(over_all),
                     "overlooked_train": roi_block(over_train), "overlooked_test": roi_block(over_test)},
        "signals": signals, "weights": weights, "thresholds": thresholds, "calibration": calib,
        "recommended_threshold": recommended,
        "market": market_analysis(recs, split),
        "big_winners": [{"date": r["date"], "city": r["city"], "race_no": r["race_no"], "name": r["name"],
                         "ganyan": r["ganyan"], "agf_rank": r["agf_rank"], "signals": r["_sig"], "score": score(r)} for r in big],
        "labels": SIGNAL_LABELS,
    }
    os.makedirs(os.path.dirname(STATS_PATH), exist_ok=True)
    with open(STATS_PATH, "w", encoding="utf-8") as f:
        json.dump(stats, f, ensure_ascii=False, indent=1)
    return stats


if __name__ == "__main__":
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("collect")
    c.add_argument("--days", type=int, default=60)
    c.add_argument("--end", help="GG/AA/YYYY (varsayılan: dün)")
    sub.add_parser("analyze")
    r = sub.add_parser("recollect", help="Bir günü arşivde yeniden topla (GG/AA/YYYY)")
    r.add_argument("date")
    a = ap.parse_args()
    if a.cmd == "collect":
        collect(a.days, a.end)
    elif a.cmd == "recollect":
        write_day(a.date, collect_day(a.date))
        log(f"{a.date} yeniden toplandı")
    else:
        st = analyze()
        print(json.dumps({"period": st["period"], "counts": st["counts"], "baseline": st["baseline"]}, ensure_ascii=False, indent=1))
