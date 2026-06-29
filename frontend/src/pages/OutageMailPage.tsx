import React, { useEffect, useMemo, useRef, useState } from 'react';
import { AlertCircle, CheckCircle2, Loader2, Send, Search, X, Mail, Pencil, RefreshCw } from 'lucide-react';
import { apiClient, type StoreManager } from '../lib/apiClient';
import type {
    OutageMailTemplateGroup, OutageMailPreview, OutageMailRequest,
} from '../types';

const OutageMailPage: React.FC = () => {
    const [groups, setGroups] = useState<OutageMailTemplateGroup[]>([]);
    const [stores, setStores] = useState<StoreManager[]>([]);
    const [loadingInit, setLoadingInit] = useState(true);

    const [issueKey, setIssueKey] = useState<string>('internet-kesinti');
    const [selectedCodes, setSelectedCodes] = useState<number[]>([]);
    const [additionalNotes, setAdditionalNotes] = useState('');
    const [storeFilter, setStoreFilter] = useState('');

    const [preview, setPreview] = useState<OutageMailPreview | null>(null);
    const [previewLoading, setPreviewLoading] = useState(false);
    const [previewError, setPreviewError] = useState<string>('');

    // Düzenlenebilir önizleme
    const [editedText, setEditedText] = useState<string>('');
    const [editedTo, setEditedTo] = useState<string>('');
    const [editedCcText, setEditedCcText] = useState<string>('');
    const [isEditing, setIsEditing] = useState(false);
    const textareaRef = useRef<HTMLTextAreaElement>(null);

    const [sending, setSending] = useState(false);
    const [sendResult, setSendResult] = useState<{ ok: boolean; message: string } | null>(null);

    const [syncingAddresses, setSyncingAddresses] = useState(false);
    const [syncResult, setSyncResult] = useState<{ ok: boolean; message: string } | null>(null);

    const handleSyncAddresses = async () => {
        setSyncingAddresses(true);
        setSyncResult(null);
        try {
            const r = await apiClient.syncStoreAddresses();
            const missingNote = r.missingCount > 0 ? ` · ${r.missingCount} mağaza için adres bulunamadı` : '';
            setSyncResult({ ok: true, message: `GENIUS3'ten ${r.fetched} adres çekildi, ${r.updated} kayıt güncellendi${missingNote}.` });
            const mgr = await apiClient.getStoreManagers();
            setStores(mgr);
        } catch (e: any) {
            setSyncResult({ ok: false, message: e?.message || 'Senkronizasyon başarısız' });
        } finally {
            setSyncingAddresses(false);
        }
    };

    useEffect(() => {
        (async () => {
            try {
                const [tpl, mgr] = await Promise.all([
                    apiClient.getOutageMailTemplates(),
                    apiClient.getStoreManagers(),
                ]);
                setGroups(tpl);
                setStores(mgr);
            } catch (e) {
                console.error(e);
            } finally {
                setLoadingInit(false);
            }
        })();
    }, []);

    // Preview — form değişince backend'den al
    useEffect(() => {
        if (selectedCodes.length === 0) {
            setPreview(null);
            setPreviewError('');
            setEditedText('');
            setEditedTo('');
            setEditedCcText('');
            setIsEditing(false);
            return;
        }
        const handle = setTimeout(async () => {
            setPreviewLoading(true);
            setPreviewError('');
            try {
                const req: OutageMailRequest = {
                    storeCodes: selectedCodes,
                    issueKey,
                    additionalNotes: additionalNotes.trim() || undefined,
                };
                const p = await apiClient.previewOutageMail(req);
                setPreview(p);
                setEditedText(p.plainText);
                setEditedTo(p.to);
                setEditedCcText(p.cc.join(', '));
                setIsEditing(false);
            } catch (e: any) {
                setPreviewError(e?.message || 'Önizleme alınamadı');
                setPreview(null);
            } finally {
                setPreviewLoading(false);
            }
        }, 300);
        return () => clearTimeout(handle);
    }, [selectedCodes, issueKey, additionalNotes]);

    // Textarea auto-resize
    useEffect(() => {
        if (textareaRef.current) {
            textareaRef.current.style.height = 'auto';
            textareaRef.current.style.height = textareaRef.current.scrollHeight + 'px';
        }
    }, [editedText, isEditing]);

    const handleToggleEdit = () => {
        if (!isEditing && preview) {
            // Düzenlemeye başlarken preview değerlerini yükle (eğer daha önce değiştirilmediyse)
            setEditedTo(prev => prev || preview.to);
            setEditedCcText(prev => prev || preview.cc.join(', '));
        }
        setIsEditing(e => !e);
    };

    const handleReset = () => {
        if (!preview) return;
        setEditedText(preview.plainText);
        setEditedTo(preview.to);
        setEditedCcText(preview.cc.join(', '));
        setIsEditing(false);
    };

    const isModified = preview
        ? (editedText !== preview.plainText || editedTo !== preview.to || editedCcText !== preview.cc.join(', '))
        : false;

    const handleSend = async () => {
        if (selectedCodes.length === 0 || !preview) return;
        setSending(true);
        setSendResult(null);
        try {
            const isTextEdited = editedText !== preview.plainText;
            const isToEdited = editedTo.trim() !== preview.to;
            const parsedCc = editedCcText.split(',').map(e => e.trim()).filter(Boolean);
            const isCcEdited = parsedCc.join(',') !== preview.cc.join(',');

            const req: OutageMailRequest = {
                storeCodes: selectedCodes,
                issueKey,
                additionalNotes: additionalNotes.trim() || undefined,
                editedPlainText: isTextEdited ? editedText : undefined,
                toOverride: isToEdited ? editedTo.trim() : undefined,
                ccOverride: isCcEdited ? parsedCc : undefined,
            };
            const res = await apiClient.sendOutageMail(req);
            if (res.success) {
                setSendResult({ ok: true, message: `Mail gönderildi → ${res.to}${res.cc.length ? ' (CC: ' + res.cc.join(', ') + ')' : ''}` });
                setSelectedCodes([]);
                setAdditionalNotes('');
            } else {
                setSendResult({ ok: false, message: res.error || 'Gönderim başarısız' });
            }
        } catch (e: any) {
            setSendResult({ ok: false, message: e?.message || 'Gönderim hatası' });
        } finally {
            setSending(false);
        }
    };

    const toggleStore = (code: number) => {
        setSelectedCodes(prev => prev.includes(code) ? prev.filter(c => c !== code) : [...prev, code]);
    };

    const filteredStores = useMemo(() => {
        const q = storeFilter.trim().toLowerCase();
        const uniq = new Map<number, StoreManager>();
        for (const s of stores) {
            if (!uniq.has(s.storeCode)) uniq.set(s.storeCode, s);
        }
        const all = [...uniq.values()].sort((a, b) => a.storeCode - b.storeCode);
        if (!q) return all;
        return all.filter(s =>
            s.storeName.toLowerCase().includes(q) ||
            String(s.storeCode).includes(q) ||
            (s.fullName || '').toLowerCase().includes(q)
        );
    }, [stores, storeFilter]);

    if (loadingInit) {
        return (
            <div className="p-6 flex items-center gap-2 text-ms-text-muted">
                <Loader2 className="w-4 h-4 animate-spin" /> Yükleniyor…
            </div>
        );
    }

    return (
        <div className="p-6 max-w-[1400px] mx-auto">
            <div className="flex items-center gap-3 mb-6">
                <div className="h-10 w-10 rounded-xl bg-violet-600/20 border border-violet-500/30 flex items-center justify-center">
                    <Mail className="w-5 h-5 text-violet-400" />
                </div>
                <div>
                    <h1 className="page-header !mb-0">Arıza Bildirimi</h1>
                    <p className="text-xs text-ms-text-muted">
                        İnternet / POS arızaları için otomatik mail oluştur ve gönder.
                    </p>
                </div>
            </div>

            <div className="grid grid-cols-1 lg:grid-cols-[400px_1fr] gap-6">
                {/* ── Sol: form ─── */}
                <div className="space-y-4">
                    {/* Sorun türü */}
                    <div className="card">
                        <label className="form-label">Sorun Türü</label>
                        <div className="space-y-3">
                            {groups.map(g => (
                                <div key={g.category}>
                                    <div className="text-[10px] font-semibold uppercase tracking-wider text-violet-400 mb-1">
                                        {g.category}
                                    </div>
                                    <div className="space-y-0.5">
                                        {g.items.map(t => (
                                            <label
                                                key={t.key}
                                                className={`flex items-center gap-2 px-2.5 py-1.5 rounded-lg cursor-pointer text-sm transition-colors ${
                                                    issueKey === t.key
                                                        ? 'bg-violet-600/20 text-violet-200 border border-violet-500/40'
                                                        : 'border border-transparent hover:bg-ms-hover-bg text-ms-text'
                                                }`}
                                            >
                                                <input
                                                    type="radio"
                                                    name="issueKey"
                                                    value={t.key}
                                                    checked={issueKey === t.key}
                                                    onChange={() => setIssueKey(t.key)}
                                                    className="accent-violet-500"
                                                />
                                                <span>{t.label}</span>
                                            </label>
                                        ))}
                                    </div>
                                </div>
                            ))}
                        </div>
                    </div>

                    {/* Alıcı + not */}
                    <div className="card space-y-3">
                        <div className="space-y-1 text-[12px] text-ms-text-muted">
                            <div>Mail "Merhaba Onur Bey" olarak başlar.</div>
                            <div>Kime: <span className="font-mono text-ms-text">onur.karagoz@turkcell.com.tr</span></div>
                            <div>CC: <span className="font-mono text-ms-text">MudoBTDestek@mudo.com.tr</span> ve gönderen teknisyen</div>
                        </div>
                        <div className="flex items-center justify-between gap-2 -mt-1">
                            <span className="text-[11px] text-ms-text-muted">
                                Adresler GENIUS3 STORE tablosundan alınır.
                            </span>
                            <button
                                onClick={handleSyncAddresses}
                                disabled={syncingAddresses}
                                className="flex items-center gap-1 text-[11px] px-2 py-1 rounded-md text-ms-text-muted hover:text-violet-400 border border-ms-border hover:border-violet-500/40 transition-colors disabled:opacity-50"
                            >
                                {syncingAddresses
                                    ? <Loader2 className="w-3 h-3 animate-spin" />
                                    : <RefreshCw className="w-3 h-3" />}
                                Adresleri senkronize et
                            </button>
                        </div>
                        {syncResult && (
                            <div className={`p-2 rounded-md text-[11px] flex items-start gap-1.5 ${
                                syncResult.ok
                                    ? 'bg-emerald-600/10 text-emerald-300 border border-emerald-500/30'
                                    : 'bg-red-600/10 text-red-300 border border-red-500/30'
                            }`}>
                                {syncResult.ok
                                    ? <CheckCircle2 className="w-3 h-3 mt-0.5 shrink-0" />
                                    : <AlertCircle className="w-3 h-3 mt-0.5 shrink-0" />}
                                <span>{syncResult.message}</span>
                            </div>
                        )}
                        <div>
                            <label className="form-label">Ek Not (opsiyonel)</label>
                            <textarea
                                rows={2}
                                placeholder="ör. Router'da elektrik vardır."
                                value={additionalNotes}
                                onChange={e => setAdditionalNotes(e.target.value)}
                            />
                            <p className="text-[11px] text-ms-text-muted mt-1">Sorun cümlesinin sonuna eklenir.</p>
                        </div>
                    </div>

                    {/* Mağaza seçimi */}
                    <div className="card">
                        <div className="flex items-center justify-between mb-2">
                            <label className="form-label !mb-0">
                                Mağaza
                                <span className="ml-2 text-violet-400 font-bold normal-case tracking-normal">
                                    {selectedCodes.length > 0 && `${selectedCodes.length} seçili`}
                                </span>
                            </label>
                            {selectedCodes.length > 0 && (
                                <button
                                    onClick={() => setSelectedCodes([])}
                                    className="text-[11px] text-ms-text-muted hover:text-violet-400 transition-colors"
                                >
                                    Temizle
                                </button>
                            )}
                        </div>

                        <div className="relative mb-2">
                            <Search className="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-ms-text-muted" />
                            <input
                                type="text"
                                placeholder="Mağaza kodu veya adı…"
                                value={storeFilter}
                                onChange={e => setStoreFilter(e.target.value)}
                                className="!pl-8"
                            />
                        </div>

                        {selectedCodes.length > 0 && (
                            <div className="flex flex-wrap gap-1 mb-2 pb-2 border-b border-ms-border">
                                {selectedCodes.map(code => {
                                    const s = stores.find(x => x.storeCode === code);
                                    return (
                                        <button
                                            key={code}
                                            onClick={() => toggleStore(code)}
                                            className="flex items-center gap-1 pl-2 pr-1 py-0.5 rounded-md bg-violet-600/20 text-violet-200 border border-violet-500/40 text-xs hover:bg-violet-600/30 transition-colors"
                                        >
                                            <span className="font-mono text-[10px] opacity-60">{code}</span>
                                            <span>{s?.storeName ?? '?'}</span>
                                            <X className="w-3 h-3" />
                                        </button>
                                    );
                                })}
                            </div>
                        )}

                        <div className="max-h-72 overflow-y-auto space-y-0.5">
                            {filteredStores.length === 0 ? (
                                <div className="text-xs text-ms-text-muted py-3 text-center">Sonuç yok</div>
                            ) : filteredStores.map(s => {
                                const checked = selectedCodes.includes(s.storeCode);
                                return (
                                    <label
                                        key={s.storeCode}
                                        className={`flex items-center gap-2 px-2 py-1.5 rounded-md cursor-pointer text-[13px] transition-colors ${
                                            checked ? 'bg-violet-600/10' : 'hover:bg-ms-hover-bg'
                                        }`}
                                    >
                                        <input
                                            type="checkbox"
                                            checked={checked}
                                            onChange={() => toggleStore(s.storeCode)}
                                            className="accent-violet-500 shrink-0"
                                        />
                                        <span className="font-mono text-[10px] text-ms-text-muted shrink-0 w-8">
                                            {s.storeCode}
                                        </span>
                                        <span className="flex-1 truncate text-ms-text">{s.storeName}</span>
                                        {s.fullName && (
                                            <span className="text-[10px] text-ms-text-muted truncate max-w-[100px]">
                                                {s.fullName}
                                            </span>
                                        )}
                                    </label>
                                );
                            })}
                        </div>
                    </div>
                </div>

                {/* ── Sağ: önizleme + gönder ─── */}
                <div className="space-y-4">
                    <div className="card">
                        <div className="flex items-center justify-between mb-3">
                            <div className="text-xs font-semibold uppercase tracking-wider text-ms-text-muted">
                                Önizleme
                            </div>
                            <div className="flex items-center gap-2">
                                {previewLoading && <Loader2 className="w-3.5 h-3.5 animate-spin text-ms-text-muted" />}
                                {preview && (
                                    <button
                                        onClick={handleToggleEdit}
                                        className={`flex items-center gap-1 text-[11px] px-2 py-1 rounded-md transition-colors ${
                                            isEditing
                                                ? 'bg-violet-600/20 text-violet-300 border border-violet-500/40'
                                                : 'text-ms-text-muted hover:text-violet-400 border border-transparent'
                                        }`}
                                    >
                                        <Pencil className="w-3 h-3" />
                                        {isEditing ? 'Düzenleniyor' : 'Düzenle'}
                                    </button>
                                )}
                            </div>
                        </div>

                        {selectedCodes.length === 0 ? (
                            <div className="py-16 text-center text-ms-text-muted text-sm">
                                Önizlemeyi görmek için mağaza seçin.
                            </div>
                        ) : previewError ? (
                            <div className="py-6 text-center text-red-400 text-sm flex items-center justify-center gap-2">
                                <AlertCircle className="w-4 h-4" /> {previewError}
                            </div>
                        ) : preview ? (
                            <div>
                                {/* Mail header */}
                                <div className="space-y-1 mb-4 pb-3 border-b border-ms-border text-[13px]">
                                    {isEditing ? (
                                        <>
                                            <EditableHeaderRow
                                                label="Kime"
                                                value={editedTo}
                                                onChange={setEditedTo}
                                                placeholder="alıcı@turkcell.com.tr"
                                            />
                                            <EditableHeaderRow
                                                label="CC"
                                                value={editedCcText}
                                                onChange={setEditedCcText}
                                                placeholder="cc1@ornek.com, cc2@ornek.com"
                                            />
                                        </>
                                    ) : (
                                        <>
                                            <HeaderRow label="Kime" value={editedTo || preview.to} />
                                            {(editedCcText || preview.cc.join(', ')) && (
                                                <HeaderRow label="CC" value={editedCcText || preview.cc.join(', ')} />
                                            )}
                                        </>
                                    )}
                                    <HeaderRow label="Konu" value={preview.subject} bold />
                                </div>

                                {/* Body — readonly veya textarea */}
                                {isEditing ? (
                                    <textarea
                                        ref={textareaRef}
                                        value={editedText}
                                        onChange={e => setEditedText(e.target.value)}
                                        className="w-full bg-ms-panel border border-ms-border rounded-lg p-3 text-[14px] text-ms-text leading-relaxed font-sans resize-none focus:outline-none focus:ring-2 focus:ring-violet-500/50"
                                        style={{ minHeight: 200 }}
                                    />
                                ) : (
                                    <pre className="whitespace-pre-wrap font-sans text-[14px] text-ms-text leading-relaxed">
{editedText}
                                    </pre>
                                )}

                                {isModified && (
                                    <div className="mt-2 flex items-center gap-2">
                                        <span className="text-[11px] text-violet-400">Manuel düzenlendi</span>
                                        <button
                                            onClick={handleReset}
                                            className="text-[11px] text-ms-text-muted hover:text-violet-400 transition-colors"
                                        >
                                            Sıfırla
                                        </button>
                                    </div>
                                )}
                            </div>
                        ) : null}
                    </div>

                    {/* Gönderim */}
                    <div className="card">
                        {sendResult && (
                            <div className={`mb-3 p-3 rounded-lg text-sm flex items-start gap-2 ${
                                sendResult.ok
                                    ? 'bg-emerald-600/10 text-emerald-300 border border-emerald-500/30'
                                    : 'bg-red-600/10 text-red-300 border border-red-500/30'
                            }`}>
                                {sendResult.ok ? <CheckCircle2 className="w-4 h-4 mt-0.5 shrink-0" /> : <AlertCircle className="w-4 h-4 mt-0.5 shrink-0" />}
                                <span>{sendResult.message}</span>
                            </div>
                        )}

                        <button
                            disabled={selectedCodes.length === 0 || sending || !preview}
                            onClick={handleSend}
                            className="btn-primary w-full justify-center"
                        >
                            {sending ? (
                                <><Loader2 className="w-4 h-4 animate-spin" /> Gönderiliyor…</>
                            ) : (
                                <><Send className="w-4 h-4" /> Maili Gönder</>
                            )}
                        </button>
                        <p className="text-[11px] text-ms-text-muted mt-2 text-center">
                            Canlı alıcı: <span className="font-mono">{preview ? (editedTo || preview.to) : 'onur.karagoz@turkcell.com.tr'}</span><br />
                            CC: <span className="font-mono">MudoBTDestek@mudo.com.tr</span> ve gönderen teknisyen
                        </p>
                    </div>
                </div>
            </div>
        </div>
    );
};

const HeaderRow: React.FC<{ label: string; value: string; bold?: boolean }> = ({ label, value, bold }) => (
    <div className="flex items-baseline gap-3">
        <span className="w-12 text-[11px] font-semibold uppercase tracking-wider text-ms-text-muted shrink-0">
            {label}
        </span>
        <span className={`flex-1 ${bold ? 'font-semibold text-ms-text' : 'text-ms-text'}`}>{value}</span>
    </div>
);

const EditableHeaderRow: React.FC<{
    label: string;
    value: string;
    onChange: (v: string) => void;
    placeholder?: string;
}> = ({ label, value, onChange, placeholder }) => (
    <div className="flex items-center gap-3">
        <span className="w-12 text-[11px] font-semibold uppercase tracking-wider text-ms-text-muted shrink-0">
            {label}
        </span>
        <input
            type="text"
            value={value}
            onChange={e => onChange(e.target.value)}
            placeholder={placeholder}
            className="flex-1 bg-ms-panel border border-violet-500/40 rounded-md px-2 py-0.5 text-[13px] text-ms-text focus:outline-none focus:ring-1 focus:ring-violet-500/50"
        />
    </div>
);

export default OutageMailPage;
