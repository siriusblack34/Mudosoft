import { useEffect, useMemo, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { ClipboardList, Plus, AlertTriangle, CheckCircle2, Clock, XCircle, FileSpreadsheet, Trash2, Building2 } from "lucide-react";
import { apiClient, type StoreOpeningListItem, type StoreOpeningTemplateSummary, type StoreOpeningStatus } from "../lib/apiClient";
import Modal from "../components/ui/Modal";

const STATUS_LABELS: Record<StoreOpeningStatus, string> = {
    Planned: "Planlandı",
    InProgress: "Devam Ediyor",
    Completed: "Tamamlandı",
    Cancelled: "İptal",
};

const STATUS_COLORS: Record<StoreOpeningStatus, string> = {
    Planned: "bg-sky-500/15 text-sky-300 border-sky-500/30",
    InProgress: "bg-amber-500/15 text-amber-300 border-amber-500/30",
    Completed: "bg-emerald-500/15 text-emerald-300 border-emerald-500/30",
    Cancelled: "bg-zinc-500/15 text-zinc-300 border-zinc-500/30",
};

const StoreOpeningsPage: React.FC = () => {
    const navigate = useNavigate();
    const [openings, setOpenings] = useState<StoreOpeningListItem[]>([]);
    const [loading, setLoading] = useState(true);
    const [statusFilter, setStatusFilter] = useState<"all" | StoreOpeningStatus>("all");
    const [search, setSearch] = useState("");
    const [newOpen, setNewOpen] = useState(false);

    const load = async () => {
        setLoading(true);
        try {
            const data = await apiClient.getStoreOpenings();
            setOpenings(data);
        } catch (e) {
            console.error(e);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => { load(); }, []);

    const filtered = useMemo(() => {
        return openings.filter(o => {
            if (statusFilter !== "all" && o.status !== statusFilter) return false;
            if (search) {
                const q = search.toLowerCase();
                if (!o.storeName.toLowerCase().includes(q) && !String(o.storeCode).includes(q)) return false;
            }
            return true;
        });
    }, [openings, statusFilter, search]);

    const stats = useMemo(() => ({
        total: openings.length,
        planned: openings.filter(o => o.status === "Planned").length,
        inProgress: openings.filter(o => o.status === "InProgress").length,
        completed: openings.filter(o => o.status === "Completed").length,
        overdue: openings.filter(o => o.isOverdue).length,
    }), [openings]);

    const handleDelete = async (id: number, storeName: string) => {
        if (!confirm(`"${storeName}" açılış kaydını silmek istediğinizden emin misiniz?`)) return;
        try {
            await apiClient.deleteStoreOpening(id);
            load();
        } catch (e) {
            alert("Silinemedi: " + (e as Error).message);
        }
    };

    return (
        <div className="p-6 space-y-6">
            <div className="flex items-center justify-between">
                <div>
                    <h1 className="text-2xl font-bold text-ms-text flex items-center gap-2">
                        <ClipboardList className="w-6 h-6 text-sky-400" />
                        Mağaza Açılışları
                    </h1>
                    <p className="text-sm text-zinc-400 mt-1">Yeni mağaza açılış checklist'leri — donanım, seri no, asset no takibi</p>
                </div>
                <div className="flex gap-2">
                    <Link
                        to="/store-openings/templates"
                        className="px-3 py-2 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded-lg text-sm font-medium transition-colors"
                    >
                        Şablonlar
                    </Link>
                    <button
                        onClick={() => setNewOpen(true)}
                        className="px-4 py-2 bg-sky-600 hover:bg-sky-500 text-white rounded-lg text-sm font-semibold flex items-center gap-2 transition-colors"
                    >
                        <Plus className="w-4 h-4" /> Yeni Açılış
                    </button>
                </div>
            </div>

            {/* Stats */}
            <div className="grid grid-cols-2 md:grid-cols-5 gap-3">
                <StatCard label="Toplam" value={stats.total} icon={<Building2 className="w-4 h-4" />} color="text-zinc-300" />
                <StatCard label="Planlandı" value={stats.planned} icon={<Clock className="w-4 h-4" />} color="text-sky-400" />
                <StatCard label="Devam Ediyor" value={stats.inProgress} icon={<ClipboardList className="w-4 h-4" />} color="text-amber-400" />
                <StatCard label="Tamamlandı" value={stats.completed} icon={<CheckCircle2 className="w-4 h-4" />} color="text-emerald-400" />
                <StatCard label="Gecikmiş" value={stats.overdue} icon={<AlertTriangle className="w-4 h-4" />} color="text-rose-400" />
            </div>

            {/* Filters */}
            <div className="flex flex-wrap items-center gap-3 bg-ms-bg-soft border border-ms-border rounded-lg p-3">
                <input
                    type="text"
                    placeholder="Mağaza adı veya kod ara..."
                    value={search}
                    onChange={e => setSearch(e.target.value)}
                    className="flex-1 min-w-[200px] px-3 py-2 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text"
                />
                <select
                    value={statusFilter}
                    onChange={e => setStatusFilter(e.target.value as any)}
                    className="px-3 py-2 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text"
                >
                    <option value="all">Tüm Durumlar</option>
                    <option value="Planned">Planlandı</option>
                    <option value="InProgress">Devam Ediyor</option>
                    <option value="Completed">Tamamlandı</option>
                    <option value="Cancelled">İptal</option>
                </select>
            </div>

            {/* List */}
            {loading ? (
                <div className="text-center py-12 text-zinc-400">Yükleniyor...</div>
            ) : filtered.length === 0 ? (
                <div className="text-center py-12 text-zinc-400 border border-dashed border-ms-border rounded-lg">
                    {openings.length === 0 ? "Henüz açılış kaydı yok. \"Yeni Açılış\" ile başlayın." : "Filtreye uygun kayıt yok."}
                </div>
            ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                    {filtered.map(o => (
                        <OpeningCard key={o.id} o={o} onDelete={() => handleDelete(o.id, o.storeName)} onOpen={() => navigate(`/store-openings/${o.id}`)} />
                    ))}
                </div>
            )}

            {newOpen && (
                <NewOpeningModal
                    onClose={() => setNewOpen(false)}
                    onCreated={(id) => { setNewOpen(false); navigate(`/store-openings/${id}`); }}
                />
            )}
        </div>
    );
};

const StatCard: React.FC<{ label: string; value: number; icon: React.ReactNode; color: string }> = ({ label, value, icon, color }) => (
    <div className="bg-ms-bg-soft border border-ms-border rounded-lg p-3">
        <div className={`flex items-center gap-2 text-xs ${color}`}>{icon}<span className="font-medium">{label}</span></div>
        <div className="text-2xl font-bold text-ms-text mt-1">{value}</div>
    </div>
);

const OpeningCard: React.FC<{ o: StoreOpeningListItem; onDelete: () => void; onOpen: () => void }> = ({ o, onDelete, onOpen }) => {
    const targetDate = new Date(o.targetOpeningDate);
    const daysLeft = Math.ceil((targetDate.getTime() - Date.now()) / 86400000);
    return (
        <div className="bg-ms-bg-soft border border-ms-border rounded-lg p-4 hover:border-sky-500/40 transition-colors group">
            <div className="flex items-start justify-between gap-2">
                <button onClick={onOpen} className="flex-1 text-left">
                    <div className="text-xs text-zinc-500">#{o.storeCode}</div>
                    <div className="font-semibold text-ms-text mt-0.5">{o.storeName}</div>
                    {o.city && <div className="text-xs text-zinc-400 mt-0.5">{o.city}</div>}
                </button>
                <div className="flex items-center gap-1">
                    <span className={`text-[10px] font-semibold uppercase px-2 py-0.5 rounded border ${STATUS_COLORS[o.status]}`}>
                        {STATUS_LABELS[o.status]}
                    </span>
                </div>
            </div>

            <button onClick={onOpen} className="block w-full text-left mt-3">
                <div className="flex items-center justify-between text-xs text-zinc-400 mb-1">
                    <span>İlerleme</span>
                    <span className="font-mono font-semibold text-ms-text">{o.progressPercent.toFixed(0)}%</span>
                </div>
                <div className="h-2 bg-zinc-900 rounded-full overflow-hidden">
                    <div
                        className={`h-full transition-all ${o.progressPercent >= 100 ? "bg-emerald-500" : o.progressPercent >= 50 ? "bg-sky-500" : "bg-amber-500"}`}
                        style={{ width: `${Math.min(100, o.progressPercent)}%` }}
                    />
                </div>
                <div className="text-[11px] text-zinc-500 mt-1">
                    {o.completedItems}/{o.totalItems - o.notApplicableItems} tamamlandı
                    {o.notApplicableItems > 0 && <span className="ml-1">({o.notApplicableItems} N/A)</span>}
                </div>
            </button>

            <div className="flex items-center justify-between mt-3 pt-3 border-t border-ms-border">
                <div className={`text-xs flex items-center gap-1 ${o.isOverdue ? "text-rose-400" : "text-zinc-400"}`}>
                    {o.isOverdue && <AlertTriangle className="w-3 h-3" />}
                    Termin: {targetDate.toLocaleDateString("tr-TR")}
                    {o.status !== "Completed" && o.status !== "Cancelled" && (
                        <span className="ml-1">({daysLeft < 0 ? `${-daysLeft} gün gecikti` : daysLeft === 0 ? "bugün" : `${daysLeft} gün`})</span>
                    )}
                </div>
                <button
                    onClick={onDelete}
                    className="opacity-0 group-hover:opacity-100 p-1 text-zinc-500 hover:text-rose-400 transition-all"
                    title="Sil"
                >
                    <Trash2 className="w-3.5 h-3.5" />
                </button>
            </div>
        </div>
    );
};

const NewOpeningModal: React.FC<{ onClose: () => void; onCreated: (id: number) => void }> = ({ onClose, onCreated }) => {
    const [templates, setTemplates] = useState<StoreOpeningTemplateSummary[]>([]);
    const [storeCode, setStoreCode] = useState("");
    const [storeName, setStoreName] = useState("");
    const [city, setCity] = useState("");
    const [address, setAddress] = useState("");
    const [targetDate, setTargetDate] = useState("");
    const [templateId, setTemplateId] = useState<number | undefined>(undefined);
    const [notes, setNotes] = useState("");
    const [roles, setRoles] = useState({
        "Donanım Sorumlusu": "",
        "Sistem / Network Sorumlusu": "",
        "Router Sorumlusu": "",
        "Kasa Sorumlusu": "",
    });
    const [saving, setSaving] = useState(false);

    useEffect(() => {
        apiClient.getStoreOpeningTemplates().then(t => {
            setTemplates(t);
            const def = t.find(x => x.isDefault);
            if (def) setTemplateId(def.id);
        });
        const tomorrow = new Date(); tomorrow.setDate(tomorrow.getDate() + 14);
        setTargetDate(tomorrow.toISOString().slice(0, 10));
    }, []);

    const submit = async () => {
        if (!storeName.trim() || !storeCode || !targetDate) {
            alert("Mağaza adı, kodu ve termin zorunlu");
            return;
        }
        setSaving(true);
        try {
            const cleanRoles: Record<string, string> = {};
            Object.entries(roles).forEach(([k, v]) => { if (v.trim()) cleanRoles[k] = v.trim(); });
            const created = await apiClient.createStoreOpening({
                storeCode: parseInt(storeCode),
                storeName: storeName.trim(),
                city: city.trim() || undefined,
                address: address.trim() || undefined,
                targetOpeningDate: new Date(targetDate).toISOString(),
                templateId,
                notes: notes.trim() || undefined,
                roleAssignments: Object.keys(cleanRoles).length ? cleanRoles : undefined,
            });
            onCreated(created.id);
        } catch (e) {
            alert("Oluşturulamadı: " + (e as Error).message);
            setSaving(false);
        }
    };

    const inputCls = "w-full px-2.5 py-1.5 bg-zinc-900 border border-ms-border rounded text-sm text-ms-text";
    const labelCls = "text-[11px] text-zinc-400 block mb-0.5";

    return (
        <Modal isOpen={true} onClose={onClose} title="Yeni Mağaza Açılışı" size="lg" dismissOnBackdrop={false}>
            <div className="space-y-2.5">
                <div className="grid grid-cols-4 gap-2">
                    <div><label className={labelCls}>Kod *</label><input type="number" value={storeCode} onChange={e => setStoreCode(e.target.value)} className={inputCls} /></div>
                    <div className="col-span-2"><label className={labelCls}>Mağaza Adı *</label><input value={storeName} onChange={e => setStoreName(e.target.value)} className={inputCls} /></div>
                    <div><label className={labelCls}>Termin *</label><input type="date" value={targetDate} onChange={e => setTargetDate(e.target.value)} className={inputCls} /></div>
                </div>
                <div className="grid grid-cols-3 gap-2">
                    <div><label className={labelCls}>Şehir</label><input value={city} onChange={e => setCity(e.target.value)} className={inputCls} /></div>
                    <div className="col-span-2"><label className={labelCls}>Adres</label><input value={address} onChange={e => setAddress(e.target.value)} className={inputCls} /></div>
                </div>

                <div>
                    <label className={labelCls}>Şablon</label>
                    <select value={templateId ?? ""} onChange={e => setTemplateId(e.target.value ? parseInt(e.target.value) : undefined)} className={inputCls}>
                        <option value="">— Şablonsuz başla —</option>
                        {templates.map(t => (
                            <option key={t.id} value={t.id}>{t.name} ({t.itemCount} kalem){t.isDefault ? " — Varsayılan" : ""}</option>
                        ))}
                    </select>
                </div>

                <div className="pt-1">
                    <div className="text-[11px] text-zinc-400 mb-1.5 font-semibold uppercase tracking-wide">Sorumlular</div>
                    <div className="grid grid-cols-2 gap-2">
                        {Object.entries(roles).map(([role, val]) => (
                            <div key={role}>
                                <label className={labelCls}>{role}</label>
                                <input value={val} onChange={e => setRoles({ ...roles, [role]: e.target.value })} placeholder="kişi adı" className={inputCls} />
                            </div>
                        ))}
                    </div>
                </div>

                <div>
                    <label className={labelCls}>Not</label>
                    <input value={notes} onChange={e => setNotes(e.target.value)} className={inputCls} />
                </div>

                <div className="flex justify-end gap-2 pt-2 border-t border-ms-border">
                    <button onClick={onClose} className="px-4 py-1.5 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded text-sm">İptal</button>
                    <button onClick={submit} disabled={saving} className="px-4 py-1.5 bg-sky-600 hover:bg-sky-500 disabled:opacity-50 text-white rounded text-sm font-semibold">
                        {saving ? "Oluşturuluyor..." : "Oluştur"}
                    </button>
                </div>
            </div>
        </Modal>
    );
};

export default StoreOpeningsPage;
