# -*- coding: utf-8 -*-
"""
MAIDEN ANALİZİ — Maiden, Şartlı 1 ve Şartlı 19 koşuları (geçmişi az atlar)

Geçmiş veri çalışması (20.09–19.11.2026, 297 koşu; öğrenme 192, test 105) bu koşularda halkın
tahminine (AGF) şu bilgilerin anlamlı katkı yaptığını gösterdi (öğrenme ve testte aynı yönde):
  • speed      : önceki koşulardaki en iyi derecenin 1200 m karşılığı (koşu içinde standartlaştırılmış)
  • no_time    : önceki derecesi yok
  • first_time : ilk kez koşuyor (halk bu atlara fazla oynuyor)
  • sire_win   : babanın taylarının birincilik oranı (koşu içinde standartlaştırılmış; en güçlü bilgi)
  • draw       : start numarası (iç kulvar küçük avantaj)
  • last_23    : son koşusunu 2. ya da 3. bitirdi (halk biraz abartıyor)
Test isabet skoru (ortalama log-olabilirlik): mevcut model −2,21 • yalnız AGF −1,93 • AGF+bilgi −1,89.
Ağırlıklar static/data/maiden_model.json dosyasındadır.
"""
import json
import math
import os
import re
import statistics
import urllib.parse
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime

import server as S

MAIDEN_CLASSES = {"maiden", "sartli1", "sartli19"}
MODEL_PATH = os.path.join(S.STATIC_DIR, "data", "maiden_model.json")


def is_maiden_race(race):
    return S.classify_race(race.get("condition", ""))[0] in MAIDEN_CLASSES


def load_model():
    cached = S.cache_get("maiden_model", 600)
    if cached is not None:
        return cached
    try:
        with open(MODEL_PATH, encoding="utf-8") as f:
            model = json.load(f)
    except (OSError, ValueError):
        model = {}
    S.cache_set("maiden_model", model)
    return model


def _z(vals):
    xs = [v for v in vals if v is not None]
    if len(xs) < 2:
        return [0.0 if v is not None else None for v in vals]
    m, sd = statistics.mean(xs), statistics.pstdev(xs) or 1.0
    return [(v - m) / sd if v is not None else None for v in vals]


def sire_stats(name):
    """Babanın taylarının TJK istatistiği (en az 30 koşu)."""
    if not name:
        return None
    url = f"https://www.tjk.org/TR/YarisSever/Query/Page/AygirIstatistikleri?QueryParameter_AygirAdi={urllib.parse.quote(name)}"
    try:
        blk = S.parse_query_table(S.fetch_cached(url, 24 * 3600), name)
    except Exception:
        return None
    if not blk or blk["starts"] < 30:
        return None
    return {"win": blk["win_pct"], "top3": blk["top3_pct"], "runners": blk["runners"], "starts": blk["starts"]}


def horse_prior(at_id, race_date):
    """Yarıştan önceki koşular: en iyi 1200 m karşılığı derece, koşu sayısı, son derece."""
    page = S.fetch_cached(f"https://www.tjk.org/TR/YarisSever/Query/ConnectedPage/AtKosuBilgileri?1=1&QueryParameter_AtId={at_id}", S.TTL_HISTORY)
    rows = [S.race_row_to_dict(t) for tb in re.findall(r'<tbody.*?</tbody>', page, re.S) for t in S.parse_horse_race_rows(tb)]
    hist = []
    for r in rows:
        try:
            d = datetime.strptime(r["tarih"], "%d.%m.%Y")
        except ValueError:
            continue
        if d < race_date:
            hist.append((d, r))
    hist.sort(key=lambda x: x[0], reverse=True)
    secs = [(r["sec"] * ((1200.0 / r["msf"]) ** 1.055), r) for _, r in hist if r.get("sec") and r.get("msf")]
    best = min(secs, key=lambda x: x[0]) if secs else None
    return {
        "n_prev": len(hist),
        "last_pos": hist[0][1]["sira"] if hist else None,
        "best_norm": round(best[0], 2) if best else None,
        "best_norm_str": S.format_seconds_to_time_str(best[0]) if best else None,
        "best_from": f"{best[1]['msf']}m {best[1]['p_name']} {best[1]['derece_raw']} ({best[1]['tarih']})" if best else None,
    }


def analyze_race(race, race_date_str):
    """Bir maiden tipi koşu için at başına özellikler ve Maiden şansı."""
    model = load_model()
    race_date = datetime.strptime(race_date_str, "%d/%m/%Y")
    horses = race["horses"]

    def prep(h):
        info = {"no": h["no"], "name": h["name"], "start": h.get("start_no"), "agf_pct": h.get("agf_pct") or 0,
                "agf_rank": h.get("agf_rank"), "sire": S.strip_country(h.get("sire", ""))}
        try:
            info.update(horse_prior(h["at_id"], race_date) if h.get("at_id") else {"n_prev": 0})
        except Exception:
            info.update({"n_prev": None})
        info["sire_stats"] = sire_stats(info["sire"])
        return info

    with ThreadPoolExecutor(max_workers=4) as pool:
        rows = list(pool.map(prep, horses))

    speed = _z([-(r["best_norm"]) if r.get("best_norm") else None for r in rows])
    sire = _z([(r.get("sire_stats") or {}).get("win") for r in rows])
    timed = sorted([r for r in rows if r.get("best_norm")], key=lambda r: r["best_norm"])
    for rank, r in enumerate(timed):
        r["speed_rank"] = rank + 1
    for i, r in enumerate(rows):
        r["features"] = {
            "speed": speed[i] if speed[i] is not None else 0.0,
            "no_time": 1.0 if speed[i] is None else 0.0,
            "first_time": 1.0 if not r.get("n_prev") else 0.0,
            "sire_win": sire[i] if sire[i] is not None else 0.0,
            "draw": -(r.get("start") or 6) / 10.0,
            "last_23": 1.0 if r.get("last_pos") in (2, 3) else 0.0,
        }

    has_agf = sum(r["agf_pct"] for r in rows) > 10
    weights = (model.get("with_agf") if has_agf else model.get("without_agf")) or {}
    utils = []
    for r in rows:
        u = sum(weights.get(k, 0.0) * v for k, v in r["features"].items())
        if has_agf:
            u += weights.get("agf", 0.85) * math.log(max(0.3, r["agf_pct"]) / 100)
        utils.append(u)
    mx = max(utils) if utils else 0
    ex = [math.exp(u - mx) for u in utils]
    tot = sum(ex) or 1
    for r, e in zip(rows, ex):
        r["maiden_prob"] = round(e / tot * 100, 1)
        r["fair_ganyan"] = round(100 / max(0.5, r["maiden_prob"]), 2)
    rows.sort(key=lambda r: -r["maiden_prob"])
    for rank, r in enumerate(rows):
        r["maiden_rank"] = rank + 1
    return {"race_no": race["race_no"], "has_agf": has_agf, "class": S.classify_race(race.get("condition", ""))[1],
            "rows": rows, "model_info": {k: model.get(k) for k in ("test", "races")}}
