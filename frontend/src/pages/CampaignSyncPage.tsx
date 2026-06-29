import React, { useState, useCallback } from 'react';
import {
    AlertTriangle,
    Archive,
    CheckCircle,
    ChevronDown,
    ChevronRight,
    Clock,
    Database,
    Monitor,
    RefreshCw,
    Search,
    Server,
    ShieldCheck,
    XCircle,
} from 'lucide-react';
import { apiClient } from '../lib/apiClient';

// ── Types ─────────────────────────────────────────────────────────────────

interface TableCheck {
    table: string;
    exists: boolean;
    count: number;
    note?: string;
}

interface MissingCampaign {
    id: number;
    code: string;
    description: string;
    fkCampaignPeriod: number;
    ckCampaignDiscountDef: number;
    tableChecks: TableCheck[];
}

interface DeviceResult {
    deviceId: string;
    deviceName: string;
    deviceType: string;
    ipAddress: string;
    status: 'ok' | 'missing' | 'offline' | 'no_conn';
    error?: string | null;
    merkezCount: number;
    deviceCount: number;
    missingCampaigns: MissingCampaign[];
}

interface SingleStoreResult {
    storeCode: number;
    checkedAt: string;
    merkezError?: string | null;
    merkezCount: number;
    merkezCampaignIds: number[];
    devices: DeviceResult[];
}

interface StoreEntry {
    storeCode: number;
    storeName: string;
    merkezCount: number;
    merkezError?: string | null;
    devices: DeviceResult[];
}

interface AllStoresResult {
    checkedAt: string;
    totalStores: number;
    stores: StoreEntry[];
}

// ── Helpers ───────────────────────────────────────────────────────────────

type DevStatus = DeviceResult['status'];

function statusBadge(s: DevStatus): string {
    switch (s) {
        case 'ok':      return 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30';
        case 'missing': return 'bg-red-500/15 text-red-400 border-red-500/30';
        case 'offline': return 'bg-zinc-700/60 text-zinc-400 border-zinc-600/40';
        case 'no_conn': return 'bg-amber-500/15 text-amber-400 border-amber-500/30';
    }
}

function statusCard(s: DevStatus): string {
    switch (s) {
        case 'ok':      return 'border-emerald-500/20 bg-emerald-950/5';
        case 'missing': return 'border-red-500/30 bg-red-950/10';
        case 'offline': return 'border-zinc-600/30 bg-zinc-800/20';
        case 'no_conn': return 'border-amber-500/30 bg-amber-950/10';
    }
}

function statusLabel(s: DevStatus): string {
    switch (s) {
        case 'ok':      return 'OK';
        case 'missing': return 'Eksik';
        case 'offline': return 'Offline';
        case 'no_conn': return 'Bağlantı yok';
    }
}

function getStoreAgg(store: StoreEntry): 'ok' | 'missing' | 'offline' | 'error' {
    if (store.merkezError) return 'error';
    if (store.devices.some(d => d.status === 'missing')) return 'missing';
    if (store.devices.length > 0 && store.devices.every(d => d.status === 'ok')) return 'ok';
    if (store.devices.some(d => d.status === 'offline' || d.status === 'no_conn')) return 'offline';
    return 'ok';
}

function totalMissingForStore(store: StoreEntry) {
    return store.devices.reduce((s, d) => s + d.missingCampaigns.length, 0);
}

// ── Small components ──────────────────────────────────────────────────────

function DeviceTypeIcon({ deviceType }: { deviceType: string }) {
    if (deviceType === 'PC')           return <Monitor className="h-4 w-4 text-sky-400" />;
    if (deviceType.startsWith('Kasa')) return <Archive className="h-4 w-4 text-violet-400" />;
    return <Server className="h-4 w-4 text-zinc-400" />;
}

function StatusIcon({ status }: { status: DevStatus }) {
    switch (status) {
        case 'ok':      return <CheckCircle   className="h-4 w-4 text-emerald-400" />;
        case 'missing': return <XCircle       className="h-4 w-4 text-red-400" />;
        case 'offline': return <AlertTriangle className="h-4 w-4 text-zinc-400" />;
        case 'no_conn': return <AlertTriangle className="h-4 w-4 text-amber-400" />;
    }
}

// Özet tablosundaki her hücre (PC, Kasa-1, vb.)
function DeviceCell({ device }: { device: DeviceResult | undefined }) {
    if (!device) return <td className="px-3 py-2 text-center text-xs text-zinc-600">—</td>;

    const missing = device.missingCampaigns.length;
    if (device.status === 'ok')
        return (
            <td className="px-3 py-2 text-center">
                <CheckCircle className="mx-auto h-4 w-4 text-emerald-400" />
            </td>
        );
    if (device.status === 'missing')
        return (
            <td className="px-3 py-2 text-center text-xs font-bold text-red-400">
                ✗{missing > 0 ? ` ${missing}` : ''}
            </td>
        );
    return (
        <td className="px-3 py-2 text-center text-xs text-zinc-500">~</td>
    );
}

// ── TableCheckRow & MissingCampaignCard & DeviceCard (detay görünümü) ─────

function TableCheckRow({ check }: { check: TableCheck }) {
    return (
        <div className={`flex items-start gap-2 rounded px-2 py-1 text-xs ${
            check.exists ? 'text-emerald-300' : 'bg-red-950/40 text-red-300 border border-red-500/20'
        }`}>
            {check.exists
                ? <CheckCircle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-emerald-400" />
                : <XCircle    className="mt-0.5 h-3.5 w-3.5 shrink-0 text-red-400" />
            }
            <span className="w-52 shrink-0 font-mono font-semibold">{check.table}</span>
            {check.note && <span className="text-zinc-400">{check.note}</span>}
        </div>
    );
}

function MissingCampaignCard({ campaign }: { campaign: MissingCampaign }) {
    const [expanded, setExpanded] = useState(true);
    const missingCount = campaign.tableChecks.filter(t => !t.exists).length;
    return (
        <div className="overflow-hidden rounded-lg border border-red-500/25 bg-red-950/20">
            <button type="button"
                className="flex w-full items-center gap-2.5 px-3 py-2.5 text-left hover:bg-red-950/30 transition-colors"
                onClick={() => setExpanded(v => !v)}
            >
                {expanded ? <ChevronDown className="h-3.5 w-3.5 shrink-0 text-zinc-500" /> : <ChevronRight className="h-3.5 w-3.5 shrink-0 text-zinc-500" />}
                <XCircle className="h-4 w-4 shrink-0 text-red-400" />
                <div className="min-w-0 flex-1">
                    <span className="text-sm font-semibold text-red-200">{campaign.code}</span>
                    <span className="ml-2 text-xs text-zinc-500">#{campaign.id}</span>
                    {campaign.description && <span className="ml-1 text-xs text-zinc-500"> — {campaign.description}</span>}
                </div>
                {missingCount > 0 && (
                    <span className="shrink-0 rounded-full bg-red-500/20 px-2 py-0.5 text-xs font-semibold text-red-300">
                        {missingCount} tablo eksik
                    </span>
                )}
            </button>
            {expanded && (
                <div className="space-y-0.5 border-t border-red-500/15 px-3 pb-3 pt-2">
                    {campaign.tableChecks.map(tc => <TableCheckRow key={tc.table} check={tc} />)}
                </div>
            )}
        </div>
    );
}

function DeviceCard({ device }: { device: DeviceResult }) {
    const diff = device.merkezCount - device.deviceCount;
    return (
        <div className={`overflow-hidden rounded-xl border ${statusCard(device.status)}`}>
            <div className="flex items-center gap-3 border-b border-zinc-700/30 px-4 py-3">
                <DeviceTypeIcon deviceType={device.deviceType} />
                <div className="min-w-0 flex-1">
                    <div className="text-sm font-bold text-zinc-100">{device.deviceName}</div>
                    <div className="text-xs text-zinc-400">{device.ipAddress}</div>
                </div>
                <span className={`shrink-0 rounded-full border px-2.5 py-0.5 text-xs font-semibold ${statusBadge(device.status)}`}>
                    {statusLabel(device.status)}
                </span>
            </div>
            <div className="flex items-center gap-5 border-b border-zinc-700/20 bg-zinc-900/30 px-4 py-2">
                <div className="text-xs text-zinc-400">Merkez: <span className="font-semibold text-zinc-100">{device.merkezCount}</span></div>
                <div className="h-3 w-px bg-zinc-600" />
                <div className="text-xs text-zinc-400">Cihaz:{' '}
                    <span className={`font-semibold ${device.status === 'ok' ? 'text-emerald-400' : device.status === 'missing' ? 'text-red-400' : 'text-zinc-400'}`}>
                        {device.deviceCount}
                    </span>
                </div>
                {device.status === 'missing' && diff > 0 && (
                    <><div className="h-3 w-px bg-zinc-600" /><div className="text-xs font-semibold text-red-400">{diff} eksik</div></>
                )}
            </div>
            {device.error && <div className="border-b border-amber-800/20 bg-amber-950/20 px-4 py-2 text-xs text-amber-300">{device.error}</div>}
            {device.missingCampaigns.length > 0 && (
                <div className="space-y-2 px-4 py-3">
                    <div className="mb-1 text-xs font-bold uppercase tracking-wider text-red-400">Eksik Kampanyalar ({device.missingCampaigns.length})</div>
                    {device.missingCampaigns.map(c => <MissingCampaignCard key={c.id} campaign={c} />)}
                </div>
            )}
            {device.status === 'ok' && (
                <div className="flex items-center gap-2 px-4 py-3 text-sm text-emerald-400">
                    <CheckCircle className="h-4 w-4" />Tüm kampanyalar senkron
                </div>
            )}
            {(device.status === 'offline' || device.status === 'no_conn') && !device.error && (
                <div className="flex items-center gap-2 px-4 py-3 text-sm text-zinc-400">
                    <AlertTriangle className="h-4 w-4" />Cihaza bağlanılamadı
                </div>
            )}
        </div>
    );
}

// ── StoreRow — tüm mağazalar tablosunun bir satırı ─────────────────────────

function StoreRow({ store }: { store: StoreEntry }) {
    const [expanded, setExpanded] = useState(false);
    const agg = getStoreAgg(store);
    const missing = totalMissingForStore(store);

    const pc     = store.devices.find(d => d.deviceType === 'PC');
    const kasa1  = store.devices.find(d => d.deviceType === 'Kasa-1');
    const kasa2  = store.devices.find(d => d.deviceType === 'Kasa-2');
    const kasa3  = store.devices.find(d => d.deviceType === 'Kasa-3');

    const rowClass = agg === 'missing'
        ? 'bg-red-950/10 hover:bg-red-950/20 border-b border-red-900/20'
        : agg === 'offline'
        ? 'bg-zinc-800/10 hover:bg-zinc-800/20 border-b border-zinc-700/20'
        : 'hover:bg-zinc-800/10 border-b border-zinc-800/30';

    return (
        <>
            <tr
                className={`cursor-pointer transition-colors ${rowClass}`}
                onClick={() => setExpanded(v => !v)}
            >
                <td className="px-3 py-2 text-zinc-500">
                    {expanded ? <ChevronDown className="h-3.5 w-3.5" /> : <ChevronRight className="h-3.5 w-3.5" />}
                </td>
                <td className="px-3 py-2">
                    <span className="text-sm font-semibold text-zinc-100">{store.storeCode}</span>
                    <span className="ml-2 text-xs text-zinc-400">{store.storeName}</span>
                </td>
                <td className="px-3 py-2 text-center text-xs text-zinc-300">{store.merkezCount}</td>
                <DeviceCell device={pc} />
                <DeviceCell device={kasa1} />
                <DeviceCell device={kasa2} />
                <DeviceCell device={kasa3} />
                <td className="px-3 py-2 text-center">
                    {agg === 'missing' ? (
                        <span className="rounded-full bg-red-500/20 px-2 py-0.5 text-xs font-semibold text-red-400">
                            {missing} eksik
                        </span>
                    ) : agg === 'ok' ? (
                        <span className="text-xs text-emerald-400">✓ OK</span>
                    ) : agg === 'error' ? (
                        <span className="text-xs text-amber-400">Hata</span>
                    ) : (
                        <span className="text-xs text-zinc-500">~</span>
                    )}
                </td>
            </tr>
            {expanded && (
                <tr className={agg === 'missing' ? 'bg-red-950/5' : 'bg-zinc-900/20'}>
                    <td colSpan={8} className="px-6 py-4">
                        {store.merkezError && (
                            <div className="mb-3 rounded border border-red-500/20 bg-red-950/20 px-3 py-2 text-xs text-red-300">
                                Merkez DB hatası: {store.merkezError}
                            </div>
                        )}
                        {store.devices.length === 0 ? (
                            <p className="text-xs text-zinc-500">Bu mağaza için kayıtlı cihaz bulunamadı.</p>
                        ) : (
                            <div className="grid grid-cols-1 gap-3 xl:grid-cols-2">
                                {store.devices.map(d => <DeviceCard key={d.deviceId} device={d} />)}
                            </div>
                        )}
                    </td>
                </tr>
            )}
        </>
    );
}

// ── SingleStoreView ───────────────────────────────────────────────────────

function SingleStoreView() {
    const [storeCode, setStoreCode] = useState('');
    const [loading, setLoading]     = useState(false);
    const [result, setResult]       = useState<SingleStoreResult | null>(null);
    const [error, setError]         = useState<string | null>(null);

    const run = useCallback(async () => {
        const code = parseInt(storeCode.trim(), 10);
        if (isNaN(code) || code <= 0) { setError('Geçerli bir mağaza kodu girin'); return; }
        setLoading(true); setError(null); setResult(null);
        try {
            const data = await apiClient.get<SingleStoreResult>(`/api/campaign-sync/${code}/check`, 300_000);
            setResult(data);
        } catch (e: unknown) {
            setError(e instanceof Error ? e.message : 'Kontrol başarısız');
        } finally { setLoading(false); }
    }, [storeCode]);

    const allOk = result?.devices.length ? result.devices.every(d => d.status === 'ok') : false;
    const totalMissing = result?.devices.reduce((s, d) => s + d.missingCampaigns.length, 0) ?? 0;
    const offlineCount = result?.devices.filter(d => d.status === 'offline' || d.status === 'no_conn').length ?? 0;

    return (
        <div className="flex flex-col gap-4">
            <div className="flex flex-wrap items-center gap-3">
                <input
                    type="number"
                    placeholder="Mağaza kodu — 107 veya 1107"
                    value={storeCode}
                    onChange={e => setStoreCode(e.target.value)}
                    onKeyDown={e => e.key === 'Enter' && run()}
                    disabled={loading}
                    className="w-60 rounded-lg border border-zinc-700 bg-zinc-800 px-4 py-2.5 text-sm text-zinc-100 placeholder:text-zinc-500 focus:border-sky-500 focus:outline-none focus:ring-1 focus:ring-sky-500/50 disabled:opacity-50"
                />
                <button onClick={run} disabled={loading || !storeCode.trim()}
                    className="flex items-center gap-2 rounded-lg bg-sky-600 px-5 py-2.5 text-sm font-semibold text-white hover:bg-sky-500 disabled:cursor-not-allowed disabled:opacity-50 transition-colors">
                    {loading ? <><RefreshCw className="h-4 w-4 animate-spin" />Kontrol ediliyor...</>
                             : <><Search    className="h-4 w-4" />Kontrol Et</>}
                </button>
                {result && !loading && (
                    <button onClick={run} className="flex items-center gap-1.5 rounded-lg border border-zinc-700 bg-zinc-800 px-3 py-2.5 text-xs text-zinc-400 hover:border-zinc-500 hover:text-zinc-200 transition-colors">
                        <RefreshCw className="h-3.5 w-3.5" />Yenile
                    </button>
                )}
            </div>

            {error && (
                <div className="flex items-center gap-3 rounded-lg border border-red-500/30 bg-red-950/20 px-4 py-3 text-sm text-red-300">
                    <AlertTriangle className="h-4 w-4 shrink-0" />{error}
                </div>
            )}
            {loading && (
                <div className="flex flex-col items-center justify-center gap-3 py-16 text-zinc-400">
                    <RefreshCw className="h-6 w-6 animate-spin text-sky-400" />
                    <p className="text-sm">Merkez ve mağaza DB'leri sorgulanıyor...</p>
                </div>
            )}
            {result && !loading && (
                <>
                    <div className="flex flex-wrap items-center gap-3 rounded-xl border border-zinc-700/50 bg-zinc-800/40 px-5 py-4">
                        <div className="flex items-center gap-2">
                            <Database className="h-4 w-4 text-sky-400" />
                            <span className="text-sm text-zinc-400">Merkez:</span>
                            <span className="text-sm font-bold text-zinc-100">{result.merkezCount} aktif kampanya</span>
                        </div>
                        <div className="h-4 w-px bg-zinc-600" />
                        <div className="flex items-center gap-2">
                            {allOk ? <CheckCircle className="h-4 w-4 text-emerald-400" /> : <XCircle className="h-4 w-4 text-red-400" />}
                            <span className={`text-sm font-semibold ${allOk ? 'text-emerald-400' : 'text-red-400'}`}>
                                {allOk ? 'Tüm cihazlar senkron' : `${totalMissing} eksik kampanya`}
                            </span>
                        </div>
                        {offlineCount > 0 && (
                            <><div className="h-4 w-px bg-zinc-600" />
                            <span className="text-sm text-amber-400">{offlineCount} cihaza ulaşılamadı</span></>
                        )}
                        <div className="ml-auto flex items-center gap-1.5 text-xs text-zinc-500">
                            <Clock className="h-3.5 w-3.5" />{new Date(result.checkedAt).toLocaleTimeString('tr-TR')}
                        </div>
                    </div>
                    {result.devices.length === 0 ? (
                        <div className="flex flex-col items-center gap-2 rounded-xl border border-zinc-700/40 bg-zinc-800/20 py-12 text-zinc-500">
                            <AlertTriangle className="h-5 w-5" />
                            <p className="text-sm">Mağaza {result.storeCode} için envanterde PC veya Kasa cihazı bulunamadı.</p>
                        </div>
                    ) : (
                        <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
                            {result.devices.map(d => <DeviceCard key={d.deviceId} device={d} />)}
                        </div>
                    )}
                </>
            )}
        </div>
    );
}

// ── AllStoresView ─────────────────────────────────────────────────────────

type AggFilter = 'all' | 'missing' | 'offline';

function AllStoresView() {
    const [loading, setLoading] = useState(false);
    const [result, setResult]   = useState<AllStoresResult | null>(null);
    const [error, setError]     = useState<string | null>(null);
    const [filter, setFilter]   = useState<AggFilter>('all');
    const [search, setSearch]   = useState('');

    const run = useCallback(async () => {
        setLoading(true); setError(null); setResult(null);
        try {
            const data = await apiClient.get<AllStoresResult>('/api/campaign-sync/all/check', 600_000);
            setResult(data);
        } catch (e: unknown) {
            setError(e instanceof Error ? e.message : 'Kontrol başarısız');
        } finally { setLoading(false); }
    }, []);

    const filtered = result?.stores.filter(s => {
        const agg = getStoreAgg(s);
        if (filter === 'missing' && agg !== 'missing') return false;
        if (filter === 'offline' && agg !== 'offline')  return false;
        if (search) {
            const q = search.toLowerCase();
            if (!String(s.storeCode).includes(q) && !s.storeName.toLowerCase().includes(q)) return false;
        }
        return true;
    }) ?? [];

    // Sorunlu önce, sonra offline, sonra OK
    const sorted = [...filtered].sort((a, b) => {
        const order = { missing: 0, error: 1, offline: 2, ok: 3 };
        return (order[getStoreAgg(a)] ?? 4) - (order[getStoreAgg(b)] ?? 4);
    });

    const missingCount  = result?.stores.filter(s => getStoreAgg(s) === 'missing').length  ?? 0;
    const offlineCount  = result?.stores.filter(s => getStoreAgg(s) === 'offline').length  ?? 0;
    const okCount       = result?.stores.filter(s => getStoreAgg(s) === 'ok').length       ?? 0;

    return (
        <div className="flex flex-col gap-4">
            {/* Tara butonu */}
            {!result && !loading && (
                <div className="flex flex-col items-center gap-4 rounded-xl border border-zinc-700/40 bg-zinc-800/20 py-16">
                    <ShieldCheck className="h-12 w-12 text-zinc-600" />
                    <p className="text-sm text-zinc-400">Tüm mağazaların aktif kampanya senkronizasyonu kontrol edilecek.</p>
                    <button onClick={run}
                        className="flex items-center gap-2 rounded-lg bg-sky-600 px-6 py-3 text-sm font-semibold text-white hover:bg-sky-500 transition-colors">
                        <Search className="h-4 w-4" />Tüm Mağazaları Tara
                    </button>
                </div>
            )}

            {loading && (
                <div className="flex flex-col items-center gap-4 rounded-xl border border-zinc-700/40 bg-zinc-800/20 py-16 text-zinc-400">
                    <RefreshCw className="h-8 w-8 animate-spin text-sky-400" />
                    <p className="text-sm">Tüm mağazalar sorgulanıyor, lütfen bekleyin...</p>
                    <p className="text-xs text-zinc-600">Bu işlem 2-5 dakika sürebilir.</p>
                </div>
            )}

            {error && (
                <div className="flex items-center gap-3 rounded-lg border border-red-500/30 bg-red-950/20 px-4 py-3 text-sm text-red-300">
                    <AlertTriangle className="h-4 w-4 shrink-0" />{error}
                </div>
            )}

            {result && !loading && (
                <>
                    {/* Özet bar */}
                    <div className="flex flex-wrap items-center gap-3 rounded-xl border border-zinc-700/50 bg-zinc-800/40 px-5 py-3">
                        <div className="text-sm text-zinc-400">
                            <span className="font-bold text-zinc-100">{result.totalStores}</span> mağaza kontrol edildi
                        </div>
                        <div className="h-4 w-px bg-zinc-600" />
                        <span className="text-sm font-semibold text-red-400">{missingCount} sorunlu</span>
                        <span className="text-sm text-zinc-500">{offlineCount} offline</span>
                        <span className="text-sm text-emerald-400">{okCount} senkron</span>
                        <div className="ml-auto flex items-center gap-3">
                            <button onClick={run} className="flex items-center gap-1.5 rounded-lg border border-zinc-700 bg-zinc-800 px-3 py-1.5 text-xs text-zinc-400 hover:border-zinc-500 hover:text-zinc-200 transition-colors">
                                <RefreshCw className="h-3.5 w-3.5" />Yeniden Tara
                            </button>
                            <div className="flex items-center gap-1.5 text-xs text-zinc-500">
                                <Clock className="h-3.5 w-3.5" />{new Date(result.checkedAt).toLocaleTimeString('tr-TR')}
                            </div>
                        </div>
                    </div>

                    {/* Filtreler */}
                    <div className="flex flex-wrap items-center gap-2">
                        {(['all', 'missing', 'offline'] as AggFilter[]).map(f => (
                            <button key={f} onClick={() => setFilter(f)}
                                className={`rounded-lg border px-3 py-1.5 text-xs font-semibold transition-colors ${
                                    filter === f
                                        ? 'border-sky-500 bg-sky-500/15 text-sky-300'
                                        : 'border-zinc-700 bg-zinc-800 text-zinc-400 hover:border-zinc-500'
                                }`}>
                                {f === 'all'     && `Tümü (${result.totalStores})`}
                                {f === 'missing' && `Sorunlu (${missingCount})`}
                                {f === 'offline' && `Offline (${offlineCount})`}
                            </button>
                        ))}
                        <input
                            type="text"
                            placeholder="Mağaza ara..."
                            value={search}
                            onChange={e => setSearch(e.target.value)}
                            className="ml-auto w-48 rounded-lg border border-zinc-700 bg-zinc-800 px-3 py-1.5 text-xs text-zinc-100 placeholder:text-zinc-500 focus:border-sky-500 focus:outline-none"
                        />
                    </div>

                    {/* Tablo */}
                    <div className="overflow-hidden rounded-xl border border-zinc-700/50">
                        <table className="w-full text-sm">
                            <thead>
                                <tr className="border-b border-zinc-700/50 bg-zinc-800/60">
                                    <th className="w-8 px-3 py-2" />
                                    <th className="px-3 py-2 text-left text-xs font-semibold uppercase tracking-wider text-zinc-400">Mağaza</th>
                                    <th className="px-3 py-2 text-center text-xs font-semibold uppercase tracking-wider text-zinc-400">Merkez</th>
                                    <th className="px-3 py-2 text-center text-xs font-semibold uppercase tracking-wider text-zinc-400">PC</th>
                                    <th className="px-3 py-2 text-center text-xs font-semibold uppercase tracking-wider text-zinc-400">KK1</th>
                                    <th className="px-3 py-2 text-center text-xs font-semibold uppercase tracking-wider text-zinc-400">KK2</th>
                                    <th className="px-3 py-2 text-center text-xs font-semibold uppercase tracking-wider text-zinc-400">KK3</th>
                                    <th className="px-3 py-2 text-center text-xs font-semibold uppercase tracking-wider text-zinc-400">Durum</th>
                                </tr>
                            </thead>
                            <tbody className="divide-y divide-transparent bg-zinc-900/40">
                                {sorted.length === 0 ? (
                                    <tr><td colSpan={8} className="py-10 text-center text-sm text-zinc-500">Sonuç bulunamadı</td></tr>
                                ) : (
                                    sorted.map(s => <StoreRow key={s.storeCode} store={s} />)
                                )}
                            </tbody>
                        </table>
                    </div>

                    {/* Alt not */}
                    <p className="text-xs text-zinc-600">
                        Kontrol edilen tablolar: CAMPAIGN · CAMPAIGN_PERIOD · CAMPAIGN_PRODUCT_RESULT · CAMPAIGN_PRODUCT_SOURCE · CAMPAIGN_DISCOUNT · CAMPAIGN_STORE · CAMPAIGN_DISCOUNT_DEF
                    </p>
                </>
            )}
        </div>
    );
}

// ── CampaignSyncPage ──────────────────────────────────────────────────────

type PageMode = 'all' | 'single';

const CampaignSyncPage: React.FC = () => {
    const [mode, setMode] = useState<PageMode>('all');

    return (
        <div className="flex flex-col gap-5 p-6">
            {/* Başlık */}
            <div className="flex items-center gap-3">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-sky-500/15">
                    <ShieldCheck className="h-5 w-5 text-sky-400" />
                </div>
                <div>
                    <h1 className="text-xl font-bold text-zinc-100">Kampanya Senkronizasyon Kontrolü</h1>
                    <p className="text-sm text-zinc-400">Merkez · Mağaza PC · Kasalar — aktif kampanya tutarlılığını karşılaştırır</p>
                </div>
            </div>

            {/* Mod seçimi */}
            <div className="flex items-center gap-2 border-b border-zinc-700/50 pb-1">
                <button onClick={() => setMode('all')}
                    className={`rounded-lg px-4 py-2 text-sm font-semibold transition-colors ${
                        mode === 'all'
                            ? 'bg-sky-600 text-white'
                            : 'text-zinc-400 hover:text-zinc-200'
                    }`}>
                    Tüm Mağazalar
                </button>
                <button onClick={() => setMode('single')}
                    className={`rounded-lg px-4 py-2 text-sm font-semibold transition-colors ${
                        mode === 'single'
                            ? 'bg-sky-600 text-white'
                            : 'text-zinc-400 hover:text-zinc-200'
                    }`}>
                    Tek Mağaza
                </button>
            </div>

            {/* İçerik */}
            {mode === 'all'    && <AllStoresView />}
            {mode === 'single' && <SingleStoreView />}
        </div>
    );
};

export default CampaignSyncPage;
