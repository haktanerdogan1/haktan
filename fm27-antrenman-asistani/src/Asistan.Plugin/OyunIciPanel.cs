#if IMGUI_PANEL
using Asistan.Core;
using Asistan.Core.Analiz;
using Asistan.Core.Arayuz;
using Asistan.Core.Planlama;
using Asistan.Core.Uygulama;
using UnityEngine;

namespace Asistan.Plugin;

/// <summary>
/// FM27'nin üzerine çizilen panel (Unity IMGUI). Kısayol tuşuyla açılır/kapanır, sürüklenebilir. Uzun işlemler arka planda
/// çalışır; panel her karede yalnızca hazır sonuçları okur, oyunu dondurmaz. Ayrıntılı ekranlar için tarayıcı paneli açılabilir.
/// </summary>
public sealed class OyunIciPanel : MonoBehaviour
{
    public OyunIciPanel(IntPtr ptr) : base(ptr) { }

    private static readonly string[] Sekmeler = { "Özet", "Program", "Oyuncular", "Uygula" };

    private bool _acik;
    private Rect _pencere = new(60, 60, 560, 700);
    private Vector2 _kaydirma;
    private int _sekme;
    private bool _onayBekliyor;
    private bool _surukleniyor;
    private Vector2 _surukleBaslangic;
    private readonly HashSet<string> _secim = new();
    private KeyCode _tus = KeyCode.F8;
    private bool _stilHazir;
    private float _icerikYuksekligi = 600;
    private GUIStyle? _baslik, _metin, _soluk, _kalin, _zemin, _kart;
    private Texture2D? _zeminDoku, _kartDoku;

    private static AntrenmanAsistani? A => Eklenti.Asistan;

    private void Start()
    {
        if (Enum.TryParse<KeyCode>(Eklenti.Kisayol, true, out var tus))
        {
            _tus = tus;
        }
    }

    private void OnGUI()
    {
        var olay = Event.current;

        // Kısayol IMGUI olayından okunur: oyun yeni giriş sistemini kullansa da çalışır.
        if (olay != null && olay.type == EventType.KeyDown && olay.keyCode == _tus)
        {
            _acik = !_acik;
            olay.Use();
        }

        if (A is null)
        {
            return;
        }

        Stiller();

        if (!_acik)
        {
            if (GUI.Button(new Rect(Screen.width - 150, Screen.height - 34, 140, 26), $"Antrenman ({_tus})"))
            {
                _acik = true;
            }

            return;
        }

        if (olay != null)
        {
            Surukle(olay);
        }

        GUI.Box(_pencere, "", _zemin);

        var x = _pencere.x + 12;
        var w = _pencere.width - 24;
        var y = _pencere.y + 10;

        GUI.Label(new Rect(x, y, w - 30, 24), "FM27 Antrenman Asistanı", _baslik);

        if (GUI.Button(new Rect(_pencere.xMax - 34, y, 24, 22), "×"))
        {
            _acik = false;
        }

        y += 26;
        var bilgi = A.Anlik is { } an ? $"{an.Kimlik.TakimAdi} · {(an.Taktik.VarMi ? an.Taktik.Deger!.Ad : "taktik okunamadı")}" : "Oyundan henüz okunmadı.";
        GUI.Label(new Rect(x, y, w, 18), bilgi, _soluk);
        y += 20;
        y += Yaz(x, y, w, (A.Mesgul ? "(çalışıyor) " : "") + A.Durum, _metin) + 4;

        if (A.PlanUyarisi is { } pu)
        {
            y += Yaz(x, y, w, "Dikkat: " + pu, _metin) + 4;
        }

        GUI.enabled = !A.Mesgul;

        if (GUI.Button(new Rect(x, y, 200, 26), "Oyundan oku ve analiz et"))
        {
            _secim.Clear();
            _onayBekliyor = false;
            Arkaplan(() => A.AnalizEtAsync());
        }

        GUI.enabled = true;

        if (Eklenti.Sunucu is { } s && GUI.Button(new Rect(x + 208, y, 200, 26), "Ayrıntılı paneli aç (tarayıcı)"))
        {
            Application.OpenURL(s.Adres);
        }

        y += 32;

        var sekmeG = w / Sekmeler.Length;

        for (var i = 0; i < Sekmeler.Length; i++)
        {
            var ad = Sekmeler[i] + (i == 3 && _secim.Count > 0 ? $" ({_secim.Count})" : "");

            if (GUI.Toggle(new Rect(x + i * sekmeG, y, sekmeG - 4, 24), _sekme == i, ad, GUI.skin.button) && _sekme != i)
            {
                _sekme = i;
                _kaydirma = Vector2.zero;
                _onayBekliyor = false;
            }
        }

        y += 30;

        var alan = new Rect(x, y, w, _pencere.yMax - y - 10);
        var icerik = new Rect(0, 0, w - 20, Math.Max(_icerikYuksekligi, alan.height));
        _kaydirma = GUI.BeginScrollView(alan, _kaydirma, icerik);
        var h = _sekme switch
        {
            0 => Ozet(w - 24),
            1 => Program(w - 24),
            2 => Oyuncular(w - 24),
            _ => Uygula(w - 24)
        };
        GUI.EndScrollView();

        // İçerik yüksekliği bir sonraki karede kaydırma alanının boyu olur.
        _icerikYuksekligi = h + 20;
    }

    // ---------------- Sekmeler ----------------

    private float Ozet(float w)
    {
        var y = 0f;

        if (A!.Analiz is not { } analiz)
        {
            return Yaz(0, y, w, "Oyun açıkken ve MCPFM çalışırken \"Oyundan oku ve analiz et\" düğmesine basın. Uygulama siz onaylamadan oyunda hiçbir şeyi değiştirmez.", _metin);
        }

        var (acik, neden) = A.YazmaIzni;

        if (!acik)
        {
            y += Yaz(0, y, w, "Oyuna yazma kapalı: " + neden, _soluk) + 6;
        }

        foreach (var not in A.Anlik!.Uyarilar.Concat(analiz.GenelNotlar).Concat(A.Plan?.Notlar ?? Array.Empty<string>()))
        {
            y += Yaz(0, y, w, "• " + not, _soluk) + 2;
        }

        y += 6;

        foreach (var k in analiz.Kartlar)
        {
            var renk = k.Oncelik switch { Oncelik.Yuksek => "#e5534b", Oncelik.Orta => "#e0a43a", Oncelik.Dusuk => "#6aa0e8", Oncelik.Yok => "#3fb950", _ => "#8a96a3" };
            var baslik = $"<b>{k.AlanAdi}</b>  <color={renk}>{Gorunum.OncelikAdi(k.Oncelik)}</color>  <color=#93a1b2>{k.Anlayis}/100 · güven %{k.Guven * 100:0}</color>";

            if (k.Oncelik is Oncelik.Yok)
            {
                y += Yaz(0, y, w, baslik, _metin) + 4;
                continue;
            }

            var govde = $"{baslik}\n{k.Gozlem}\n<color=#93a1b2>{k.IlgiliTalimat}</color>\n→ {k.Oneri}";
            var yuk = _kart!.CalcHeight(new GUIContent(govde), w);
            GUI.Box(new Rect(0, y, w, yuk + 4), "", _kart);
            GUI.Label(new Rect(6, y + 2, w - 12, yuk), govde, _metin);
            y += yuk + 10;
        }

        return y;
    }

    private float Program(float w)
    {
        var y = 0f;

        if (A!.Plan is not { } plan)
        {
            return Yaz(0, y, w, "Önce analiz edin.", _metin);
        }

        y += Yaz(0, y, w, "İşaretlediğiniz değişiklikler \"Uygula\" sekmesine eklenir. Yük değerleri tahminidir.", _soluk) + 6;

        foreach (var hafta in plan.Haftalar)
        {
            y += Yaz(0, y, w, $"<b>{hafta.No}. hafta</b> · {Tip(hafta.Tip)} · tahmini yük {hafta.TahminiYuk}/{hafta.YukSiniri}", _metin) + 4;

            foreach (var gun in plan.Gunler.Where(g => (g.Tarih.DayNumber - hafta.Baslangic.DayNumber) is >= 0 and < 7))
            {
                var mac = gun.Mac is null ? "" : $" <color=#a78bfa>MAÇ: {gun.Mac}</color>";
                y += Yaz(0, y, w, $"<b>{gun.Tarih:ddd dd.MM}</b>{mac}  <color=#93a1b2>{gun.Baglam}</color>", _metin) + 2;

                foreach (var hucre in gun.Hucreler)
                {
                    var anahtar = "seans:" + hucre.Anahtar;

                    if (hucre.Durum == HucreDurumu.Degisiyor)
                    {
                        var metin = $"{hucre.Slot + 1}. {hucre.Mevcut?.Ad ?? "—"} → {hucre.Onerilen?.Ad}";
                        Isaret(new Rect(12, y, w - 12, 20), anahtar, metin);
                        y += 20;
                        y += Yaz(32, y, w - 32, hucre.Gerekce, _soluk) + 2;
                    }
                    else
                    {
                        var etiket = hucre.Durum switch { HucreDurumu.Mac => "maç", HucreDurumu.Kilitli => "kilitli", HucreDurumu.Duzenlenemez => "düzenlenemez", HucreDurumu.Korundu => "korundu", _ => "aynı" };
                        y += Yaz(32, y, w - 32, $"<color=#93a1b2>{hucre.Slot + 1}. {hucre.Mevcut?.Ad ?? "—"} ({etiket})</color>", _metin);
                    }
                }

                y += 4;
            }
        }

        return y;
    }

    private float Oyuncular(float w)
    {
        var y = 0f;

        if (A!.Bireysel.Count == 0)
        {
            return Yaz(0, y, w, "Önce analiz edin.", _metin);
        }

        foreach (var o in A.Bireysel)
        {
            var p = o.Oyuncu;
            var kon = p.Kondisyon.VarMi ? $"%{p.Kondisyon.Deger}" : "kondisyon okunamadı";
            y += Yaz(0, y, w, $"<b>{p.Ad}</b> <color=#93a1b2>{p.RolAdi ?? p.RolAnahtari} · {kon} · {Gorunum.KararAdi(o.Karar)}</color>", _metin) + 2;

            if (o.OdakDegisiyor)
            {
                Isaret(new Rect(12, y, w - 12, 20), $"odak:{p.Id}", $"Ek odak: {p.Odak?.Ad ?? "Yok"} → {o.OnerilenOdak!.Ad}");
                y += 20;
            }

            if (o.YogunlukDegisiyor)
            {
                Isaret(new Rect(12, y, w - 12, 20), $"yogunluk:{p.Id}", $"Yoğunluk: {(p.YogunlukOtomatik ? "Otomatik" : p.Yogunluk?.Ad)} → {o.OnerilenYogunluk!.Ad}");
                y += 20;
            }

            var neden = string.Join(" ", o.YogunlukGerekcesi.Concat(o.OdakGerekcesi).Concat(o.Uyarilar.Select(u => "Dikkat: " + u)));
            y += Yaz(12, y, w - 12, neden, _soluk) + 8;
        }

        return y;
    }

    private float Uygula(float w)
    {
        var y = 0f;
        var tum = A!.Degisiklikler();
        _secim.IntersectWith(tum.Select(d => d.Anahtar));
        var (acik, neden) = A.YazmaIzni;

        y += Yaz(0, y, w, $"{tum.Count} önerilen değişiklik, {_secim.Count} seçili. Yalnızca seçilenler gönderilir; her biri oyunun ön kontrolünden geçer ve oyundan geri okunur.", _metin) + 6;

        if (GUI.Button(new Rect(0, y, 140, 24), "Tümünü seç"))
        {
            foreach (var d in tum) _secim.Add(d.Anahtar);
        }

        if (GUI.Button(new Rect(148, y, 140, 24), "Seçimi temizle"))
        {
            _secim.Clear();
            _onayBekliyor = false;
        }

        y += 30;

        if (!acik)
        {
            y += Yaz(0, y, w, "Yazma kapalı: " + neden, _soluk) + 6;
        }

        GUI.enabled = acik && _secim.Count > 0 && !A.Mesgul;

        if (!_onayBekliyor)
        {
            if (GUI.Button(new Rect(0, y, 300, 28), $"Seçilen {_secim.Count} değişikliği uygula"))
            {
                _onayBekliyor = true;
            }
        }
        else
        {
            if (GUI.Button(new Rect(0, y, 220, 28), "Onayla: oyuna gönder"))
            {
                var secilen = _secim.ToList();
                _onayBekliyor = false;
                _secim.Clear();
                Arkaplan(() => A.UygulaAsync(secilen));
            }

            GUI.enabled = true;

            if (GUI.Button(new Rect(228, y, 100, 28), "Vazgeç"))
            {
                _onayBekliyor = false;
            }
        }

        GUI.enabled = true;
        y += 36;

        foreach (var d in tum)
        {
            Isaret(new Rect(0, y, w, 20), d.Anahtar, $"{d.Baslik}: {d.Eski?.Ad ?? "—"} → {d.Hedef.Ad}");
            y += 20;
        }

        if (A.SonIslem is { } r)
        {
            y += 10;
            y += Yaz(0, y, w, $"<b>Son işlem ({r.TakimAdi})</b>\n{r.Ozet}", _metin) + 4;

            foreach (var n in r.Notlar)
            {
                y += Yaz(0, y, w, "• " + n, _soluk);
            }

            foreach (var k in r.Kalemler)
            {
                var renk = k.Durum is KalemDurumu.Dogrulandi or KalemDurumu.ZatenUygulanmis ? "#3fb950" : k.Durum is KalemDurumu.Basarisiz or KalemDurumu.Belirsiz ? "#e5534b" : "#e0a43a";
                y += Yaz(0, y, w, $"<color={renk}>{Gorunum.KalemAdi(k.Durum)}</color> {k.Degisiklik.Baslik}: {k.Degisiklik.Hedef.Ad}" + (k.Hata is null ? "" : $" <color=#93a1b2>({k.Hata})</color>"), _metin);
            }

            if (!r.GeriAlma && r.Kalemler.Any(k => k.Durum == KalemDurumu.Dogrulandi))
            {
                y += 6;
                GUI.enabled = !A.Mesgul;

                if (GUI.Button(new Rect(0, y, 220, 26), "Bu işlemi geri al"))
                {
                    var id = r.Id;
                    Arkaplan(() => A.GeriAlAsync(id));
                }

                GUI.enabled = true;
                y += 30;
            }
        }

        return y;
    }

    // ---------------- Yardımcılar ----------------

    private void Isaret(Rect r, string anahtar, string metin)
    {
        var secili = _secim.Contains(anahtar);
        var yeni = GUI.Toggle(r, secili, " " + metin);

        if (yeni != secili)
        {
            if (yeni) _secim.Add(anahtar);
            else _secim.Remove(anahtar);
            _onayBekliyor = false;
        }
    }

    private float Yaz(float x, float y, float w, string metin, GUIStyle? stil)
    {
        var h = stil!.CalcHeight(new GUIContent(metin), w);
        GUI.Label(new Rect(x, y, w, h), metin, stil);
        return h;
    }

    private static string Tip(HaftaTipi t) => t switch { HaftaTipi.CiftMacli => "çift maçlı", HaftaTipi.TekMacli => "tek maçlı", _ => "maçsız" };

    private static void Arkaplan(Func<Task> is_)
    {
        Task.Run(async () =>
        {
            try
            {
                await is_().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Eklenti.Logla?.Invoke("Antrenman Asistanı işlem hatası: " + e);
            }
        });
    }

    private void Surukle(Event olay)
    {
        var tutamak = new Rect(_pencere.x, _pencere.y, _pencere.width - 40, 30);

        if (olay.type == EventType.MouseDown && tutamak.Contains(olay.mousePosition))
        {
            _surukleniyor = true;
            _surukleBaslangic = olay.mousePosition - new Vector2(_pencere.x, _pencere.y);
            olay.Use();
        }
        else if (olay.type == EventType.MouseDrag && _surukleniyor)
        {
            _pencere.x = Mathf.Clamp(olay.mousePosition.x - _surukleBaslangic.x, 0, Screen.width - 100);
            _pencere.y = Mathf.Clamp(olay.mousePosition.y - _surukleBaslangic.y, 0, Screen.height - 60);
            olay.Use();
        }
        else if (olay.type == EventType.MouseUp)
        {
            _surukleniyor = false;
        }

        _pencere.height = Mathf.Min(_pencere.height, Screen.height - _pencere.y - 10);
    }

    private void Stiller()
    {
        if (_stilHazir && _zeminDoku != null)
        {
            return;
        }

        _zeminDoku = Doku(new Color(0.07f, 0.09f, 0.11f, 0.97f));
        _kartDoku = Doku(new Color(0.13f, 0.16f, 0.20f, 1f));

        _zemin = new GUIStyle(GUI.skin.box);
        _zemin.normal.background = _zeminDoku;
        _kart = new GUIStyle(GUI.skin.box);
        _kart.normal.background = _kartDoku;
        _kart.wordWrap = true;
        _kart.richText = true;

        _metin = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true, fontSize = 13 };
        _metin.normal.textColor = new Color(0.9f, 0.92f, 0.95f);
        _soluk = new GUIStyle(_metin) { fontSize = 12 };
        _soluk.normal.textColor = new Color(0.58f, 0.63f, 0.70f);
        _kalin = new GUIStyle(_metin) { fontStyle = FontStyle.Bold };
        _baslik = new GUIStyle(_metin) { fontSize = 16, fontStyle = FontStyle.Bold };
        _baslik.normal.textColor = new Color(0.17f, 0.70f, 0.64f);
        _stilHazir = true;
    }

    private static Texture2D Doku(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }
}
#endif
