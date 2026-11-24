# -*- coding: utf-8 -*-
"""
KOŞU SİMÜLASYONU — her koşu 500 kez koşturulur.

1) Derece modeli (atın kendi geçmişi): son koşulardaki dereceler bugünkü mesafeye çevrilir
   (t × (D / msf)^1.055), o koşudaki kilo ile bugünkü kilonun farkı düzeltilir
   (1 kg ≈ 1000 m'de 0,125 sn) ve farklı pistteki dereceler pist katsayısıyla çevrilir.
   Atın beklenen derecesi en iyi dereceleri ağırlıklı ortalaması, sapması tutarlılığıdır.
2) Her turda her ata beklenen derecesi etrafında rastgele bir derece çekilir; koşunun tamamına
   ortak bir sapma (pist / tempo) eklenir. Dereceye göre sıralanınca bitiş sırası çıkar.
3) Uyum: turlarda atların kazanma sıklığı, sitenin en iyi tahminine (AGF varsa halkın
   düzeltilmiş şansı, Maiden koşularında Maiden modeli, yoksa model) oturtulur. Simülasyon şansı
   değiştirmez; ona gerçekçi derece, bitiş sırası ve fark ekler.
"""
import math
import random
import statistics
from datetime import datetime

KG_SEC_PER_M = 0.000125          # 1 kg ≈ 1000 m'de 0,125 sn
DIST_EXP = 1.055
# Pistler arası kaba katsayı (Kum derecesine göre): çim ve sentetik daha hızlı
SURF_FACTOR = {"Kum": 1.0, "Çim": 0.975, "Sentetik": 0.985}
RACE_SD = 0.6                    # tüm koşuya ortak sapma (pist / tempo), sn
# Atın en iyi dereceleri iyimserdir: arşivdeki 226 koşuda kazanan derecesini tutturan düzeltme
# (km başına +1,25 sn; düzeltmeyle kazanan derecesi ortalama hatası ≈0, tipik sapma ±2 sn)
TIME_OFFSET_PER_KM = 1.25
MIN_SD = 0.5
N_SIMS = 500


def norm_time(r, dist, surface, kg_today):
    """Geçmiş bir koşunun derecesini bugünkü mesafe, pist ve kiloya çevirir."""
    if not r.get("sec") or not r.get("msf"):
        return None
    t = r["sec"] * ((dist / r["msf"]) ** DIST_EXP)
    if r.get("p_name") != surface:
        t = t / SURF_FACTOR.get(r.get("p_name"), 1.0) * SURF_FACTOR.get(surface, 1.0)
    if kg_today and r.get("siklet"):
        t += (kg_today - r["siklet"]) * KG_SEC_PER_M * dist
    return t


def time_profile(hist, dist, surface, kg_today, race_date=None):
    """Atın bugünkü koşu için beklenen derecesi (sn), sapması ve dayanağı.
    hist: koşu sözlükleri (en yeni önce olması gerekmez)."""
    rows = []
    for r in hist:
        try:
            d = datetime.strptime(r["tarih"], "%d.%m.%Y")
        except (ValueError, KeyError):
            continue
        if race_date and d >= race_date:
            continue
        rows.append((d, r))
    rows.sort(key=lambda x: x[0], reverse=True)
    same, other = [], []
    for d, r in rows[:15]:
        # Mesafe çok farklıysa çeviri güvenilmez: ±%35 dışını alma
        if not r.get("msf") or abs(r["msf"] - dist) / dist > 0.35:
            continue
        t = norm_time(r, dist, surface, kg_today)
        if t is None:
            continue
        (same if r.get("p_name") == surface else other).append(t)
    pool = same if len(same) >= 2 else same + other
    if not pool:
        return None
    pool = sorted(pool)[:max(2, min(5, len(pool)))]  # en iyi derecelerin ortalaması
    mu = sum(pool) / len(pool) + TIME_OFFSET_PER_KM * dist / 1000.0
    sd = statistics.pstdev(pool) if len(pool) > 1 else 1.2
    return {"mu": mu, "sd": max(MIN_SD, min(2.5, sd)), "n": len(pool),
            "basis": "same" if len(same) >= 2 else ("mixed" if same else "other")}


def simulate(entries, n=N_SIMS, seed=7, iters=6, samples=25):
    """
    entries: [{"no", "mu", "sd", "target"}]  (target: hedef kazanma olasılığı, toplamı 1)
    Dönen: at başına kazanma/ilk 3 oranı, ortalama sıra, sıra dağılımı, derece yüzdelikleri.
    """
    rng = random.Random(seed)
    k = len(entries)
    # Ortak rastgele sayılar: uyum adımları aynı turlar üzerinde yapılır
    zs = [[rng.gauss(0, 1) for _ in range(k)] for _ in range(n)]
    shared = [rng.gauss(0, RACE_SD) for _ in range(n)]
    shift = [0.0] * k

    def run():
        wins = [0] * k
        for s in range(n):
            best, bi = None, 0
            for i, e in enumerate(entries):
                t = e["mu"] + shift[i] + e["sd"] * zs[s][i]
                if best is None or t < best:
                    best, bi = t, i
            wins[bi] += 1
        return wins

    # Kazanma sıklığını hedefe oturt (yinelemeli kaydırma)
    for _ in range(iters):
        wins = run()
        for i, e in enumerate(entries):
            got = (wins[i] + 0.5) / (n + 0.5 * k)
            tgt = max(0.002, e["target"])
            shift[i] -= 0.45 * e["sd"] * math.log(tgt / got)

    pos_counts = [[0] * k for _ in range(k)]
    times = [[] for _ in range(k)]
    win_times = []
    margins = [[] for _ in range(k)]
    sample_runs = []
    # Sürpriz: şans sıralamasında ilk 3 favori dışındaki bir atın birinci gelmesi
    favs = set(sorted(range(k), key=lambda i: -entries[i]["target"])[:3])
    surprise_runs = []
    surprise_wins = 0
    surprise_win_times = [[] for _ in range(k)]
    long_top3 = 0  # ilk 3'ünde en az bir favori dışı at olan koşu
    for s in range(n):
        ts = [entries[i]["mu"] + shift[i] + entries[i]["sd"] * zs[s][i] + shared[s] for i in range(k)]
        order = sorted(range(k), key=lambda i: ts[i])
        for p, i in enumerate(order):
            pos_counts[i][p] += 1
            times[i].append(ts[i])
            margins[i].append(ts[i] - ts[order[0]])
        win_times.append(ts[order[0]])
        run = [{"no": entries[i]["no"], "t": ts[i], "gap": ts[i] - ts[order[0]]} for i in order]
        if s < samples:
            sample_runs.append(run)
        if order[0] not in favs:
            surprise_wins += 1
            surprise_win_times[order[0]].append(ts[order[0]])
            if len(surprise_runs) < samples:
                surprise_runs.append(run)
        if any(i not in favs for i in order[:3]):
            long_top3 += 1

    def pct(xs, q):
        xs = sorted(xs)
        return xs[min(len(xs) - 1, int(q * len(xs)))]

    out = []
    for i, e in enumerate(entries):
        pc = pos_counts[i]
        out.append({
            "no": e["no"],
            "win": round(pc[0] / n * 100, 1),
            "top3": round(sum(pc[:3]) / n * 100, 1),
            "avg_pos": round(sum((p + 1) * c for p, c in enumerate(pc)) / n, 2),
            "pos_dist": [round(c / n * 100, 1) for c in pc],
            "t_med": pct(times[i], 0.5), "t_lo": pct(times[i], 0.1), "t_hi": pct(times[i], 0.9),
            "margin_med": pct(margins[i], 0.5),
        })
    surprises = []
    for i, e in enumerate(entries):
        if i in favs:
            continue
        w, t3 = pos_counts[i][0], sum(pos_counts[i][:3])
        if t3 == 0:
            continue
        surprises.append({"no": e["no"], "wins": w, "top3": t3, "win_pct": round(w / n * 100, 1),
                          "top3_pct": round(t3 / n * 100, 1),
                          "eq_ganyan": round(n / w, 1) if w else None,
                          "t_win": pct(surprise_win_times[i], 0.5) if surprise_win_times[i] else None})
    surprises.sort(key=lambda x: (-x["wins"], -x["top3"]))
    return {"rows": out, "win_time_med": pct(win_times, 0.5), "win_time_lo": pct(win_times, 0.1),
            "win_time_hi": pct(win_times, 0.9), "n": n, "samples": sample_runs,
            "favorites": [entries[i]["no"] for i in sorted(favs, key=lambda i: -entries[i]["target"])],
            "surprise_wins": surprise_wins, "surprise_top3_runs": long_top3,
            "surprises": surprises, "surprise_samples": surprise_runs}


def fmt(sec):
    """Saniyeyi TJK derece biçimine çevirir: 85.27 -> 1.25.27"""
    if sec is None:
        return None
    m = int(sec // 60)
    rest = sec - m * 60
    return f"{m}.{rest:05.2f}" if m else f"{rest:.2f}"


BOY_SEC = 0.2  # 1 boy ≈ 0,2 sn


def simulate_race(race, race_date_str, targets, history_of, n=N_SIMS):
    """race: bülten koşusu; targets: {no: kazanma olasılığı %}; history_of(at_id) -> koşu listesi."""
    race_date = datetime.strptime(race_date_str, "%d/%m/%Y")
    dist = race.get("distance_m") or 1400
    su = (race.get("surface") or "").upper()
    surface = "Çim" if "ÇİM" in su or "ÇIM" in su else "Sentetik" if "SENTET" in su else "Kum"
    profiles = {}
    for h in race["horses"]:
        try:
            hist = history_of(h["at_id"]) if h.get("at_id") else []
        except Exception:
            hist = []
        profiles[h["no"]] = time_profile(hist, dist, surface, h.get("weight"), race_date)
    known = [p["mu"] for p in profiles.values() if p]
    base = statistics.median(known) if known else dist / 1000.0 * 62
    tot = sum(max(0.1, targets.get(h["no"], 0)) for h in race["horses"]) or 1
    entries = []
    for h in race["horses"]:
        p = profiles[h["no"]]
        entries.append({"no": h["no"], "mu": p["mu"] if p else base + 1.0, "sd": p["sd"] if p else 1.5,
                        "target": max(0.1, targets.get(h["no"], 0)) / tot})
    res = simulate(entries, n=n)
    names = {h["no"]: h for h in race["horses"]}
    rows = []
    for e in res["rows"]:
        h = names[e["no"]]
        p = profiles[e["no"]]
        rows.append({
            "no": e["no"], "name": h["name"], "jockey": h.get("jockey"), "weight": h.get("weight"),
            "win": e["win"], "top3": e["top3"], "avg_pos": e["avg_pos"], "pos_dist": e["pos_dist"],
            "t_sec": round(e["t_med"], 2), "time": fmt(e["t_med"]), "time_lo": fmt(e["t_lo"]), "time_hi": fmt(e["t_hi"]),
            "gap_sec": round(e["margin_med"], 2), "gap_boy": round(e["margin_med"] / BOY_SEC, 1),
            "basis": p["basis"] if p else "none", "target": round(targets.get(e["no"], 0), 1),
        })
    rows.sort(key=lambda r: (r["avg_pos"], -r["win"]))
    for i, r in enumerate(rows):
        r["sim_rank"] = i + 1
    def pack(runs):
        return [[{"no": x["no"], "name": names[x["no"]]["name"], "time": fmt(x["t"]), "gap_boy": round(x["gap"] / BOY_SEC, 1)}
                 for x in run] for run in runs]
    samples = pack(res["samples"])
    surprises = [dict(x, name=names[x["no"]]["name"], jockey=names[x["no"]].get("jockey"),
                      t_win=fmt(x["t_win"])) for x in res["surprises"]]
    return {"race_no": race["race_no"], "n": res["n"], "distance": dist, "surface": surface,
            "record": race.get("record_time"), "win_time": fmt(res["win_time_med"]),
            "win_time_lo": fmt(res["win_time_lo"]), "win_time_hi": fmt(res["win_time_hi"]),
            "rows": rows, "samples": samples,
            "favorites": res["favorites"], "surprise_wins": res["surprise_wins"],
            "surprise_top3_runs": res["surprise_top3_runs"], "surprises": surprises,
            "surprise_samples": pack(res["surprise_samples"])}
