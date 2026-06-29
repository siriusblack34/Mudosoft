import React, { useState, useRef, useEffect, useMemo } from 'react';
import {
    AlertTriangle,
    CheckCircle,
    ChevronDown,
    ChevronRight,
    Coins,
    Gift,
    LayoutList,
    Loader2,
    Minus,
    Percent,
    Plus,
    Receipt,
    Search,
    Tag,
    Ticket,
    UserX,
    XCircle,
} from 'lucide-react';
import { apiClient } from '../lib/apiClient';

// ── Types ─────────────────────────────────────────────────────────────────

interface StoreOption { storeCode: number; storeName: string; }
interface ParamEntry { num: string; param1: string; }

interface ProductInfo {
    stockCardId: string;
    code: string;
    name: string;
    unitPrice: number;
    labelPrice: number;
    parameters: ParamEntry[];
    parameterValues: string[];
}

interface RuleItem { recValue: string; compareType: number; }
interface Tier { minAmount: number; discount: number; }

interface CampaignResult {
    campaignId: number;
    campaignCode: string;
    campaignDesc: string;
    isIncluded: boolean;
    includeRules: RuleItem[];
    excludeRules: RuleItem[];
    matchedParams: string[];
    kind: 'indirim' | 'puan' | 'cek' | string;
    unit: '%' | 'TL' | string;
    dtype: number;
    requiresCode: boolean;
    poolNum: number;
    priority: number;
    effect: string;
    tiers: Tier[];
    customerScope: 'all' | 'typed' | string;
    customerLabel: string;
    customerCodes: string[];
    customerNames: { code: string; name: string }[];
    minThreshold: number;
}

interface CheckResult {
    barcode: string;
    storeCode: number;
    geniusStoreCode: number;
    deviceId: string;
    deviceIp: string;
    product: ProductInfo;
    campaigns: CampaignResult[];
    checkedAt: string;
}

type SaleType = 'musterisiz' | 'musterili';
type FireState = 'works' | 'needsCode' | 'needsCustomer' | 'needsAmount' | 'excluded';
type TabKey = 'kosul' | 'sepet';

// ── Helpers ─────────────────────────────────────────────────────────────────

const fmtTL = (n: number) => n.toLocaleString('tr-TR', { maximumFractionDigits: 0 });
const fmtMoney = (n: number) => n.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

function evalCampaign(c: CampaignResult, sale: SaleType, cartTotal: number | ''): FireState {
    if (!c.isIncluded) return 'excluded';
    if (c.requiresCode) return 'needsCode';                       // çek/kod kampanyası — sadece kod girilince
    if (sale === 'musterisiz' && c.customerScope === 'typed') return 'needsCustomer';
    if (c.minThreshold > 0 && cartTotal !== '' && Number(cartTotal) < c.minThreshold) return 'needsAmount';
    return 'works';
}

const baseDiscount = (c: CampaignResult) => (c.tiers.length > 0 ? c.tiers[0].discount : 0);
const isTiered = (c: CampaignResult) => c.tiers.some(t => t.minAmount > 0);
const isGift   = (c: CampaignResult) => c.kind === 'indirim' && c.unit === '%' && baseDiscount(c) >= 100;

// ── Component ─────────────────────────────────────────────────────────────

export default function KampanyaKontrolPage() {
    const [stores, setStores]       = useState<StoreOption[]>([]);
    const [storeCode, setStoreCode] = useState<number | ''>('');
    const [barcode, setBarcode]     = useState('');
    const [loading, setLoading]     = useState(false);
    const [storesLoading, setStoresLoading] = useState(true);
    const [result, setResult]       = useState<CheckResult | null>(null);
    const [error, setError]         = useState<string | null>(null);
    const [expanded, setExpanded]   = useState<Set<number>>(new Set());
    const [tab, setTab]             = useState<TabKey>('kosul');

    // senaryo (koşul sekmesi)
    const [saleType, setSaleType]   = useState<SaleType>('musterisiz');
    const [cartTotal, setCartTotal] = useState<number | ''>('');

    const barcodeRef = useRef<HTMLInputElement>(null);

    useEffect(() => {
        apiClient.get<StoreOption[]>('/api/campaign-control/stores')
            .then(data => setStores(data))
            .catch(() => setError('Mağaza listesi yüklenemedi'))
            .finally(() => setStoresLoading(false));
    }, []);

    const handleCheck = async () => {
        if (!barcode.trim() || storeCode === '') return;
        setLoading(true);
        setError(null);
        setResult(null);
        setExpanded(new Set());
        try {
            const data = await apiClient.get<CheckResult>(
                `/api/campaign-control/check?barcode=${encodeURIComponent(barcode.trim())}&storeCode=${storeCode}`,
                30_000
            );
            setResult(data);
        } catch (e: unknown) {
            setError(e instanceof Error ? e.message : 'Sorgu başarısız');
        } finally {
            setLoading(false);
        }
    };

    const handleKeyDown = (e: React.KeyboardEvent) => { if (e.key === 'Enter') handleCheck(); };

    const toggleExpand = (id: number) => {
        setExpanded(prev => {
            const next = new Set(prev);
            next.has(id) ? next.delete(id) : next.add(id);
            return next;
        });
    };

    const evaluated = useMemo(() => {
        if (!result) return [];
        const order: Record<FireState, number> = { works: 0, needsAmount: 1, needsCustomer: 2, needsCode: 3, excluded: 4 };
        return result.campaigns
            .map(c => ({ c, state: evalCampaign(c, saleType, cartTotal) }))
            .sort((a, b) => order[a.state] - order[b.state] || a.c.campaignId - b.c.campaignId);
    }, [result, saleType, cartTotal]);

    const counts = useMemo(() => {
        const acc = { works: 0, needsAmount: 0, needsCustomer: 0, needsCode: 0, excluded: 0 };
        evaluated.forEach(e => { acc[e.state]++; });
        return acc;
    }, [evaluated]);

    return (
        <div className="p-4 space-y-3 max-w-6xl mx-auto">
            {/* Header + Form */}
            <div className="flex flex-wrap items-end gap-3 bg-zinc-800/60 border border-zinc-700/50 rounded-xl px-4 py-3">
                <div className="flex items-center gap-2.5 mr-2">
                    <div className="p-1.5 bg-amber-500/15 rounded-lg">
                        <Tag className="h-4 w-4 text-amber-400" />
                    </div>
                    <h1 className="text-base font-semibold text-white whitespace-nowrap">Kampanya Kontrol</h1>
                </div>

                <div className="flex-1 min-w-[180px] space-y-1">
                    <label className="text-[10px] font-medium text-zinc-500 uppercase tracking-wide">Mağaza</label>
                    <select
                        value={storeCode}
                        onChange={e => setStoreCode(e.target.value === '' ? '' : Number(e.target.value))}
                        disabled={storesLoading}
                        className="w-full bg-zinc-900/80 border border-zinc-600/50 rounded-lg px-2.5 py-2 text-sm text-white focus:outline-none focus:border-amber-500/60 disabled:opacity-50"
                    >
                        <option value="">{storesLoading ? 'Yükleniyor…' : 'Mağaza seçin'}</option>
                        {stores.map(s => (
                            <option key={s.storeCode} value={s.storeCode}>{s.storeCode} – {s.storeName}</option>
                        ))}
                    </select>
                </div>

                <div className="flex-1 min-w-[200px] space-y-1">
                    <label className="text-[10px] font-medium text-zinc-500 uppercase tracking-wide">Barkod</label>
                    <input
                        ref={barcodeRef}
                        type="text"
                        value={barcode}
                        onChange={e => setBarcode(e.target.value)}
                        onKeyDown={handleKeyDown}
                        placeholder="Barkod girin veya okutun"
                        className="w-full bg-zinc-900/80 border border-zinc-600/50 rounded-lg px-2.5 py-2 text-sm text-white placeholder-zinc-500 focus:outline-none focus:border-amber-500/60"
                        autoComplete="off"
                    />
                </div>

                <button
                    onClick={handleCheck}
                    disabled={loading || !barcode.trim() || storeCode === ''}
                    className="flex items-center gap-2 px-4 py-2 bg-amber-500 hover:bg-amber-400 disabled:bg-zinc-700 disabled:text-zinc-500 text-zinc-900 font-medium rounded-lg text-sm transition-colors"
                >
                    {loading
                        ? <><Loader2 className="h-4 w-4 animate-spin" /> Sorgulanıyor…</>
                        : <><Search className="h-4 w-4" /> Sorgula</>
                    }
                </button>
            </div>

            {error && (
                <div className="flex items-start gap-2.5 bg-red-500/10 border border-red-500/30 rounded-xl px-4 py-2.5">
                    <XCircle className="h-4 w-4 text-red-400 flex-shrink-0 mt-0.5" />
                    <p className="text-sm text-red-300">{error}</p>
                </div>
            )}

            {result && (
                <div className="space-y-3">
                    {/* Ürün şeridi */}
                    <div className="bg-zinc-800/60 border border-zinc-700/50 rounded-xl px-4 py-3">
                        <div className="flex flex-wrap items-center gap-x-5 gap-y-1.5">
                            <div className="min-w-0">
                                <span className="text-sm font-semibold text-white">{result.product.name || '—'}</span>
                                <span className="text-xs text-zinc-500 ml-2 font-mono">{result.product.code}</span>
                            </div>
                            <span className="text-xs text-zinc-500 font-mono">Barkod: {result.barcode}</span>
                            {result.product.unitPrice > 0 && (
                                <span className="text-xs text-emerald-300 font-mono font-semibold">{fmtMoney(result.product.unitPrice)} TL</span>
                            )}
                            <span className="text-[11px] text-zinc-600 ml-auto">{result.deviceId} · ID {result.product.stockCardId}</span>
                        </div>
                        <div className="flex flex-wrap items-center gap-1 mt-2 pt-2 border-t border-zinc-700/40">
                            <span className="text-[10px] text-zinc-500 uppercase tracking-wide mr-1">PARAM_1</span>
                            {result.product.parameterValues.length > 0
                                ? result.product.parameters.map((p, i) => (
                                    <span key={i} className="text-[11px] font-mono bg-zinc-900/70 border border-zinc-700/50 rounded px-1.5 py-0.5 text-zinc-300">
                                        {p.param1}{p.num && <span className="text-zinc-600 ml-0.5">#{p.num}</span>}
                                    </span>
                                ))
                                : <span className="text-[11px] text-amber-400">parametre bulunamadı</span>
                            }
                        </div>
                    </div>

                    {/* Sekmeler */}
                    <div className="flex items-center gap-1 bg-zinc-900/60 border border-zinc-700/50 rounded-lg p-0.5 w-fit">
                        <TabBtn active={tab === 'kosul'} onClick={() => setTab('kosul')} icon={<LayoutList className="h-4 w-4" />} label="Koşul Görünümü" />
                        <TabBtn active={tab === 'sepet'} onClick={() => setTab('sepet')} icon={<Receipt className="h-4 w-4" />} label="Sepet Simülasyonu" />
                    </div>

                    {/* ── KOŞUL GÖRÜNÜMÜ ── */}
                    {tab === 'kosul' && (
                        <>
                            <div className="bg-zinc-800/60 border border-zinc-700/50 rounded-xl px-4 py-3 flex flex-wrap items-center gap-4">
                                <span className="text-xs font-semibold text-zinc-300">Senaryo:</span>
                                <div className="flex items-center gap-1 bg-zinc-900/70 border border-zinc-700/50 rounded-lg p-0.5">
                                    {(['musterisiz', 'musterili'] as SaleType[]).map(t => (
                                        <button key={t} onClick={() => setSaleType(t)}
                                            className={`text-xs px-3 py-1.5 rounded-md font-medium transition-colors ${
                                                saleType === t ? 'bg-amber-500 text-zinc-900' : 'text-zinc-400 hover:text-zinc-200'}`}>
                                            {t === 'musterisiz' ? 'Müşterisiz' : 'Müşterili (kartlı)'}
                                        </button>
                                    ))}
                                </div>
                                <div className="flex items-center gap-2">
                                    <label className="text-xs text-zinc-400">Sepet tutarı</label>
                                    <input type="number" value={cartTotal}
                                        onChange={e => setCartTotal(e.target.value === '' ? '' : Number(e.target.value))}
                                        placeholder="opsiyonel"
                                        className="w-28 bg-zinc-900/80 border border-zinc-600/50 rounded-lg px-2.5 py-1.5 text-sm text-white placeholder-zinc-600 focus:outline-none focus:border-amber-500/60" />
                                    <span className="text-xs text-zinc-500">TL</span>
                                </div>
                                <div className="flex flex-wrap items-center gap-2 ml-auto text-xs">
                                    <Badge tone="emerald" icon={<CheckCircle className="h-3.5 w-3.5" />} text={`${counts.works} çalışır`} />
                                    {counts.needsAmount > 0 && <Badge tone="amber" icon={<AlertTriangle className="h-3.5 w-3.5" />} text={`${counts.needsAmount} tutar eşiği`} />}
                                    {counts.needsCustomer > 0 && <Badge tone="sky" icon={<UserX className="h-3.5 w-3.5" />} text={`${counts.needsCustomer} müşteri gerekli`} />}
                                    {counts.needsCode > 0 && <Badge tone="violet" icon={<Ticket className="h-3.5 w-3.5" />} text={`${counts.needsCode} çek/kod`} />}
                                    {counts.excluded > 0 && <Badge tone="red" icon={<XCircle className="h-3.5 w-3.5" />} text={`${counts.excluded} hariç`} />}
                                </div>
                            </div>

                            <p className="text-[11px] text-zinc-500 px-1 -mt-1">
                                ⓘ Durumlar <span className="text-zinc-400">ön koşul</span> kontrolüdür (müşteri tipi + tutar eşiği). Kesin tetiklenme; sepetteki diğer kampanyalar, öncelik ve net tutar hesabına göre değişebilir.
                            </p>

                            {evaluated.length > 0 && (
                                <div className="grid grid-cols-1 lg:grid-cols-2 gap-2">
                                    {evaluated.map(({ c, state }) => (
                                        <CampaignRow key={c.campaignId} campaign={c} state={state}
                                            expanded={expanded.has(c.campaignId)} onToggle={() => toggleExpand(c.campaignId)} />
                                    ))}
                                </div>
                            )}
                            {result.campaigns.length === 0 && (
                                <div className="flex items-center gap-2 bg-zinc-700/40 border border-zinc-600/30 rounded-lg px-3 py-2 w-fit">
                                    <AlertTriangle className="h-4 w-4 text-zinc-400" />
                                    <span className="text-sm text-zinc-400">Ürün hiçbir aktif kampanyada değil</span>
                                </div>
                            )}
                        </>
                    )}

                    {/* ── SEPET SİMÜLASYONU ── */}
                    {tab === 'sepet' && <CartSimulator product={result.product} campaigns={result.campaigns} />}
                </div>
            )}
        </div>
    );
}

// ── Sepet Simülatörü ───────────────────────────────────────────────────────

function CartSimulator({ product, campaigns }: { product: ProductInfo; campaigns: CampaignResult[] }) {
    const [qty, setQty]         = useState(1);
    const [custCode, setCustCode] = useState('');
    const [applyCek, setApplyCek] = useState(false);

    const cekCount = useMemo(() => campaigns.filter(c => c.kind === 'cek' && c.isIncluded).length, [campaigns]);

    const custOptions = useMemo(() => {
        const map = new Map<string, string>();
        campaigns.forEach(c => c.customerNames.forEach(cn => map.set(cn.code, cn.name)));
        return [{ code: '', name: 'Müşterisiz (kartsız satış)' },
                ...[...map.entries()].sort((a, b) => Number(a[0]) - Number(b[0])).map(([code, name]) => ({ code, name }))];
    }, [campaigns]);

    const sim = useMemo(() => {
        const unitPrice = product.unitPrice || 0;
        const araToplam = qty * unitPrice;
        const activeCust = custCode === '' ? new Set<string>() : new Set([custCode]);

        const applies = (c: CampaignResult) =>
            c.isIncluded && (c.customerScope === 'all' || c.customerCodes.some(code => activeCust.has(code)));

        const eligible = campaigns.filter(applies);

        // Havuz çakışması: aynı POOL_NUM içinde yalnızca en düşük PRIORITY çalışır.
        // (poolNum=0 → havuzsuz, hepsi çalışır)
        const resolvePool = (list: CampaignResult[]) => {
            const byPool = new Map<number, CampaignResult[]>();
            const winners: CampaignResult[] = [];
            const suppressed: { c: CampaignResult; winner: CampaignResult }[] = [];
            for (const c of list) {
                if (!c.poolNum) { winners.push(c); continue; }
                const g = byPool.get(c.poolNum) ?? [];
                g.push(c); byPool.set(c.poolNum, g);
            }
            byPool.forEach(grp => {
                const sorted = [...grp].sort((a, b) => a.priority - b.priority);
                winners.push(sorted[0]);
                sorted.slice(1).forEach(c => suppressed.push({ c, winner: sorted[0] }));
            });
            return { winners, suppressed };
        };

        // çek/kod kampanyaları yalnızca "çek uygulandı" işaretliyse indirim olarak girer
        const isDiscount = (c: CampaignResult) => c.kind === 'indirim' || (applyCek && c.kind === 'cek');
        // Çalışma satırı: RESULT (ürün hedef) doluysa ürün-bazlı/ara toplam, boşsa toplam.
        // Havuz çakışması yalnızca AYNI satır içinde geçerli (farklı satır + aynı havuz → ikisi de çalışır).
        const discountCamps = eligible.filter(isDiscount);
        const araLine    = resolvePool(discountCamps.filter(c => c.includeRules.length > 0));
        const topLine    = resolvePool(discountCamps.filter(c => c.includeRules.length === 0));
        const pointPool  = resolvePool(eligible.filter(c => c.kind === 'puan'));

        const discWinners  = [...araLine.winners, ...topLine.winners];
        const productLevel = discWinners.filter(c => !isTiered(c));
        const totalLevel   = discWinners.filter(c => isTiered(c));
        const pointCamps   = pointPool.winners;
        const suppressed   = [...araLine.suppressed, ...topLine.suppressed, ...pointPool.suppressed];

        const discounts: { code: string; name: string; amount: number; note: string }[] = [];
        let running = araToplam;

        // 1) Ürün seviyesi (1A1H / yüzde / tutar) — net tutarı düşürür
        for (const c of productLevel) {
            let amount = 0; let note = '';
            const d = baseDiscount(c);
            if (isGift(c)) {
                const free = Math.floor(qty / 2);   // 1 Al 1 Hediye yaklaşımı
                amount = free * unitPrice;
                note = `${free} adet bedelsiz`;
            } else if (c.unit === '%') {
                amount = running * d / 100;
                note = `%${fmtTL(d)}`;
            } else {
                amount = d;
                note = `${fmtMoney(d)} TL sabit`;
            }
            amount = Math.min(amount, running);
            if (amount > 0) { running -= amount; discounts.push({ code: c.campaignCode, name: c.campaignDesc, amount, note }); }
        }

        // 2) Toplam seviyesi (kademeli tutar eşiği) — düşen net tutara göre
        for (const c of totalLevel) {
            const tier = [...c.tiers].filter(t => running >= t.minAmount).sort((a, b) => b.minAmount - a.minAmount)[0];
            if (!tier) continue;
            const amount = c.unit === '%' ? running * tier.discount / 100 : tier.discount;
            const real = Math.min(amount, running);
            if (real > 0) {
                running -= real;
                discounts.push({ code: c.campaignCode, name: c.campaignDesc, amount: real, note: `≥${fmtTL(tier.minAmount)} TL eşiği` });
            }
        }

        const toplam = running;
        const points = pointCamps.map(c => ({
            code: c.campaignCode, name: c.campaignDesc,
            value: toplam * baseDiscount(c) / 100, rate: baseDiscount(c),
        }));

        return { unitPrice, araToplam, discounts, toplam, points, suppressed, eligibleCount: eligible.length };
    }, [product, campaigns, qty, custCode, applyCek]);

    const noPrice = !product.unitPrice;

    return (
        <div className="grid grid-cols-1 lg:grid-cols-[320px_1fr] gap-3">
            {/* Kontroller */}
            <div className="space-y-3">
                <div className="bg-zinc-800/60 border border-zinc-700/50 rounded-xl p-4 space-y-3">
                    <div className="space-y-1.5">
                        <label className="text-[10px] font-medium text-zinc-500 uppercase tracking-wide">Müşteri Tipi</label>
                        <select value={custCode} onChange={e => setCustCode(e.target.value)}
                            className="w-full bg-zinc-900/80 border border-zinc-600/50 rounded-lg px-2.5 py-2 text-sm text-white focus:outline-none focus:border-amber-500/60">
                            {custOptions.map(o => <option key={o.code || 'none'} value={o.code}>{o.name}{o.code && ` (#${o.code})`}</option>)}
                        </select>
                    </div>

                    <div className="space-y-1.5">
                        <label className="text-[10px] font-medium text-zinc-500 uppercase tracking-wide">Adet</label>
                        <div className="flex items-center gap-2">
                            <button onClick={() => setQty(q => Math.max(1, q - 1))}
                                className="p-2 bg-zinc-900/80 border border-zinc-600/50 rounded-lg text-zinc-300 hover:bg-zinc-700 transition-colors">
                                <Minus className="h-4 w-4" />
                            </button>
                            <input type="number" min={1} value={qty}
                                onChange={e => setQty(Math.max(1, Number(e.target.value) || 1))}
                                className="w-16 text-center bg-zinc-900/80 border border-zinc-600/50 rounded-lg px-2 py-2 text-sm text-white focus:outline-none focus:border-amber-500/60" />
                            <button onClick={() => setQty(q => q + 1)}
                                className="p-2 bg-zinc-900/80 border border-zinc-600/50 rounded-lg text-zinc-300 hover:bg-zinc-700 transition-colors">
                                <Plus className="h-4 w-4" />
                            </button>
                            <span className="text-xs text-zinc-500 ml-1">× {fmtMoney(sim.unitPrice)} TL</span>
                        </div>
                    </div>

                    {cekCount > 0 && (
                        <label className="flex items-center gap-2 pt-2 border-t border-zinc-700/40 cursor-pointer select-none">
                            <input type="checkbox" checked={applyCek} onChange={e => setApplyCek(e.target.checked)}
                                className="accent-violet-500 h-4 w-4" />
                            <span className="text-xs text-zinc-300 flex items-center gap-1.5">
                                <Ticket className="h-3.5 w-3.5 text-violet-400" />
                                Çek / kod uygula
                                <span className="text-zinc-500">({cekCount} adet)</span>
                            </span>
                        </label>
                    )}

                    <div className="pt-2 border-t border-zinc-700/40 text-xs text-zinc-400">
                        Uygulanan kampanya: <span className="text-emerald-300 font-medium">{sim.discounts.length} indirim</span>
                        {sim.points.length > 0 && <span> · <span className="text-violet-300">{sim.points.length} puan</span></span>}
                    </div>
                </div>

                <p className="text-[11px] text-zinc-500 px-1">
                    ⓘ Simülasyon: ürün-seviyesi kampanyalar (1A1H, %) önce, ardından tutar-eşikli toplam kampanyaları net tutara uygulanır. Gerçek kasada öncelik/pool kuralları küçük farklar yaratabilir.
                </p>
            </div>

            {/* POS Fiş Ekranı */}
            <div className="rounded-xl overflow-hidden border border-zinc-700/50 shadow-lg">
                <div className="bg-[#3949ab] text-white text-sm font-semibold px-4 py-2 flex items-center justify-between">
                    <span>Fiş Belgesi</span>
                    <span className="text-xs font-normal opacity-80">
                        {custCode === '' ? 'SATICISIZ / MÜŞTERİSİZ' : custOptions.find(o => o.code === custCode)?.name}
                    </span>
                </div>
                <div className="bg-[#fbfbe9] text-zinc-800 px-4 py-3 font-mono text-sm min-h-[300px]">
                    {noPrice ? (
                        <div className="flex items-center gap-2 text-amber-700">
                            <AlertTriangle className="h-4 w-4" /> Bu ürün için fiyat bulunamadı (STOCK_PRICE).
                        </div>
                    ) : (
                        <>
                            {/* Satırlar */}
                            <div className="space-y-0.5">
                                {Array.from({ length: qty }).map((_, i) => (
                                    <div key={i}>
                                        <div className="flex justify-between text-[13px]">
                                            <span className="truncate pr-2">{i + 1}. {product.name}</span>
                                        </div>
                                        <div className="flex justify-between text-[13px] text-blue-700">
                                            <span className="pl-3">1 ad x {fmtMoney(sim.unitPrice)} TL</span>
                                            <span>{fmtMoney(sim.unitPrice)} TL</span>
                                        </div>
                                    </div>
                                ))}
                            </div>

                            {/* Ara Toplam */}
                            <div className="flex justify-between font-bold text-[15px] border-t border-zinc-400/60 mt-2 pt-1.5">
                                <span>Ara Toplam</span>
                                <span>{fmtMoney(sim.araToplam)} TL</span>
                            </div>

                            {/* İndirim kampanyaları */}
                            {sim.discounts.map((d, i) => (
                                <div key={i} className="bg-[#e6e6fa] rounded my-1 px-2 py-1.5">
                                    <div className="text-[12px] font-semibold text-[#2e2e6e]">Toplam kampanyası: {d.code}</div>
                                    <div className="flex justify-between text-[12px] text-[#1a1aa0]">
                                        <span>İndirim tutarı : {fmtMoney(d.amount)} TL</span>
                                        <span className="text-[#6b6b9e]">{d.note}</span>
                                    </div>
                                </div>
                            ))}

                            {/* Toplam */}
                            <div className="flex justify-between font-bold text-[15px] border-t border-zinc-400/60 mt-1 pt-1.5">
                                <span>Toplam :</span>
                                <span>{fmtMoney(sim.toplam)} TL</span>
                            </div>

                            {/* Puan kampanyaları */}
                            {sim.points.map((p, i) => (
                                <div key={i} className="bg-[#dcedff] rounded my-1 px-2 py-1.5">
                                    <div className="text-[12px] font-semibold text-[#0b3d91]">Toplam kampanyası: {p.code}</div>
                                    <div className="flex justify-between text-[12px] text-[#0b5cad]">
                                        <span>Kazanılan Puan : {fmtMoney(p.value)} TL</span>
                                        <span className="text-[#5a7fb0]">%{fmtTL(p.rate)} puan</span>
                                    </div>
                                </div>
                            ))}

                            {/* Kalan bakiye */}
                            <div className="flex justify-between font-bold text-[15px] border-t-2 border-dashed border-zinc-500/70 mt-2 pt-1.5">
                                <span>Kalan bakiye</span>
                                <span>{fmtMoney(sim.toplam)} TL</span>
                            </div>

                            {sim.discounts.length === 0 && sim.points.length === 0 && (
                                <div className="text-[12px] text-zinc-500 mt-3 italic">Bu müşteri tipi ve adette uygulanan kampanya yok.</div>
                            )}

                            {/* Havuz çakışması ile elenenler */}
                            {sim.suppressed.length > 0 && (
                                <div className="mt-3 pt-2 border-t border-dashed border-zinc-400/50">
                                    <div className="text-[11px] font-semibold text-zinc-500 mb-1">Havuz çakışması ile elenenler:</div>
                                    {sim.suppressed.map((s, i) => (
                                        <div key={i} className="flex justify-between text-[11px] text-zinc-400 line-through decoration-zinc-400/60">
                                            <span>{s.c.campaignCode}</span>
                                            <span className="no-underline text-zinc-500">havuz {s.c.poolNum} · öncelik {s.c.priority} &gt; {s.winner.campaignCode}</span>
                                        </div>
                                    ))}
                                </div>
                            )}
                        </>
                    )}
                </div>
            </div>
        </div>
    );
}

// ── Sub-components ────────────────────────────────────────────────────────

function TabBtn({ active, onClick, icon, label }: { active: boolean; onClick: () => void; icon: React.ReactNode; label: string }) {
    return (
        <button onClick={onClick}
            className={`flex items-center gap-2 text-sm px-3.5 py-1.5 rounded-md font-medium transition-colors ${
                active ? 'bg-amber-500 text-zinc-900' : 'text-zinc-400 hover:text-zinc-200'}`}>
            {icon}{label}
        </button>
    );
}

function Badge({ tone, icon, text }: { tone: 'emerald' | 'amber' | 'sky' | 'red' | 'violet'; icon: React.ReactNode; text: string }) {
    const cls = {
        emerald: 'bg-emerald-500/10 border-emerald-500/25 text-emerald-300',
        amber:   'bg-amber-500/10 border-amber-500/25 text-amber-300',
        sky:     'bg-sky-500/10 border-sky-500/25 text-sky-300',
        red:     'bg-red-500/10 border-red-500/25 text-red-300',
        violet:  'bg-violet-500/10 border-violet-500/25 text-violet-300',
    }[tone];
    return <span className={`flex items-center gap-1.5 border rounded-md px-2 py-1 font-medium ${cls}`}>{icon}{text}</span>;
}

const STATE_META: Record<FireState, { label: string; dot: string; border: string; bg: string; badge: string }> = {
    works:         { label: 'ÇALIŞIR',         dot: 'bg-emerald-400', border: 'border-emerald-500/30', bg: 'bg-emerald-500/5', badge: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30' },
    needsAmount:   { label: 'TUTAR EŞİĞİ',     dot: 'bg-amber-400',   border: 'border-amber-500/25',   bg: 'bg-amber-500/5',   badge: 'bg-amber-500/15 text-amber-400 border-amber-500/30' },
    needsCustomer: { label: 'MÜŞTERİ GEREKLİ', dot: 'bg-sky-400',     border: 'border-sky-500/25',     bg: 'bg-sky-500/5',     badge: 'bg-sky-500/15 text-sky-400 border-sky-500/30' },
    needsCode:     { label: 'ÇEK / KOD',       dot: 'bg-violet-400',  border: 'border-violet-500/25',  bg: 'bg-violet-500/5',  badge: 'bg-violet-500/15 text-violet-400 border-violet-500/30' },
    excluded:      { label: 'HARİÇ',           dot: 'bg-red-400',     border: 'border-red-500/25',     bg: 'bg-red-500/5',     badge: 'bg-red-500/15 text-red-400 border-red-500/30' },
};

function KindIcon({ kind, unit }: { kind: string; unit: string }) {
    if (kind === 'puan') return <Coins className="h-4 w-4 text-violet-400" />;
    if (kind === 'cek')  return <Ticket className="h-4 w-4 text-violet-400" />;
    if (unit === '%')    return <Percent className="h-4 w-4 text-amber-400" />;
    return <Gift className="h-4 w-4 text-amber-400" />;
}

function CampaignRow({ campaign, state, expanded, onToggle }: {
    campaign: CampaignResult;
    state: FireState;
    expanded: boolean;
    onToggle: () => void;
}) {
    const { campaignId, campaignCode, campaignDesc, includeRules, excludeRules, matchedParams,
            kind, unit, effect, customerScope, customerLabel } = campaign;
    const meta = STATE_META[state];

    return (
        <div className={`border rounded-lg overflow-hidden ${meta.border} ${meta.bg}`}>
            <button onClick={onToggle} className="w-full flex items-center gap-2.5 px-3 py-2 text-left hover:bg-white/[0.02] transition-colors">
                <span className={`h-2 w-2 rounded-full flex-shrink-0 ${meta.dot}`} />
                <KindIcon kind={kind} unit={unit} />
                <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-1.5">
                        <span className="text-sm font-medium text-white truncate">{campaignCode}</span>
                        {campaignDesc && campaignDesc !== campaignCode && (
                            <span className="text-xs text-zinc-500 truncate">· {campaignDesc}</span>
                        )}
                    </div>
                    <div className="flex items-center gap-2 mt-0.5">
                        <span className="text-[11px] text-amber-300/90 font-medium truncate">{effect}</span>
                    </div>
                </div>
                <div className="flex flex-col items-end gap-1 flex-shrink-0">
                    <span className={`text-[10px] px-1.5 py-0.5 rounded border font-medium ${meta.badge}`}>
                        {meta.label}
                    </span>
                    <span className={`text-[10px] px-1.5 py-0.5 rounded ${
                        customerScope === 'all' ? 'text-zinc-400 bg-zinc-700/40' : 'text-sky-300/80 bg-sky-500/10'}`}>
                        {customerLabel}
                    </span>
                </div>
                {expanded
                    ? <ChevronDown  className="h-3.5 w-3.5 text-zinc-500 flex-shrink-0" />
                    : <ChevronRight className="h-3.5 w-3.5 text-zinc-500 flex-shrink-0" />
                }
            </button>

            {expanded && (
                <div className="px-3 pb-2.5 pt-1 border-t border-white/5 space-y-2 text-xs">
                    <div className="flex flex-wrap gap-x-4 gap-y-1 text-[11px] text-zinc-400">
                        <span>Kampanya ID: <span className="text-zinc-300">{campaignId}</span></span>
                        <span>Tip: <span className="text-zinc-300">{kind === 'puan' ? 'Puan' : kind === 'cek' ? 'Çek/Kod' : 'İndirim'}</span></span>
                        <span>Satır: <span className="text-zinc-300">{kind === 'puan' ? 'Toplam (puan)' : includeRules.length > 0 ? 'Ürün bazlı (ara toplam)' : 'Toplam (sepet)'}</span></span>
                        {campaign.poolNum > 0 && <span>Havuz: <span className="text-zinc-300">{campaign.poolNum}</span></span>}
                        {campaign.priority > 0 && <span>Öncelik: <span className="text-zinc-300">{campaign.priority}</span></span>}
                        {campaign.minThreshold > 0 && <span>Min. sepet: <span className="text-zinc-300">{fmtTL(campaign.minThreshold)} TL</span></span>}
                    </div>

                    <div className="flex flex-wrap items-center gap-1.5">
                        <span className="text-[10px] text-zinc-500">Müşteri:</span>
                        {customerScope === 'all'
                            ? <span className="text-[11px] px-1.5 py-0.5 rounded bg-zinc-700/40 text-zinc-300">Herkes (müşterisiz dahil)</span>
                            : campaign.customerNames.map((cn, i) => (
                                <span key={i} className="text-[11px] px-1.5 py-0.5 rounded bg-sky-500/10 text-sky-300 border border-sky-500/20">
                                    {cn.name} <span className="text-sky-500/60 font-mono">#{cn.code}</span>
                                </span>
                            ))
                        }
                    </div>

                    {campaign.tiers.length > 1 && (
                        <div className="flex flex-wrap items-center gap-1.5">
                            <span className="text-[10px] text-zinc-500">Kademeler:</span>
                            {campaign.tiers.map((t, i) => (
                                <span key={i} className="font-mono text-[11px] px-1.5 py-0.5 rounded border bg-amber-500/10 border-amber-500/20 text-amber-300">
                                    ≥{fmtTL(t.minAmount)}→{unit === '%' ? `%${fmtTL(t.discount)}` : `${fmtTL(t.discount)} TL`}
                                </span>
                            ))}
                        </div>
                    )}

                    {includeRules.length > 0 && <RuleGroup title="Ürün dahil (TYPE=0)" rules={includeRules} color="emerald" />}
                    {excludeRules.length > 0 && <RuleGroup title="Ürün hariç (TYPE=3)" rules={excludeRules} color="red" />}
                    <div className="text-[10px] text-zinc-600">
                        Eşleşen parametre: <span className="text-zinc-400 font-mono">{matchedParams.join(', ')}</span>
                    </div>
                </div>
            )}
        </div>
    );
}

function RuleGroup({ title, rules, color }: { title: string; rules: RuleItem[]; color: 'emerald' | 'red' }) {
    const chipCls = color === 'emerald'
        ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-300'
        : 'bg-red-500/10 border-red-500/20 text-red-300';
    const labelCls = color === 'emerald' ? 'text-emerald-400' : 'text-red-400';
    return (
        <div className="flex flex-wrap items-center gap-1.5">
            <span className={`text-[10px] font-medium ${labelCls}`}>{title}:</span>
            {rules.map((r, i) => (
                <span key={i} className={`font-mono text-[11px] px-1.5 py-0.5 rounded border ${chipCls}`}>{r.recValue}</span>
            ))}
        </div>
    );
}
