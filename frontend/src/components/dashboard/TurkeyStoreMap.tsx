import React from "react";
import {
    projectTurkeyCoordinate,
    TURKEY_MAP_PATH,
    TURKEY_MAP_VIEW_BOX,
    TURKEY_PROVINCE_CENTERS,
} from "./turkeyMapData";

type StoreStatus = "stable" | "watch" | "critical" | "closed";

type StoreMapStore = {
    code: number;
    name: string;
    totalPos: number;
    activePos: number;
    onlinePos: number;
    offlinePos: number;
    closedPos: number;
    since: string | null;
    reason: string | null;
    status: StoreStatus;
};

type MapPalette = {
    cardSoft: string;
    border: string;
    borderStrong: string;
    text: string;
    muted: string;
    subtle: string;
    track: string;
    sky: string;
    skySoft: string;
    emerald: string;
    emeraldSoft: string;
    amber: string;
    amberSoft: string;
    rose: string;
    roseSoft: string;
    slate: string;
    violet: string;
};

type ProvincePoint = {
    name: string;
    x?: number;
    y?: number;
    aliases?: string[];
};

type ProvinceBucket = {
    province: ProvincePoint;
    stores: StoreMapStore[];
    status: StoreStatus;
    activePos: number;
    onlinePos: number;
    offlinePos: number;
    closedPos: number;
};

type HoverState = {
    bucket: ProvinceBucket;
    x: number;
    y: number;
    anchorRight: boolean;
    anchorAbove: boolean;
};

const STATUS_WEIGHT: Record<StoreStatus, number> = {
    stable: 1,
    closed: 2,
    watch: 3,
    critical: 4,
};

const PROVINCES: ProvincePoint[] = [
    { name: "Adana", x: 555, y: 296, aliases: ["01 burda", "m1 city"] },
    { name: "Adıyaman", x: 625, y: 278 },
    { name: "Afyonkarahisar", x: 315, y: 246, aliases: ["afyon"] },
    { name: "Ağrı", x: 835, y: 198, aliases: ["agri"] },
    { name: "Aksaray", x: 485, y: 230 },
    { name: "Amasya", x: 552, y: 126 },
    { name: "Ankara", x: 390, y: 190, aliases: ["ankamall", "cepa", "gordion", "kentpark", "next level", "panora", "arcadium", "ankara optimum"] },
    { name: "Antalya", x: 355, y: 315, aliases: ["alanya", "kas marina", "kaş marina", "lara", "deepo", "rixos marina", "antalya agora"] },
    { name: "Ardahan", x: 830, y: 126 },
    { name: "Artvin", x: 800, y: 95 },
    { name: "Aydın", x: 188, y: 295, aliases: ["kusadasi", "kuşadası"] },
    { name: "Balıkesir", x: 153, y: 198, aliases: ["akcay", "akçay", "bandirma", "bandırma", "ayvalik", "ayvalık", "susurluk"] },
    { name: "Bartın", x: 367, y: 75 },
    { name: "Batman", x: 768, y: 278 },
    { name: "Bayburt", x: 735, y: 145 },
    { name: "Bilecik", x: 272, y: 172 },
    { name: "Bingöl", x: 740, y: 220 },
    { name: "Bitlis", x: 815, y: 253 },
    { name: "Bolu", x: 330, y: 123 },
    { name: "Burdur", x: 308, y: 296 },
    { name: "Bursa", x: 220, y: 175, aliases: ["korupark", "anatolium", "fsm concept", "downtown"] },
    { name: "Çanakkale", x: 88, y: 188, aliases: ["canakkale burda", "çanakkale burda"] },
    { name: "Çankırı", x: 424, y: 145 },
    { name: "Çorum", x: 500, y: 130, aliases: ["ahlpark"] },
    { name: "Denizli", x: 250, y: 285 },
    { name: "Diyarbakır", x: 715, y: 268 },
    { name: "Düzce", x: 298, y: 108 },
    { name: "Edirne", x: 70, y: 82, aliases: ["margi"] },
    { name: "Elazığ", x: 668, y: 228 },
    { name: "Erzincan", x: 680, y: 164 },
    { name: "Erzurum", x: 760, y: 168 },
    { name: "Eskişehir", x: 325, y: 183, aliases: ["eskisehir", "vega"] },
    { name: "Gaziantep", x: 600, y: 312, aliases: ["sanko"] },
    { name: "Giresun", x: 680, y: 104 },
    { name: "Gümüşhane", x: 700, y: 137 },
    { name: "Hakkari", x: 885, y: 330 },
    { name: "Hatay", x: 618, y: 330, aliases: ["iskenderun", "forbes"] },
    { name: "Iğdır", x: 890, y: 180, aliases: ["igdir"] },
    { name: "Isparta", x: 341, y: 278 },
    { name: "İstanbul", x: 197, y: 112, aliases: ["212 outlet", "airport outlet", "akasia", "akasya", "akbati", "akbatı", "akmerkez", "aqua florya", "arenapark", "bagdat", "bağdat", "bahariye", "balat marina", "buyaka", "buyukada", "büyükada", "capitol", "carousel", "cevahir", "emaar", "emar", "goztepe optimum", "göztepe optimum", "icerenkoy", "içerenköy", "istinyepark", "istmarin", "kozz", "kozzy", "maltepe", "maslak", "masko", "mecidiyekoy", "mecidiyeköy", "modoko", "mall of istanbul", "nautilus", "nisantasi", "nişantaşı", "palladium", "pendik", "tema world", "tuzla", "umraniye", "ümraniye", "vadistanbul", "viaport"] },
    { name: "İzmir", x: 150, y: 262, aliases: ["alsancak", "cesme", "çeşme", "izmir agora", "izmir arastapark", "izmir forum", "izmir hilltown", "izmir istinyepark", "izmir optimum", "mavibahce", "mavibahçe", "selway", "urla"] },
    { name: "Kahramanmaraş", x: 632, y: 270, aliases: ["k maras", "k maraş", "k.maras", "k.maraş", "maras piazza", "maraş piazza"] },
    { name: "Karabük", x: 392, y: 99 },
    { name: "Karaman", x: 477, y: 288 },
    { name: "Kars", x: 850, y: 156 },
    { name: "Kastamonu", x: 445, y: 87 },
    { name: "Kayseri", x: 566, y: 214, aliases: ["kayseri park"] },
    { name: "Kırıkkale", x: 436, y: 178 },
    { name: "Kırklareli", x: 113, y: 69 },
    { name: "Kırşehir", x: 473, y: 190 },
    { name: "Kilis", x: 610, y: 335 },
    { name: "Kocaeli", x: 242, y: 125, aliases: ["gebze", "izmit", "izmit burda"] },
    { name: "Konya", x: 430, y: 255 },
    { name: "Kütahya", x: 278, y: 218 },
    { name: "Malatya", x: 628, y: 230, aliases: ["malatya park"] },
    { name: "Manisa", x: 178, y: 233 },
    { name: "Mardin", x: 755, y: 315 },
    { name: "Mersin", x: 505, y: 315, aliases: ["mersin forum", "yat limani", "yat limanı"] },
    { name: "Muğla", x: 221, y: 318, aliases: ["anthaven", "avenue", "bodrum", "fethiye", "gocek", "göcek", "gurece", "gürece", "marmaris", "milta", "midtown", "netsel", "turgutreis", "yalikavak", "yalıkavak"] },
    { name: "Muş", x: 790, y: 227 },
    { name: "Nevşehir", x: 504, y: 210 },
    { name: "Niğde", x: 525, y: 245 },
    { name: "Ordu", x: 640, y: 100 },
    { name: "Osmaniye", x: 592, y: 300 },
    { name: "Rize", x: 764, y: 108 },
    { name: "Sakarya", x: 276, y: 123, aliases: ["adapazari", "adapazarı", "sakarya agora"] },
    { name: "Samsun", x: 565, y: 93, aliases: ["samsun piazza", "yesilyurt", "yeşilyurt"] },
    { name: "Siirt", x: 805, y: 293 },
    { name: "Sinop", x: 500, y: 67 },
    { name: "Sivas", x: 630, y: 183 },
    { name: "Şanlıurfa", x: 660, y: 310 },
    { name: "Şırnak", x: 835, y: 322 },
    { name: "Tekirdağ", x: 132, y: 106, aliases: ["tekira"] },
    { name: "Tokat", x: 596, y: 138 },
    { name: "Trabzon", x: 725, y: 106, aliases: ["trabzon forum"] },
    { name: "Tunceli", x: 690, y: 200 },
    { name: "Uşak", x: 250, y: 246 },
    { name: "Van", x: 880, y: 260 },
    { name: "Yalova", x: 231, y: 146, aliases: ["yalova setur"] },
    { name: "Yozgat", x: 530, y: 171 },
    { name: "Zonguldak", x: 335, y: 83, aliases: ["eregli ereylin", "ereğli ereylin", "ereylin"] },
];

const TURKISH_CHAR_MAP: Record<string, string> = {
    ç: "c",
    ğ: "g",
    ı: "i",
    ö: "o",
    ş: "s",
    ü: "u",
};

function normalizeText(value: string) {
    return value
        .toLocaleLowerCase("tr-TR")
        .replace(/[çğıöşü]/g, (char) => TURKISH_CHAR_MAP[char] ?? char)
        .normalize("NFD")
        .replace(/[\u0300-\u036f]/g, "")
        .replace(/[^a-z0-9]+/g, " ")
        .replace(/\s+/g, " ")
        .trim();
}

const PROVINCE_LOOKUP = PROVINCES
    .map((province) => ({ province, key: normalizeText(province.name) }))
    .sort((a, b) => b.key.length - a.key.length);

const ALIAS_LOOKUP = PROVINCES
    .flatMap((province) => (province.aliases ?? []).map((alias) => ({ province, key: normalizeText(alias) })))
    .filter((item) => item.key.length > 0)
    .sort((a, b) => b.key.length - a.key.length);

function containsNormalizedTerm(text: string, key: string) {
    return ` ${text} `.includes(` ${key} `);
}

function resolveProvince(storeName: string) {
    const normalized = normalizeText(storeName);
    if (!normalized) return null;

    const direct = PROVINCE_LOOKUP.find((item) => containsNormalizedTerm(normalized, item.key));
    if (direct) return direct.province;

    const alias = ALIAS_LOOKUP.find((item) => containsNormalizedTerm(normalized, item.key));
    return alias?.province ?? null;
}

function statusColor(status: StoreStatus, c: MapPalette) {
    if (status === "critical") return c.rose;
    if (status === "watch") return c.amber;
    if (status === "closed") return c.slate;
    return c.emerald;
}

function statusLabel(status: StoreStatus) {
    if (status === "critical") return "Tam kesinti";
    if (status === "watch") return "Kısmi sorun";
    if (status === "closed") return "Tamamı planlı kapalı";
    return "Sağlıklı";
}

function deriveProvinceStatus(stores: StoreMapStore[]): StoreStatus {
    if (stores.some((store) => store.status === "critical")) return "critical";
    if (stores.some((store) => store.status === "watch")) return "watch";
    // Planlı kapalı, sadece şehirde çalışan mağaza kalmadıysa il rengini gri yapar.
    if (stores.length > 0 && stores.every((store) => store.status === "closed")) return "closed";
    return "stable";
}

function affectedStoreCount(bucket: ProvinceBucket) {
    return bucket.stores.filter((store) => store.status !== "stable" || store.closedPos > 0).length;
}

function closedAffectedStoreCount(bucket: ProvinceBucket) {
    return bucket.stores.filter((store) => store.status === "closed" || store.closedPos > 0).length;
}

function storeDetailText(store: StoreMapStore) {
    if (store.status === "critical") {
        return `${store.activePos} POS erişim yok, tam kesinti`;
    }
    if (store.status === "watch") {
        return `${store.offlinePos} POS sorunlu, ${store.onlinePos}/${store.activePos} aktif POS online`;
    }
    if (store.status === "closed") {
        return `${store.closedPos || store.totalPos} POS planlı kapalı${store.reason ? `: ${store.reason}` : ""}`;
    }
    if (store.closedPos > 0) {
        return `${store.closedPos} POS planlı kapalı, ${store.onlinePos}/${store.activePos} aktif POS online`;
    }
    return `${store.onlinePos}/${store.activePos} aktif POS online`;
}

function storeSortScore(store: StoreMapStore) {
    if (store.status === "critical") return 4;
    if (store.status === "watch") return 3;
    if (store.status === "closed" || store.closedPos > 0) return 2;
    return 1;
}

function storeBadgeLabel(store: StoreMapStore) {
    if (store.status === "critical") return "Kritik";
    if (store.status === "watch") return "İzleme";
    if (store.status === "closed" || store.closedPos > 0) return "Planlı";
    return "OK";
}

function storeBadgeColor(store: StoreMapStore, c: MapPalette) {
    if (store.status === "critical") return c.rose;
    if (store.status === "watch") return c.amber;
    if (store.status === "closed" || store.closedPos > 0) return c.slate;
    return c.emerald;
}

function fmtAge(iso?: string | null) {
    if (!iso) return null;
    const time = new Date(iso).getTime();
    if (Number.isNaN(time)) return null;
    const minutes = Math.max(0, Math.floor((Date.now() - time) / 60000));
    if (minutes < 60) return `${minutes}dk`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}sa`;
    return `${Math.floor(hours / 24)}g`;
}

function buildMapData(stores: StoreMapStore[]) {
    const buckets = new Map<string, ProvinceBucket>();
    const unmatched: StoreMapStore[] = [];

    for (const store of stores) {
        const province = resolveProvince(store.name);
        if (!province) {
            unmatched.push(store);
            continue;
        }

        const existing = buckets.get(province.name);
        if (!existing) {
            buckets.set(province.name, {
                province,
                stores: [store],
                status: store.status,
                activePos: store.activePos,
                onlinePos: store.onlinePos,
                offlinePos: store.offlinePos,
                closedPos: store.closedPos,
            });
            continue;
        }

        existing.stores.push(store);
        existing.activePos += store.activePos;
        existing.onlinePos += store.onlinePos;
        existing.offlinePos += store.offlinePos;
        existing.closedPos += store.closedPos;
        if (STATUS_WEIGHT[store.status] > STATUS_WEIGHT[existing.status]) {
            existing.status = store.status;
        }
    }

    return {
        provinces: Array.from(buckets.values())
            .map((bucket) => ({ ...bucket, status: deriveProvinceStatus(bucket.stores) }))
            .sort((a, b) => STATUS_WEIGHT[b.status] - STATUS_WEIGHT[a.status] || affectedStoreCount(b) - affectedStoreCount(a) || b.stores.length - a.stores.length),
        unmatched,
    };
}

const TurkeyStoreMap: React.FC<{ c: MapPalette; stores: StoreMapStore[] }> = ({ c, stores }) => {
    const { provinces, unmatched } = React.useMemo(() => buildMapData(stores), [stores]);
    const [hovered, setHovered] = React.useState<HoverState | null>(null);
    const criticalStores = stores.filter((store) => store.status === "critical");
    const watchStores = stores.filter((store) => store.status === "watch");
    const closedStores = stores.filter((store) => store.status === "closed" || store.closedPos > 0);
    const matchedStoreCount = provinces.reduce((total, bucket) => total + bucket.stores.length, 0);

    const updateHover = React.useCallback((event: React.MouseEvent<SVGGElement>, bucket: ProvinceBucket) => {
        const rect = event.currentTarget.ownerSVGElement?.getBoundingClientRect();
        if (!rect) return;
        const x = event.clientX - rect.left;
        const y = event.clientY - rect.top;
        setHovered({
            bucket,
            x,
            y,
            anchorRight: x > rect.width - 330,
            anchorAbove: y > rect.height - 190,
        });
    }, []);

    if (stores.length === 0) {
        return (
            <div className="rounded-xl border p-5 text-sm" style={{ background: c.cardSoft, borderColor: c.border, color: c.muted }}>
                Mağaza verisi bekleniyor.
            </div>
        );
    }

    return (
        <div className="rounded-xl border p-3" style={{ background: `linear-gradient(135deg, ${c.cardSoft}, ${c.track})`, borderColor: c.border }}>
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
                <div>
                    <div className="text-xs font-semibold" style={{ color: c.text }}>Türkiye Mağaza Dağılımı</div>
                    <div className="text-[10px]" style={{ color: c.muted }}>
                        {matchedStoreCount} mağaza, {provinces.length} ilde canlı takipte
                    </div>
                </div>
                <div className="flex flex-wrap gap-1.5">
                    <MapStat label="Sağlıklı" value={stores.filter((store) => store.status === "stable").length} color={c.emerald} bg={c.emeraldSoft} />
                    <MapStat label="İzleme" value={watchStores.length} color={c.amber} bg={c.amberSoft} />
                    <MapStat label="Kritik" value={criticalStores.length} color={c.rose} bg={c.roseSoft} pulse={criticalStores.length > 0} />
                    <MapStat label="Planlı" value={closedStores.length} color={c.slate} bg={c.track} />
                </div>
            </div>

            <div
                className="relative rounded-xl border"
                onMouseLeave={() => setHovered(null)}
                style={{ borderColor: c.borderStrong, background: `radial-gradient(circle at 50% 40%, ${c.skySoft}, transparent 42%), ${c.track}` }}
            >
                <svg viewBox={TURKEY_MAP_VIEW_BOX} className="h-[260px] w-full lg:h-[300px]" role="img" aria-label="Türkiye mağaza dağılım haritası">
                    <defs>
                        <linearGradient id="turkey-map-fill" x1="0" x2="1" y1="0" y2="1">
                            <stop offset="0%" stopColor={c.sky} stopOpacity="0.28" />
                            <stop offset="48%" stopColor={c.violet} stopOpacity="0.18" />
                            <stop offset="100%" stopColor={c.emerald} stopOpacity="0.18" />
                        </linearGradient>
                        <filter id="map-soft-glow" x="-20%" y="-20%" width="140%" height="140%">
                            <feGaussianBlur stdDeviation="4" result="blur" />
                            <feMerge>
                                <feMergeNode in="blur" />
                                <feMergeNode in="SourceGraphic" />
                            </feMerge>
                        </filter>
                    </defs>

                    <path
                        d={TURKEY_MAP_PATH}
                        fill="url(#turkey-map-fill)"
                        stroke={c.borderStrong}
                        strokeWidth="1.6"
                        filter="url(#map-soft-glow)"
                    />
                    <path
                        d={TURKEY_MAP_PATH}
                        fill="none"
                        stroke={c.sky}
                        strokeOpacity="0.24"
                        strokeWidth="0.8"
                    />

                    {provinces.map((bucket) => {
                        const color = statusColor(bucket.status, c);
                        const radius = Math.min(19, 7 + Math.sqrt(bucket.stores.length) * 3.2);
                        const hasIssue = bucket.status === "critical" || bucket.status === "watch";
                        const closedCount = closedAffectedStoreCount(bucket);
                        const center = TURKEY_PROVINCE_CENTERS[bucket.province.name];
                        const point = center
                            ? projectTurkeyCoordinate(center.lon, center.lat)
                            : { x: bucket.province.x ?? 0, y: bucket.province.y ?? 0 };
                        const title = `${bucket.province.name}: ${bucket.stores.length} mağaza | ${statusLabel(bucket.status)} | POS ${bucket.onlinePos}/${bucket.activePos} online`;
                        return (
                            <g
                                key={bucket.province.name}
                                className="cursor-default"
                                onMouseEnter={(event) => updateHover(event, bucket)}
                                onMouseMove={(event) => updateHover(event, bucket)}
                                aria-label={title}
                            >
                                {bucket.status === "critical" && (
                                    <circle cx={point.x} cy={point.y} r={radius} fill={color} opacity="0.28">
                                        <animate attributeName="r" values={`${radius};${radius + 16};${radius}`} dur="1.25s" repeatCount="indefinite" />
                                        <animate attributeName="opacity" values="0.32;0.04;0.32" dur="1.25s" repeatCount="indefinite" />
                                    </circle>
                                )}
                                <circle cx={point.x} cy={point.y} r={radius + 3} fill={color} opacity="0.14" />
                                <circle cx={point.x} cy={point.y} r={radius} fill={color} stroke={c.cardSoft} strokeWidth="2.5" />
                                {closedCount > 0 && bucket.status !== "closed" && (
                                    <>
                                        <circle cx={point.x + radius - 2} cy={point.y - radius + 2} r="6.5" fill={c.slate} stroke={c.cardSoft} strokeWidth="2" />
                                        <text
                                            x={point.x + radius - 2}
                                            y={point.y - radius + 5}
                                            textAnchor="middle"
                                            className="select-none text-[8px] font-black"
                                            fill="#ffffff"
                                        >
                                            {closedCount}
                                        </text>
                                    </>
                                )}
                                <text
                                    x={point.x}
                                    y={point.y + 3}
                                    textAnchor="middle"
                                    className="select-none text-[10px] font-black"
                                    fill="#ffffff"
                                >
                                    {bucket.stores.length}
                                </text>
                                {hasIssue && (
                                    <text
                                        x={point.x}
                                        y={point.y - radius - 7}
                                        textAnchor="middle"
                                        className="select-none text-[9px] font-bold"
                                        fill={color}
                                    >
                                        {bucket.province.name}
                                    </text>
                                )}
                            </g>
                        );
                    })}
                </svg>
                {hovered && <ProvinceTooltip c={c} hover={hovered} />}
            </div>

            <div className="mt-3 grid gap-2 lg:grid-cols-[1fr_auto]">
                <div className="flex flex-wrap items-center gap-2 text-[10px]" style={{ color: c.muted }}>
                    <LegendDot color={c.emerald} label="Sağlıklı / çalışan il" />
                    <LegendDot color={c.amber} label="Kısmi sorun" />
                    <LegendDot color={c.rose} label="Tam kesinti, yanıp söner" />
                    <LegendDot color={c.slate} label="Gri rozet: planlı kapalı mağaza" />
                </div>
                <div className="text-[10px]" style={{ color: unmatched.length > 0 ? c.amber : c.subtle }}>
                    {unmatched.length > 0
                        ? `İl eşleşmeyen: ${unmatched.length} (${unmatched.slice(0, 3).map((store) => store.code).join(", ")}${unmatched.length > 3 ? "..." : ""})`
                        : "Tüm mağazalar il ile eşleşti"}
                </div>
            </div>
        </div>
    );
};

const MapStat: React.FC<{ label: string; value: number; color: string; bg: string; pulse?: boolean }> = ({ label, value, color, bg, pulse }) => (
    <div className="flex items-center gap-1.5 rounded-full border px-2 py-1" style={{ background: bg, borderColor: color + "30" }}>
        <span className={`h-1.5 w-1.5 rounded-full ${pulse ? "animate-pulse" : ""}`} style={{ background: color }} />
        <span className="text-[9px] font-semibold" style={{ color }}>{label}</span>
        <span className="text-[9px] font-black tabular-nums" style={{ color }}>{value}</span>
    </div>
);

const LegendDot: React.FC<{ color: string; label: string }> = ({ color, label }) => (
    <span className="inline-flex items-center gap-1.5">
        <span className="h-2 w-2 rounded-full" style={{ background: color }} />
        {label}
    </span>
);

const ProvinceTooltip: React.FC<{ c: MapPalette; hover: HoverState }> = ({ c, hover }) => {
    const { bucket } = hover;
    const issueStores = bucket.stores
        .filter((store) => store.status !== "stable" || store.closedPos > 0)
        .sort((a, b) => storeSortScore(b) - storeSortScore(a) || b.offlinePos - a.offlinePos || a.code - b.code);
    const shownStores = (issueStores.length > 0 ? issueStores : [...bucket.stores].sort((a, b) => a.code - b.code)).slice(0, 9);
    const hiddenHealthyCount = bucket.stores.length - shownStores.length;
    const color = statusColor(bucket.status, c);
    const criticalCount = bucket.stores.filter((store) => store.status === "critical").length;
    const watchCount = bucket.stores.filter((store) => store.status === "watch").length;
    const plannedCount = closedAffectedStoreCount(bucket);

    const top = Math.max(10, hover.anchorAbove ? hover.y - 184 : hover.y + 14);
    const positionStyle: React.CSSProperties = {
        top,
        ...(hover.anchorRight ? { right: 12 } : { left: hover.x + 14 }),
    };

    return (
        <div
            className="pointer-events-none absolute z-30 w-[320px] rounded-xl border p-3 shadow-2xl backdrop-blur-md"
            style={{ ...positionStyle, background: c.cardSoft, borderColor: color + "55", boxShadow: `0 18px 50px rgba(0,0,0,0.38), 0 0 24px ${color}18` }}
        >
            <div className="mb-2 flex items-start justify-between gap-3">
                <div>
                    <div className="text-sm font-black" style={{ color: c.text }}>{bucket.province.name}</div>
                    <div className="text-[10px]" style={{ color: c.muted }}>
                        {bucket.stores.length} mağaza, POS {bucket.onlinePos}/{bucket.activePos} online
                    </div>
                </div>
                <span className="rounded-full px-2 py-1 text-[9px] font-black uppercase" style={{ background: color + "22", color }}>
                    {statusLabel(bucket.status)}
                </span>
            </div>

            <div className="mb-2 flex flex-wrap gap-1.5">
                {criticalCount > 0 && <MiniStat label="Tam" value={criticalCount} color={c.rose} />}
                {watchCount > 0 && <MiniStat label="Kısmi" value={watchCount} color={c.amber} />}
                {plannedCount > 0 && <MiniStat label="Planlı" value={plannedCount} color={c.slate} />}
                {criticalCount === 0 && watchCount === 0 && plannedCount === 0 && <MiniStat label="Sorunsuz" value={bucket.stores.length} color={c.emerald} />}
            </div>

            <div className="space-y-1.5">
                {shownStores.map((store) => {
                    const storeColor = storeBadgeColor(store, c);
                    const age = fmtAge(store.since);
                    return (
                        <div key={`${bucket.province.name}-${store.code}`} className="rounded-lg border px-2.5 py-2" style={{ background: storeColor + "10", borderColor: storeColor + "28" }}>
                            <div className="flex items-center gap-2">
                                <span className="rounded px-1.5 py-0.5 text-[8px] font-black uppercase" style={{ background: storeColor + "24", color: storeColor }}>
                                    {storeBadgeLabel(store)}
                                </span>
                                <span className="min-w-0 flex-1 truncate text-[11px] font-bold" style={{ color: c.text }}>
                                    [{store.code}] {store.name}
                                </span>
                            </div>
                            <div className="mt-1 text-[10px] leading-snug" style={{ color: c.muted }}>
                                {storeDetailText(store)}
                                {age && <span style={{ color: storeColor }}> - {age}</span>}
                            </div>
                        </div>
                    );
                })}
            </div>

            {hiddenHealthyCount > 0 && (
                <div className="mt-2 rounded-lg px-2.5 py-1.5 text-[10px]" style={{ background: c.track, color: c.subtle }}>
                    +{hiddenHealthyCount} mağaza listede gizlendi; öncelik kapalı/sorunlu mağazalarda.
                </div>
            )}
        </div>
    );
};

const MiniStat: React.FC<{ label: string; value: number; color: string }> = ({ label, value, color }) => (
    <span className="inline-flex items-center gap-1 rounded-full px-2 py-1 text-[9px] font-bold" style={{ background: color + "18", color }}>
        {label}
        <span className="font-black">{value}</span>
    </span>
);

export default TurkeyStoreMap;
