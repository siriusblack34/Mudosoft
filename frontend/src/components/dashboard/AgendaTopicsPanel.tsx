import React, { useCallback, useEffect, useMemo, useState } from "react";
import { CalendarClock, Edit, Megaphone, Save, Search, Trash2 } from "lucide-react";
import Modal from "../ui/Modal";
import {
  AGENDA_UPDATED_EVENT,
  CATEGORY_OPTIONS,
  createAgendaItem,
  deleteAgendaItem,
  EMPTY_AGENDA_FORM,
  fetchAgendaItems,
  type AgendaFormData,
  type AgendaItem,
  PRIORITY_OPTIONS,
  sortAgendaItems,
  STATUS_OPTIONS,
  updateAgendaItem,
} from "../../lib/agendaStore";

type PanelPalette = {
  text: string;
  muted: string;
  subtle: string;
  border: string;
  card: string;
  cardAlt: string;
  cardSoft: string;
  track: string;
  rose: string;
  amber: string;
  sky: string;
  emerald: string;
  violet: string;
  roseSoft: string;
  amberSoft: string;
  skySoft: string;
  emeraldSoft: string;
};

interface AgendaTopicsPanelProps {
  c: PanelPalette;
}

function formatAgendaDate(value: string) {
  if (!value) return "Termin yok";

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "Termin yok";

  return date.toLocaleDateString("tr-TR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

function getStatusStyle(status: AgendaItem["status"], c: PanelPalette) {
  if (status === "Tamamlandi") return { background: c.emeraldSoft, color: c.emerald };
  if (status === "Planlandi") return { background: c.skySoft, color: c.sky };
  return { background: c.amberSoft, color: c.amber };
}

function getPriorityStyle(priority: AgendaItem["priority"], c: PanelPalette) {
  if (priority === "Yuksek") return { background: c.roseSoft, color: c.rose };
  if (priority === "Dusuk") return { background: c.track, color: c.subtle };
  return { background: `${c.violet}20`, color: c.violet };
}

export default function AgendaTopicsPanel({ c }: AgendaTopicsPanelProps) {
  const [items, setItems] = useState<AgendaItem[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [agendaError, setAgendaError] = useState<string | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [editingItem, setEditingItem] = useState<AgendaItem | null>(null);
  const [formData, setFormData] = useState<AgendaFormData>(EMPTY_AGENDA_FORM);
  const [searchTerm, setSearchTerm] = useState("");

  const loadAgenda = useCallback(async () => {
    try {
      setAgendaError(null);
      setItems(sortAgendaItems(await fetchAgendaItems()));
    } catch (error) {
      setAgendaError(error instanceof Error ? error.message : "Gundem yuklenemedi.");
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadAgenda();
    const syncAgenda = () => { void loadAgenda(); };
    window.addEventListener(AGENDA_UPDATED_EVENT, syncAgenda);
    return () => {
      window.removeEventListener(AGENDA_UPDATED_EVENT, syncAgenda);
    };
  }, [loadAgenda]);

  const previewItems = useMemo(
    () => items.filter((item) => item.status !== "Tamamlandi").slice(0, 3),
    [items],
  );

  const filteredItems = useMemo(() => {
    const query = searchTerm.trim().toLowerCase();
    return items.filter((item) => (
      !query || `${item.title} ${item.content} ${item.category} ${item.createdBy}`.toLowerCase().includes(query)
    ));
  }, [items, searchTerm]);

  const resetForm = () => {
    setEditingItem(null);
    setFormData(EMPTY_AGENDA_FORM);
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();

    setIsSaving(true);
    try {
      if (editingItem) await updateAgendaItem(editingItem.id, formData);
      else await createAgendaItem(formData);
      await loadAgenda();
      resetForm();
    } catch (error) {
      console.error("Gundem kaydedilemedi:", error);
      window.alert(error instanceof Error ? error.message : "Gundem kaydedilemedi.");
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = async (id: string) => {
    if (!window.confirm("Bu gundem basligini silmek istiyor musunuz?")) return;
    try {
      await deleteAgendaItem(id);
      await loadAgenda();
      if (editingItem?.id === id) resetForm();
    } catch (error) {
      console.error("Gundem silinemedi:", error);
      window.alert(error instanceof Error ? error.message : "Gundem silinemedi.");
    }
  };

  const handleEdit = (item: AgendaItem) => {
    setEditingItem(item);
    setFormData({
      title: item.title,
      content: item.content,
      status: item.status,
      priority: item.priority,
      category: item.category,
      dueDate: item.dueDate,
    });
  };

  return (
    <>
      <div className="mt-4 border-t pt-4" style={{ borderColor: c.border }}>
        <div className="mb-3 flex items-start justify-between gap-3">
          <div>
            <div className="flex items-center gap-2">
              <Megaphone className="h-4 w-4" style={{ color: c.amber }} />
              <span className="text-sm font-bold tracking-[0.01em]" style={{ color: c.text }}>{"G\u00FCndem"}</span>
            </div>
            <p className="mt-1 text-[10px]" style={{ color: c.muted }}>
              Kesinti, acilis ve takip notlarini bu karttan yonetin.
            </p>
          </div>

          <button
            type="button"
            onClick={() => setIsModalOpen(true)}
            className="rounded-lg px-3 py-1.5 text-[11px] font-semibold transition-all"
            style={{ background: c.amberSoft, color: c.amber, border: `1px solid ${c.border}` }}
          >
            {items.length > 0 ? "Yonet" : "Baslik Ekle"}
          </button>
        </div>

        {agendaError && (
          <div className="mb-2 rounded-lg px-3 py-2 text-[10px]" style={{ background: c.roseSoft, color: c.rose }}>
            {agendaError}
          </div>
        )}

        {isLoading ? (
          <div className="rounded-xl border px-4 py-4 text-xs" style={{ borderColor: c.border, background: c.cardSoft, color: c.muted }}>
            Gundem yukleniyor...
          </div>
        ) : previewItems.length === 0 ? (
          <button
            type="button"
            onClick={() => setIsModalOpen(true)}
            className="w-full rounded-xl border border-dashed px-4 py-4 text-left transition-colors"
            style={{ borderColor: c.border, background: c.cardSoft }}
          >
            <div className="text-xs font-semibold" style={{ color: c.text }}>
              Henuz gundem basligi eklenmedi
            </div>
            <div className="mt-1 text-[10px]" style={{ color: c.muted }}>
              Ornek: yeni magaza acilisi, internet kesintisi takibi, terminal kurulumu.
            </div>
          </button>
        ) : (
          <div className="space-y-2">
            {previewItems.map((item) => {
              const statusStyle = getStatusStyle(item.status, c);
              return (
                <button
                  key={item.id}
                  type="button"
                  onClick={() => {
                    setIsModalOpen(true);
                    handleEdit(item);
                  }}
                  className="w-full rounded-xl border px-3 py-2.5 text-left transition-all hover:brightness-105"
                  style={{ background: c.cardSoft, borderColor: c.border }}
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className="text-[12px] font-semibold truncate" style={{ color: c.text }}>
                        {item.title}
                      </div>
                      <div className="mt-1 flex flex-wrap items-center gap-2">
                        <span
                          className="rounded-full px-2 py-0.5 text-[9px] font-bold uppercase"
                          style={{ background: statusStyle.background, color: statusStyle.color }}
                        >
                          {item.status}
                        </span>
                        <span className="text-[10px]" style={{ color: c.muted }}>
                          {item.category}
                        </span>
                      </div>
                    </div>
                    <span className="shrink-0 text-[10px] font-semibold" style={{ color: c.amber }}>
                      {formatAgendaDate(item.dueDate)}
                    </span>
                  </div>
                </button>
              );
            })}

            {items.length > previewItems.length && (
              <div className="text-[10px]" style={{ color: c.muted }}>
                +{items.length - previewItems.length} baslik daha var
              </div>
            )}
          </div>
        )}
      </div>

      <Modal isOpen={isModalOpen} onClose={() => { setIsModalOpen(false); resetForm(); }} title="Kesinti Gundemi" size="xl">
        <div className="grid gap-5 xl:grid-cols-[360px_minmax(0,1fr)]">
          <form
            onSubmit={handleSubmit}
            className="rounded-2xl border p-4 space-y-3 h-fit"
            style={{ background: c.cardAlt, borderColor: c.border }}
          >
            <div>
              <div className="text-sm font-semibold" style={{ color: c.text }}>
                {editingItem ? "Basligi Duzenle" : "Yeni Konu Basligi"}
              </div>
              <div className="mt-1 text-[11px]" style={{ color: c.muted }}>
                Kesinti veya kapali magazalarla ilgili aksiyon basligini hizlica ekleyin.
              </div>
            </div>

            <div>
              <label className="mb-1.5 block text-[11px] font-semibold" style={{ color: c.muted }}>
                Baslik
              </label>
              <input
                type="text"
                required
                value={formData.title}
                onChange={(event) => setFormData((current) => ({ ...current, title: event.target.value }))}
                className="w-full rounded-xl px-3 py-2.5 text-sm outline-none"
                style={{ background: c.card, border: `1px solid ${c.border}`, color: c.text }}
                placeholder="Ornek: 258 Datca Marina acilis hazirligi"
              />
            </div>

            <div>
              <label className="mb-1.5 block text-[11px] font-semibold" style={{ color: c.muted }}>
                Aciklama
              </label>
              <textarea
                rows={4}
                value={formData.content}
                onChange={(event) => setFormData((current) => ({ ...current, content: event.target.value }))}
                className="w-full resize-none rounded-xl px-3 py-2.5 text-sm outline-none"
                style={{ background: c.card, border: `1px solid ${c.border}`, color: c.text }}
                placeholder="Kisa not, sorumlu kisi veya beklenen aksiyon..."
              />
            </div>

            <div className="grid grid-cols-2 gap-2">
              <select
                value={formData.status}
                onChange={(event) => setFormData((current) => ({ ...current, status: event.target.value as AgendaItem["status"] }))}
                className="rounded-xl px-3 py-2.5 text-sm outline-none"
                style={{ background: c.card, border: `1px solid ${c.border}`, color: c.text }}
              >
                {STATUS_OPTIONS.map((status) => (
                  <option key={status} value={status}>{status}</option>
                ))}
              </select>

              <select
                value={formData.priority}
                onChange={(event) => setFormData((current) => ({ ...current, priority: event.target.value as AgendaItem["priority"] }))}
                className="rounded-xl px-3 py-2.5 text-sm outline-none"
                style={{ background: c.card, border: `1px solid ${c.border}`, color: c.text }}
              >
                {PRIORITY_OPTIONS.map((priority) => (
                  <option key={priority} value={priority}>{priority}</option>
                ))}
              </select>

              <select
                value={formData.category}
                onChange={(event) => setFormData((current) => ({ ...current, category: event.target.value as AgendaItem["category"] }))}
                className="rounded-xl px-3 py-2.5 text-sm outline-none"
                style={{ background: c.card, border: `1px solid ${c.border}`, color: c.text }}
              >
                {CATEGORY_OPTIONS.map((category) => (
                  <option key={category} value={category}>{category}</option>
                ))}
              </select>

              <input
                type="date"
                value={formData.dueDate}
                onChange={(event) => setFormData((current) => ({ ...current, dueDate: event.target.value }))}
                className="rounded-xl px-3 py-2.5 text-sm outline-none"
                style={{ background: c.card, border: `1px solid ${c.border}`, color: c.text }}
              />
            </div>

            <div className="flex items-center justify-between gap-2 pt-1">
              {editingItem ? (
                <button
                  type="button"
                  onClick={resetForm}
                  className="rounded-lg px-3 py-2 text-[11px] font-semibold"
                  style={{ color: c.muted }}
                >
                  Yeni Kayit
                </button>
              ) : <span />}

              <button
                type="submit"
                disabled={isSaving}
                className="inline-flex items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold"
                style={{ background: c.amberSoft, color: c.amber, border: `1px solid ${c.border}` }}
              >
                <Save className="h-4 w-4" />
                {isSaving ? "Kaydediliyor..." : editingItem ? "Guncelle" : "Kaydet"}
              </button>
            </div>
          </form>

          <div className="min-w-0">
            <div className="mb-3 flex items-center justify-between gap-3">
              <div>
                <div className="text-sm font-semibold" style={{ color: c.text }}>Baslik Listesi</div>
                <div className="text-[11px]" style={{ color: c.muted }}>{items.length} kayit</div>
              </div>

              <div className="relative w-full max-w-xs">
                <Search className="absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2" style={{ color: c.subtle }} />
                <input
                  type="text"
                  value={searchTerm}
                  onChange={(event) => setSearchTerm(event.target.value)}
                  placeholder="Basliklarda ara..."
                  className="w-full rounded-xl py-2 pl-9 pr-3 text-sm outline-none"
                  style={{ background: c.card, border: `1px solid ${c.border}`, color: c.text }}
                />
              </div>
            </div>

            {filteredItems.length === 0 ? (
              <div className="rounded-2xl border border-dashed px-5 py-12 text-center" style={{ borderColor: c.border, background: c.cardAlt }}>
                <Megaphone className="mx-auto h-8 w-8" style={{ color: c.subtle }} />
                <div className="mt-3 text-sm font-semibold" style={{ color: c.text }}>Listelenecek baslik yok</div>
                <div className="mt-1 text-[11px]" style={{ color: c.muted }}>Aramayi temizleyin veya yeni bir gundem konusu ekleyin.</div>
              </div>
            ) : (
              <div className="space-y-3 max-h-[60vh] overflow-y-auto pr-1">
                {filteredItems.map((item) => {
                  const statusStyle = getStatusStyle(item.status, c);
                  const priorityStyle = getPriorityStyle(item.priority, c);

                  return (
                    <div
                      key={item.id}
                      className="rounded-2xl border p-4"
                      style={{ background: c.cardAlt, borderColor: editingItem?.id === item.id ? c.amber : c.border }}
                    >
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <div className="text-[14px] font-semibold" style={{ color: c.text }}>
                            {item.title}
                          </div>
                          <div className="mt-2 flex flex-wrap gap-2">
                            <span
                              className="rounded-full px-2 py-0.5 text-[9px] font-bold uppercase"
                              style={{ background: statusStyle.background, color: statusStyle.color }}
                            >
                              {item.status}
                            </span>
                            <span
                              className="rounded-full px-2 py-0.5 text-[9px] font-bold uppercase"
                              style={{ background: priorityStyle.background, color: priorityStyle.color }}
                            >
                              {item.priority}
                            </span>
                            <span className="rounded-full px-2 py-0.5 text-[9px] font-bold uppercase" style={{ background: c.track, color: c.muted }}>
                              {item.category}
                            </span>
                          </div>
                        </div>

                        <div className="flex items-center gap-1">
                          <button
                            type="button"
                            onClick={() => handleEdit(item)}
                            className="rounded-lg p-2 transition-colors"
                            style={{ background: c.track, color: c.sky }}
                            title="Duzenle"
                          >
                            <Edit className="h-4 w-4" />
                          </button>
                          <button
                            type="button"
                            onClick={() => handleDelete(item.id)}
                            className="rounded-lg p-2 transition-colors"
                            style={{ background: c.roseSoft, color: c.rose }}
                            title="Sil"
                          >
                            <Trash2 className="h-4 w-4" />
                          </button>
                        </div>
                      </div>

                      {item.content && (
                        <p className="mt-3 whitespace-pre-wrap text-[12px] leading-5" style={{ color: c.muted }}>
                          {item.content}
                        </p>
                      )}

                      <div className="mt-3 flex flex-wrap items-center gap-3 text-[10px]" style={{ color: c.subtle }}>
                        <span className="inline-flex items-center gap-1">
                          <CalendarClock className="h-3.5 w-3.5" />
                          {formatAgendaDate(item.dueDate)}
                        </span>
                        <span>{item.createdBy}</span>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        </div>
      </Modal>
    </>
  );
}
