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
  • form_avg / last_speed : son 3 koşunun ortalama sırası ve son koşu derecesi. AGF yokken en güçlü
    bilgi (test −2,18 → −2,09); AGF varken katkısı yok, çünkü halk formu zaten fiyatlıyor.
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


def _history_rows(at_id):
    page = S.fetch_cached(f"https://www.tjk.org/TR/YarisSever/Query/ConnectedPage/AtKosuBilgileri?1=1&QueryParameter_AtId={at_id}", S.TTL_HISTORY)
    return page, [S.race_row_to_dict(t) for tb in re.findall(r'<tbody.*?</tbody>', page, re.S) for t in S.parse_horse_race_rows(tb)]


def form_label(positions, n_prev):
    """Son 3 koşunun derecelerinden kısa form yorumu."""
    ps = [p for p in positions if p]
    if not n_prev:
        return "İlk koşu"
    if not ps:
        return "Derecesiz"
    avg = sum(ps) / len(ps)
    if ps[0] <= 3 and avg <= 3.5:
        return "Formda"
    if len(ps) >= 2 and ps[0] < ps[1] and ps[0] <= 5:
        return "Yükselişte"
    if avg <= 5:
        return "Orta"
    return "Durgun"


def horse_prior(at_id, race_date, msf=None, surface=None):
    """Yarıştan önceki koşular: en iyi ve son 1200 m karşılığı derece, son 3 sıra, form, dinlenme;
    msf/surface verilirse bugünkü mesafe ve pistteki en iyi derece ve o derecedeki kilo."""
    _, rows = _history_rows(at_id)
    hist = []
    for r in rows:
        try:
            d = datetime.strptime(r["tarih"], "%d.%m.%Y")
        except ValueError:
            continue
        if d < race_date:
            hist.append((d, r))
    hist.sort(key=lambda x: x[0], reverse=True)
    norm = lambda r: r["sec"] * ((1200.0 / r["msf"]) ** 1.055) if r.get("sec") and r.get("msf") else None
    secs = [(norm(r), r) for _, r in hist if norm(r)]
    best = min(secs, key=lambda x: x[0]) if secs else None
    last_norm = norm(hist[0][1]) if hist else None
    last3 = [r["sira"] for _, r in hist[:3]]
    here = [r for _, r in hist if r.get("sec") and msf and r["msf"] == msf and (not surface or r["p_name"] == surface)]
    best_here = min(here, key=lambda r: r["sec"]) if here else None
    return {
        "best_here_str": best_here["derece_raw"] if best_here else None,
        "best_here_kg": best_here["siklet"] if best_here else None,
        "best_here_from": f"{best_here['sehir']} {best_here['tarih']} • {best_here['sira'] or '-'}." if best_here else None,
        "n_here": len(here),
        "n_prev": len(hist),
        "last_pos": hist[0][1]["sira"] if hist else None,
        "last3": last3,
        "days_since": (race_date - hist[0][0]).days if hist else None,
        "form": form_label(last3, len(hist)),
        "last_norm": round(last_norm, 2) if last_norm else None,
        "last_norm_str": S.format_seconds_to_time_str(last_norm) if last_norm else None,
        "best_norm": round(best[0], 2) if best else None,
        "best_norm_str": S.format_seconds_to_time_str(best[0]) if best else None,
        "best_from": f"{best[1]['msf']}m {best[1]['p_name']} {best[1]['derece_raw']} ({best[1]['tarih']})" if best else None,
    }


# ---------------- AİLE: anne, baba ve dedeler ilk koşularını kazanmış mı? ----------------
def _clean_name(n):
    return S.tr_upper(re.sub(r'\s*\((?:T|[A-Z]{2,3})\)|[%*]', '', n or '')).strip()


def find_horse(name, sex=None, irk="İngiliz"):
    """TJK at aramasından (Atlar) ada göre at kodu ve orijini (baba - anne)."""
    target = _clean_name(S.strip_country(name))
    if not target or target == "-":
        return None
    page = S.fetch_cached("https://www.tjk.org/TR/YarisSever/Query/Page/Atlar?QueryParameter_AtIsmi=" + urllib.parse.quote(target), 7 * 24 * 3600)
    m = re.search(r'<table[^>]*id="queryTable".*?</table>', page, re.S)
    if not m:
        return None
    heads = [S.clean_cell(h) for h in re.findall(r'<th[^>]*>(.*?)</th>', m.group(0), re.S)]
    best = None
    for tr in re.findall(r'<tr[^>]*>(.*?)</tr>', m.group(0), re.S):
        ids = re.findall(r'QueryParameter_AtId=(\d+)', tr)
        v = dict(zip(heads, [S.clean_cell(c).replace("\xa0", " ") for c in re.findall(r'<td[^>]*>(.*?)</td>', tr, re.S)]))
        raw = v.get("At İsmi", "")
        if not ids or "Tayı" in raw or _clean_name(raw) != target:
            continue
        if irk and v.get("Irk") and v["Irk"] != irk:
            continue
        if sex and v.get("Cinsiyet") and v["Cinsiyet"] != sex:
            continue
        orj = [x.strip() for x in v.get("Orijin(Baba-Anne)", "").split(" - ")]
        cand = {"at_id": ids[0], "name": raw.replace("%", "").strip(), "sire": orj[0] if orj else "",
                "dam": orj[1] if len(orj) > 1 else "", "imported": bool(re.search(r'\([A-Z]{2,3}\)', raw))}
        if best is None:
            best = cand
    return best


def horse_debut(at_id):
    """Atın TJK'daki ilk koşusu (en eski koşu). Kariyer 50 koşudan uzunsa ilk yılın sayfasına bakılır."""
    page, rows = _history_rows(at_id)
    years = sorted(set(re.findall(r'QueryParameter_Yil=(\d{4})', page)))
    if years and rows:
        oldest = min(datetime.strptime(r["tarih"], "%d.%m.%Y") for r in rows if re.match(r'\d{2}\.\d{2}\.\d{4}$', r["tarih"]))
        if int(years[0]) < oldest.year:
            p2 = S.fetch_cached(f"https://www.tjk.org/TR/YarisSever/Query/ConnectedPage/AtKosuBilgileri?1=1&QueryParameter_AtId={at_id}&QueryParameter_Yil={years[0]}", S.TTL_HISTORY)
            rows = rows + [S.race_row_to_dict(t) for tb in re.findall(r'<tbody.*?</tbody>', p2, re.S) for t in S.parse_horse_race_rows(tb)]
    dated = [(datetime.strptime(r["tarih"], "%d.%m.%Y"), r) for r in rows if re.match(r'\d{2}\.\d{2}\.\d{4}$', r["tarih"])]
    if not dated:
        return None
    dated.sort(key=lambda x: x[0])
    d, r = dated[0]
    wins = sum(1 for _, x in dated if x["sira"] == 1)
    return {"tarih": r["tarih"], "sira": r["sira"], "sehir": r["sehir"], "kosu": f"{r['msf']}m {r['p_name']}",
            "sinif": r["cls_label"], "won": r["sira"] == 1, "starts": len(dated), "wins": wins}


def relative_debut(name, sex, irk):
    key = f"debut_{_clean_name(name)}_{sex}_{irk}"
    cached = S.cache_get(key, 7 * 24 * 3600)
    if cached is not None:
        return cached
    h = find_horse(name, sex, irk)
    out = {"name": S.strip_country(name), "found": bool(h)}
    if h:
        out.update({"name": h["name"], "sire": h["sire"], "dam": h["dam"], "imported": h["imported"]})
        out["debut"] = horse_debut(h["at_id"])
    S.cache_set(key, out)
    return out


def family_debuts(horses, irk="İngiliz"):
    """Koşudaki her at için baba, baba babası, anne ve anne babasının ilk koşu sonucu."""
    names = {}
    for h in horses:
        if h.get("sire"):
            names[("baba", h["sire"])] = ("Erkek", h["sire"])
        if h.get("dam"):
            names[("anne", h["dam"])] = ("Dişi", h["dam"])

    def load(item):
        try:
            return item[0], relative_debut(item[1][1], item[1][0], irk)
        except Exception:
            return item[0], {"name": item[1][1], "found": False, "error": True}

    with ThreadPoolExecutor(max_workers=4) as pool:
        first = dict(pool.map(load, names.items()))
    # Dedeler: babanın babası ve annenin babası (orijinden)
    grand = {}
    for (kind, _), info in first.items():
        if info.get("sire"):
            grand[("dede", info["sire"])] = ("Erkek", info["sire"])
    with ThreadPoolExecutor(max_workers=4) as pool:
        second = dict(pool.map(load, grand.items()))
    out = []
    for h in horses:
        sire = first.get(("baba", h.get("sire")), {})
        dam = first.get(("anne", h.get("dam")), {})
        members = [("Baba", sire), ("Baba babası", second.get(("dede", sire.get("sire")), {}) if sire.get("sire") else {}),
                   ("Anne", dam), ("Anne babası", second.get(("dede", dam.get("sire")), {}) if dam.get("sire") else {})]
        fam = [dict(info, role=role) for role, info in members if info]
        known = [m for m in fam if m.get("debut")]
        out.append({"no": h["no"], "name": h["name"], "family": fam,
                    "debut_wins": sum(1 for m in known if m["debut"]["won"]), "debut_known": len(known)})
    return out


def race_surface(race):
    s = S.tr_upper(race.get("surface", ""))
    return "Çim" if "ÇİM" in s else "Sentetik" if "SENTET" in s else "Kum"


def analyze_race(race, race_date_str, general=False):
    """At başına form tablosu. Maiden / Şartlı 1 / Şartlı 19'da Maiden modelinin şansı;
    general=True (diğer koşular) ise sitenin olağan şansı: AGF varsa halkın düzeltilmiş şansı, yoksa model."""
    model = load_model()
    race_date = datetime.strptime(race_date_str, "%d/%m/%Y")
    horses = race["horses"]

    def prep(h):
        info = {"no": h["no"], "name": h["name"], "start": h.get("start_no"), "agf_pct": h.get("agf_pct") or 0,
                "agf_rank": h.get("agf_rank"), "sire": S.strip_country(h.get("sire", ""))}
        try:
            info.update(horse_prior(h["at_id"], race_date, race.get("distance_m"), race_surface(race)) if h.get("at_id") else {"n_prev": 0})
        except Exception:
            info.update({"n_prev": None})
        info["sire_stats"] = sire_stats(info["sire"])
        return info

    with ThreadPoolExecutor(max_workers=4) as pool:
        rows = list(pool.map(prep, horses))

    speed = _z([-(r["best_norm"]) if r.get("best_norm") else None for r in rows])
    sire = _z([(r.get("sire_stats") or {}).get("win") for r in rows])
    last_speed = _z([-(r["last_norm"]) if r.get("last_norm") else None for r in rows])

    def avgpos(r):
        ps = [p for p in (r.get("last3") or []) if p]
        return -sum(ps) / len(ps) if ps else None
    form_avg = _z([avgpos(r) for r in rows])
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
            "last_speed": last_speed[i] if last_speed[i] is not None else 0.0,
            "form_avg": form_avg[i] if form_avg[i] is not None else 0.0,
        }

    has_agf = sum(r["agf_pct"] for r in rows) > 10
    if general:
        by_no = {h["no"]: h.get("simulation") or {} for h in horses}
        for r in rows:
            sim = by_no.get(r["no"], {})
            p = sim.get("market_prob") if sim.get("market_prob") is not None else sim.get("win_prob")
            r["maiden_prob"] = round(p or 0, 1)
            r["fair_ganyan"] = round(100 / max(0.5, r["maiden_prob"]), 2)
        rows.sort(key=lambda r: -r["maiden_prob"])
        for rank, r in enumerate(rows):
            r["maiden_rank"] = rank + 1
        return {"race_no": race["race_no"], "has_agf": has_agf, "general": True,
                "class": S.classify_race(race.get("condition", ""))[1], "rows": rows}
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
