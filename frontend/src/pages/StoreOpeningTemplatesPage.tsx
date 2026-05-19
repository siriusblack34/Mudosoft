import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { ArrowLeft, Plus, Trash2, Edit3, Copy, Star, StarOff, Save, X } from "lucide-react";
import { apiClient, type StoreOpeningTemplateSummary, type StoreOpeningTemplateDetail, type StoreOpeningTemplateItemInput } from "../lib/apiClient";
import Modal from "../components/ui/Modal";

const StoreOpeningTemplatesPage: React.FC = () => {
    const [templates, setTemplates] = useState<StoreOpeningTemplateSummary[]>([]);
    const [editingId, setEditingId] = useState<number | "new" | null>(null);
    const [loading, setLoading] = useState(true);

    const load = async () => {
        setLoading(true);
        try {
            const data = await apiClient.getStoreOpeningTemplates();
            setTemplates(data);
        } finally { setLoading(false); }
    };

    useEffect(() => { load(); }, []);

    const remove = async (id: number, name: string) => {
        if (!confirm(`"${name}" şablonunu silmek istiyor musunuz?`)) return;
        await apiClient.deleteStoreOpeningTemplate(id);
        load();
    };

    return (
        <div className="p-6 space-y-4">
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-3">
                    <Link to="/store-openings" className="p-2 bg-zinc-800 hover:bg-zinc-700 rounded-lg text-zinc-300"><ArrowLeft className="w-4 h-4" /></Link>
                    <div>
                        <h1 className="text-2xl font-bold text-ms-text">Açılış Şablonları</h1>
                        <p className="text-sm text-zinc-400 mt-1">Yeniden kullanılabilir donanım checklist şablonları</p>
                    </div>
                </div>
                <button onClick={() => setEditingId("new")} className="px-4 py-2 bg-sky-600 hover:bg-sky-500 text-white rounded-lg text-sm font-semibold flex items-center gap-2">
                    <Plus className="w-4 h-4" /> Yeni Şablon
                </button>
            </div>

            {loading ? (
                <div className="text-center py-12 text-zinc-400">Yükleniyor...</div>
            ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                    {templates.map(t => (
                        <div key={t.id} className="bg-ms-bg-soft border border-ms-border rounded-lg p-4">
                            <div className="flex items-start justify-between">
                                <div>
                                    <div className="flex items-center gap-2">
                                        <h3 className="font-semibold text-ms-text">{t.name}</h3>
                                        {t.isDefault && <Star className="w-4 h-4 text-amber-400 fill-amber-400" />}
                                    </div>
                                    {t.description && <p className="text-xs text-zinc-400 mt-1">{t.description}</p>}
                                </div>
                                <span className="text-xs text-zinc-500">{t.itemCount} kalem</span>
                            </div>
                            <div className="flex gap-2 mt-3">
                                <button onClick={() => setEditingId(t.id)} className="flex-1 px-3 py-1.5 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded text-xs flex items-center justify-center gap-1">
                                    <Edit3 className="w-3 h-3" /> Düzenle
                                </button>
                                <button onClick={() => remove(t.id, t.name)} className="px-3 py-1.5 bg-zinc-800 hover:bg-rose-500/20 text-zinc-200 hover:text-rose-300 rounded text-xs">
                                    <Trash2 className="w-3 h-3" />
                                </button>
                            </div>
                        </div>
                    ))}
                </div>
            )}

            {editingId !== null && (
                <TemplateEditor
                    templateId={editingId === "new" ? null : editingId}
                    onClose={() => setEditingId(null)}
                    onSaved={() => { setEditingId(null); load(); }}
                />
            )}
        </div>
    );
};

const ROLES = ["Donanım Sorumlusu", "Sistem / Network Sorumlusu", "Router Sorumlusu", "Kasa Sorumlusu"];

const TemplateEditor: React.FC<{ templateId: number | null; onClose: () => void; onSaved: () => void }> = ({ templateId, onClose, onSaved }) => {
    const [name, setName] = useState("");
    const [description, setDescription] = useState("");
    const [isDefault, setIsDefault] = useState(false);
    const [items, setItems] = useState<StoreOpeningTemplateItemInput[]>([]);
    const [loaded, setLoaded] = useState(templateId === null);
    const [saving, setSaving] = useState(false);

    useEffect(() => {
        if (templateId === null) { setLoaded(true); return; }
        apiClient.getStoreOpeningTemplate(templateId).then(t => {
            setName(t.name);
            setDescription(t.description || "");
            setIsDefault(t.isDefault);
            setItems(t.items.map(i => ({
                categoryName: i.categoryName,
                assignedRole: i.assignedRole,
                itemName: i.itemName,
                parentName: i.parentName,
                hasSerialNumber: i.hasSerialNumber,
                hasAssetNumber: i.hasAssetNumber,
                sortOrder: i.sortOrder,
            })));
            setLoaded(true);
        });
    }, [templateId]);

    const grouped = useMemo(() => {
        const map = new Map<string, StoreOpeningTemplateItemInput[]>();
        items.forEach(i => {
            if (!map.has(i.categoryName)) map.set(i.categoryName, []);
            map.get(i.categoryName)!.push(i);
        });
        return Array.from(map.entries());
    }, [items]);

    const addItem = () => {
        const lastSort = items.length > 0 ? Math.max(...items.map(i => i.sortOrder)) : 0;
        setItems([...items, {
            categoryName: "Yeni Kategori",
            assignedRole: ROLES[0],
            itemName: "Yeni Kalem",
            hasSerialNumber: false,
            hasAssetNumber: false,
            sortOrder: lastSort + 10,
        }]);
    };

    const updateItem = (idx: number, patch: Partial<StoreOpeningTemplateItemInput>) => {
        setItems(items.map((it, i) => i === idx ? { ...it, ...patch } : it));
    };

    const deleteItem = (idx: number) => {
        setItems(items.filter((_, i) => i !== idx));
    };

    const save = async () => {
        if (!name.trim()) { alert("Şablon adı zorunlu"); return; }
        setSaving(true);
        try {
            const payload = { name: name.trim(), description: description.trim() || undefined, isDefault, items };
            if (templateId === null) await apiClient.createStoreOpeningTemplate(payload);
            else await apiClient.updateStoreOpeningTemplate(templateId, payload);
            onSaved();
        } catch (e) {
            alert("Kaydedilemedi: " + (e as Error).message);
            setSaving(false);
        }
    };

    if (!loaded) return <Modal isOpen={true} onClose={onClose} title="..." size="xl" dismissOnBackdrop={false}><div className="text-zinc-400">Yükleniyor...</div></Modal>;

    return (
        <Modal isOpen={true} onClose={onClose} title={templateId === null ? "Yeni Şablon" : "Şablonu Düzenle"} size="xl" dismissOnBackdrop={false}>
            <div className="space-y-3">
                <div className="grid grid-cols-2 gap-3">
                    <div>
                        <label className="text-xs text-zinc-400">Ad *</label>
                        <input value={name} onChange={e => setName(e.target.value)} className="w-full mt-1 px-3 py-2 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text" />
                    </div>
                    <div className="flex items-end">
                        <label className="flex items-center gap-2 text-sm text-zinc-300 cursor-pointer">
                            <input type="checkbox" checked={isDefault} onChange={e => setIsDefault(e.target.checked)} className="w-4 h-4" />
                            Varsayılan şablon (yeni açılışlarda öneri)
                        </label>
                    </div>
                </div>
                <div>
                    <label className="text-xs text-zinc-400">Açıklama</label>
                    <input value={description} onChange={e => setDescription(e.target.value)} className="w-full mt-1 px-3 py-2 bg-zinc-900 border border-ms-border rounded-md text-sm text-ms-text" />
                </div>

                <div className="flex items-center justify-between pt-2">
                    <div className="text-sm text-zinc-300 font-semibold">Kalemler ({items.length})</div>
                    <button onClick={addItem} className="px-3 py-1.5 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded text-xs flex items-center gap-1">
                        <Plus className="w-3 h-3" /> Kalem Ekle
                    </button>
                </div>

                <div className="max-h-[400px] overflow-y-auto space-y-2 pr-1">
                    {grouped.map(([cat, catItems]) => (
                        <div key={cat} className="border border-ms-border rounded-md p-2">
                            <div className="text-xs font-semibold text-sky-400 mb-2">{cat}</div>
                            <div className="space-y-1.5">
                                {catItems.map(it => {
                                    const idx = items.indexOf(it);
                                    return (
                                        <div key={idx} className="grid grid-cols-12 gap-1.5 items-center bg-zinc-900/30 p-2 rounded">
                                            <input value={it.categoryName} onChange={e => updateItem(idx, { categoryName: e.target.value })} className="col-span-2 px-2 py-1 bg-zinc-900 border border-ms-border rounded text-xs text-ms-text" title="Kategori" />
                                            <input value={it.itemName} onChange={e => updateItem(idx, { itemName: e.target.value })} className="col-span-3 px-2 py-1 bg-zinc-900 border border-ms-border rounded text-xs text-ms-text" placeholder="Kalem" />
                                            <input value={it.parentName || ""} onChange={e => updateItem(idx, { parentName: e.target.value })} className="col-span-2 px-2 py-1 bg-zinc-900 border border-ms-border rounded text-xs text-ms-text" placeholder="Üst" />
                                            <select value={it.assignedRole || ""} onChange={e => updateItem(idx, { assignedRole: e.target.value })} className="col-span-2 px-1 py-1 bg-zinc-900 border border-ms-border rounded text-xs text-ms-text">
                                                <option value="">— rol —</option>
                                                {ROLES.map(r => <option key={r} value={r}>{r}</option>)}
                                            </select>
                                            <label className="col-span-1 text-[10px] text-zinc-400 flex items-center gap-1 cursor-pointer" title="Seri No">
                                                <input type="checkbox" checked={it.hasSerialNumber} onChange={e => updateItem(idx, { hasSerialNumber: e.target.checked })} />S
                                            </label>
                                            <label className="col-span-1 text-[10px] text-zinc-400 flex items-center gap-1 cursor-pointer" title="Asset No">
                                                <input type="checkbox" checked={it.hasAssetNumber} onChange={e => updateItem(idx, { hasAssetNumber: e.target.checked })} />A
                                            </label>
                                            <button onClick={() => deleteItem(idx)} className="col-span-1 text-zinc-500 hover:text-rose-400">
                                                <Trash2 className="w-3 h-3" />
                                            </button>
                                        </div>
                                    );
                                })}
                            </div>
                        </div>
                    ))}
                    {items.length === 0 && <div className="text-center py-6 text-zinc-500 text-sm">Henüz kalem yok. "Kalem Ekle" ile başlayın.</div>}
                </div>

                <div className="flex justify-end gap-2 pt-3 border-t border-ms-border">
                    <button onClick={onClose} className="px-4 py-2 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 rounded-lg text-sm flex items-center gap-1"><X className="w-3.5 h-3.5" /> İptal</button>
                    <button onClick={save} disabled={saving} className="px-4 py-2 bg-sky-600 hover:bg-sky-500 disabled:opacity-50 text-white rounded-lg text-sm font-semibold flex items-center gap-1">
                        <Save className="w-3.5 h-3.5" /> {saving ? "Kaydediliyor..." : "Kaydet"}
                    </button>
                </div>
            </div>
        </Modal>
    );
};

export default StoreOpeningTemplatesPage;
