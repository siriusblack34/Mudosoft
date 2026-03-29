import React, { useState } from "react";
import { CalendarDays, Building2, AlertTriangle, Info } from "lucide-react";

// ─── Veri ────────────────────────────────────────────────────────────────────

type HolidayType = "resmi" | "sirket" | "tadilat";

interface Holiday {
    date: string;       // Görüntüleme için
    dateSort: string;   // Sıralama için (YYYYMMDD)
    day: string;
    name: string;
    type: HolidayType;
    duration: string;   // "1 Gün", "3 Gün", "Yarım Gün" vs.
    note?: string;
}

const holidays2026: Holiday[] = [
    // ── Ocak ─────────────────────────────────────────────────────────────────
    { date: "1 Ocak",    dateSort: "20260101", day: "Perşembe", name: "Yılbaşı",                                   type: "resmi",   duration: "1 Gün" },
    { date: "2 Ocak",    dateSort: "20260102", day: "Cuma",     name: "Şirket İzni (Köprü)",                       type: "sirket",  duration: "1 Gün" },

    // ── Mart ─────────────────────────────────────────────────────────────────
    { date: "19 Mart",   dateSort: "20260319", day: "Perşembe", name: "Şirket İzni (Arefe Köprüsü)",               type: "sirket",  duration: "Yarım Gün" },
    { date: "20-22 Mart",dateSort: "20260320", day: "Cum–Paz",  name: "Ramazan Bayramı",                           type: "resmi",   duration: "3 Gün" },

    // ── Nisan ────────────────────────────────────────────────────────────────
    { date: "23 Nisan",  dateSort: "20260423", day: "Perşembe", name: "Ulusal Egemenlik ve Çocuk Bayramı",         type: "resmi",   duration: "1 Gün" },
    { date: "24 Nisan",  dateSort: "20260424", day: "Cuma",     name: "Şirket İzni (Köprü)",                       type: "sirket",  duration: "1 Gün" },

    // ── Mayıs ────────────────────────────────────────────────────────────────
    { date: "1 Mayıs",   dateSort: "20260501", day: "Cuma",     name: "Emek ve Dayanışma Günü",                    type: "resmi",   duration: "1 Gün" },
    { date: "18 Mayıs",  dateSort: "20260518", day: "Pazartesi",name: "Şirket İzni (Köprü)",                       type: "sirket",  duration: "1 Gün" },
    { date: "19 Mayıs",  dateSort: "20260519", day: "Salı",     name: "Atatürk'ü Anma, Gençlik ve Spor Bayramı",  type: "resmi",   duration: "1 Gün" },
    { date: "25 Mayıs",  dateSort: "20260525", day: "Pazartesi",name: "Şirket İzni (Köprü)",                       type: "sirket",  duration: "1 Gün" },
    { date: "26 Mayıs",  dateSort: "20260526", day: "Salı",     name: "Şirket İzni (Arefe Köprüsü)",               type: "sirket",  duration: "Yarım Gün" },
    { date: "27-30 Mayıs",dateSort:"20260527", day: "Çar–Cmt",  name: "Kurban Bayramı",                            type: "resmi",   duration: "4 Gün" },

    // ── Temmuz ───────────────────────────────────────────────────────────────
    { date: "15 Temmuz", dateSort: "20260715", day: "Çarşamba", name: "Demokrasi ve Milli Birlik Günü",            type: "resmi",   duration: "1 Gün" },

    // ── Ağustos ──────────────────────────────────────────────────────────────
    { date: "10-14 Ağustos",dateSort:"20260810",day: "Pzt–Cum", name: "Ofis Tadilat Kapanışı",                     type: "tadilat", duration: "5 Gün", note: "Her iki ofis de kapalıdır" },
    { date: "30 Ağustos", dateSort: "20260830", day: "Pazar",   name: "Zafer Bayramı",                             type: "resmi",   duration: "1 Gün" },

    // ── Ekim ─────────────────────────────────────────────────────────────────
    { date: "28 Ekim",   dateSort: "20261028", day: "Çarşamba", name: "Şirket İzni (Köprü)",                       type: "sirket",  duration: "Yarım Gün" },
    { date: "29 Ekim",   dateSort: "20261029", day: "Perşembe", name: "Cumhuriyet Bayramı",                        type: "resmi",   duration: "1 Gün" },
    { date: "30 Ekim",   dateSort: "20261030", day: "Cuma",     name: "Şirket İzni (Köprü)",                       type: "sirket",  duration: "1 Gün" },
];

// ─── Yardımcılar ─────────────────────────────────────────────────────────────

const TYPE_CONFIG: Record<HolidayType, { label: string; dot: string; badge: string; text: string }> = {
    resmi:    { label: "Resmi Tatil",     dot: "bg-violet-400",  badge: "bg-violet-500/15 border-violet-500/30 text-violet-300", text: "text-violet-300" },
    sirket:   { label: "Şirket İzni",     dot: "bg-emerald-400", badge: "bg-emerald-500/15 border-emerald-500/30 text-emerald-300", text: "text-emerald-300" },
    tadilat:  { label: "Ofis Kapalı",     dot: "bg-amber-400",   badge: "bg-amber-500/15 border-amber-500/30 text-amber-300", text: "text-amber-300" },
};

const months = ["Ocak","Şubat","Mart","Nisan","Mayıs","Haziran","Temmuz","Ağustos","Eylül","Ekim","Kasım","Aralık"];

function getMonth(h: Holiday): number {
    for (let i = 0; i < months.length; i++) {
        if (h.date.includes(months[i])) return i;
    }
    return -1;
}

// Toplam kapalı gün sayısını hesapla
function totalDays(list: Holiday[]): number {
    return list.reduce((acc, h) => {
        const n = parseInt(h.duration);
        return acc + (isNaN(n) ? 0.5 : n);
    }, 0);
}

// ─── Bileşen ─────────────────────────────────────────────────────────────────

type FilterType = "hepsi" | HolidayType;

export default function HolidaysPage() {
    const [filter, setFilter] = useState<FilterType>("hepsi");

    const filtered = filter === "hepsi"
        ? holidays2026
        : holidays2026.filter(h => h.type === filter);

    const resmiCount   = holidays2026.filter(h => h.type === "resmi").length;
    const sirketCount  = holidays2026.filter(h => h.type === "sirket").length;
    const tadilatCount = holidays2026.filter(h => h.type === "tadilat").length;
    const toplamGun    = totalDays(holidays2026);

    return (
        <div className="p-6 max-w-4xl mx-auto space-y-6">

            {/* Başlık */}
            <div className="flex items-center gap-3">
                <div className="h-10 w-10 rounded-xl bg-violet-600/20 border border-violet-500/20 flex items-center justify-center">
                    <CalendarDays className="w-5 h-5 text-violet-400" />
                </div>
                <div>
                    <h1 className="text-xl font-bold text-ms-text">Resmi Tatiller 2026</h1>
                    <p className="text-sm text-ms-text-muted">Mudolular için tatil takvimi</p>
                </div>
            </div>

            {/* Tadilat uyarısı */}
            <div className="flex items-start gap-3 bg-amber-500/5 border border-amber-500/20 rounded-xl p-4">
                <AlertTriangle className="w-4 h-4 text-amber-400 mt-0.5 shrink-0" />
                <p className="text-sm text-amber-300">
                    <span className="font-semibold">10–14 Ağustos (5 gün)</span> — Her iki ofisimiz tadilat dolayısıyla kapalı olacaktır.
                </p>
            </div>

            {/* Özet kartlar */}
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                <StatCard label="Toplam Kapalı Gün" value={`${toplamGun}`} sub="gün" color="text-ms-text" />
                <StatCard label="Resmi Tatil" value={String(resmiCount)} sub="dönem" color="text-violet-400" />
                <StatCard label="Şirket İzni" value={String(sirketCount)} sub="gün" color="text-emerald-400" />
                <StatCard label="Tadilat" value={String(tadilatCount)} sub="kapatma" color="text-amber-400" />
            </div>

            {/* Legend + Filtre */}
            <div className="flex flex-wrap items-center gap-2">
                <FilterBtn active={filter === "hepsi"}   onClick={() => setFilter("hepsi")}>Tümü</FilterBtn>
                {(["resmi","sirket","tadilat"] as HolidayType[]).map(t => (
                    <FilterBtn key={t} active={filter === t} onClick={() => setFilter(t)}
                        dot={TYPE_CONFIG[t].dot}>
                        {TYPE_CONFIG[t].label}
                    </FilterBtn>
                ))}
            </div>

            {/* Tatil listesi */}
            <div className="space-y-2">
                {filtered.map((h, i) => {
                    const cfg = TYPE_CONFIG[h.type];
                    return (
                        <div key={i}
                            className="flex items-center gap-4 bg-ms-bg-soft border border-ms-border rounded-xl px-4 py-3 hover:border-white/10 transition-colors">

                            {/* Tarih */}
                            <div className="w-36 shrink-0">
                                <div className="text-sm font-semibold text-ms-text">{h.date}</div>
                                <div className="text-xs text-ms-text-muted">{h.day}</div>
                            </div>

                            {/* Ad */}
                            <div className="flex-1 min-w-0">
                                <div className="text-sm text-ms-text truncate">{h.name}</div>
                                {h.note && (
                                    <div className="flex items-center gap-1 mt-0.5">
                                        <Info className="w-3 h-3 text-amber-400 shrink-0" />
                                        <span className="text-xs text-amber-300">{h.note}</span>
                                    </div>
                                )}
                            </div>

                            {/* Tip badge */}
                            <div className={`text-xs font-medium px-2.5 py-1 rounded-lg border shrink-0 hidden sm:block ${cfg.badge}`}>
                                {cfg.label}
                            </div>

                            {/* Süre */}
                            <div className="text-xs font-semibold text-ms-text-muted w-20 text-right shrink-0">
                                {h.duration}
                            </div>
                        </div>
                    );
                })}
            </div>

            {/* Aylık özet */}
            <div>
                <h2 className="text-sm font-semibold text-ms-text-muted uppercase tracking-widest mb-3">Aylık Dağılım</h2>
                <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
                    {months.map((month, idx) => {
                        const items = holidays2026.filter(h => getMonth(h) === idx);
                        if (items.length === 0) return (
                            <div key={month} className="bg-ms-bg-soft border border-ms-border rounded-xl px-4 py-3 opacity-40">
                                <div className="text-xs font-semibold text-ms-text-muted">{month}</div>
                                <div className="text-xs text-ms-text-muted mt-1">Tatil yok</div>
                            </div>
                        );
                        return (
                            <div key={month} className="bg-ms-bg-soft border border-ms-border rounded-xl px-4 py-3">
                                <div className="text-xs font-semibold text-ms-text mb-2">{month}</div>
                                <div className="space-y-1">
                                    {items.map((h, i) => {
                                        const cfg = TYPE_CONFIG[h.type];
                                        return (
                                            <div key={i} className="flex items-center gap-2">
                                                <div className={`w-1.5 h-1.5 rounded-full shrink-0 ${cfg.dot}`} />
                                                <span className="text-xs text-ms-text-muted truncate">{h.date} — {h.duration}</span>
                                            </div>
                                        );
                                    })}
                                </div>
                            </div>
                        );
                    })}
                </div>
            </div>
        </div>
    );
}

// ─── Alt bileşenler ───────────────────────────────────────────────────────────

function StatCard({ label, value, sub, color }: { label: string; value: string; sub: string; color: string }) {
    return (
        <div className="bg-ms-bg-soft border border-ms-border rounded-xl p-4">
            <div className={`text-2xl font-bold ${color}`}>{value}</div>
            <div className="text-xs text-ms-text-muted mt-0.5">{sub}</div>
            <div className="text-xs text-ms-text-muted mt-1">{label}</div>
        </div>
    );
}

function FilterBtn({ children, active, onClick, dot }: {
    children: React.ReactNode; active: boolean; onClick: () => void; dot?: string;
}) {
    return (
        <button onClick={onClick}
            className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium border transition-colors ${
                active
                    ? "bg-violet-600/20 border-violet-500/40 text-violet-300"
                    : "bg-ms-bg-soft border-ms-border text-ms-text-muted hover:text-ms-text"
            }`}>
            {dot && <div className={`w-1.5 h-1.5 rounded-full ${dot}`} />}
            {children}
        </button>
    );
}
