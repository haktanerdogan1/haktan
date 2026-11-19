/**
 * VELİEFENDİ & PADOK — Elit Yarış ve Derece Bülteni İstemcisi
 * Klasik Kulüp / Yarış Gazetesi Estetiği (Sıcak Fildişi, Yarış Yeşili, Bordo ve Kehribar)
 */

let STATE = {
  program: null,
  currentCityId: null,
  currentRaceIndex: 0,
  activeTab: 'race',
  sortMode: 'quant', // 'quant' | 'agf' | 'no'
  altiliSelections: {},
  altiliGroupIdx: 0,
  date: null,      // null = bugün; 'GG/AA/YYYY' = TJK'nın yayınladığı ileri tarihli bülten
  dates: [],
  extras: {},      // at_id -> safkan karnesi (hafif): 1-5 yüzdeleri, sınıf karnesi, HP seyri
  idman: {},       // kosu_kodu -> koşunun idman (galop/sprint/kenter) tablosu
  pending: {},     // devam eden istekler (aynı veri iki kez istenmesin)
  weights: null // null = sunucu varsayılanı; sunucu tek doğruluk kaynağıdır
};

const WEIGHT_KEYS = ['speed', 'form', 'class', 'jockey', 'weight'];
const WEIGHT_INPUTS = { speed: 'wSpeed', form: 'wForm', class: 'wClass', jockey: 'wJockey', weight: 'wWeight' };
const FALLBACK_DEFAULT_WEIGHTS = { speed: 25, form: 24, class: 18, jockey: 14, weight: 19 };

try {
  const saved = JSON.parse(localStorage.getItem('padok_weights') || 'null');
  if (saved && WEIGHT_KEYS.every(k => typeof saved[k] === 'number')) STATE.weights = saved;
} catch (e) { /* tarayıcı depolaması kapalı olabilir */ }

let LOADING_MARKUP = null;

// TJK'dan gelen tüm metinler şablonlara HTML olarak girer. Veriyi yüklendiği anda
// tek noktada kaçışlıyoruz; böylece hiçbir şablonda unutulma riski kalmaz.
const HTML_ESC = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
function esc(str) {
  return String(str).replace(/[&<>"']/g, ch => HTML_ESC[ch]);
}
function deepEscape(value) {
  if (typeof value === 'string') return esc(value);
  if (Array.isArray(value)) return value.map(deepEscape);
  if (value && typeof value === 'object') {
    const out = {};
    Object.keys(value).forEach(k => { out[esc(k)] = deepEscape(value[k]); });
    return out;
  }
  return value;
}
let HORSE_MODAL_TOKEN = 0;

document.addEventListener('DOMContentLoaded', () => {
  LOADING_MARKUP = document.getElementById('loadingOverlay').innerHTML;
  // Alttan açılan pencereler: karartılmış alana dokununca kapanır
  ['horseModal', 'weightsModal'].forEach(id => {
    const el = document.getElementById(id);
    el.addEventListener('click', e => { if (e.target === el) el.classList.add('hidden'); });
  });
  document.addEventListener('keydown', e => {
    if (e.key === 'Escape') { closeHorseModal(); closeWeightsModal(); }
  });
  loadProgram();

  // PWA: servis çalışanı yalnızca güvenli bağlamda (localhost veya HTTPS) çalışır
  if ('serviceWorker' in navigator && window.isSecureContext) {
    navigator.serviceWorker.register('/sw.js').catch(() => { /* PWA desteklenmiyor */ });
  }
});

async function loadProgram(cityId = null, forceRefresh = false, opts = {}) {
  const loadingEl = document.getElementById('loadingOverlay');
  const mainEl = document.getElementById('mainContent');
  if (LOADING_MARKUP !== null) loadingEl.innerHTML = LOADING_MARKUP;
  loadingEl.classList.remove('hidden');
  mainEl.classList.add('hidden');

  try {
    let url = `/api/program?refresh=${forceRefresh ? '1' : '0'}`;
    if (STATE.date) url += `&date=${encodeURIComponent(STATE.date)}`;
    if (cityId) url += `&city_id=${encodeURIComponent(cityId)}`;
    if (STATE.weights) url += `&w=${WEIGHT_KEYS.map(k => STATE.weights[k]).join(',')}`;

    const res = await fetch(url);
    let json;
    try {
      json = await res.json();
    } catch (e) {
      throw new Error(`Sunucu yanıtı okunamadı (HTTP ${res.status})`);
    }
    if (!json.ok) throw new Error(json.error || 'Bülten verisi alınamadı');

    const sameCity = String(STATE.currentCityId) === String(json.data.city_id);
    STATE.program = deepEscape(json.data);
    STATE.currentCityId = json.data.city_id;
    if (!(opts.keepRace && sameCity) || STATE.currentRaceIndex >= json.data.races.length) {
      STATE.currentRaceIndex = 0;
    }
    if (!(opts.keepRace && sameCity) || STATE.altiliGroupIdx >= (json.data.altili_groups || []).length) {
      STATE.altiliGroupIdx = 0;
    }
    syncWeightSliders(json.data.weights);

    renderHeaderAndTabs();
    loadDates();
    renderCurrentRace();
    renderValueRadar();
    generateAltiliTicket();
    populateXraySelectors();

    loadingEl.classList.add('hidden');
    mainEl.classList.remove('hidden');
  } catch (err) {
    loadingEl.innerHTML = `
      <div class="p-6 rounded-xl bg-white border border-[#7A1C1C]/40 text-center max-w-xs shadow-md">
        <div class="text-2xl mb-2">⚠️</div>
        <h3 class="serif-title text-base font-bold text-[#7A1C1C] mb-1">Bülten Bağlantı Hatası</h3>
        <p class="text-xs text-[#57534E] mb-4">${esc(err.message)}</p>
        <button onclick="loadProgram(STATE.currentCityId, true, { keepRace: true })" class="px-4 py-2 rounded-lg bg-[#0F291E] text-[#F9F6F0] text-xs font-bold">Tekrar Dene</button>
      </div>
    `;
  }
}

function refreshProgram(force) {
  if (force) {
    // ↻: sunucu önbelleği temizlenir; tarayıcıdaki karne/idman kopyaları da tazelensin
    STATE.extras = {};
    STATE.idman = {};
    STATE.dates = [];  // gece yarısından sonra "Bugün" yeni güne geçsin
  }
  loadProgram(STATE.currentCityId, force, { keepRace: true });
}

// ---------------- GÜN SEÇİCİ ----------------
function loadDates() {
  if (STATE.dates.length) return renderDateTabs();
  fetchJSON('/api/dates').then(days => { STATE.dates = days; renderDateTabs(); }).catch(() => {});
}

function renderDateTabs() {
  const el = document.getElementById('dateTabs');
  if (!el || !STATE.dates.length) return;
  const current = STATE.date || (STATE.dates[0] && STATE.dates[0].date);
  el.innerHTML = STATE.dates.map(d => {
    const label = d.offset === 0 ? 'Bugün' : d.offset === 1 ? 'Yarın' : d.weekday;
    const active = d.date === current;
    const dom = d.domestic.length ? d.domestic.join(', ') : 'Yalnız yabancı';
    return `
      <button onclick="selectDate('${d.date}')" title="${dom}"
        class="px-2.5 py-1 rounded-md whitespace-nowrap flex items-center gap-1.5 text-[11px] font-bold ${
          active ? 'bg-[#F6F3EC] text-[#0F291E]' : 'bg-[#091912]/60 text-[#B7C9BE] border border-white/10'
        }">
        <span>${label}</span><span class="mono text-[10px] opacity-80">${d.date.slice(0, 5)}</span>
        <span class="text-[9px] opacity-70">${d.cities} hip.</span>
      </button>`;
  }).join('');
}

function selectDate(date) {
  const first = STATE.dates[0] && STATE.dates[0].date;
  STATE.date = date === first ? null : date;
  STATE.altiliGroupIdx = 0;
  renderDateTabs();
  // Aynı hipodrom o gün de varsa onu aç, yoksa ilk hipodrom
  loadProgram(STATE.currentCityId, false);
}

function renderHeaderAndTabs() {
  const p = STATE.program;
  document.getElementById('headerMeta').innerHTML = `${p.city_name} ${p.meeting_day} • ${p.date} • Saat: ${p.fetched_at}`;

  // City Pills
  const cityContainer = document.getElementById('cityTabs');
  cityContainer.innerHTML = p.available_cities.map(c => {
    const active = String(c.id) === String(p.city_id);
    return `
      <button onclick="loadProgram('${c.id}', false)"
        class="px-3 py-1 rounded-md text-xs font-bold whitespace-nowrap transition-all flex items-center gap-1.5 ${
          active
            ? 'bg-[#D4AF37] text-[#0F291E] shadow-xs'
            : 'bg-[#1B4332]/80 text-[#E7DFC6] hover:bg-[#23533F] border border-white/10'
        }">
        <span>${c.name}</span>
      </button>
    `;
  }).join('');

  renderRacePills();
}

function renderRacePills() {
  const p = STATE.program;
  const raceContainer = document.getElementById('raceTabs');
  raceContainer.innerHTML = p.races.map((r, idx) => {
    const active = idx === STATE.currentRaceIndex;
    const isAltiliStart = !!r.is_altili_start;
    return `
      <button onclick="selectRace(${idx})"
        class="px-2.5 py-1 rounded-md text-xs font-bold whitespace-nowrap transition-all flex items-center gap-1 ${
          active
            ? 'bg-[#F6F3EC] text-[#0F291E] shadow-xs'
            : 'bg-[#091912]/60 text-[#B7C9BE] hover:text-white border border-white/10'
        }">
        <span>${r.race_no}. Koşu</span>
        <span class="text-[10px] opacity-80 mono">${r.time}</span>
        ${isAltiliStart ? '<span class="ml-0.5 px-1 py-0.2 bg-[#7A1C1C] text-white rounded text-[9px]">6\'LI</span>' : ''}
      </button>
    `;
  }).join('');
}

function selectRace(idx) {
  STATE.currentRaceIndex = idx;
  renderRacePills();
  renderCurrentRace();
  populateXraySelectors();
}

function switchTab(tabName) {
  STATE.activeTab = tabName;
  const tabs = ['race', 'pace', 'value', 'altili', 'xray'];
  tabs.forEach(t => {
    const viewEl = document.getElementById(`view-${t}`);
    const navEl = document.getElementById(`nav-${t}`);
    if (t === tabName) {
      viewEl.classList.remove('hidden');
      navEl.className = 'relative flex flex-col items-center py-1 px-2.5 rounded-lg text-[#0F291E] bg-[#0F291E]/10 font-bold';
    } else {
      viewEl.classList.add('hidden');
      navEl.className = 'relative flex flex-col items-center py-1 px-2.5 rounded-lg text-[#57534E] hover:text-[#0F291E]';
    }
  });
  window.scrollTo({ top: 0, behavior: 'smooth' });
}

function setSortMode(mode) {
  STATE.sortMode = mode;
  ['quant', 'agf', 'no'].forEach(m => {
    const btn = document.getElementById(`sort-${m}`);
    if (m === mode) {
      btn.className = 'px-2.5 py-1 rounded-md bg-[#0F291E] text-[#F9F6F0] font-bold text-[11px]';
    } else {
      btn.className = 'px-2.5 py-1 rounded-md bg-[#E8E3D8] text-[#44403C] font-semibold text-[11px]';
    }
  });
  renderCurrentRace();
}

function surfaceBadge(surface) {
  if (surface.includes('Çim')) {
    return '<span class="px-2 py-0.5 rounded bg-[#1B4332] text-[#F9F6F0] text-[10px] font-bold tracking-wide">ÇİM PİST</span>';
  }
  if (surface.includes('Sentetik')) {
    return '<span class="px-2 py-0.5 rounded bg-[#3F3F46] text-[#F9F6F0] text-[10px] font-bold tracking-wide">SENTETİK PİST</span>';
  }
  return '<span class="px-2 py-0.5 rounded bg-[#8C5A3C] text-[#F9F6F0] text-[10px] font-bold tracking-wide">KUM PİST</span>';
}

function roleBadgeHTML(badge) {
  const map = {
    banko: { cls: 'bg-[#0F291E] text-[#E6C762] border border-[#A67C37]', text: 'BAŞ FAVORİ / TEK' },
    favori: { cls: 'bg-[#1B4332]/15 text-[#0F291E] border border-[#1B4332]/30 font-bold', text: 'ÖN PLANDA' },
    value: { cls: 'bg-[#7A1C1C] text-white font-bold', text: 'CAZİP SÜRPRİZ' },
    plase: { cls: 'bg-[#EFECE4] text-[#44403C] border border-[#D6D0C2] font-semibold', text: 'CİDDİ RAKİP' },
    bomba: { cls: 'bg-[#F7F1E3] text-[#8C5A3C] border border-[#D4AF37]/40 font-semibold', text: 'GENİŞ KUPON' },
    out: { cls: 'bg-[#F5F5F4] text-[#78716C]', text: 'GERİ PLANDA' }
  };
  const item = map[badge.type] || map.out;
  return `<span class="px-2 py-0.5 rounded text-[10px] tracking-wide font-bold ${item.cls}">${item.text}</span>`;
}

function cleanXaiNote(note) {
  return (note || '')
    .replace('VALUE BET: Model kazanma ihtimalini', 'CAZİP ORAN: Bülten hesabımız kazanma ihtimalini')
    .replace('AI Model:', 'Bülten:');
}

function renderCurrentRace() {
  const race = STATE.program.races[STATE.currentRaceIndex];
  if (!race) return;

  const firstPrize = race.prizes && race.prizes.length ? race.prizes[0].replace('1.)', '') : '';
  document.getElementById('raceHeaderCard').innerHTML = `
    <div class="flex items-start justify-between gap-2 pb-2.5 border-b border-[#E2DDD2]">
      <div>
        <div class="flex items-center gap-2 flex-wrap">
          <h2 class="serif-title text-xl font-bold text-[#0F291E]">${race.race_no}. KOŞU • SAAT ${race.time}</h2>
          ${surfaceBadge(race.surface)}
          <span class="px-2 py-0.5 rounded bg-[#EFECE4] text-[#18181B] border border-[#D8D2C5] text-[11px] font-bold mono">${race.distance_m}m</span>
        </div>
        ${race.cup_name ? `<div class="serif-title text-sm font-bold text-[#7A1C1C] mt-0.5">🏆 ${race.cup_name}</div>` : ''}
        <p class="text-xs text-[#57534E] font-medium mt-0.5">${race.condition} • ${race.age_group}</p>
      </div>
      <div class="text-right">
        <div class="text-[10px] text-[#57534E] uppercase font-bold">Birincilik İkramiyesi</div>
        <div class="text-xs font-extrabold text-[#0F291E] mono">${firstPrize || '-'}</div>
        ${race.record_time ? `<div class="text-[10px] text-[#57534E] mono mt-0.5">Pist Rekoru: ${race.record_time}</div>` : ''}
      </div>
    </div>

    <div class="mt-2 flex items-center justify-between text-[11px]">
      <div class="flex items-center gap-1.5 text-[#44403C]">
        <span>⏱️ <strong>Koşu Gidişatı:</strong></span>
        <span class="text-[#7A1C1C] font-bold">${race.pace_scenario.type}</span>
      </div>
      <button onclick="switchTab('pace')" class="text-[#0F291E] font-bold underline">Gidişat Şeması →</button>
    </div>
    ${race.ekuri_info ? `<div class="mt-2 text-[11px] text-[#0F291E] bg-[#EFECE4] border border-[#D8D2C5] rounded-lg px-2.5 py-1 font-medium">🔗 ${race.ekuri_info}</div>` : ''}
    ${(race.scratched || []).length ? `<div class="mt-2 text-[11px] text-[#7A1C1C] bg-[#7A1C1C]/5 border border-[#7A1C1C]/30 rounded-lg px-2.5 py-1 font-semibold">🚫 Koşmaz: ${race.scratched.map(x => `#${x.no} ${x.name}`).join(' • ')} <span class="font-normal text-[#57534E]">(analize dahil edilmedi)</span></div>` : ''}
  `;

  // 2. Başhandikapçı & Bülten İlk 4 Sıralaması
  const sortedByRank = [...race.horses].sort((a, b) => a.model_rank - b.model_rank);
  const top4 = sortedByRank.slice(0, 4);
  const darkHorse = sortedByRank.find(h => h.simulation.is_value_bet || h.simulation.is_dark_horse);

  document.getElementById('aiPicksBanner').innerHTML = `
    <div class="flex items-center justify-between mb-2">
      <div class="serif-title text-sm font-bold text-[#0F291E] uppercase tracking-wider">
        🏛️ BÜLTEN &amp; DERECE SIRALAMASI (ÖNE ÇIKANLAR)
      </div>
      <span class="text-[10px] text-[#57534E] font-semibold">Sıklet &amp; Kronometre Hesabı</span>
    </div>
    <div class="grid grid-cols-4 gap-1.5">
      ${top4.map((h, i) => `
        <div onclick="openHorseModal(${h.no})" class="cursor-pointer bg-[#F6F3EC] hover:bg-[#EFECE4] border ${i === 0 ? 'border-[#0F291E] ring-1 ring-[#0F291E]/20' : 'border-[#D8D2C5]'} rounded-lg p-2 text-center transition">
          <div class="text-[9px] font-extrabold uppercase ${i === 0 ? 'text-[#0F291E]' : 'text-[#57534E]'}">${i === 0 ? 'BAŞ FAVORİ' : (i + 1) + '. ADAY'}</div>
          <div class="text-sm font-extrabold text-[#18181B] mono mt-0.5">#${h.no}</div>
          <div class="text-[10px] font-bold text-[#18181B] truncate">${h.name}</div>
          <div class="text-[11px] font-extrabold text-[#0F291E] mono mt-0.5">%${h.simulation.win_prob}</div>
        </div>
      `).join('')}
    </div>
    ${darkHorse ? `
      <div onclick="openHorseModal(${darkHorse.no})" class="cursor-pointer mt-2 pt-2 border-t border-[#E2DDD2] flex items-center justify-between text-xs">
        <div class="flex items-center gap-1.5">
          <span class="px-1.5 py-0.5 rounded bg-[#7A1C1C] text-white font-bold text-[9px] uppercase">CAZİP SAFKAN</span>
          <span class="font-bold text-[#18181B]">#${darkHorse.no} ${darkHorse.name}</span>
        </div>
        <span class="mono text-[#7A1C1C] font-bold text-[11px]">Bülten: %${darkHorse.simulation.win_prob} • AGF: %${darkHorse.agf_pct}</span>
      </div>
    ` : ''}
  `;

  // 2.5 Kupon Ölçeği Şablonu (Küçük / Orta / Geniş)
  const tiers = race.coupon_tiers || { kucuk: [], orta: [], genis: [] };
  const tierEl = document.getElementById('bankomatTierCard');
  if (tierEl) {
    const fmtTier = (arr, badgeCls) => arr.length
      ? arr.map(x => `<span onclick="openHorseModal(${x.no})" class="cursor-pointer inline-flex items-center gap-1 px-2 py-0.5 rounded ${badgeCls} text-[11px] font-bold mono">#${x.no} ${x.name} <span class="opacity-80">(${x.grade})</span></span>`).join(' ')
      : '<span class="text-[10px] text-[#78716C]">-</span>';

    tierEl.innerHTML = `
      <div class="flex items-center justify-between mb-2">
        <span class="serif-title text-sm font-bold text-[#0F291E] uppercase">
          📋 KUPON ÖLÇEĞİ ŞABLONU (DERECE BÜLTENİ)
        </span>
        <span class="text-[10px] text-[#57534E] font-medium">Safkana Tıkla ➔ Soyağacı &amp; Geçmiş</span>
      </div>
      <div class="grid grid-cols-3 gap-2 text-xs">
        <div class="bg-[#F6F3EC] border border-[#0F291E]/30 rounded-lg p-2">
          <div class="text-[9px] font-extrabold text-[#0F291E] uppercase mb-1">KÜÇÜK ÖLÇEKLİ</div>
          <div class="flex flex-wrap gap-1">${fmtTier(tiers.kucuk, 'bg-[#0F291E] text-[#F9F6F0]')}</div>
        </div>
        <div class="bg-[#F6F3EC] border border-[#A67C37]/40 rounded-lg p-2">
          <div class="text-[9px] font-extrabold text-[#8C5A3C] uppercase mb-1">ORTA ÖLÇEKLİ</div>
          <div class="flex flex-wrap gap-1">${fmtTier(tiers.orta, 'bg-[#EFECE4] text-[#18181B] border border-[#C8C1B2]')}</div>
        </div>
        <div class="bg-[#F6F3EC] border border-[#7A1C1C]/30 rounded-lg p-2">
          <div class="text-[9px] font-extrabold text-[#7A1C1C] uppercase mb-1">GENİŞ KUPONLAR</div>
          <div class="flex flex-wrap gap-1">${fmtTier(tiers.genis, 'bg-[#7A1C1C]/10 text-[#7A1C1C] border border-[#7A1C1C]/30')}</div>
        </div>
      </div>
    `;
  }

  // 3. Sort Horses
  let displayHorses = [...race.horses];
  if (STATE.sortMode === 'quant') {
    displayHorses.sort((a, b) => a.model_rank - b.model_rank);
  } else if (STATE.sortMode === 'agf') {
    displayHorses.sort((a, b) => (b.agf_pct - a.agf_pct) || (a.model_rank - b.model_rank));
  } else {
    displayHorses.sort((a, b) => a.no - b.no);
  }

  document.getElementById('raceHorseCount').textContent = `${displayHorses.length} Safkan Kayıtlı`;

  // 4. Render Horse Cards
  const container = document.getElementById('horseListContainer');
  container.innerHTML = displayHorses.map(h => {
    const isTop = h.model_rank === 1;
    const isVal = h.simulation.is_value_bet || h.simulation.is_dark_horse;
    const cardCls = isTop ? 'club-card-featured' : (isVal ? 'club-card-cazip' : 'club-card');

    const son6Pills = (h.form_analysis.items || []).map(item => {
      const sColor = item.surface === 'Ç' ? 'text-[#0F291E] border-[#1B4332]/40 bg-[#1B4332]/10'
                   : item.surface === 'S' ? 'text-[#3F3F46] border-[#71717A]/40 bg-[#F4F4F5]'
                   : item.surface === '?' ? 'text-[#44403C] border-[#D8D2C5] bg-[#EFECE4]'
                   : 'text-[#8C5A3C] border-[#8C5A3C]/40 bg-[#8C5A3C]/10';
      const winHighlight = item.pos === 1 ? 'ring-1 ring-[#0F291E] font-extrabold' : '';
      return `<span class="px-1.5 py-0.5 rounded border text-[10px] mono ${sColor} ${winHighlight}">${item.display}</span>`;
    }).join('');

    const styleColors = {
      E: 'text-[#7A1C1C] bg-[#7A1C1C]/10 border-[#7A1C1C]/30',
      P: 'text-[#0F291E] bg-[#0F291E]/10 border-[#0F291E]/30',
      S: 'text-[#8C5A3C] bg-[#8C5A3C]/10 border-[#8C5A3C]/30'
    };

    return `
      <div onclick="openHorseModal(${h.no})" class="${cardCls} rounded-xl p-3.5 cursor-pointer transition-all hover:translate-y-[-1px]">
        <!-- Top Row -->
        <div class="flex items-start justify-between gap-2">
          <div class="flex items-center gap-2.5 min-w-0">
            <div class="relative flex-shrink-0">
              ${h.forma_url
                ? `<img src="${h.forma_url}" alt="Forma" class="w-11 h-11 rounded-lg object-contain bg-[#F6F3EC] p-0.5 border border-[#D8D2C5]" onerror="this.style.display='none'" />`
                : `<div class="w-11 h-11 rounded-lg bg-[#0F291E] text-[#F9F6F0] flex items-center justify-center font-extrabold text-sm">${h.no}</div>`
              }
              <span class="absolute -top-1.5 -left-1.5 w-5 h-5 rounded-full bg-[#0F291E] text-[#F9F6F0] border border-[#D4AF37] text-[11px] font-extrabold flex items-center justify-center mono">${h.no}</span>
            </div>

            <div class="min-w-0">
              <div class="flex items-center gap-1.5 flex-wrap">
                <h3 class="serif-title text-base font-bold text-[#18181B] truncate leading-tight">${h.name}</h3>
                ${h.is_ekuri ? '<span class="px-1.5 py-0.2 rounded bg-[#0F291E]/10 text-[#0F291E] text-[9px] font-bold">EKÜRİ</span>' : ''}
                ${roleBadgeHTML(h.role_badge)}
                ${weightBadgeHTML(h)}
              </div>
              <div class="text-[11px] text-[#44403C] truncate mt-0.5">
                <strong class="text-[#18181B]">${h.jockey}</strong> • ${h.weight}kg • Kulvar:${h.start_no} ${h.handicap ? `• HP:${h.handicap}` : ''}
              </div>
              <div class="text-[10px] text-[#57534E] truncate">
                ${h.age} • <strong class="text-[#44403C]">${h.sire}</strong> - ${h.dam}${h.damsire ? ` / <span class="text-[#7A1C1C] font-semibold">${h.damsire}</span>` : ''}
              </div>
            </div>
          </div>

          <!-- Right Score & Grade -->
          <div class="text-right flex-shrink-0">
            <div class="inline-flex items-center gap-1">
              <span class="px-1.5 py-0.5 rounded bg-[#F7F1E3] border border-[#A67C37] text-xs font-extrabold text-[#8C5A3C] mono" title="Form Derecesi">${h.form_grade || 'B'}</span>
              <div class="inline-flex items-center gap-1 px-2 py-0.5 rounded bg-[#0F291E] text-[#F9F6F0]">
                <span class="text-[9px] text-[#D4AF37] font-bold uppercase">PUAN</span>
                <span class="text-sm font-extrabold mono">${h.quant_score}</span>
              </div>
            </div>
            <div class="text-xs font-extrabold text-[#18181B] mono mt-1">
              Şans: <span class="text-[#0F291E]">%${h.simulation.win_prob}</span>
            </div>
            ${h.agf_pct > 0 ? `
              <div class="text-[10px] mono ${h.simulation.value_edge >= 3.5 ? 'text-[#7A1C1C] font-bold' : 'text-[#57534E]'}">
                AGF: %${h.agf_pct} (${h.agf_rank}.)
              </div>
            ` : `<div class="text-[10px] text-[#57534E] mono">Ederi: ${h.simulation.fair_ganyan}</div>`}
          </div>
        </div>

        <!-- Middle Row: 5 Classic Bülten Columns -->
        <div class="grid grid-cols-5 gap-1.5 mt-2.5 pt-2.5 border-t border-[#E8E3D8] text-center">
          <div class="bg-[#F6F3EC] rounded p-1.5 border border-[#E2DDD2]">
            <div class="text-[9px] text-[#57534E] font-bold">PERFORMANS</div>
            <div class="text-xs font-extrabold text-[#0F291E] mono">${h.bankomat_summary ? h.bankomat_summary.performans : h.metrics.speed_figure}</div>
          </div>
          <div class="bg-[#F6F3EC] rounded p-1.5 border border-[#E2DDD2]">
            <div class="text-[9px] text-[#57534E] font-bold">KİLOYA GÖRE</div>
            <div class="text-xs font-extrabold text-[#18181B] mono">${h.bankomat_summary ? h.bankomat_summary.kiloya_gore : h.metrics.weight_rating}</div>
          </div>
          <div class="bg-[#F6F3EC] rounded p-1.5 border border-[#E2DDD2]">
            <div class="text-[9px] text-[#57534E] font-bold">ZORLUK</div>
            <div class="text-xs font-extrabold text-[#44403C] mono">${h.bankomat_summary ? h.bankomat_summary.zorluk : h.metrics.class_rating}</div>
          </div>
          <div class="bg-[#F6F3EC] rounded p-1.5 border border-[#E2DDD2]">
            <div class="text-[9px] text-[#57534E] font-bold">ORT. POZ %</div>
            <div class="text-xs font-extrabold text-[#0F291E] mono">%${h.bankomat_summary ? h.bankomat_summary.ortalama_poz : h.simulation.top3_prob}</div>
          </div>
          <div class="bg-[#F6F3EC] rounded p-1.5 border border-[#E2DDD2]">
            <div class="text-[9px] text-[#57534E] font-bold">EN İYİ D.</div>
            <div class="text-[11px] font-extrabold text-[#7A1C1C] mono">${h.best_time || '-'}</div>
          </div>
        </div>

        <!-- Bottom Row: Son 6 & Takılar -->
        <div class="mt-2.5 flex items-center justify-between gap-2 flex-wrap">
          <div class="flex items-center gap-1 flex-wrap">
            ${son6Pills || '<span class="text-[10px] text-[#78716C]">İlk Koşu / Kayıt Yok</span>'}
          </div>
          <div class="flex items-center gap-1">
            <span class="px-1.5 py-0.5 rounded border text-[10px] font-bold ${styleColors[h.running_style.code]}">${h.running_style.label}</span>
            ${h.equipment.map(eq => `<span class="px-1.5 py-0.5 rounded bg-[#EFECE4] text-[#18181B] border border-[#D8D2C5] text-[10px] font-bold mono">${eq}</span>`).join('')}
          </div>
        </div>

        <!-- Derece Karnesi (1.-5.'lik %) & Son İdman (TJK kaydı olmayan yabancı atlarda gösterilmez) -->
        ${(h.at_id || race.kosu_kodu) ? `<div id="hx-${h.no}" class="mt-2 text-[10px] bg-[#F6F3EC] rounded-lg px-2.5 py-1.5 border border-[#E2DDD2]">${horseExtrasHTML(race, h)}</div>` : ''}

        <!-- Komiser / Handikapçı Notu -->
        <div class="mt-2 text-[11px] text-[#44403C] bg-[#FAF8F3] rounded-lg px-2.5 py-1.5 border border-[#E2DDD2] flex items-start gap-1.5">
          <span class="text-[#8C5A3C] font-bold">✒️</span>
          <span class="leading-snug font-medium">${cleanXaiNote(h.xai_notes[0])}</span>
        </div>
      </div>
    `;
  }).join('');

  renderPaceAndSimulationView(race);
  renderDistanceCompare(race);
  renderRaceWarnings(race);
  loadRaceExtras(race);
}

// ------------------------------------------------------------------
// SAFKAN KARNESİ (1.-5.'lik yüzdeleri, HP) ve İDMAN — koşu açıldığında arka planda yüklenir
// ------------------------------------------------------------------
function fetchJSON(url) {
  if (STATE.pending[url]) return STATE.pending[url];
  const p = fetch(url).then(r => r.json()).then(json => {
    if (!json.ok) throw new Error(json.error || 'Veri alınamadı');
    return deepEscape(json.data);
  }).finally(() => { delete STATE.pending[url]; });
  STATE.pending[url] = p;
  return p;
}

// TJK zaman zaman yanıt vermez: başarısız kayıtlar kalıcı değildir, 30 sn sonra yeniden denenir
const RETRY_AFTER_MS = 30000;
function needsFetch(entry) {
  return !entry || (entry.failed && Date.now() - (entry.failedAt || 0) > RETRY_AFTER_MS);
}

function ensureRaceIdman(race) {
  if (!race.kosu_kodu || !race.idman_at_kodu) return Promise.resolve(null);
  if (!needsFetch(STATE.idman[race.kosu_kodu])) return Promise.resolve(STATE.idman[race.kosu_kodu]);
  const date = encodeURIComponent(STATE.program.date || '');
  return fetchJSON(`/api/race_idman?kosu=${race.kosu_kodu}&at=${race.idman_at_kodu}&date=${date}`)
    .then(d => {
      // Son 400m sıralaması yalnızca koşacak atlar arasında (koşmaz atlar idman tablosunda kalabilir)
      const active = new Set(race.horses.map(h => h.no));
      const timed = (d.rows || []).filter(r => active.has(r.no) && r.son_400).sort((a, b) => a.son_400 - b.son_400);
      (d.rows || []).forEach(r => { r.son_400_rank = null; });
      timed.forEach((r, i) => { r.son_400_rank = i + 1; });
      d.timed_count = timed.length;
      STATE.idman[race.kosu_kodu] = d;
      return d;
    })
    .catch(() => { STATE.idman[race.kosu_kodu] = { rows: [], failed: true, failedAt: Date.now() }; return STATE.idman[race.kosu_kodu]; });
}

function idmanForHorse(race, h) {
  const d = race.kosu_kodu ? STATE.idman[race.kosu_kodu] : null;
  if (!d) return undefined;               // henüz yüklenmedi
  return (d.rows || []).find(r => r.no === h.no) || null;  // null = idmanı yok
}

// Ekrandaki koşu hâlâ bu koşu mu? (indeks yetmez: hipodrom değişince yeni programda da 0. koşu olur)
function isCurrentRace(race) {
  return STATE.program && STATE.program.races[STATE.currentRaceIndex] === race;
}

function loadRaceExtras(race) {
  ensureRaceIdman(race).then(() => {
    if (!isCurrentRace(race)) return;
    race.horses.forEach(h => paintHorseExtras(race, h));
  });
  // Hafif karne yalnız o mesafenin derecelerini taşır: başka mesafede koşuyorsa yeniden iste
  const stale = ex => ex && !ex.failed && !ex._full && ex._dist !== race.distance_m;
  const queue = race.horses.filter(h => h.at_id && (needsFetch(STATE.extras[h.at_id]) || stale(STATE.extras[h.at_id])));
  race.horses.filter(h => !queue.includes(h)).forEach(h => paintHorseExtras(race, h));
  let active = 0;
  const next = () => {
    if (!isCurrentRace(race)) return;  // başka koşuya / hipodroma geçildi
    while (active < 3 && queue.length) {
      const h = queue.shift();
      active++;
      const isArab = (race.age_group || '').toLowerCase().includes('arap') ? '1' : '0';
      fetchJSON(`/api/horse_history?lite=1&at_id=${encodeURIComponent(h.at_id)}&weight=${h.weight}&dist=${race.distance_m}&arab=${isArab}`)
        .then(d => { d._dist = race.distance_m; if (!(STATE.extras[h.at_id] || {})._full) STATE.extras[h.at_id] = d; })
        .catch(() => { STATE.extras[h.at_id] = { failed: true, failedAt: Date.now() }; })
        .finally(() => {
          active--;
          if (isCurrentRace(race)) paintHorseExtras(race, h);
          next();
        });
    }
  };
  next();
}

function paintHorseExtras(race, h) {
  const el = document.getElementById(`hx-${h.no}`);
  if (el) el.innerHTML = horseExtrasHTML(race, h);
  scheduleDistanceCompare(race);
}

function pctPill(label, val, strong) {
  return `<span class="${strong ? 'text-[#0F291E] font-extrabold' : 'text-[#44403C] font-bold'}">${label} <span class="mono">%${val}</span></span>`;
}

function hpTrendHTML(hp) {
  if (!hp || hp.current == null) return '';
  const ch = hp.change_5 || 0;
  const arrow = ch > 0 ? `<span class="text-[#0F291E]">▲${ch}</span>` : (ch < 0 ? `<span class="text-[#7A1C1C]">▼${Math.abs(ch)}</span>` : '<span class="text-[#78716C]">=</span>');
  return `<span class="mono font-bold text-[#18181B]" title="Son 5 koşudaki HP değişimi">HP ${hp.current} ${arrow}</span>`;
}

function idmanDurumBadge(durum) {
  if (!durum) return '';
  const u = durum.toLocaleUpperCase('tr-TR');
  const cls = u.includes('ÇOK RAHAT') ? 'bg-[#0F291E] text-[#E6C762]'
            : u.includes('RAHAT') ? 'bg-[#1B4332]/15 text-[#0F291E] border border-[#1B4332]/30'
            : u.includes('ÇALIŞ') ? 'bg-[#F7F1E3] text-[#8C5A3C] border border-[#A67C37]/40'
            : 'bg-[#7A1C1C]/10 text-[#7A1C1C]';
  return `<span class="px-1.5 py-0.5 rounded text-[9px] font-extrabold ${cls}">${durum}</span>`;
}

function idmanSplitsText(row) {
  return (row.splits || []).map(x => `<span class="mono">${x.dist}m ${x.time}</span>`).join(' • ');
}

function horseExtrasHTML(race, h) {
  const ex = h.at_id ? STATE.extras[h.at_id] : null;
  let karne;
  if (!h.at_id) {
    karne = '<span class="text-[#78716C]">Karne: TJK kimliği yok</span>';
  } else if (!ex || (ex.failed && needsFetch(ex))) {
    karne = '<span class="text-[#78716C]">⏳ Derece karnesi yükleniyor…</span>';
  } else if (ex.failed || !ex.career_stats) {
    karne = '<span class="text-[#78716C]">Karne alınamadı (TJK yanıt vermedi) — koşuyu yeniden açınca tekrar denenir</span>';
  } else {
    const c = ex.career_stats;
    karne = c.starts ? `
      <span class="text-[9px] font-extrabold text-[#8C5A3C] uppercase">Karne</span>
      <span class="mono font-bold text-[#18181B]">${c.starts}K</span>
      ${pctPill('1.', c.win_pct, true)} ${pctPill('2.', c.second_pct)} ${pctPill('3.', c.third_pct)}
      ${pctPill('İlk 3', c.top3_pct, true)} ${pctPill('İlk 5', c.top5_pct)}
      ${hpTrendHTML(ex.hp_history)}
    ` : '<span class="text-[#78716C]">İlk koşusu — karne yok</span>';
  }

  const idm = idmanForHorse(race, h);
  let idman;
  if (!race.kosu_kodu) {
    idman = '';
  } else if (idm === undefined) {
    idman = '<span class="text-[#78716C]">⏳ İdman bilgisi yükleniyor…</span>';
  } else if (idm === null) {
    idman = '<span class="text-[#78716C]">Bu koşu için kayıtlı idmanı yok</span>';
  } else {
    const ago = idm.days_before != null ? ` <span class="text-[#57534E]">(${idm.days_before} gün önce)</span>` : '';
    const fastest = idm.son_400_rank === 1 ? '<span class="px-1.5 py-0.5 rounded bg-[#7A1C1C] text-white text-[9px] font-bold">EN HIZLI SON 400</span>' : '';
    idman = `
      <span class="text-[9px] font-extrabold text-[#0F291E] uppercase">İdman</span>
      <span class="font-bold text-[#18181B]">${idm.tarih ? idm.tarih.slice(0, 5) : ''}${ago}</span>
      <span class="font-bold text-[#8C5A3C]">${idm.tur || ''}</span>
      ${idmanSplitsText(idm)} ${idmanDurumBadge(idm.galop_durumu)} ${fastest}
    `;
  }
  const sf = seasonFlag(h);
  const seasonLine = sf ? `<div class="mt-1 pt-1 border-t border-[#E8E3D8] font-bold ${sf.type === 'weak' ? 'text-[#7A1C1C]' : 'text-[#0F291E]'}">${sf.text}</div>` : '';
  return `
    <div class="flex items-center gap-x-2 gap-y-0.5 flex-wrap">${karne}</div>
    ${idman ? `<div class="flex items-center gap-x-2 gap-y-0.5 flex-wrap mt-1 pt-1 border-t border-[#E8E3D8]">${idman}</div>` : ''}
    ${seasonLine}
  `;
}

// ---------------- MEVSİM ve KİLO UYARILARI ----------------
const SEASON_BY_MONTH = { 12: 'Kış', 1: 'Kış', 2: 'Kış', 3: 'İlkbahar', 4: 'İlkbahar', 5: 'İlkbahar',
                          6: 'Yaz', 7: 'Yaz', 8: 'Yaz', 9: 'Sonbahar', 10: 'Sonbahar', 11: 'Sonbahar' };
const SEASON_LOC = { 'Kış': 'Kışın', 'İlkbahar': 'İlkbaharda', 'Yaz': 'Yazın', 'Sonbahar': 'Sonbaharda' };

function raceSeason() {
  const m = parseInt(((STATE.program && STATE.program.date) || '').split('/')[1], 10);
  return SEASON_BY_MONTH[m] || null;
}

// Atın bu mevsimdeki başarısı diğer mevsimlerden belirgin farklı mı?
function seasonFlag(h) {
  const ex = h.at_id ? STATE.extras[h.at_id] : null;
  const rows = ex && ex.breakdowns && ex.breakdowns.season;
  const cur = raceSeason();
  if (!rows || !cur) return null;
  const c = rows.find(r => r.label === cur);
  const others = rows.filter(r => r.label !== cur);
  const oStarts = others.reduce((a, r) => a + r.starts, 0);
  if (!c || c.starts < 3 || oStarts < 4) return null;
  const top3 = r => r.w1 + r.w2 + r.w3;
  const oTop3 = Math.round(others.reduce((a, r) => a + top3(r), 0) / oStarts * 1000) / 10;
  const best = others.filter(r => r.starts >= 3).sort((a, b) => (b.w1 - a.w1) || (b.top3_pct - a.top3_pct))[0];
  const winlessHere = c.w1 === 0 && c.starts >= 4 && best && best.w1 >= 2;
  if (winlessHere || oTop3 - c.top3_pct >= 20) {
    return { type: 'weak', no: h.no, name: h.name,
      text: `⚠️ MEVSİM ZAAFI: ${SEASON_LOC[cur]} ${c.starts} koşuda ${c.w1} galibiyet (İlk 3 %${c.top3_pct})` +
            (best ? ` — ${SEASON_LOC[best.label]} ${best.starts} koşuda ${best.w1} galibiyet (İlk 3 %${best.top3_pct})` : ` — diğer mevsimlerde İlk 3 %${oTop3}`) };
  }
  if (c.top3_pct - oTop3 >= 20 && c.w1 >= 1) {
    return { type: 'strong', no: h.no, name: h.name,
      text: `🌤️ MEVSİM ATI: ${SEASON_LOC[cur]} ${c.starts} koşuda ${c.w1} galibiyet (İlk 3 %${c.top3_pct}) — diğer mevsimlerde İlk 3 %${oTop3}` };
  }
  return null;
}

function weightBadgeHTML(h) {
  const f = h.weight_flag;
  if (!f) return '';
  const light = f.type === 'light';
  const cls = light ? 'bg-[#0F291E] text-[#E6C762]' : 'bg-[#7A1C1C] text-white';
  return `<span class="px-1.5 py-0.5 rounded text-[9px] font-extrabold ${cls}" title="${f.text}">⚖️ ${light ? 'HAFİF' : 'AĞIR'} ${f.diff > 0 ? '+' : ''}${f.diff}kg${f.alone ? ' • TEK' : ''}</span>`;
}

function renderRaceWarnings(race) {
  const el = document.getElementById('raceWarnings');
  if (!el) return;
  const wm = race.weight_margin || { flags: [] };
  const season = race.horses.map(seasonFlag).filter(Boolean);
  const pending = race.horses.filter(h => h.at_id && !STATE.extras[h.at_id]).length;
  if (!wm.flags.length && !season.length) {
    el.classList.add('hidden');
    return;
  }
  el.classList.remove('hidden');
  const isHandikap = /handikap/i.test(race.condition || '');
  const item = (no, html, tone) => `
    <div onclick="openHorseModal(${no})" class="cursor-pointer flex items-start gap-2 text-[11px] py-1 border-t border-[#E8E3D8] first:border-t-0">
      <span class="mono font-extrabold ${tone} w-7 flex-shrink-0">#${no}</span><span class="text-[#18181B] leading-snug">${html}</span>
    </div>`;
  el.innerHTML = `
    <div class="flex items-center justify-between mb-1.5">
      <span class="serif-title text-sm font-bold text-[#7A1C1C] uppercase">⚠️ Başhandikapçı Uyarıları</span>
      ${pending ? `<span class="text-[10px] text-[#57534E]">⏳ mevsim karnesi: ${pending} at</span>` : `<span class="text-[10px] text-[#57534E]">${raceSeason() || ''} koşusu</span>`}
    </div>
    ${wm.flags.length ? `
      <div class="mb-1.5">
        <div class="text-[9px] font-extrabold text-[#8C5A3C] uppercase">Kilo Marjı • ortanca ${wm.median} kg • aralık ${wm.min}–${wm.max} kg (${wm.spread} kg)</div>
        ${wm.flags.map(f => item(f.no, `<strong>${f.name}</strong> — ${f.text.replace(/^⚖️ /, '')}`, f.type === 'light' ? 'text-[#0F291E]' : 'text-[#7A1C1C]')).join('')}
        ${isHandikap ? '<div class="text-[9px] text-[#78716C] mt-0.5">Not: Handikap koşusunda kilolar handikap puanına göre verilir; hafif kilo genelde düşük puanı da gösterir.</div>' : ''}
      </div>` : ''}
    ${season.length ? `
      <div>
        <div class="text-[9px] font-extrabold text-[#8C5A3C] uppercase">Mevsim Başarısı</div>
        ${season.map(f => item(f.no, `<strong>${f.name}</strong> — ${f.text.replace(/^(⚠️|🌤️) /, '')}`, f.type === 'weak' ? 'text-[#7A1C1C]' : 'text-[#0F291E]')).join('')}
      </div>` : ''}
  `;
}

// ---------------- AYNI MESAFE, FARKLI İLLER: koşudaki atların dereceleri ----------------
let DIST_CMP_TIMER = null;
function scheduleDistanceCompare(race) {
  clearTimeout(DIST_CMP_TIMER);
  DIST_CMP_TIMER = setTimeout(() => { if (isCurrentRace(race)) { renderDistanceCompare(race); renderRaceWarnings(race); } }, 150);
}

function raceSurfaceName(race) {
  const s = race.surface || '';
  return s.includes('Çim') ? 'Çim' : s.includes('Sentetik') ? 'Sentetik' : 'Kum';
}

function renderDistanceCompare(race) {
  const el = document.getElementById('distanceCompare');
  if (!el) return;
  const surf = raceSurfaceName(race);
  const rows = [];
  const cities = new Map();
  race.horses.forEach(h => {
    const ex = h.at_id ? STATE.extras[h.at_id] : null;
    const grp = ex && (ex.distance_city_times || []).find(g => g.msf === race.distance_m && g.surface === surf);
    if (grp) grp.cities.forEach(c => cities.set(c.city, (cities.get(c.city) || 0) + c.count));
    rows.push({ h, grp, loaded: !!ex || !h.at_id });
  });
  const cityList = [...cities.entries()].sort((a, b) => b[1] - a[1]).map(x => x[0]);
  // Her il sütununda en hızlı dereceyi vurgula
  const bestBy = {};
  cityList.forEach(c => {
    rows.forEach(r => {
      const cell = r.grp && r.grp.cities.find(x => x.city === c);
      if (cell && (bestBy[c] == null || cell.best_sec < bestBy[c])) bestBy[c] = cell.best_sec;
    });
  });
  const overallBest = r => r.grp ? Math.min(...r.grp.cities.map(c => c.best_sec)) : Infinity;
  rows.sort((a, b) => overallBest(a) - overallBest(b));
  const pending = rows.filter(r => !r.loaded).length;
  el.innerHTML = `
    <div class="flex items-start justify-between gap-2 mb-1">
      <h3 class="serif-title text-base font-bold text-[#0F291E] uppercase">📏 ${race.distance_m}m ${surf}: İl İl Dereceler</h3>
      ${pending ? `<span class="text-[10px] text-[#57534E] whitespace-nowrap">⏳ ${pending} at yükleniyor</span>` : ''}
    </div>
    <p class="text-[11px] text-[#57534E] mb-3">Koşudaki atların bugünkü mesafe ve pistte, farklı hipodromlarda koştuğu en iyi dereceler (parantezde koşu sayısı). Sütundaki en hızlı derece vurgulanır.</p>
    ${cityList.length ? `
      <div class="overflow-x-auto -mx-1">
        <table class="w-full text-[11px] whitespace-nowrap mono">
          <thead><tr class="text-[9px] text-[#57534E] border-b border-[#D8D2C5] uppercase font-sans">
            <th class="pb-1.5 px-1 text-left">Safkan</th>
            ${cityList.map(c => `<th class="pb-1.5 px-1 text-right">${c}</th>`).join('')}
          </tr></thead>
          <tbody class="divide-y divide-[#E8E3D8]">
            ${rows.map(r => `
              <tr onclick="openHorseModal(${r.h.no})" class="cursor-pointer hover:bg-[#F6F3EC]">
                <td class="py-1.5 px-1 font-sans font-bold text-[#18181B]">#${r.h.no} ${r.h.name}</td>
                ${cityList.map(c => {
                  const cell = r.grp && r.grp.cities.find(x => x.city === c);
                  if (!cell) return `<td class="py-1.5 px-1 text-right text-[#C8C1B2]">${r.loaded ? '—' : '…'}</td>`;
                  const top = Math.abs(cell.best_sec - bestBy[c]) < 0.001;
                  return `<td class="py-1.5 px-1 text-right ${top ? 'font-extrabold text-[#7A1C1C]' : 'font-semibold text-[#18181B]'}" title="En iyi: ${cell.best_date} • ${cell.best_sira}. • ${cell.best_pd} | Ortalama ${cell.avg}">${cell.best}<span class="text-[9px] text-[#78716C]"> (${cell.count})</span></td>`;
                }).join('')}
              </tr>`).join('')}
          </tbody>
        </table>
      </div>` : `<div class="text-xs text-[#78716C]">${pending ? 'Safkan karneleri yükleniyor…' : `Koşudaki atların ${race.distance_m}m ${surf} pistte kayıtlı derecesi yok.`}</div>`}
  `;
}

function renderPaceAndSimulationView(race) {
  const ps = race.pace_scenario;
  document.getElementById('paceScenarioCard').innerHTML = `
    <div class="flex items-center justify-between mb-2">
      <span class="serif-title text-base font-bold text-[#0F291E] uppercase">⏱️ KOŞU GİDİŞATI &amp; TEMPO ANALİZİ</span>
      <span class="px-2 py-0.5 rounded bg-[#7A1C1C]/10 text-[#7A1C1C] border border-[#7A1C1C]/30 text-[11px] font-bold">${ps.type}</span>
    </div>
    <p class="text-xs text-[#44403C] leading-relaxed mb-3 font-medium">${ps.description}</p>
    <div class="grid grid-cols-3 gap-2 text-center">
      <div class="p-2 rounded-lg bg-[#FAF8F3] border border-[#7A1C1C]/30">
        <div class="text-xs font-extrabold text-[#7A1C1C] mono">${ps.counts.E} Safkan</div>
        <div class="text-[10px] text-[#57534E] font-semibold">Kaçak / Önde (E)</div>
      </div>
      <div class="p-2 rounded-lg bg-[#FAF8F3] border border-[#0F291E]/30">
        <div class="text-xs font-extrabold text-[#0F291E] mono">${ps.counts.P} Safkan</div>
        <div class="text-[10px] text-[#57534E] font-semibold">Takipçi (P)</div>
      </div>
      <div class="p-2 rounded-lg bg-[#FAF8F3] border border-[#8C5A3C]/40">
        <div class="text-xs font-extrabold text-[#8C5A3C] mono">${ps.counts.S} Safkan</div>
        <div class="text-[10px] text-[#57534E] font-semibold">Son Düzlükçü (S)</div>
      </div>
    </div>
  `;

  const sortedByEarly = [...race.horses].sort((a, b) => b.pace_positions.m400 - a.pace_positions.m400);
  document.getElementById('visualPaceMap').innerHTML = `
    <div class="flex items-center justify-between mb-2">
      <h3 class="serif-title text-base font-bold text-[#0F291E] uppercase">🏁 İlk 600m Piste Yayılış Şeması</h3>
      <span class="text-[10px] text-[#57534E] font-semibold">Sağ Taraf = Bariyer Liderliği</span>
    </div>
    <div class="space-y-2.5 mt-3">
      ${sortedByEarly.map(h => {
        const dotColor = h.running_style.code === 'E' ? 'bg-[#7A1C1C]' : (h.running_style.code === 'P' ? 'bg-[#0F291E]' : 'bg-[#8C5A3C]');
        return `
          <div class="space-y-1">
            <div class="flex justify-between text-[11px]">
              <span class="font-bold text-[#18181B]">#${h.no} ${h.name} <span class="text-[10px] text-[#57534E]">(${h.running_style.code} • St:${h.start_no})</span></span>
              <span class="mono text-[10px] text-[#0F291E] font-bold">Bülten Puanı: ${h.quant_score}</span>
            </div>
            <div class="relative h-5 bg-[#F6F3EC] rounded-full border border-[#D8D2C5] overflow-hidden flex items-center px-2">
              <div style="left: ${h.pace_positions.m400}%" class="absolute -translate-x-1/2 flex items-center gap-1">
                <span class="w-3 h-3 rounded-full ${dotColor} ring-2 ring-white"></span>
                <span class="text-[9px] font-bold text-[#18181B] mono">${h.no}</span>
              </div>
            </div>
          </div>
        `;
      }).join('')}
    </div>
  `;

  const sortedBySim = [...race.horses].sort((a, b) => b.simulation.win_prob - a.simulation.win_prob);
  document.getElementById('monteCarloTable').innerHTML = `
    <h3 class="serif-title text-base font-bold text-[#0F291E] uppercase mb-1">📊 İhtimal &amp; Eder Ganyan Tablosu</h3>
    <p class="text-[11px] text-[#57534E] mb-3">Derece istikrarı ve sıklet dağılımına göre hesaplanan birincilik, ikili ve tabela yüzdeleri:</p>
    <div class="overflow-x-auto">
      <table class="w-full text-left text-xs">
        <thead>
          <tr class="text-[10px] text-[#57534E] border-b border-[#D8D2C5] uppercase">
            <th class="pb-2">Safkan</th>
            <th class="pb-2 text-right">Kazanır %</th>
            <th class="pb-2 text-right">İlk 2 %</th>
            <th class="pb-2 text-right">Tabela %</th>
            <th class="pb-2 text-right">Eder Gny</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-[#E8E3D8]">
          ${sortedBySim.map(h => `
            <tr onclick="openHorseModal(${h.no})" class="hover:bg-[#F6F3EC] cursor-pointer">
              <td class="py-2 font-bold text-[#18181B]">#${h.no} ${h.name}</td>
              <td class="py-2 text-right font-extrabold text-[#0F291E] mono">%${h.simulation.win_prob}</td>
              <td class="py-2 text-right text-[#44403C] mono font-semibold">%${h.simulation.top2_prob}</td>
              <td class="py-2 text-right text-[#8C5A3C] mono font-semibold">%${h.simulation.top3_prob}</td>
              <td class="py-2 text-right text-[#7A1C1C] mono font-bold">${h.simulation.fair_ganyan}</td>
            </tr>
          `).join('')}
        </tbody>
      </table>
    </div>
  `;
}

function renderValueRadar() {
  const p = STATE.program;
  const valuePicks = [];

  p.races.forEach((r, rIdx) => {
    r.horses.forEach(h => {
      if (h.simulation.is_value_bet || h.simulation.is_dark_horse || h.simulation.value_edge >= 3.2) {
        valuePicks.push({ race: r, raceIndex: rIdx, horse: h });
      }
    });
  });

  valuePicks.sort((a, b) => b.horse.simulation.value_edge - a.horse.simulation.value_edge);
  document.getElementById('valueBadgeCount').textContent = valuePicks.length;

  const container = document.getElementById('valueRadarContainer');
  if (!valuePicks.length) {
    container.innerHTML = `<div class="club-card rounded-xl p-6 text-center text-xs text-[#57534E]">Bu programda AGF oranına göre belirgin fark taşıyan cazip sürpriz bulunamadı.</div>`;
    return;
  }

  container.innerHTML = valuePicks.map(item => {
    const { race, raceIndex, horse } = item;
    return `
      <div onclick="selectRace(${raceIndex}); switchTab('race'); openHorseModal(${horse.no});"
        class="club-card-cazip rounded-xl p-3.5 cursor-pointer transition hover:translate-y-[-1px]">
        <div class="flex items-center justify-between text-[11px] text-[#8C5A3C] font-bold mb-1">
          <span>${race.race_no}. KOŞU (${race.time}) • ${race.distance_m}m ${race.surface}</span>
          <span class="px-2 py-0.5 rounded bg-[#7A1C1C] text-white font-bold text-[10px]">+${horse.simulation.value_edge}% ŞANS FARKI</span>
        </div>
        <div class="flex items-center justify-between">
          <div>
            <h3 class="serif-title text-base font-bold text-[#18181B]">#${horse.no} ${horse.name}</h3>
            <p class="text-[11px] text-[#57534E]">${horse.jockey} • ${horse.weight}kg • Stil: ${horse.running_style.label}</p>
          </div>
          <div class="text-right mono">
            <div class="text-xs font-extrabold text-[#0F291E]">Bülten Şansı: %${horse.simulation.win_prob}</div>
            <div class="text-[11px] text-[#57534E]">Halk AGF: %${horse.agf_pct || '-'}</div>
          </div>
        </div>
        <div class="mt-2 pt-2 border-t border-[#E8E3D8] text-[11px] text-[#44403C]">
          ✒️ ${cleanXaiNote(horse.xai_notes[0])}
        </div>
      </div>
    `;
  }).join('');
}

function getAltiliGroup() {
  const p = STATE.program;
  const groups = (p && p.altili_groups) || [];
  if (groups.length) return groups[Math.min(STATE.altiliGroupIdx, groups.length - 1)];
  // Eski sunucu yanıtı için yedek: son 6 koşu
  const start = Math.max(0, p.races.length - 6);
  return { label: 'Altılı Ganyan (Tahmini: Son 6 Koşu)', source: 'fallback',
           race_indices: p.races.slice(start).map((_, i) => start + i) };
}

function getAltiliRaces() {
  return getAltiliGroup().race_indices.map(i => STATE.program.races[i]).filter(Boolean);
}

function selectAltiliGroup(idx) {
  STATE.altiliGroupIdx = parseInt(idx, 10) || 0;
  generateAltiliTicket();
}

function renderAltiliGroupPicker() {
  const groups = STATE.program.altili_groups || [];
  const picker = document.getElementById('altiliGroupPicker');
  const sel = document.getElementById('altiliGroup');
  if (picker && sel) {
    if (groups.length > 1) {
      sel.innerHTML = groups.map((g, i) => {
        const races = g.race_indices.map(ri => STATE.program.races[ri]);
        const first = races[0], last = races[races.length - 1];
        return `<option value="${i}">${g.label} • ${first.race_no}.–${last.race_no}. Koşular (${first.time})</option>`;
      }).join('');
      sel.value = String(STATE.altiliGroupIdx);
      picker.classList.remove('hidden');
    } else {
      picker.classList.add('hidden');
    }
  }
  const note = document.getElementById('altiliGroupNote');
  if (note) {
    const g = getAltiliGroup();
    note.innerHTML = g.source === 'fallback'
      ? `<div class="club-card rounded-xl px-3 py-2 text-[11px] text-[#7A1C1C] border-l-4 border-l-[#7A1C1C]">TJK bülteninde "6'LI GANYAN Bu koşudan başlar" işareti bulunamadı; ayaklar tahmini olarak günün son 6 koşusundan kuruldu.</div>`
      : '';
  }
}

function generateAltiliTicket() {
  const p = STATE.program;
  if (!p || !p.races.length) return;

  renderAltiliGroupPicker();
  const altiliRaces = getAltiliRaces();
  const strategy = document.getElementById('altiliStrategy').value;

  const legOrder = altiliRaces.map((r, idx) => ({
    idx,
    race: r,
    confidence: r.banko_confidence,
    topProb: [...r.horses].sort((a, b) => a.model_rank - b.model_rank)[0]?.simulation.win_prob || 0
  })).sort((a, b) => (b.topProb + b.confidence) - (a.topProb + a.confidence));

  const countsPerLeg = {};
  legOrder.forEach((leg, rankIdx) => {
    if (strategy === 'economic') {
      countsPerLeg[leg.idx] = rankIdx <= 1 ? 1 : (rankIdx <= 3 ? 2 : 3);
    } else if (strategy === 'balanced') {
      countsPerLeg[leg.idx] = rankIdx === 0 ? 1 : (rankIdx <= 2 ? 2 : (rankIdx <= 4 ? 4 : 5));
    } else {
      countsPerLeg[leg.idx] = rankIdx === 0 ? 1 : (rankIdx === 1 ? 3 : 5);
    }
  });

  STATE.altiliSelections = {};
  altiliRaces.forEach((r, idx) => {
    const sorted = [...r.horses].sort((a, b) => a.model_rank - b.model_rank);
    const takeCount = Math.min(sorted.length, countsPerLeg[idx] || 3);
    const chosen = new Set(sorted.slice(0, takeCount).map(h => h.no));

    if (strategy !== 'economic' && takeCount >= 3) {
      sorted.forEach(h => {
        if ((h.simulation.is_value_bet || h.simulation.is_dark_horse) && chosen.size < 6) {
          chosen.add(h.no);
        }
      });
    }
    STATE.altiliSelections[idx] = chosen;
  });

  renderAltiliLegs(altiliRaces);
  updateAltiliCost();
}

// TJK'da eküri atlar bahiste tek at sayılır: biri seçilince arkadaşları da kapsanır
function ekuriCovered(race, selectedSet) {
  const covered = new Set();
  (race.ekuri_groups || []).forEach(g => {
    if (g.some(no => selectedSet.has(no))) g.forEach(no => { if (!selectedSet.has(no)) covered.add(no); });
  });
  return covered;
}

function toggleAltiliHorse(legIdx, horseNo) {
  const set = STATE.altiliSelections[legIdx];
  if (!set) return;
  if (set.has(horseNo)) {
    if (set.size > 1) set.delete(horseNo);
  } else {
    set.add(horseNo);
  }
  renderAltiliLegs(getAltiliRaces());
  updateAltiliCost();
}

function renderAltiliLegs(altiliRaces) {
  const container = document.getElementById('altiliLegsContainer');
  container.innerHTML = altiliRaces.map((r, legIdx) => {
    const selectedSet = STATE.altiliSelections[legIdx] || new Set();
    const isBanko = selectedSet.size === 1;
    const sorted = [...r.horses].sort((a, b) => a.model_rank - b.model_rank);
    const covered = ekuriCovered(r, selectedSet);

    return `
      <div class="${isBanko ? 'club-card-featured' : 'club-card'} rounded-xl p-3.5">
        <div class="flex items-center justify-between mb-2">
          <div class="flex items-center gap-2">
            <span class="px-2 py-0.5 rounded bg-[#0F291E] text-[#E6C762] font-bold text-xs">${legIdx + 1}. AYAK</span>
            <span class="text-xs font-bold text-[#18181B]">${r.race_no}. Koşu (${r.time}) • ${r.distance_m}m ${r.surface}</span>
          </div>
          <span class="text-[11px] font-extrabold mono ${isBanko ? 'text-[#0F291E]' : 'text-[#8C5A3C]'}">
            ${isBanko ? '🔒 TEK (BANKO)' : `${selectedSet.size} Safkan`}
          </span>
        </div>

        <div class="flex flex-wrap gap-1.5 mt-2">
          ${sorted.map(h => {
            const active = selectedSet.has(h.no);
            const viaEkuri = !active && covered.has(h.no);
            return `
              <button onclick="toggleAltiliHorse(${legIdx}, ${h.no})" ${viaEkuri ? 'title="Eküri arkadaşı seçili: bu at da kapsanıyor"' : ''}
                class="px-2.5 py-1.5 rounded-lg text-xs font-bold transition flex items-center gap-1.5 ${
                  active
                    ? 'bg-[#0F291E] text-[#F9F6F0] shadow-xs'
                    : viaEkuri
                      ? 'bg-[#1B4332]/10 text-[#0F291E] border border-dashed border-[#1B4332]/50'
                      : 'bg-[#F6F3EC] text-[#57534E] border border-[#D8D2C5] hover:text-[#18181B]'
                }">
                <span class="mono font-extrabold text-[#D4AF37]">#${h.no}</span>
                <span class="truncate max-w-[95px]">${h.name}</span>
                <span class="text-[10px] opacity-80 mono">%${h.simulation.win_prob}</span>
                ${viaEkuri ? '<span class="text-[9px] font-extrabold">🔗EKÜRİ</span>' : ''}
              </button>
            `;
          }).join('')}
        </div>
      </div>
    `;
  }).join('');
}

function updateAltiliCost() {
  const p = STATE.program;
  if (!p) return;
  const altiliRaces = getAltiliRaces();
  const unitCost = parseFloat(document.getElementById('altiliUnitCost').value) || 1.25;

  let combinations = 1;
  let hitProb = 1.0;

  altiliRaces.forEach((r, idx) => {
    const sel = STATE.altiliSelections[idx] || new Set();
    combinations *= Math.max(1, sel.size);
    let legProbSum = 0;
    const covered = ekuriCovered(r, sel);
    r.horses.forEach(h => {
      if (sel.has(h.no) || covered.has(h.no)) legProbSum += h.simulation.win_prob;
    });
    hitProb *= Math.min(99.0, legProbSum) / 100.0;
  });

  const totalCost = (combinations * unitCost).toFixed(2);
  const totalProbPct = (hitProb * 100).toFixed(1);

  document.getElementById('altiliStatsBar').innerHTML = `
    <div>
      <div class="text-[10px] text-[#B7C9BE] uppercase">Kombinasyon</div>
      <div class="text-sm font-extrabold text-white mono">${combinations}</div>
    </div>
    <div>
      <div class="text-[10px] text-[#B7C9BE] uppercase">Kupon Tutarı</div>
      <div class="text-sm font-extrabold text-[#E6C762] mono">${totalCost} ₺</div>
    </div>
    <div>
      <div class="text-[10px] text-[#B7C9BE] uppercase">Kapsama Gücü</div>
      <div class="text-sm font-extrabold text-white mono">%${totalProbPct}</div>
    </div>
  `;
}

function openHorseModal(horseNo) {
  let race = STATE.program.races[STATE.currentRaceIndex];
  let h = race && race.horses.find(x => x.no === horseNo);
  if (!h) return;  // seçili koşuda bu numarada koşan at yok (örn. koşmaz)

  const modal = document.getElementById('horseModal');
  const content = document.getElementById('horseModalContent');
  const takiLegend = STATE.program.taki_legend || {};

  const metricBar = (label, val, color = 'bg-[#0F291E]') => `
    <div>
      <div class="flex justify-between text-xs mb-1">
        <span class="text-[#44403C] font-semibold">${label}</span>
        <span class="mono font-bold text-[#18181B]">${val} / 100</span>
      </div>
      <div class="h-2 bg-[#E8E3D8] rounded-full overflow-hidden">
        <div class="h-full ${color} rounded-full" style="width: ${Math.min(100, val)}%"></div>
      </div>
    </div>
  `;

  content.innerHTML = `
    <div class="flex items-start justify-between gap-3 mb-3 pb-2.5 border-b border-[#D8D2C5]">
      <div class="flex items-center gap-3">
        ${h.forma_url ? `<img src="${h.forma_url}" class="w-12 h-12 rounded-xl bg-white p-1 border border-[#D8D2C5]" />` : ''}
        <div>
          <div class="flex items-center gap-2">
            <span class="px-2 py-0.5 rounded bg-[#0F291E] text-[#E6C762] font-extrabold text-xs mono">#${h.no}</span>
            <h2 class="serif-title text-xl font-bold text-[#0F291E]">${h.name}</h2>
          </div>
          <p class="text-xs text-[#57534E] mt-0.5">${h.age} • Baba: <strong class="text-[#18181B]">${h.sire}</strong> • Anne: <strong class="text-[#18181B]">${h.dam}</strong></p>
        </div>
      </div>
      <button onclick="closeHorseModal()" class="w-8 h-8 rounded-full bg-[#E8E3D8] text-[#18181B] font-bold">✕</button>
    </div>

    <!-- Key Summary Boxes -->
    <div class="grid grid-cols-4 gap-2 mb-3.5 text-center">
      <div class="bg-[#0F291E] text-[#F9F6F0] rounded-lg p-2">
        <div class="text-[9px] text-[#D4AF37] font-bold">BÜLTEN PUANI</div>
        <div class="text-base font-extrabold mono">${h.quant_score}</div>
      </div>
      <div class="bg-white border border-[#D8D2C5] rounded-lg p-2">
        <div class="text-[9px] text-[#57534E] font-bold">KAZANMA %</div>
        <div class="text-base font-extrabold text-[#0F291E] mono">%${h.simulation.win_prob}</div>
      </div>
      <div class="bg-white border border-[#D8D2C5] rounded-lg p-2">
        <div class="text-[9px] text-[#57534E] font-bold">İLK 3 (TABELA)</div>
        <div class="text-base font-extrabold text-[#8C5A3C] mono">%${h.simulation.top3_prob}</div>
      </div>
      <div class="bg-white border border-[#D8D2C5] rounded-lg p-2">
        <div class="text-[9px] text-[#57534E] font-bold">EDER GANYAN</div>
        <div class="text-base font-extrabold text-[#7A1C1C] mono">${h.simulation.fair_ganyan}</div>
      </div>
    </div>

    <!-- Künye Bilgileri -->
    <div class="bg-white rounded-xl p-3 border border-[#D8D2C5] mb-3.5 grid grid-cols-2 gap-2 text-xs">
      <div><span class="text-[#57534E]">Jokey:</span> <strong class="text-[#18181B]">${h.jockey}</strong></div>
      <div><span class="text-[#57534E]">Sıklet / Kulvar:</span> <strong class="text-[#18181B]">${h.weight} kg / St: ${h.start_no}</strong></div>
      <div><span class="text-[#57534E]">Antrenör:</span> <strong class="text-[#18181B]">${h.trainer}</strong></div>
      <div><span class="text-[#57534E]">Sahip:</span> <strong class="text-[#18181B]">${h.owner}</strong></div>
      <div><span class="text-[#57534E]">KGS (Dinlenme):</span> <strong class="text-[#0F291E]">${h.metrics.kgs_status}</strong></div>
      <div><span class="text-[#57534E]">En İyi Derece:</span> <strong class="text-[#7A1C1C] mono">${h.best_time || 'İlk / Kayıtsız'}</strong></div>
    </div>

    <!-- Koşu Stili Gerekçesi -->
    ${(h.running_style.factors && h.running_style.factors.length) ? `
      <div class="bg-white rounded-xl p-3 border border-[#D8D2C5] mb-3.5">
        <div class="flex items-center justify-between mb-1.5">
          <h4 class="serif-title text-sm font-bold text-[#0F291E] uppercase">🏁 Koşu Stili: ${h.running_style.label}</h4>
          <span class="text-[10px] mono text-[#57534E] font-bold">Erken Hız Endeksi ${h.running_style.index} • Sahada ${h.running_style.field_rank}.</span>
        </div>
        <div class="flex flex-wrap gap-1">
          ${h.running_style.factors.map(f => `<span class="px-1.5 py-0.5 rounded bg-[#FAF8F3] border border-[#E2DDD2] text-[10px] text-[#44403C]">${f}</span>`).join('')}
        </div>
      </div>
    ` : ''}

    <!-- Komiser / Başhandikapçı Değerlendirmesi -->
    <div class="bg-[#F7F1E3] border border-[#A67C37]/50 rounded-xl p-3 mb-3.5">
      <h4 class="serif-title text-sm font-bold text-[#0F291E] uppercase mb-1">✒️ Başhandikapçı &amp; Bülten Notu</h4>
      <ul class="space-y-1 text-xs text-[#18181B] list-disc pl-4 font-medium">
        ${h.xai_notes.map(n => `<li>${cleanXaiNote(n)}</li>`).join('')}
      </ul>
    </div>

    <!-- DERECE KARNESİ: YARIŞ BAŞINA 1.-2.-3.-4.-5.'LİK -->
    <div id="careerKarneSection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ Derece karnesi (1.–5.'lik yüzdeleri) yükleniyor...</div>
    </div>

    <!-- SON İDMAN: GALOP / SPRINT / KENTER -->
    <div id="idmanSection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ Son idman bilgisi yükleniyor...</div>
    </div>

    <!-- YILLIK KARNE & KAZANÇ -->
    <div id="yearlySection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ Yıllık karne ve kazanç yükleniyor...</div>
    </div>

    <!-- PİST / ZEMİN / MEVSİM / MESAFE KIRILIMI -->
    <div id="breakdownSection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ Pist, zemin, mevsim ve mesafe başarı kırılımı yükleniyor...</div>
    </div>

    <!-- AYNI MESAFE, FARKLI İLLER -->
    <div id="distanceCitySection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ Mesafe × il dereceleri yükleniyor...</div>
    </div>

    <!-- İL (HİPODROM) KARNESİ -->
    <div id="citySection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ İl il hipodrom karnesi yükleniyor...</div>
    </div>

    <!-- KOŞU SINIFI KARNESİ: GRUP / KV / ŞARTLI / HANDİKAP -->
    <div id="classKarneSection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ Koşu sınıfı karnesi yükleniyor...</div>
    </div>

    <!-- HANDİKAP PUANI SEYRİ -->
    <div id="hpHistorySection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ Handikap puanı seyri yükleniyor...</div>
    </div>

    <!-- 1. PİST SEVGİSİ (KUM vs ÇİM vs SENTETİK) & MESAFE KARNESİ -->
    <div id="surfaceAffinitySection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ Çim / Kum / Sentetik pist karnesi ve mesafe istatistikleri yükleniyor...</div>
    </div>

    <!-- 2. 3 KUŞAK SOYAĞACI (PEDİGRİ: BABA, ANNE, KISRAK BABASI VE ATALARI) -->
    <div id="pedigreeTreeSection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ 3 Kuşak Soyağacı (Pedigri) tablosu yükleniyor...</div>
    </div>

    <!-- AKRABA İSTATİSTİKLERİ: BABA / ANNE / KISRAK BABASI / KARDEŞLER -->
    <div id="relativesSection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ Baba, anne, kısrak babası ve kardeş istatistikleri yükleniyor...</div>
    </div>

    <!-- PİST BAZINDA KOŞULAR (ÇİM / KUM / SENTETİK) -->
    <div id="surfaceRacesSection" class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="text-xs text-[#57534E] py-2 text-center">⏳ Çim / Kum / Sentetik koşu listeleri yükleniyor...</div>
    </div>

    <!-- 3. ÖZEL PERFORMANS VE DERECE BÜLTENİ TABLOSU -->
    <div class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 mb-3.5">
      <div class="mb-2">
        <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">📑 Özel Performans ve Derece Bülteni (Geçmiş Koşular)</h4>
        <p class="text-[10px] text-[#57534E]">Pist Durumu (P.D), Koşu Zorluğu, Harf Formu (A-E), Bugünkü Kiloya (${h.weight}kg) Göre Performans ve 1200m Normalize Derece</p>
      </div>
      <div id="bankomatHistoryTable" class="text-xs text-[#57534E] py-3 text-center">
        ⏳ Safkanın geçmiş koşu dereceleri derleniyor...
      </div>
    </div>

    <!-- 6-Dimension Bar Breakdown -->
    <div class="bg-white border border-[#D8D2C5] rounded-xl p-3.5 space-y-2">
      <h4 class="serif-title text-sm font-bold text-[#0F291E] uppercase">📊 Handikap Terazi Dağılımı</h4>
      ${metricBar('Kronometre & Derece Puanı', h.metrics.speed_figure, 'bg-[#0F291E]')}
      ${metricBar('Son 6 Koşu Form & Pist Uyumu', h.metrics.form_rating, 'bg-[#1B4332]')}
      ${metricBar('Handikap & Grup Klası', h.metrics.class_rating, 'bg-[#8C5A3C]')}
      ${metricBar('Jokey & Eküri İstikrarı (s20)', h.metrics.jockey_rating, 'bg-[#A67C37]')}
      ${metricBar('Sıklet (Kilo) & Mesafe Uyumu', h.metrics.weight_rating, 'bg-[#7A1C1C]')}
    </div>
  `;

  modal.classList.remove('hidden');

  const token = ++HORSE_MODAL_TOKEN;
  const showHistoryError = (msg) => {
    if (token !== HORSE_MODAL_TOKEN) return;
    const errHTML = `<div class="text-xs text-[#7A1C1C] py-2 text-center">⚠️ ${esc(msg)}</div>`;
    ['surfaceAffinitySection', 'pedigreeTreeSection', 'bankomatHistoryTable', 'careerKarneSection', 'classKarneSection', 'hpHistorySection', 'yearlySection', 'citySection', 'surfaceRacesSection', 'relativesSection', 'breakdownSection', 'distanceCitySection'].forEach(id => {
      const el = document.getElementById(id);
      if (el) el.innerHTML = errHTML;
    });
  };

  // Son idman (koşu idman tablosundan)
  const paintIdman = () => {
    if (token !== HORSE_MODAL_TOKEN) return;
    const el = document.getElementById('idmanSection');
    if (el) el.innerHTML = modalIdmanHTML(race, h);
  };
  paintIdman();
  ensureRaceIdman(race).then(paintIdman);

  if (!h.at_id) {
    showHistoryError(race.kosu_kodu
      ? 'Bu safkanın TJK kimliği bültende bulunamadı; geçmiş koşu, soyağacı ve pist karnesi çekilemedi.'
      : 'Yabancı hipodrom: TJK bu safkanın geçmiş koşu, soyağacı ve idman kayıtlarını yayınlamıyor.');
  } else {
    const irk = (race.age_group || '').toLowerCase().includes('arap') ? '2' : ((race.age_group || '').toLowerCase().includes('ingiliz') ? '1' : '');
    fetchJSON(`/api/horse_relatives?at_id=${encodeURIComponent(h.at_id)}&irk=${irk}`)
      .then(rel => {
        if (token !== HORSE_MODAL_TOKEN) return;
        const el = document.getElementById('relativesSection');
        if (el) el.innerHTML = relativesHTML(rel, h, race);
      })
      .catch(err => {
        if (token !== HORSE_MODAL_TOKEN) return;
        const el = document.getElementById('relativesSection');
        if (el) el.innerHTML = `<div class="text-xs text-[#7A1C1C] py-2 text-center">⚠️ Akraba istatistikleri alınamadı: ${esc(err.message)}</div>`;
      });

    const isArab = (race.age_group || '').toLowerCase().includes('arap') ? '1' : '0';
    fetch(`/api/horse_history?at_id=${encodeURIComponent(h.at_id)}&weight=${encodeURIComponent(h.weight)}&dist=${encodeURIComponent(race.distance_m)}&arab=${isArab}`)
      .then(r => r.json())
      .then(json => {
        // Kullanıcı bu arada başka safkan açtıysa eski yanıtı yazma
        if (token !== HORSE_MODAL_TOKEN) return;
        const surfEl = document.getElementById('surfaceAffinitySection');
        const pedEl = document.getElementById('pedigreeTreeSection');
        const tblEl = document.getElementById('bankomatHistoryTable');
        if (!json.ok || !json.data) {
          showHistoryError(`TJK safkan sayfası alınamadı${json.error ? ': ' + json.error : ''}`);
          return;
        }

        const d = deepEscape(json.data);
        d._full = true;  // tüm mesafeler ve pist listeleri dahil
        STATE.extras[h.at_id] = d;
        if (isCurrentRace(race)) paintHorseExtras(race, h);
        const ck = document.getElementById('careerKarneSection');
        if (ck) ck.innerHTML = careerKarneHTML(d);
        const cl = document.getElementById('classKarneSection');
        if (cl) cl.innerHTML = classKarneHTML(d);
        const hpEl = document.getElementById('hpHistorySection');
        if (hpEl) hpEl.innerHTML = hpHistoryHTML(d);
        const yEl = document.getElementById('yearlySection');
        if (yEl) yEl.innerHTML = yearlyHTML(d);
        const cEl = document.getElementById('citySection');
        if (cEl) cEl.innerHTML = cityHTML(d);
        const bEl = document.getElementById('breakdownSection');
        if (bEl) bEl.innerHTML = `
          <div class="mb-2">
            <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">🌦️ Pist • Zemin • Mevsim • Mesafe Başarısı</h4>
            <p class="text-[10px] text-[#57534E]">Tüm kariyer (${d.breakdowns ? d.breakdowns.races : 0} koşu) • yarış başına yüzdeler</p>
          </div>${breakdownHTML(d.breakdowns, race)}`;
        const dEl = document.getElementById('distanceCitySection');
        if (dEl) dEl.innerHTML = distanceCityHTML(d, race);
        STATE.modalSurfaceRaces = d.surface_races || {};
        const todaySurf = (race.surface || '').includes('Çim') ? 'Çim' : (race.surface || '').includes('Sentetik') ? 'Sentetik' : 'Kum';
        renderSurfaceRaces(STATE.modalSurfaceRaces[todaySurf] && STATE.modalSurfaceRaces[todaySurf].length ? todaySurf
          : (['Çim', 'Kum', 'Sentetik'].find(k => (STATE.modalSurfaceRaces[k] || []).length) || todaySurf));

        // A) Surface Affinity (Kum vs Çim vs Sentetik)
        if (surfEl) {
          const ss = d.surface_summary || {};
          const surfaces = [
            { key: 'Çim', icon: '🌱', barCls: 'bg-[#1B4332]', badgeCls: 'text-[#0F291E] bg-[#1B4332]/10 border-[#1B4332]/30' },
            { key: 'Kum', icon: '🏜️', barCls: 'bg-[#8C5A3C]', badgeCls: 'text-[#8C5A3C] bg-[#8C5A3C]/10 border-[#8C5A3C]/30' },
            { key: 'Sentetik', icon: '⚡', barCls: 'bg-[#3F3F46]', badgeCls: 'text-[#3F3F46] bg-[#E4E4E7] border-[#A1A1AA]' }
          ];
          const tot = ss['TOPLAM'] || { starts: 0, w1: 0, w2: 0, w3: 0, w4: 0, earnings: '0 t' };

          const ds = d.detailed_stats || {};
          const pistDurumRows = ds['Pist'] || [];
          const mesafeKey = Object.keys(ds).find(k => k.startsWith('Mesafe')) || '';
          const mesafeRows = ds[mesafeKey] || [];

          surfEl.innerHTML = `
            <div class="flex items-center justify-between mb-2">
              <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">🏟️ Pist Yatkınlığı (Çim • Kum • Sentetik)</h4>
              <span class="text-[10px] text-[#57534E] font-bold mono">Toplam: ${tot.starts} Koşu (${tot.w1} Birincilik)</span>
            </div>
            <div class="mb-3 px-3 py-2 rounded-lg bg-[#0F291E] text-[#F9F6F0] text-xs font-bold flex items-center gap-2">
              <span class="text-[#D4AF37]">🏆</span>
              <span>${d.fav_surface_verdict || ''}</span>
            </div>

            <div class="space-y-2 mb-3">
              ${surfaces.map(sf => {
                const st = ss[sf.key] || { starts: 0, w1: 0, w2: 0, w3: 0, w4: 0, win_pct: 0, podium_pct: 0, tabela_pct: 0, affinity_score: 0, earnings: '0 t' };
                return `
                  <div class="bg-[#FAF8F3] rounded-lg p-2.5 border border-[#E2DDD2]">
                    <div class="flex items-center justify-between text-xs mb-1">
                      <div class="flex items-center gap-1.5">
                        <span>${sf.icon}</span>
                        <strong class="text-[#18181B] uppercase">${sf.key} PİST</strong>
                        <span class="px-1.5 py-0.5 rounded border text-[10px] font-bold mono ${sf.badgeCls}">
                          ${st.starts} Koşu: ${st.w1}-${st.w2}-${st.w3}-${st.w4}-${st.w5 || 0}
                        </span>
                      </div>
                      <span class="text-[11px] font-extrabold text-[#0F291E] mono">Uyum: ${st.affinity_score}/100</span>
                    </div>
                    <div class="h-2 bg-[#E8E3D8] rounded-full overflow-hidden mb-1.5">
                      <div class="h-full ${sf.barCls} rounded-full" style="width: ${st.affinity_score}%"></div>
                    </div>
                    <div class="flex items-center justify-between text-[10px] text-[#57534E] mono">
                      <span>Birincilik: <strong class="text-[#18181B]">%${st.win_pct}</strong></span>
                      <span>İlk 3: <strong class="text-[#0F291E]">%${st.podium_pct}</strong></span>
                      <span>Tabela: <strong class="text-[#8C5A3C]">%${st.tabela_pct}</strong></span>
                      <span>Kazanç: <strong class="text-[#18181B]">${st.earnings}</strong></span>
                    </div>
                  </div>
                `;
              }).join('')}
            </div>

            ${(pistDurumRows.length || mesafeRows.length) ? `
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-2 pt-2 border-t border-[#E2DDD2] text-[11px]">
                ${pistDurumRows.length ? `
                  <div class="bg-[#FAF8F3] rounded-lg p-2.5 border border-[#E2DDD2]">
                    <div class="text-[10px] font-extrabold text-[#8C5A3C] uppercase mb-1">🌧️ Zemin Durumu Karnesi</div>
                    <div class="space-y-1 mono text-[10px]">
                      ${pistDurumRows.map(r => `
                        <div class="flex justify-between">
                          <span class="text-[#44403C]">${r[0]} (${r[1]})</span>
                          <span class="text-[#18181B] font-bold">${r[2]}K • 1.(${r[3]}) 2.(${r[4]}) 3.(${r[5]})</span>
                        </div>
                      `).join('')}
                    </div>
                  </div>
                ` : ''}
                ${mesafeRows.length ? `
                  <div class="bg-[#FAF8F3] rounded-lg p-2.5 border border-[#E2DDD2]">
                    <div class="text-[10px] font-extrabold text-[#0F291E] uppercase mb-1">📏 Mesafe Karnesi (${mesafeKey})</div>
                    <div class="space-y-1 mono text-[10px]">
                      ${mesafeRows.map(r => `
                        <div class="flex justify-between">
                          <span class="text-[#44403C]">${r[0]}m</span>
                          <span class="text-[#18181B] font-bold">${r[1]}K • 1.(${r[2]}) 2.(${r[3]}) 3.(${r[4]})</span>
                        </div>
                      `).join('')}
                    </div>
                  </div>
                ` : ''}
              </div>
            ` : ''}
          `;
        }

        // B) 3-Generation Pedigree Tree
        if (pedEl) {
          const p = d.pedigree;
          if (!p) {
            pedEl.innerHTML = `
              <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase mb-2">🧬 Soyağacı (Orijin)</h4>
              <div class="grid grid-cols-3 gap-2 text-xs">
                <div class="bg-[#FAF8F3] p-2.5 rounded-lg border border-[#E2DDD2]"><span class="text-[#57534E] block text-[10px]">BABA</span><strong class="text-[#18181B]">${h.sire}</strong></div>
                <div class="bg-[#FAF8F3] p-2.5 rounded-lg border border-[#E2DDD2]"><span class="text-[#57534E] block text-[10px]">ANNE</span><strong class="text-[#18181B]">${h.dam}</strong></div>
                <div class="bg-[#FAF8F3] p-2.5 rounded-lg border border-[#E2DDD2]"><span class="text-[#57534E] block text-[10px]">KISRAK BABASI</span><strong class="text-[#7A1C1C]">${h.damsire || '-'}</strong></div>
              </div>
            `;
          } else {
            pedEl.innerHTML = `
              <div class="mb-2.5">
                <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">🧬 3 Kuşak Soyağacı (Pedigri Şeması)</h4>
                <p class="text-[10px] text-[#57534E]">Baba, Anne, Kısrak Babası ve 3. Kuşak Kan Hattı</p>
              </div>

              <div class="space-y-2.5 text-[11px]">
                <!-- BABA HATTI -->
                <div class="bg-[#FAF8F3] border border-[#0F291E]/30 rounded-xl p-2.5">
                  <div class="text-[9px] font-extrabold text-[#0F291E] uppercase mb-1.5">♂ BABA KAN HATTI (AYGIR SOYU)</div>
                  <div class="grid grid-cols-12 gap-1.5 items-center">
                    <div class="col-span-4 bg-[#0F291E] text-[#F9F6F0] p-2 rounded-lg">
                      <div class="text-[9px] text-[#D4AF37] font-bold">BABA</div>
                      <div class="font-extrabold text-xs leading-tight">${p.baba}</div>
                    </div>
                    <div class="col-span-4 space-y-1.5">
                      <div class="bg-white p-1.5 rounded border border-[#D8D2C5]">
                        <div class="text-[8px] text-[#57534E]">Baba Babası</div>
                        <div class="font-bold text-[#18181B] text-[10px] leading-tight">${p.baba_baba}</div>
                      </div>
                      <div class="bg-white p-1.5 rounded border border-[#D8D2C5]">
                        <div class="text-[8px] text-[#57534E]">Baba Annesi</div>
                        <div class="font-bold text-[#18181B] text-[10px] leading-tight">${p.baba_anne}</div>
                      </div>
                    </div>
                    <div class="col-span-4 space-y-1 text-[9px] text-[#44403C]">
                      <div class="bg-white px-1.5 py-0.5 rounded border border-[#E2DDD2] truncate">♂ ${p.baba_baba_baba}</div>
                      <div class="bg-white px-1.5 py-0.5 rounded border border-[#E2DDD2] truncate">♀ ${p.baba_baba_anne}</div>
                      <div class="bg-white px-1.5 py-0.5 rounded border border-[#E2DDD2] truncate">♂ ${p.baba_anne_baba}</div>
                      <div class="bg-white px-1.5 py-0.5 rounded border border-[#E2DDD2] truncate">♀ ${p.baba_anne_anne}</div>
                    </div>
                  </div>
                </div>

                <!-- ANNE & KISRAK BABASI HATTI -->
                <div class="bg-[#FAF8F3] border border-[#8C5A3C]/40 rounded-xl p-2.5">
                  <div class="text-[9px] font-extrabold text-[#7A1C1C] uppercase mb-1.5">♀ ANNE KAN HATTI (KISRAK &amp; KISRAK BABASI)</div>
                  <div class="grid grid-cols-12 gap-1.5 items-center">
                    <div class="col-span-4 bg-[#7A1C1C] text-white p-2 rounded-lg">
                      <div class="text-[9px] text-[#F7F1E3] font-bold">ANNE</div>
                      <div class="font-extrabold text-xs leading-tight">${p.anne}</div>
                    </div>
                    <div class="col-span-4 space-y-1.5">
                      <div class="bg-[#F7F1E3] p-1.5 rounded border border-[#A67C37]">
                        <div class="text-[8px] text-[#7A1C1C] font-bold">Kısrak Babası</div>
                        <div class="font-extrabold text-[#18181B] text-[10px] leading-tight">${p.anne_baba}</div>
                      </div>
                      <div class="bg-white p-1.5 rounded border border-[#D8D2C5]">
                        <div class="text-[8px] text-[#57534E]">Anne Annesi</div>
                        <div class="font-bold text-[#18181B] text-[10px] leading-tight">${p.anne_anne}</div>
                      </div>
                    </div>
                    <div class="col-span-4 space-y-1 text-[9px] text-[#44403C]">
                      <div class="bg-white px-1.5 py-0.5 rounded border border-[#E2DDD2] truncate">♂ ${p.anne_baba_baba}</div>
                      <div class="bg-white px-1.5 py-0.5 rounded border border-[#E2DDD2] truncate">♀ ${p.anne_baba_anne}</div>
                      <div class="bg-white px-1.5 py-0.5 rounded border border-[#E2DDD2] truncate">♂ ${p.anne_anne_baba}</div>
                      <div class="bg-white px-1.5 py-0.5 rounded border border-[#E2DDD2] truncate">♀ ${p.anne_anne_anne}</div>
                    </div>
                  </div>
                </div>
              </div>
            `;
          }
        }

        // C) Past Races Performance Table
        if (!tblEl) return;
        if (!d.races || !d.races.length) {
          tblEl.innerHTML = `<span class="text-[#78716C]">Bu safkanın geçmiş yarış kaydı bulunamadı.</span>`;
          return;
        }
        const rows = d.races;
        const sum = d.summary;
        const gradeBadge = g => {
          const map = {
            A: 'bg-[#0F291E] text-[#E6C762]',
            B: 'bg-[#1B4332]/15 text-[#0F291E] border border-[#1B4332]/30',
            C: 'bg-[#F7F1E3] text-[#8C5A3C] border border-[#A67C37]/40',
            D: 'bg-[#EFECE4] text-[#57534E]',
            E: 'bg-[#7A1C1C]/10 text-[#7A1C1C]'
          };
          return `<span class="px-1.5 py-0.5 rounded font-extrabold text-[10px] mono ${map[g] || map.C}">${g}</span>`;
        };

        tblEl.innerHTML = `
          <div class="grid grid-cols-3 gap-2 mb-3 text-center bg-[#FAF8F3] p-2 rounded-lg border border-[#E2DDD2]">
            <div>
              <div class="text-[9px] text-[#57534E] font-bold uppercase">ORT. PERFORMANS</div>
              <div class="text-xs font-extrabold text-[#0F291E] mono">${sum.avg_performans} <span class="text-[10px] text-[#57534E]">(Max: ${sum.max_performans})</span></div>
            </div>
            <div>
              <div class="text-[9px] text-[#57534E] font-bold uppercase">KİLOYA GÖRE ORT.</div>
              <div class="text-xs font-extrabold text-[#18181B] mono">${sum.avg_kiloya_gore}</div>
            </div>
            <div>
              <div class="text-[9px] text-[#57534E] font-bold uppercase">ORTALAMA POZ.</div>
              <div class="text-xs font-extrabold text-[#8C5A3C] mono">%${sum.avg_poz_pct}</div>
            </div>
          </div>
          <div class="overflow-x-auto -mx-1">
            <table class="w-full text-left text-[11px] whitespace-nowrap">
              <thead>
                <tr class="text-[9px] text-[#57534E] border-b border-[#D8D2C5] uppercase">
                  <th class="pb-1.5 px-1">Tarih/Şehir</th>
                  <th class="pb-1.5 px-1">Şart</th>
                  <th class="pb-1.5 px-1">Pist</th>
                  <th class="pb-1.5 px-1">P.D</th>
                  <th class="pb-1.5 px-1 text-right">HP</th>
                  <th class="pb-1.5 px-1 text-center">Sıra</th>
                  <th class="pb-1.5 px-1 text-right">Perf.</th>
                  <th class="pb-1.5 px-1 text-right">Zorluk</th>
                  <th class="pb-1.5 px-1 text-center">Form</th>
                  <th class="pb-1.5 px-1 text-right">Kiloya G.</th>
                  <th class="pb-1.5 px-1 text-right" title="1200m Normalize Derece">Norm.Der</th>
                  <th class="pb-1.5 px-1 text-right">Poz%</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-[#E8E3D8] mono">
                ${rows.map(r => `
                  <tr class="hover:bg-[#FAF8F3]">
                    <td class="py-1.5 px-1 text-[#18181B] font-sans"><strong>${r.tarih.slice(0,5)}</strong> <span class="text-[10px] text-[#57534E]">${r.sehir}</span></td>
                    <td class="py-1.5 px-1 text-[#44403C] font-sans truncate max-w-[90px]" title="${r.sart}">${r.sart}</td>
                    <td class="py-1.5 px-1 text-[#18181B] font-semibold">${r.pist}</td>
                    <td class="py-1.5 px-1 text-[#8C5A3C] font-bold" title="${r.pd_label || ''}${r.penetrometre ? ' • Penetrometre ' + r.penetrometre : ''}">${r.pd}${r.penetrometre ? `<span class="text-[9px] text-[#78716C]"> ${r.penetrometre}</span>` : ''}</td>
                    <td class="py-1.5 px-1 text-right text-[#18181B] font-bold">${r.hp}</td>
                    <td class="py-1.5 px-1 text-center font-extrabold ${r.sira <= 3 ? 'text-[#0F291E]' : 'text-[#57534E]'}">${r.sira}</td>
                    <td class="py-1.5 px-1 text-right font-bold text-[#18181B]">${r.performans.toFixed(2)}</td>
                    <td class="py-1.5 px-1 text-right text-[#57534E]">${r.zorluk.toFixed(1)}</td>
                    <td class="py-1.5 px-1 text-center">${gradeBadge(r.form)}</td>
                    <td class="py-1.5 px-1 text-right font-bold text-[#0F291E]">${r.kiloya_gore.toFixed(2)}</td>
                    <td class="py-1.5 px-1 text-right text-[#7A1C1C] font-bold" title="Gerçek Derece: ${r.gercek_derece}">${r.norm_1200_derece}</td>
                    <td class="py-1.5 px-1 text-right text-[#44403C]">%${r.poz_pct}</td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        `;
      })
      .catch(err => showHistoryError(`TJK bağlantı hatası: ${err.message}`));
  }
}

const SURF_STYLE = {
  'Çim': { icon: '🌱', on: 'bg-[#1B4332] text-[#F9F6F0]', text: 'text-[#0F291E]' },
  'Kum': { icon: '🏜️', on: 'bg-[#8C5A3C] text-[#F9F6F0]', text: 'text-[#8C5A3C]' },
  'Sentetik': { icon: '⚡', on: 'bg-[#3F3F46] text-[#F9F6F0]', text: 'text-[#3F3F46]' },
};

function surfMix(sf) {
  if (!sf) return '';
  return ['Çim', 'Kum', 'Sentetik'].filter(k => sf[k]).map(k => `<span class="${SURF_STYLE[k].text}">${k[0]}${sf[k]}</span>`).join(' ');
}

function yearlyHTML(d) {
  const rows = d.yearly || [];
  const head = `<h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">📅 Yıllık Karne &amp; Kazanç</h4>`;
  if (!rows.length) return head + '<div class="text-xs text-[#78716C] mt-1">Henüz koşu kaydı yok.</div>';
  return `
    <div class="mb-2">${head}<p class="text-[10px] text-[#57534E]">Her yılın koşu sayısı, 1.–5.'likleri, pist dağılımı (Ç/K/S) ve TJK resmi kazancı</p></div>
    <div class="overflow-x-auto -mx-1">
      <table class="w-full text-[11px] whitespace-nowrap mono">
        <thead><tr class="text-[9px] text-[#57534E] border-b border-[#D8D2C5] uppercase font-sans">
          <th class="pb-1 px-1 text-left">Yıl</th><th class="pb-1 px-1 text-right">K</th><th class="pb-1 px-1 text-right">1-2-3-4-5</th>
          <th class="pb-1 px-1 text-right">1.%</th><th class="pb-1 px-1 text-right">İlk 3</th><th class="pb-1 px-1 text-right">Pist</th>
          <th class="pb-1 px-1 text-right">Kazanç</th>
        </tr></thead>
        <tbody class="divide-y divide-[#E8E3D8]">
          ${rows.map(y => `
            <tr>
              <td class="py-1.5 px-1 font-sans font-extrabold text-[#0F291E]">${y.year}</td>
              <td class="py-1.5 px-1 text-right">${y.starts}</td>
              <td class="py-1.5 px-1 text-right text-[#57534E]">${y.w1}-${y.w2}-${y.w3}-${y.w4}-${y.w5}</td>
              <td class="py-1.5 px-1 text-right font-bold text-[#0F291E]">%${y.win_pct}</td>
              <td class="py-1.5 px-1 text-right font-bold">%${y.top3_pct}</td>
              <td class="py-1.5 px-1 text-right text-[10px] font-bold">${surfMix(y.surfaces)}</td>
              <td class="py-1.5 px-1 text-right font-extrabold text-[#8C5A3C]">${y.earnings || '-'}</td>
            </tr>`).join('')}
        </tbody>
      </table>
    </div>
  `;
}

function cityHTML(d) {
  const rows = d.by_city || [];
  const head = `<h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">🗺️ İl İl Hipodrom Karnesi</h4>`;
  if (!rows.length) return head + '<div class="text-xs text-[#78716C] mt-1">Henüz koşu kaydı yok.</div>';
  const best = rows.filter(r => r.starts >= 2).sort((a, b) => b.top3_pct - a.top3_pct)[0];
  return `
    <div class="flex items-start justify-between gap-2 mb-2">
      <div>${head}<p class="text-[10px] text-[#57534E]">Tüm kariyer • İkramiye: koşulardan kazanılan yarış ikramiyelerinin toplamı</p></div>
      ${best ? `<span class="px-2 py-0.5 rounded bg-[#F7F1E3] border border-[#A67C37] text-[10px] font-bold text-[#8C5A3C] whitespace-nowrap">En iyi: ${best.city} %${best.top3_pct}</span>` : ''}
    </div>
    <div class="overflow-x-auto -mx-1">
      <table class="w-full text-[11px] whitespace-nowrap mono">
        <thead><tr class="text-[9px] text-[#57534E] border-b border-[#D8D2C5] uppercase font-sans">
          <th class="pb-1 px-1 text-left">Hipodrom</th><th class="pb-1 px-1 text-right">K</th><th class="pb-1 px-1 text-right">1-2-3</th>
          <th class="pb-1 px-1 text-right">1.%</th><th class="pb-1 px-1 text-right">İlk 3</th><th class="pb-1 px-1 text-right">İlk 5</th>
          <th class="pb-1 px-1 text-right">Ort.S</th><th class="pb-1 px-1 text-right">Pist</th><th class="pb-1 px-1 text-right">İkramiye</th>
        </tr></thead>
        <tbody class="divide-y divide-[#E8E3D8]">
          ${rows.map(c => `
            <tr>
              <td class="py-1.5 px-1 font-sans font-extrabold text-[#18181B]">${c.city}</td>
              <td class="py-1.5 px-1 text-right">${c.starts}</td>
              <td class="py-1.5 px-1 text-right text-[#57534E]">${c.w1}-${c.w2}-${c.w3}</td>
              <td class="py-1.5 px-1 text-right font-bold text-[#0F291E]">%${c.win_pct}</td>
              <td class="py-1.5 px-1 text-right font-extrabold text-[#0F291E]">%${c.top3_pct}</td>
              <td class="py-1.5 px-1 text-right">%${c.top5_pct}</td>
              <td class="py-1.5 px-1 text-right text-[#57534E]">${c.avg_pos ?? '-'}</td>
              <td class="py-1.5 px-1 text-right text-[10px] font-bold">${surfMix(c.surfaces)}</td>
              <td class="py-1.5 px-1 text-right font-bold text-[#8C5A3C]">${c.earnings}</td>
            </tr>`).join('')}
        </tbody>
      </table>
    </div>
  `;
}

function renderSurfaceRaces(active) {
  const el = document.getElementById('surfaceRacesSection');
  if (!el) return;
  const all = STATE.modalSurfaceRaces || {};
  const list = all[active] || [];
  el.innerHTML = `
    <div class="flex items-center justify-between mb-2 gap-2 flex-wrap">
      <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">🏟️ Pist Bazında Koşular</h4>
      <div class="flex gap-1">
        ${['Çim', 'Kum', 'Sentetik'].map(k => `
          <button onclick="renderSurfaceRaces('${k}')" class="px-2.5 py-1 rounded-md text-[11px] font-bold ${k === active ? SURF_STYLE[k].on : 'bg-[#EFECE4] text-[#44403C]'}">
            ${SURF_STYLE[k].icon} ${k} <span class="mono opacity-80">(${(all[k] || []).length})</span>
          </button>`).join('')}
      </div>
    </div>
    ${list.length ? `
      <div class="overflow-x-auto -mx-1 max-h-80 overflow-y-auto">
        <table class="w-full text-[11px] whitespace-nowrap mono">
          <thead class="sticky top-0 bg-white"><tr class="text-[9px] text-[#57534E] border-b border-[#D8D2C5] uppercase font-sans">
            <th class="pb-1 px-1 text-left">Tarih</th><th class="pb-1 px-1 text-left">Şehir</th><th class="pb-1 px-1 text-right">Msf</th>
            <th class="pb-1 px-1">P.D</th><th class="pb-1 px-1 text-center">Sıra</th><th class="pb-1 px-1 text-right">Derece</th>
            <th class="pb-1 px-1 text-left">Sınıf</th><th class="pb-1 px-1 text-right">Kilo</th><th class="pb-1 px-1 text-left">Jokey</th>
            <th class="pb-1 px-1 text-right">İkramiye</th>
          </tr></thead>
          <tbody class="divide-y divide-[#E8E3D8]">
            ${list.map(r => `
              <tr>
                <td class="py-1 px-1">${r.tarih}</td>
                <td class="py-1 px-1 font-sans">${r.sehir}</td>
                <td class="py-1 px-1 text-right">${r.msf}</td>
                <td class="py-1 px-1 text-[#8C5A3C] font-bold" title="${r.penetrometre ? 'Penetrometre ' + r.penetrometre : ''}">${r.pd}${r.penetrometre ? `<span class="text-[9px] text-[#78716C]"> ${r.penetrometre}</span>` : ''}</td>
                <td class="py-1 px-1 text-center font-extrabold ${r.sira !== '-' && r.sira <= 3 ? 'text-[#0F291E]' : 'text-[#57534E]'}">${r.sira}</td>
                <td class="py-1 px-1 text-right text-[#7A1C1C] font-bold">${r.derece || '-'}</td>
                <td class="py-1 px-1 font-sans" title="${r.kcins}">${r.sinif}</td>
                <td class="py-1 px-1 text-right">${r.siklet ?? '-'}</td>
                <td class="py-1 px-1 font-sans">${r.jokey}</td>
                <td class="py-1 px-1 text-right font-bold text-[#8C5A3C]">${r.ikramiye || '-'}</td>
              </tr>`).join('')}
          </tbody>
        </table>
      </div>` : `<div class="text-xs text-[#78716C] py-1">Bu safkan ${active} pistte hiç koşmadı.</div>`}
  `;
}

// Pist / zemin / mevsim / mesafe kırılım tabloları (at ve akrabaları için ortak)
function breakdownHTML(b, race) {
  if (!b || !b.races) return '<div class="text-xs text-[#78716C]">Koşu kaydı yok.</div>';
  const today = race ? raceSurfaceName(race) : null;
  const todayBucket = race ? (race.distance_m <= 1200 ? 'Sprint' : race.distance_m <= 1500 ? '1300' : race.distance_m <= 1700 ? 'Mil' : race.distance_m <= 2000 ? 'Orta' : 'Uzun') : null;
  const table = (title, rows, isToday) => rows && rows.length ? `
    <div class="bg-[#FAF8F3] rounded-lg p-2 border border-[#E2DDD2]">
      <div class="text-[9px] font-extrabold text-[#8C5A3C] uppercase mb-1">${title}</div>
      <table class="w-full text-[10px] mono">
        <thead><tr class="text-[8px] text-[#78716C] uppercase font-sans"><th class="text-left font-bold"></th><th class="text-right font-bold">K</th><th class="text-right font-bold">1.%</th><th class="text-right font-bold">İlk 3</th><th class="text-right font-bold">İlk 5</th></tr></thead>
        <tbody>
          ${rows.map(r => {
            const hl = isToday && isToday(r.label);
            const strong = r.starts >= 3 && r.top3_pct >= 40;
            return `<tr class="${hl ? 'bg-[#0F291E]/10' : ''}">
              <td class="py-0.5 font-sans font-bold ${hl ? 'text-[#0F291E]' : 'text-[#18181B]'}">${r.label}${hl ? ' ◀' : ''}</td>
              <td class="py-0.5 text-right text-[#57534E]">${r.starts}</td>
              <td class="py-0.5 text-right font-bold text-[#0F291E]">%${r.win_pct}</td>
              <td class="py-0.5 text-right ${strong ? 'font-extrabold text-[#7A1C1C]' : 'font-bold'}">%${r.top3_pct}</td>
              <td class="py-0.5 text-right text-[#44403C]">%${r.top5_pct}</td>
            </tr>`;
          }).join('')}
        </tbody>
      </table>
    </div>` : '';
  const cond = b.condition || {};
  return `
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-2">
      ${table('🏟️ Pist', b.surface, l => l === today)}
      ${table('🗓️ Mevsim', b.season, null)}
      ${['Kum', 'Çim', 'Sentetik'].filter(k => cond[k] && cond[k].length).map(k => table(`${SURF_STYLE[k].icon} ${k} Zemin Durumu`, cond[k], null)).join('')}
      ${table('📏 Mesafe', b.distance, l => todayBucket && l.startsWith(todayBucket))}
    </div>
    <div class="text-[9px] text-[#78716C] mt-1.5">◀ bugünkü koşunun pisti/mesafesi • <span class="text-[#7A1C1C] font-bold">kırmızı</span>: en az 3 koşuda %40+ İlk 3</div>
  `;
}

function distanceCityHTML(d, race) {
  const groups = d.distance_city_times || [];
  const head = `<h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">📏 Aynı Mesafe, Farklı İller</h4>`;
  if (!groups.length) return head + '<div class="text-xs text-[#78716C] mt-1">Derece kaydı yok.</div>';
  const surf = raceSurfaceName(race);
  const sorted = [...groups].sort((a, b) => ((b.msf === race.distance_m && b.surface === surf) - (a.msf === race.distance_m && a.surface === surf)) || (b.count - a.count));
  return `
    <div class="mb-2">${head}<p class="text-[10px] text-[#57534E]">Her mesafe ve pistte hipodrom hipodrom en iyi ve ortalama derece (bugünkü mesafe en üstte)</p></div>
    <div class="space-y-2 max-h-96 overflow-y-auto">
      ${sorted.map(g => {
        const isToday = g.msf === race.distance_m && g.surface === surf;
        return `
          <div class="rounded-lg p-2 border ${isToday ? 'border-[#0F291E] bg-[#0F291E]/5' : 'border-[#E2DDD2] bg-[#FAF8F3]'}">
            <div class="text-[10px] font-extrabold uppercase mb-1 ${isToday ? 'text-[#0F291E]' : 'text-[#8C5A3C]'}">${g.msf}m ${g.surface}${isToday ? ' • BUGÜNKÜ MESAFE' : ''} <span class="text-[#78716C] font-bold">(${g.count} koşu)</span></div>
            <table class="w-full text-[11px] mono">
              <thead><tr class="text-[8px] text-[#78716C] uppercase font-sans"><th class="text-left">Hipodrom</th><th class="text-right">En İyi</th><th class="text-right">Tarih / Sıra / P.D</th><th class="text-right">Ortalama</th><th class="text-right">K</th></tr></thead>
              <tbody>
                ${g.cities.map((c, i) => `<tr>
                  <td class="py-0.5 font-sans font-bold text-[#18181B]">${c.city}</td>
                  <td class="py-0.5 text-right ${i === 0 ? 'font-extrabold text-[#7A1C1C]' : 'font-bold'}">${c.best}</td>
                  <td class="py-0.5 text-right text-[#57534E] text-[10px]">${c.best_date.slice(0, 5)}.${c.best_date.slice(8)} • ${c.best_sira}. • ${c.best_pd}</td>
                  <td class="py-0.5 text-right text-[#44403C]">${c.avg}</td>
                  <td class="py-0.5 text-right text-[#57534E]">${c.count}</td>
                </tr>`).join('')}
              </tbody>
            </table>
          </div>`;
      }).join('')}
    </div>
  `;
}

// Akrabanın (baba / anne / kısrak babası) yavrularından ayrıntılı kırılım — düğmeyle yüklenir
function loadRelativeBreakdown(kind, atId) {
  const el = document.getElementById(`relbd-${kind}`);
  if (!el) return;
  el.innerHTML = `<div class="text-[11px] text-[#57534E] py-2 text-center">⏳ Yavruların koşu kayıtları TJK'dan derleniyor (aygırlarda 10–20 sn sürebilir)...</div>`;
  const race = STATE.program.races[STATE.currentRaceIndex];
  fetchJSON(`/api/relative_breakdown?kind=${kind}&at_id=${encodeURIComponent(atId)}`)
    .then(d => {
      const box = document.getElementById(`relbd-${kind}`);
      if (!box) return;
      const note = d.kind === 'anne'
        ? `Annenin koşmuş ${d.sample_size} yavrusu (bu safkan dahil) • ${d.breakdowns.races} koşu`
        : `${d.is_sample ? `Örneklem: koşmuş ${d.sample_size} tayı` : `Koşmuş ${d.sample_size} tayı`} • ${d.breakdowns.races} koşu${d.is_sample ? ` (TJK'da toplam ${d.offspring_listed} kayıtlı tay)` : ''}`;
      box.innerHTML = `<div class="text-[10px] text-[#57534E] mb-1.5">${note}</div>${breakdownHTML(d.breakdowns, race)}`;
    })
    .catch(err => {
      const box = document.getElementById(`relbd-${kind}`);
      if (box) box.innerHTML = `<div class="text-[11px] text-[#7A1C1C] py-1">⚠️ Alınamadı: ${esc(err.message)} <button onclick="loadRelativeBreakdown('${kind}', '${atId}')" class="underline font-bold">Tekrar dene</button></div>`;
    });
}

function relativeBreakdownSlot(kind, atId, label) {
  return `
    <div id="relbd-${kind}" class="mt-2 pt-2 border-t border-[#E2DDD2]">
      <button onclick="loadRelativeBreakdown('${kind}', '${atId}')" class="w-full py-1.5 rounded-md bg-[#0F291E] text-[#F9F6F0] text-[11px] font-bold">
        🔍 ${label}: Pist • Zemin • Mevsim • Mesafe Analizi
      </button>
    </div>`;
}

function relativeCard(title, tone, blk, opts = {}) {
  if (!blk) {
    return `<div class="bg-[#FAF8F3] rounded-lg p-2.5 border border-[#E2DDD2]"><div class="text-[10px] font-extrabold uppercase ${tone}">${title}</div><div class="text-[11px] text-[#78716C] mt-1">TJK istatistiği bulunamadı.</div></div>`;
  }
  const stat = (label, val, strong) => `<div><div class="text-[9px] text-[#57534E] font-bold">${label}</div><div class="text-xs mono ${strong ? 'font-extrabold text-[#0F291E]' : 'font-bold text-[#18181B]'}">${val}</div></div>`;
  return `
    <div class="bg-[#FAF8F3] rounded-lg p-2.5 border border-[#E2DDD2]">
      <div class="flex items-center justify-between gap-2">
        <div class="text-[10px] font-extrabold uppercase ${tone}">${title}</div>
        <div class="text-[10px] text-[#57534E]">${[blk.country, blk.breed].filter(Boolean).join(' • ')}</div>
      </div>
      <div class="serif-title text-base font-bold text-[#18181B] leading-tight mb-1.5">${blk.name}</div>
      <div class="grid grid-cols-4 gap-1.5 text-center">
        ${stat(opts.runnersLabel || 'Koşan Tay', blk.runners)}
        ${blk.winners ? stat('Kazanan Yavru', `${blk.winners}${blk.winners_pct ? ` <span class="text-[9px] text-[#57534E]">%${blk.winners_pct}</span>` : ''}`) : stat('Koşu', blk.starts)}
        ${stat('1.%', '%' + blk.win_pct, true)}
        ${stat('İlk 3', '%' + blk.top3_pct, true)}
      </div>
      <div class="flex justify-between text-[10px] text-[#57534E] mono mt-1.5">
        <span>${blk.starts} koşu: ${blk.w1}-${blk.w2}-${blk.w3}-${blk.w4}-${blk.w5}</span>
        <span>İlk 5 %${blk.top5_pct}</span>
        <span class="font-bold text-[#8C5A3C]">${blk.earnings}${/t$/.test(blk.earnings) ? '' : ' t'}</span>
      </div>
      ${opts.extra || ''}
    </div>
  `;
}

function relativesHTML(rel, h, race) {
  const today = (race.surface || '').includes('Çim') ? 'Çim' : (race.surface || '').includes('Sentetik') ? 'Sentetik' : 'Kum';
  const bySurf = (rel.baba && rel.baba.by_surface) || {};
  const sireSurface = Object.keys(bySurf).length ? `
    <div class="mt-2 pt-2 border-t border-[#E2DDD2]">
      <div class="text-[9px] font-extrabold text-[#57534E] uppercase mb-1">Taylarının pist başarısı</div>
      <div class="grid grid-cols-3 gap-1.5 text-center">
        ${['Çim', 'Kum', 'Sentetik'].filter(k => bySurf[k]).map(k => `
          <div class="rounded p-1.5 ${k === today ? 'bg-[#0F291E] text-[#F9F6F0]' : 'bg-white border border-[#E2DDD2]'}">
            <div class="text-[9px] font-bold ${k === today ? 'text-[#D4AF37]' : 'text-[#57534E]'}">${SURF_STYLE[k].icon} ${k}${k === today ? ' • BUGÜN' : ''}</div>
            <div class="text-[11px] mono font-extrabold">1. %${bySurf[k].win_pct} • İlk3 %${bySurf[k].top3_pct}</div>
            <div class="text-[9px] mono opacity-80">${bySurf[k].starts} koşu</div>
          </div>`).join('')}
      </div>
    </div>` : '';
  const kd = rel.kardesler || { list: [], total: {} };
  const kardesHTML = kd.list.length ? `
    <div class="bg-[#FAF8F3] rounded-lg p-2.5 border border-[#E2DDD2]">
      <div class="flex items-center justify-between">
        <div class="text-[10px] font-extrabold uppercase text-[#0F291E]">👪 Kardeşler (aynı anneden)</div>
        <div class="text-[10px] mono text-[#57534E]">${kd.total.count} kardeş • ${kd.total.winners} kazanan • ${kd.total.starts} koşu • 1. %${kd.total.win_pct} • İlk 3 %${kd.total.top3_pct}</div>
      </div>
      <div class="overflow-x-auto -mx-1 mt-1.5">
        <table class="w-full text-[11px] whitespace-nowrap mono">
          <thead><tr class="text-[9px] text-[#57534E] border-b border-[#D8D2C5] uppercase font-sans">
            <th class="pb-1 px-1 text-left">Kardeşi</th><th class="pb-1 px-1 text-left">Babası</th><th class="pb-1 px-1 text-right">K</th>
            <th class="pb-1 px-1 text-right">1-2-3-4</th><th class="pb-1 px-1 text-right">1.%</th><th class="pb-1 px-1 text-right">İlk 3</th><th class="pb-1 px-1 text-right">Kazanç</th>
          </tr></thead>
          <tbody class="divide-y divide-[#E8E3D8]">
            ${kd.list.map(k => `
              <tr>
                <td class="py-1 px-1 font-sans font-bold text-[#18181B]">${k.name}</td>
                <td class="py-1 px-1 font-sans text-[#57534E]">${k.sire}</td>
                <td class="py-1 px-1 text-right">${k.starts}</td>
                <td class="py-1 px-1 text-right text-[#57534E]">${k.w1}-${k.w2}-${k.w3}-${k.w4}</td>
                <td class="py-1 px-1 text-right font-bold text-[#0F291E]">%${k.win_pct}</td>
                <td class="py-1 px-1 text-right font-bold">%${k.top3_pct}</td>
                <td class="py-1 px-1 text-right font-bold text-[#8C5A3C]">${k.earnings}</td>
              </tr>`).join('')}
          </tbody>
        </table>
      </div>
    </div>` : `<div class="bg-[#FAF8F3] rounded-lg p-2.5 border border-[#E2DDD2] text-[11px] text-[#78716C]">👪 TJK kayıtlarında yarışmış kardeşi bulunmuyor.</div>`;
  return `
    <div class="mb-2">
      <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">🧬 Akraba İstatistikleri</h4>
      <p class="text-[10px] text-[#57534E]">Babasının, annesinin ve kısrak babasının yavrularıyla kardeşlerinin TJK yarış başarısı (yarış başına)</p>
    </div>
    <div class="space-y-2">
      ${relativeCard('♂ Baba (Aygır)', 'text-[#0F291E]', rel.baba, { extra: sireSurface + (rel.baba ? relativeBreakdownSlot('baba', h.at_id, 'Taylarının') : '') })}
      ${relativeCard('♀ Anne (Kısrak)', 'text-[#7A1C1C]', rel.anne, { extra: rel.anne ? relativeBreakdownSlot('anne', h.at_id, 'Yavrularının') : '' })}
      ${relativeCard('♂ Kısrak Babası (Annenin Babası)', 'text-[#8C5A3C]', rel.kisrak_baba, { runnersLabel: 'Kızlarının Tayı', extra: rel.kisrak_baba ? relativeBreakdownSlot('kisrak_baba', h.at_id, 'Kendi Taylarının') : '' })}
      ${kardesHTML}
    </div>
  `;
}

function careerKarneHTML(d) {
  const ss = d.surface_summary || {};
  const c = d.career_stats;
  if (!c || !c.starts) {
    return `<h4 class="serif-title text-base font-bold text-[#0F291E] uppercase mb-1">🏅 Derece Karnesi</h4><div class="text-xs text-[#78716C]">Henüz koşu kaydı yok.</div>`;
  }
  const rows = [
    ['Kariyer', c, true],
    ['Son 10 Koşu', c.last10, false],
    ['Çim', ss['Çim'], false],
    ['Kum', ss['Kum'], false],
    ['Sentetik', ss['Sentetik'], false],
  ].filter(r => r[1] && r[1].starts);
  const cell = (v, strong) => `<td class="py-1.5 px-1 text-right ${strong ? 'font-extrabold text-[#0F291E]' : 'text-[#44403C] font-semibold'}">%${v}</td>`;
  return `
    <div class="flex items-center justify-between mb-2">
      <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">🏅 Derece Karnesi (Yarış Başına)</h4>
      <span class="text-[10px] text-[#57534E] font-bold mono">${c.starts} Koşu: ${c.w1}-${c.w2}-${c.w3}-${c.w4}-${c.w5}</span>
    </div>
    <div class="grid grid-cols-5 gap-1.5 mb-3 text-center">
      ${[['1.\'lik', c.win_pct, 'bg-[#0F291E] text-[#F9F6F0]'], ['2.\'lik', c.second_pct, 'bg-white border border-[#D8D2C5]'], ['3.\'lük', c.third_pct, 'bg-white border border-[#D8D2C5]'],
         ['İlk 3', c.top3_pct, 'bg-[#F7F1E3] border border-[#A67C37]'], ['İlk 5', c.top5_pct, 'bg-[#F7F1E3] border border-[#A67C37]']].map(([l, v, cls]) => `
        <div class="${cls} rounded-lg p-1.5">
          <div class="text-[9px] font-bold ${cls.includes('0F291E') ? 'text-[#D4AF37]' : 'text-[#57534E]'}">${l}</div>
          <div class="text-sm font-extrabold mono">%${v}</div>
        </div>`).join('')}
    </div>
    <div class="overflow-x-auto -mx-1">
      <table class="w-full text-[11px] whitespace-nowrap mono">
        <thead><tr class="text-[9px] text-[#57534E] border-b border-[#D8D2C5] uppercase font-sans">
          <th class="pb-1 px-1 text-left">Kırılım</th><th class="pb-1 px-1 text-right">K</th><th class="pb-1 px-1 text-right">1-2-3-4-5</th>
          <th class="pb-1 px-1 text-right">1.%</th><th class="pb-1 px-1 text-right">2.%</th><th class="pb-1 px-1 text-right">3.%</th>
          <th class="pb-1 px-1 text-right">İlk 3</th><th class="pb-1 px-1 text-right">İlk 5</th>
        </tr></thead>
        <tbody class="divide-y divide-[#E8E3D8]">
          ${rows.map(([label, b, bold]) => `
            <tr>
              <td class="py-1.5 px-1 font-sans ${bold ? 'font-extrabold text-[#0F291E]' : 'font-bold text-[#18181B]'}">${label}</td>
              <td class="py-1.5 px-1 text-right">${b.starts}</td>
              <td class="py-1.5 px-1 text-right text-[#57534E]">${b.w1}-${b.w2}-${b.w3}-${b.w4}-${b.w5}</td>
              ${cell(b.win_pct, true)}${cell(b.second_pct)}${cell(b.third_pct)}${cell(b.top3_pct, true)}${cell(b.top5_pct)}
            </tr>`).join('')}
        </tbody>
      </table>
    </div>
  `;
}

function classKarneHTML(d) {
  const rows = d.class_breakdown || [];
  if (!rows.length) {
    return `<h4 class="serif-title text-base font-bold text-[#0F291E] uppercase mb-1">🏛️ Koşu Sınıfı Karnesi</h4><div class="text-xs text-[#78716C]">Henüz koşu kaydı yok.</div>`;
  }
  const tone = key => key.startsWith('grup') ? 'text-[#7A1C1C]' : key === 'kv' ? 'text-[#8C5A3C]' : key.startsWith('sartli') ? 'text-[#0F291E]' : key.startsWith('handikap') ? 'text-[#1B4332]' : 'text-[#57534E]';
  return `
    <div class="mb-2">
      <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">🏛️ Koşu Sınıfı Karnesi</h4>
      <p class="text-[10px] text-[#57534E]">Grup, Kısa Vade, Şartlı 1–5 ve Handikap koşularında yarış başına derece başarısı (tüm kariyer)</p>
    </div>
    <div class="overflow-x-auto -mx-1">
      <table class="w-full text-[11px] whitespace-nowrap mono">
        <thead><tr class="text-[9px] text-[#57534E] border-b border-[#D8D2C5] uppercase font-sans">
          <th class="pb-1 px-1 text-left">Sınıf</th><th class="pb-1 px-1 text-right">K</th><th class="pb-1 px-1 text-right">1-2-3</th>
          <th class="pb-1 px-1 text-right">1.%</th><th class="pb-1 px-1 text-right">İlk 3</th><th class="pb-1 px-1 text-right">İlk 5</th>
          <th class="pb-1 px-1 text-right">Ort.S</th><th class="pb-1 px-1 text-right">En İyi</th>
        </tr></thead>
        <tbody class="divide-y divide-[#E8E3D8]">
          ${rows.map(b => `
            <tr>
              <td class="py-1.5 px-1 font-sans font-extrabold ${tone(b.key)}">${b.label}</td>
              <td class="py-1.5 px-1 text-right">${b.starts}</td>
              <td class="py-1.5 px-1 text-right text-[#57534E]">${b.w1}-${b.w2}-${b.w3}</td>
              <td class="py-1.5 px-1 text-right font-bold text-[#0F291E]">%${b.win_pct}</td>
              <td class="py-1.5 px-1 text-right font-extrabold text-[#0F291E]">%${b.top3_pct}</td>
              <td class="py-1.5 px-1 text-right text-[#44403C]">%${b.top5_pct}</td>
              <td class="py-1.5 px-1 text-right text-[#57534E]">${b.avg_pos ?? '-'}</td>
              <td class="py-1.5 px-1 text-right font-bold ${b.best_pos === 1 ? 'text-[#7A1C1C]' : 'text-[#18181B]'}">${b.best_pos ?? '-'}</td>
            </tr>`).join('')}
        </tbody>
      </table>
    </div>
  `;
}

function hpHistoryHTML(d) {
  const hp = d.hp_history;
  if (!hp || !hp.points || !hp.points.length) {
    return `<h4 class="serif-title text-base font-bold text-[#0F291E] uppercase mb-1">📈 Handikap Puanı Seyri</h4><div class="text-xs text-[#78716C]">Handikap puanı kaydı yok.</div>`;
  }
  const pts = [...hp.points].reverse(); // eskiden yeniye
  const lo = Math.max(0, (hp.min || 0) - 4), hi = (hp.max || 1) + 2;
  const ch = hp.change_5 || 0;
  return `
    <div class="flex items-center justify-between mb-2">
      <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">📈 Handikap Puanı Seyri</h4>
      <span class="text-[10px] font-bold mono text-[#57534E]">Güncel <strong class="text-[#0F291E]">${hp.current}</strong> • En Yüksek ${hp.max} • En Düşük ${hp.min} •
        <span class="${ch > 0 ? 'text-[#0F291E]' : ch < 0 ? 'text-[#7A1C1C]' : ''}">${ch > 0 ? '▲' : ch < 0 ? '▼' : '='}${Math.abs(ch)} (son 5)</span></span>
    </div>
    <div class="flex items-end gap-1 h-24 px-1 border-b border-[#D8D2C5]">
      ${pts.map((p, i) => {
        const hgt = Math.round(((p.hp - lo) / Math.max(1, hi - lo)) * 100);
        const last = i === pts.length - 1;
        return `
          <div class="flex-1 flex flex-col items-center justify-end h-full" title="${p.tarih} • ${p.kcins} • ${p.sira ? p.sira + '.' : 'derecesiz'}">
            <span class="text-[9px] mono font-bold ${last ? 'text-[#0F291E]' : 'text-[#57534E]'}">${p.hp}</span>
            <div class="w-full rounded-t ${last ? 'bg-[#0F291E]' : (p.sira && p.sira <= 3 ? 'bg-[#A67C37]' : 'bg-[#C8C1B2]')}" style="height:${Math.max(6, hgt)}%"></div>
          </div>`;
      }).join('')}
    </div>
    <div class="flex gap-1 px-1 mt-1">
      ${pts.map(p => `<div class="flex-1 text-center text-[8px] text-[#78716C] mono">${p.tarih.slice(0, 5)}</div>`).join('')}
    </div>
    <div class="mt-1.5 text-[9px] text-[#57534E] flex gap-3"><span><span class="inline-block w-2 h-2 bg-[#A67C37] rounded-sm"></span> İlk 3'e girdiği koşu</span><span><span class="inline-block w-2 h-2 bg-[#0F291E] rounded-sm"></span> Son koşu</span></div>
  `;
}

function modalIdmanHTML(race, h) {
  const head = `<h4 class="serif-title text-base font-bold text-[#0F291E] uppercase mb-2">🏇 Son İdman (Galop • Sprint • Kenter)</h4>`;
  if (!race.kosu_kodu) return head + '<div class="text-xs text-[#78716C]">TJK bülteninde bu koşu için idman bağlantısı yok.</div>';
  const idm = idmanForHorse(race, h);
  if (idm === undefined) return head + '<div class="text-xs text-[#57534E] py-1 text-center">⏳ Son idman bilgisi yükleniyor...</div>';
  if (idm === null) {
    const failed = STATE.idman[race.kosu_kodu] && STATE.idman[race.kosu_kodu].failed;
    return head + `<div class="text-xs text-[#78716C]">${failed ? 'TJK idman sayfası alınamadı.' : 'Bu koşu öncesinde kayıtlı idmanı bulunmuyor.'}</div>`;
  }
  const d = STATE.idman[race.kosu_kodu];
  const field = (label, val) => val ? `<div><span class="text-[#57534E]">${label}:</span> <strong class="text-[#18181B]">${val}</strong></div>` : '';
  return `
    <div class="flex items-center justify-between mb-2">
      <h4 class="serif-title text-base font-bold text-[#0F291E] uppercase">🏇 Son İdman (Galop • Sprint • Kenter)</h4>
      ${idmanDurumBadge(idm.galop_durumu)}
    </div>
    <div class="grid grid-cols-3 gap-2 mb-2.5 text-center">
      <div class="bg-[#0F291E] text-[#F9F6F0] rounded-lg p-2">
        <div class="text-[9px] text-[#D4AF37] font-bold">İDMAN TÜRÜ</div>
        <div class="text-sm font-extrabold">${idm.tur || '-'}</div>
      </div>
      <div class="bg-white border border-[#D8D2C5] rounded-lg p-2">
        <div class="text-[9px] text-[#57534E] font-bold">TARİH</div>
        <div class="text-sm font-extrabold mono text-[#18181B]">${idm.tarih || '-'}</div>
        ${idm.days_before != null ? `<div class="text-[9px] text-[#57534E]">${idm.days_before} gün önce</div>` : ''}
      </div>
      <div class="bg-white border border-[#D8D2C5] rounded-lg p-2">
        <div class="text-[9px] text-[#57534E] font-bold">SON 400m</div>
        <div class="text-sm font-extrabold mono text-[#7A1C1C]">${idm.son_400 != null ? idm.son_400.toFixed(2) + 's' : '-'}</div>
        ${idm.son_400_rank ? `<div class="text-[9px] text-[#57534E]">Koşuda ${idm.son_400_rank}. / ${d.timed_count}</div>` : ''}
      </div>
    </div>
    <div class="bg-[#FAF8F3] rounded-lg p-2.5 border border-[#E2DDD2] mb-2">
      <div class="text-[10px] font-extrabold text-[#8C5A3C] uppercase mb-1">Ara Dereceler</div>
      <div class="flex flex-wrap gap-1.5">
        ${(idm.splits || []).map(x => `<span class="px-2 py-0.5 rounded bg-white border border-[#D8D2C5] text-[11px] mono"><strong>${x.dist}m</strong> ${x.time}</span>`).join('') || '<span class="text-[11px] text-[#78716C]">Ara derece yok</span>'}
      </div>
      ${idm.pace_200 ? `<div class="text-[10px] text-[#57534E] mt-1.5 mono">Ortalama tempo: ${idm.pace_200}s / 200m (${idm.longest.dist}m üzerinden)</div>` : ''}
    </div>
    <div class="grid grid-cols-2 gap-1.5 text-xs">
      ${field('Galop Durumu', idm.galop_durumu)}
      ${field('İdman Jokeyi', idm.idman_jokeyi)}
      ${field('Pist', idm.pist)}
      ${field('Pist Durumu', idm.pist_durumu)}
      ${field('Zemin', idm.zemin)}
      ${field('Hava', idm.hava)}
      ${field('Hipodrom', idm.hipodrom)}
      ${field('Takı', idm.taki)}
    </div>
    ${idm.aciklama ? `<div class="mt-2 text-[11px] text-[#44403C] bg-[#F7F1E3] border border-[#A67C37]/40 rounded-lg px-2.5 py-1.5">✒️ ${idm.aciklama}</div>` : ''}
  `;
}

function closeHorseModal() {
  document.getElementById('horseModal').classList.add('hidden');
}

function populateXraySelectors() {
  const race = STATE.program.races[STATE.currentRaceIndex];
  if (!race || !race.horses.length) return;

  const sorted = [...race.horses].sort((a, b) => a.model_rank - b.model_rank);
  const selA = document.getElementById('xrayHorseA');
  const selB = document.getElementById('xrayHorseB');

  const optionsHTML = sorted.map(h => `<option value="${h.no}">#${h.no} ${h.name} (Puan: ${h.quant_score})</option>`).join('');
  selA.innerHTML = optionsHTML;
  selB.innerHTML = optionsHTML;

  if (sorted.length > 1) {
    selA.value = sorted[0].no;
    selB.value = sorted[1].no;
  }
  renderXrayComparison();
}

function renderXrayComparison() {
  const race = STATE.program.races[STATE.currentRaceIndex];
  const noA = parseInt(document.getElementById('xrayHorseA').value);
  const noB = parseInt(document.getElementById('xrayHorseB').value);
  const hA = race.horses.find(h => h.no === noA) || race.horses[0];
  const hB = race.horses.find(h => h.no === noB) || race.horses[0];

  const rows = [
    { label: 'Genel Bülten Puanı', a: hA.quant_score, b: hB.quant_score },
    { label: 'Kazanma Şansı (%)', a: hA.simulation.win_prob, b: hB.simulation.win_prob },
    { label: 'Kronometre & Hız Derecesi', a: hA.metrics.speed_figure, b: hB.metrics.speed_figure },
    { label: 'Son 6 Koşu Form & Pist', a: hA.metrics.form_rating, b: hB.metrics.form_rating },
    { label: 'Handikap & Grup Klası', a: hA.metrics.class_rating, b: hB.metrics.class_rating },
    { label: 'Jokey & Eküri İstikrarı', a: hA.metrics.jockey_rating, b: hB.metrics.jockey_rating },
    { label: 'Sıklet (Kilo) Avantajı', a: hA.metrics.weight_rating, b: hB.metrics.weight_rating },
    { label: 'KGS Dinlenme & Tazelik', a: hA.metrics.fitness_rating, b: hB.metrics.fitness_rating }
  ];
  const cA = (STATE.extras[hA.at_id] || {}).career_stats, cB = (STATE.extras[hB.at_id] || {}).career_stats;
  if (cA && cB && cA.starts && cB.starts) {
    rows.push(
      { label: 'Kariyer Birincilik %', a: cA.win_pct, b: cB.win_pct },
      { label: 'Kariyer İlk 3 %', a: cA.top3_pct, b: cB.top3_pct },
      { label: 'Kariyer İlk 5 %', a: cA.top5_pct, b: cB.top5_pct }
    );
  }

  document.getElementById('xrayComparisonBody').innerHTML = `
    <div class="space-y-3">
      ${rows.map(r => {
        const total = (r.a + r.b) || 1;
        const pctA = Math.round((r.a / total) * 100);
        const aWins = r.a >= r.b;
        return `
          <div>
            <div class="flex justify-between text-xs mb-1">
              <span class="mono font-extrabold ${aWins ? 'text-[#0F291E]' : 'text-[#78716C]'}">${r.a}</span>
              <span class="text-[#44403C] font-bold text-[11px]">${r.label}</span>
              <span class="mono font-extrabold ${!aWins ? 'text-[#7A1C1C]' : 'text-[#78716C]'}">${r.b}</span>
            </div>
            <div class="h-2.5 bg-[#E8E3D8] rounded-full overflow-hidden flex">
              <div style="width: ${pctA}%" class="bg-[#0F291E] h-full"></div>
              <div style="width: ${100 - pctA}%" class="bg-[#7A1C1C] h-full"></div>
            </div>
          </div>
        `;
      }).join('')}
    </div>
  `;
}

function openWeightsModal() {
  // Uygulanmamış sürgü değişikliklerini at, geçerli teraziyi göster
  syncWeightSliders(STATE.program && STATE.program.weights);
  document.getElementById('weightsModal').classList.remove('hidden');
}

function closeWeightsModal() {
  document.getElementById('weightsModal').classList.add('hidden');
}

function updateWeightLabels() {
  Object.values(WEIGHT_INPUTS).forEach(id => {
    const val = document.getElementById(id).value;
    document.getElementById(`lbl-${id}`).textContent = `${val}%`;
  });
}

function syncWeightSliders(weights) {
  const w = weights || STATE.weights || FALLBACK_DEFAULT_WEIGHTS;
  WEIGHT_KEYS.forEach(k => {
    const el = document.getElementById(WEIGHT_INPUTS[k]);
    if (el && w[k] != null) el.value = Math.round(w[k]);
  });
  updateWeightLabels();
}

function resetWeights() {
  syncWeightSliders((STATE.program && STATE.program.default_weights) || FALLBACK_DEFAULT_WEIGHTS);
}

function applyCustomWeights() {
  const w = {};
  WEIGHT_KEYS.forEach(k => { w[k] = parseFloat(document.getElementById(WEIGHT_INPUTS[k]).value); });
  const defaults = (STATE.program && STATE.program.default_weights) || FALLBACK_DEFAULT_WEIGHTS;
  const isDefault = WEIGHT_KEYS.every(k => Math.abs(w[k] - defaults[k]) < 0.01);
  STATE.weights = isDefault ? null : w;
  try {
    if (STATE.weights) localStorage.setItem('padok_weights', JSON.stringify(STATE.weights));
    else localStorage.removeItem('padok_weights');
  } catch (e) { /* depolama kapalı */ }

  closeWeightsModal();
  // Sunucu tüm analizi (tempo, Monte Carlo, rozetler, kupon ölçekleri) yeni teraziyle baştan yapar
  loadProgram(STATE.currentCityId, false, { keepRace: true });
}
