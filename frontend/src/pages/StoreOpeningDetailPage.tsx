import { useEffect, useRef, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
    ArrowLeft, CheckCircle2, ChevronDown, ChevronRight, Circle, MinusCircle,
    FileSpreadsheet, Image as ImageIcon, Trash2, Upload, AlertTriangle, History, Edit3, Save, X
} from "lucide-react";
import {
    apiClient, type StoreOpeningDetail, type StoreOpeningItem, type StoreOpeningItemStatus,
    type StoreOpeningActivity, type StoreOpeningCategoryGroup, type StoreOpeningStatus
} from "../lib/apiClient";
import Modal from "../components/ui/Modal";

const STATUS_LABELS: Record<StoreOpeningStatus, string> = {
    Planned: "Planlandı", InProgress: "Devam Ediyor", Completed: "Tamamlandı", Cancelled: "İptal",
};

const STATUS_COLORS: Record<StoreOpeningStatus, string> = {
    Planned: "bg-sky-500/15 text-sky-300 border-sky-500/30",
    InProgress: "bg-amber-500/15 text-amber-300 border-amber-500/30",
    Completed: "bg-emerald-500/15 text-emerald-300 border-emerald-500/30",
    Cancelled: "bg-zinc-500/15 text-zinc-300 border-zinc-500/30",
};

const StoreOpeningDetailPage: React.FC = () => {
    const { id } = useParams<{ id: string }>();
    const navigate = useNavigate();
    const openingId = parseInt(id || "0");
    const [opening, setOpening] = useState<StoreOpeningDetail | null>(null);
    const [loading, setLoading] = useState(true);
    const [expanded, setExpanded] = useState<Set<string>>(new Set());
    const [editMeta, setEditMeta] = useState(false);
    const [activityOpen, setActivityOpen] = useState(false);
    const [activities, setActivities] = useState<StoreOpeningActivity[]>([]);

    const load = async () => {
        try {
            const data = await apiClient.getStoreOpening(openingId);
            setOpening(data);
            // First load: expand all categories
            setExpanded(prev => prev.size === 0 ? new Set(data.categories.map(c => c.categoryName)) : prev);
        } catch (e) {
            console.error(e);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => { load(); }, [openingId]);

    const toggleCategory = (cat: string) => {
        setExpanded(s => {
            const next = new Set(s);
            if (next.has(cat)) next.delete(cat); else next.add(cat);
            return next;
        });
    };

    const openActivity = async () => {
        const data = await apiClient.getStoreOpeningActivity(openingId);
        setActivities(data);
        setActivityOpen(true);
    };

    const markCompleted = async () => {
        if (!opening) return;
        if (!confirm("Bu açılışı 'Tamamlandı' olarak işaretlemek istiyor musunuz?")) return;
        await apiClient.updateStoreOpening(openingId, { status: "Completed" });
        load();
    };

    const downloadExcel = async () => {
        const url = apiClient.getStoreOpeningExportUrl(openingId);
        const token = localStorage.getItem("token");
        const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } });
        if (!res.ok) { alert("Export başarısız"); return; }
        const blob = await res.blob();
        const a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = `acilis-${opening?.storeCode}-${opening?.storeName}.xlsx`;
        a.click();
        URL.revokeObjectURL(a.href);
    };

    if (loading) return <div className="p-6 text-zinc-400">Yükleniyor...</div>;
    if (!opening) return <div className="p-6 text-rose-400">Açılış bulunamadı</div>;

    const targetDate = new Date(opening.targetOpeningDate);
    const isOverdue = opening.status !== "Completed" && opening.status !== "Cancelled" && targetDate < new Date();
    const daysLeft = Math.ceil((targetDate.getTime() - Date.now()) / 86400000);

    return (
        <div className="p-6 space-y-4">
            {/* Header */}
            <div className="flex items-start justify-between gap-4 flex-wrap">
                <div className="flex items-start gap-3">
                    <Link to="/store-openings" className="p-2 bg-zinc-800 hover:bg-zinc-700 rounded-lg text-zinc-300">
                        <ArrowLeft className="w-4 h-4" />
                    </Link>
                    <div>
                        <div className="text-xs text-zinc-500">#{opening.storeCode}{opening.city ? ` · ${opening.city}` : ""}</div>
                        <h1 className="text-2xl font-bold text-ms-text">{opening.storeName}</h1>
                        <div className="flex items-center gap-2 mt-1">
                            <span className={`text-[10px] font-semibold uppercase px-2 py-0.5 rounded border ${STATUS_COLORS[opening.status]}`}>
                                {STATUS_LABELS[opening.status]}
                            </span>
                            <span className={`text-xs flex items-center gap-1 ${isOverdue ? "text-rose-400" : "text-zinc-400"}`}>
                                {isOverdue && <AlertTriangle className="w-3 h-3" />}
                                Termin: {targetDate.toLocaleDateString("tr-TR")}
                                {opening.status !== "Completed" && opening.status !== "Cancelled" && (
                                    <span>({daysLeft < 0 ? `${-daysLeft} gün gecikti` : daysLeft === 0 ? "bugün" : `${daysLeft} gün kaldı`})</span>
                                )}
                            </span>
                        </div>
                    </div>
                </div>
                <div className="flex flex-wrap gap-2">
                    <button onClick={() => setEditMeta(true)} className="px-3 py-2 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded-lg text-sm flex items-center gap-1.5">
                        <Edit3 className="w-3.5 h-3.5" /> Düzenle
                    </button>
                    <button onClick={openActivity} className="px-3 py-2 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded-lg text-sm flex items-center gap-1.5">
                        <History className="w-3.5 h-3.5" /> Aktivite
                    </button>
                    <button onClick={downloadExcel} className="px-3 py-2 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded-lg text-sm flex items-center gap-1.5">
                        <FileSpreadsheet className="w-3.5 h-3.5" /> Excel
                    </button>
                    {opening.status !== "Completed" && opening.status !== "Cancelled" && (
                        <button onClick={markCompleted} className="px-4 py-2 bg-emerald-600 hover:bg-emerald-500 text-white rounded-lg text-sm font-semibold flex items-center gap-1.5">
                            <CheckCircle2 className="w-4 h-4" /> Tamamla
                        </button>
                    )}
                </div>
            </div>

            {/* Progress + Roles */}
            <div className="grid grid-cols-1 lg:grid-cols-3 gap-3">
                <div className="bg-ms-bg-soft border border-ms-border rounded-lg p-4 lg:col-span-2">
                    <div className="flex items-center justify-between text-sm text-zinc-300 mb-2">
                        <span>Toplam İlerleme</span>
                        <span className="font-mono font-bold text-ms-text text-lg">{opening.progressPercent.toFixed(1)}%</span>
                    </div>
                    <div className="h-3 bg-zinc-900 rounded-full overflow-hidden">
                        <div
                            className={`h-full transition-all ${opening.progressPercent >= 100 ? "bg-emerald-500" : opening.progressPercent >= 50 ? "bg-sky-500" : "bg-amber-500"}`}
                            style={{ width: `${Math.min(100, opening.progressPercent)}%` }}
                        />
                    </div>
                    {opening.notes && <div className="text-xs text-zinc-400 mt-3 italic">"{opening.notes}"</div>}
                </div>
                <div className="bg-ms-bg-soft border border-ms-border rounded-lg p-4">
                    <div className="text-xs text-zinc-400 mb-2 font-semibold">Sorumlular</div>
                    {Object.keys(opening.roleAssignments).length === 0 ? (
                        <div className="text-xs text-zinc-500 italic">Henüz atama yok</div>
                    ) : (
                        <div className="space-y-1">
                            {Object.entries(opening.roleAssignments).map(([role, user]) => (
                                <div key={role} className="text-xs flex items-center justify-between gap-2">
                                    <span className="text-zinc-400 truncate">{role}</span>
                                    <span className="text-ms-text font-medium truncate">{user}</span>
                                </div>
                            ))}
                        </div>
                    )}
                </div>
            </div>

            {/* Categories */}
            <div className="space-y-3">
                {opening.categories.map(cat => (
                    <CategorySection
                        key={cat.categoryName}
                        cat={cat}
                        expanded={expanded.has(cat.categoryName)}
                        onToggle={() => toggleCategory(cat.categoryName)}
                        openingId={openingId}
                        onChange={load}
                        readonly={opening.status === "Completed" || opening.status === "Cancelled"}
                    />
                ))}
            </div>

            {editMeta && (
                <EditMetaModal opening={opening} onClose={() => setEditMeta(false)} onSaved={() => { setEditMeta(false); load(); }} />
            )}

            <Modal isOpen={activityOpen} onClose={() => setActivityOpen(false)} title="Aktivite Geçmişi" size="lg">
                {activities.length === 0 ? (
                    <div className="text-center py-6 text-zinc-400 text-sm">Henüz aktivite yok</div>
                ) : (
                    <div className="space-y-2">
                        {activities.map(a => (
                            <div key={a.id} className="flex items-start gap-3 p-2 border border-ms-border rounded-md">
                                <div className="text-xs text-zinc-500 w-32 shrink-0">{new Date(a.createdAt).toLocaleString("tr-TR")}</div>
                                <div className="text-xs text-sky-400 w-32 shrink-0">{a.username}</div>
                                <div className="flex-1 text-xs text-ms-text">
                                    <div className="font-semibold">{a.action}</div>
                                    {a.details && <div className="text-zinc-400 mt-0.5">{a.details}</div>}
                                </div>
                            </div>
                        ))}
                    </div>
                )}
            </Modal>
        </div>
    );
};

const CategorySection: React.FC<{
    cat: StoreOpeningCategoryGroup;
    expanded: boolean;
    onToggle: () => void;
    openingId: number;
    onChange: () => void;
    readonly: boolean;
}> = ({ cat, expanded, onToggle, openingId, onChange, readonly }) => {
    const applicable = cat.totalItems - cat.notApplicableItems;
    const isComplete = applicable > 0 && cat.completedItems === applicable;

    return (
        <div className={`bg-ms-bg-soft border rounded-lg ${isComplete ? "border-emerald-500/30" : "border-ms-border"}`}>
            <button onClick={onToggle} className="w-full flex items-center justify-between p-4 hover:bg-zinc-800/30 transition-colors">
                <div className="flex items-center gap-3">
                    {expanded ? <ChevronDown className="w-4 h-4 text-zinc-400" /> : <ChevronRight className="w-4 h-4 text-zinc-400" />}
                    <div className="text-left">
                        <div className="font-semibold text-ms-text flex items-center gap-2">
                            {cat.categoryName}
                            {isComplete && <CheckCircle2 className="w-4 h-4 text-emerald-400" />}
                        </div>
                        {cat.assignedRole && <div className="text-xs text-zinc-500 mt-0.5">{cat.assignedRole}</div>}
                    </div>
                </div>
                <div className="flex items-center gap-3">
                    <div className="text-xs text-zinc-400">{cat.completedItems}/{applicable}</div>
                    <div className="w-24 h-1.5 bg-zinc-900 rounded-full overflow-hidden">
                        <div className={`h-full ${isComplete ? "bg-emerald-500" : "bg-sky-500"}`} style={{ width: `${cat.progressPercent}%` }} />
                    </div>
                    <div className="text-xs font-mono w-12 text-right text-ms-text">{cat.progressPercent.toFixed(0)}%</div>
                </div>
            </button>
            {expanded && (
                <div className="border-t border-ms-border p-3 space-y-2">
                    {cat.items.map(item => (
                        <ItemRow key={item.id} item={item} openingId={openingId} onChange={onChange} readonly={readonly} />
                    ))}
                </div>
            )}
        </div>
    );
};

const ItemRow: React.FC<{ item: StoreOpeningItem; openingId: number; onChange: () => void; readonly: boolean }> = ({ item, openingId, onChange, readonly }) => {
    const [serial, setSerial] = useState(item.serialNumber || "");
    const [asset, setAsset] = useState(item.assetNumber || "");
    const [notes, setNotes] = useState(item.notes || "");
    const [photoOpen, setPhotoOpen] = useState(false);
    const fileRef = useRef<HTMLInputElement>(null);
    const saveTimer = useRef<number | null>(null);

    useEffect(() => {
        setSerial(item.serialNumber || "");
        setAsset(item.assetNumber || "");
        setNotes(item.notes || "");
    }, [item.id, item.serialNumber, item.assetNumber, item.notes]);

    const debouncedSave = (payload: Record<string, string>) => {
        if (saveTimer.current) window.clearTimeout(saveTimer.current);
        saveTimer.current = window.setTimeout(async () => {
            try { await apiClient.updateOpeningItem(openingId, item.id, payload); } catch (e) { console.error(e); }
        }, 600);
    };

    const cycleStatus = async () => {
        if (readonly) return;
        const next: StoreOpeningItemStatus =
            item.status === "Pending" ? "Completed" :
            item.status === "Completed" ? "NotApplicable" : "Pending";
        await apiClient.updateOpeningItem(openingId, item.id, { status: next });
        onChange();
    };

    const uploadPhoto = async (e: React.ChangeEvent<HTMLInputElement>) => {
        const file = e.target.files?.[0];
        if (!file) return;
        try {
            await apiClient.uploadOpeningItemPhoto(openingId, item.id, file);
            onChange();
        } catch (err) {
            alert("Foto yükleme başarısız: " + (err as Error).message);
        }
        e.target.value = "";
    };

    const deletePhoto = async () => {
        if (!confirm("Fotoğrafı silmek istiyor musunuz?")) return;
        await apiClient.deleteOpeningItemPhoto(openingId, item.id);
        onChange();
    };

    const StatusIcon = item.status === "Completed" ? CheckCircle2 : item.status === "NotApplicable" ? MinusCircle : Circle;
    const statusColor = item.status === "Completed" ? "text-emerald-400" : item.status === "NotApplicable" ? "text-zinc-500" : "text-zinc-400 hover:text-sky-400";

    return (
        <div className={`p-3 rounded-md border ${item.status === "Completed" ? "border-emerald-500/20 bg-emerald-500/5" : item.status === "NotApplicable" ? "border-zinc-700 bg-zinc-900/30 opacity-60" : "border-ms-border bg-zinc-900/30"}`}>
            <div className="flex items-start gap-3">
                <button onClick={cycleStatus} disabled={readonly} className={`mt-0.5 ${statusColor} transition-colors disabled:cursor-not-allowed`} title="Tıkla: Pending → Completed → N/A">
                    <StatusIcon className="w-5 h-5" />
                </button>
                <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                        {item.parentName && <span className="text-xs text-zinc-500">{item.parentName} ›</span>}
                        <span className={`text-sm font-medium ${item.status === "Completed" ? "text-emerald-300" : "text-ms-text"}`}>{item.itemName}</span>
                    </div>

                    {(item.hasSerialNumber || item.hasAssetNumber) && (
                        <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 mt-2">
                            {item.hasSerialNumber && (
                                <input
                                    placeholder="Seri No"
                                    value={serial}
                                    disabled={readonly}
                                    onChange={e => { setSerial(e.target.value); debouncedSave({ serialNumber: e.target.value }); }}
                                    className="px-2 py-1.5 bg-zinc-900 border border-ms-border rounded text-xs text-ms-text disabled:opacity-60"
                                />
                            )}
                            {item.hasAssetNumber && (
                                <input
                                    placeholder="Asset No"
                                    value={asset}
                                    disabled={readonly}
                                    onChange={e => { setAsset(e.target.value); debouncedSave({ assetNumber: e.target.value }); }}
                                    className="px-2 py-1.5 bg-zinc-900 border border-ms-border rounded text-xs text-ms-text disabled:opacity-60"
                                />
                            )}
                        </div>
                    )}

                    <input
                        placeholder="Not..."
                        value={notes}
                        disabled={readonly}
                        onChange={e => { setNotes(e.target.value); debouncedSave({ notes: e.target.value }); }}
                        className="w-full mt-2 px-2 py-1 bg-transparent border-b border-ms-border text-xs text-zinc-300 placeholder-zinc-600 focus:outline-none focus:border-sky-500 disabled:opacity-60"
                    />

                    {item.completedBy && item.completedAt && (
                        <div className="text-[11px] text-zinc-500 mt-1.5">
                            ✓ {item.completedBy} · {new Date(item.completedAt).toLocaleString("tr-TR")}
                        </div>
                    )}
                </div>

                <div className="flex flex-col items-center gap-1">
                    {item.photoUrl ? (
                        <>
                            <button onClick={() => setPhotoOpen(true)} className="w-12 h-12 rounded border border-ms-border overflow-hidden hover:border-sky-500" title="Fotoyu görüntüle">
                                <img src={apiClient.getOpeningItemPhotoUrl(openingId, item.id)} alt="" className="w-full h-full object-cover" />
                            </button>
                            {!readonly && (
                                <button onClick={deletePhoto} className="text-zinc-500 hover:text-rose-400" title="Foto sil">
                                    <Trash2 className="w-3 h-3" />
                                </button>
                            )}
                        </>
                    ) : !readonly && (
                        <button onClick={() => fileRef.current?.click()} className="w-12 h-12 rounded border border-dashed border-ms-border flex items-center justify-center text-zinc-500 hover:text-sky-400 hover:border-sky-500" title="Foto yükle">
                            <ImageIcon className="w-4 h-4" />
                        </button>
                    )}
                    <input ref={fileRef} type="file" accept="image/*" hidden onChange={uploadPhoto} />
                </div>
            </div>

            {photoOpen && item.photoUrl && (
                <Modal isOpen={true} onClose={() => setPhotoOpen(false)} title={item.itemName} size="xl">
                    <img src={apiClient.getOpeningItemPhotoUrl(openingId, item.id)} alt={item.itemName} className="w-full h-auto rounded" />
                </Modal>
            )}
        </div>
    );
};

const EditMetaModal: React.FC<{ opening: StoreOpeningDetail; onClose: () => void; onSaved: () => void }> = ({ opening, onClose, onSaved }) => {
    const [storeName, setStoreName] = useState(opening.storeName);
    const [city, setCity] = useState(opening.city || "");
    const [address, setAddress] = useState(opening.address || "");
    const [targetDate, setTargetDate] = useState(opening.targetOpeningDate.slice(0, 10));
    const [notes, setNotes] = useState(opening.notes || "");
    const [status, setStatus] = useState<StoreOpeningStatus>(opening.status);
    const [roles, setRoles] = useState<Record<string, string>>({
        "Donanım Sorumlusu": opening.roleAssignments["Donanım Sorumlusu"] || "",
        "Sistem / Network Sorumlusu": opening.roleAssignments["Sistem / Network Sorumlusu"] || "",
        "Router Sorumlusu": opening.roleAssignments["Router Sorumlusu"] || "",
        "Kasa Sorumlusu": opening.roleAssignments["Kasa Sorumlusu"] || "",
    });

    const save = async () => {
        const cleanRoles: Record<string, string> = {};
        Object.entries(roles).forEach(([k, v]) => { if (v.trim()) cleanRoles[k] = v.trim(); });
        await apiClient.updateStoreOpening(opening.id, {
            storeName: storeName.trim(),
            city: city.trim(),
            address: address.trim(),
            targetOpeningDate: new Date(targetDate).toISOString(),
            notes,
            status,
            roleAssignments: cleanRoles,
        });
        onSaved();
    };

    return (
        <Modal isOpen={true} onClose={onClose} title="Açılış Bilgileri" size="lg" dismissOnBackdrop={false}>
            <div className="space-y-3">
                <div className="grid grid-cols-2 gap-3">
                    <Field label="Mağaza Adı"><input value={storeName} onChange={e => setStoreName(e.target.value)} className="w-full px-3 py-2 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text" /></Field>
                    <Field label="Şehir"><input value={city} onChange={e => setCity(e.target.value)} className="w-full px-3 py-2 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text" /></Field>
                </div>
                <Field label="Adres"><input value={address} onChange={e => setAddress(e.target.value)} className="w-full px-3 py-2 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text" /></Field>
                <div className="grid grid-cols-2 gap-3">
                    <Field label="Termin"><input type="date" value={targetDate} onChange={e => setTargetDate(e.target.value)} className="w-full px-3 py-2 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text" /></Field>
                    <Field label="Durum">
                        <select value={status} onChange={e => setStatus(e.target.value as StoreOpeningStatus)} className="w-full px-3 py-2 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text">
                            <option value="Planned">Planlandı</option>
                            <option value="InProgress">Devam Ediyor</option>
                            <option value="Completed">Tamamlandı</option>
                            <option value="Cancelled">İptal</option>
                        </select>
                    </Field>
                </div>
                <Field label="Not"><textarea value={notes} onChange={e => setNotes(e.target.value)} rows={2} className="w-full px-3 py-2 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text" /></Field>

                <div className="text-xs text-zinc-400 font-semibold pt-2 border-t border-ms-border">Sorumlular</div>
                {Object.entries(roles).map(([role, val]) => (
                    <div key={role} className="grid grid-cols-3 gap-2 items-center">
                        <div className="text-xs text-zinc-300">{role}</div>
                        <input value={val} onChange={e => setRoles({ ...roles, [role]: e.target.value })} className="col-span-2 px-3 py-1.5 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text" />
                    </div>
                ))}

                <div className="flex justify-end gap-2 pt-3">
                    <button onClick={onClose} className="px-4 py-2 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded-lg text-sm flex items-center gap-1"><X className="w-3.5 h-3.5" /> İptal</button>
                    <button onClick={save} className="px-4 py-2 bg-sky-600 hover:bg-sky-500 text-white rounded-lg text-sm font-semibold flex items-center gap-1"><Save className="w-3.5 h-3.5" /> Kaydet</button>
                </div>
            </div>
        </Modal>
    );
};

const Field: React.FC<{ label: string; children: React.ReactNode }> = ({ label, children }) => (
    <div>
        <label className="text-xs text-zinc-400 block mb-1">{label}</label>
        {children}
    </div>
);

export default StoreOpeningDetailPage;
