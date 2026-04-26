import React, { useEffect, useState } from "react";
import { Edit, Globe2, Lock, Plus, Save, Search, StickyNote, Trash2, UserRound, X } from "lucide-react";
import { apiClient } from "../lib/apiClient";
import { useAuth } from "../contexts/AuthContext";

export interface Note {
    id: string;
    ownerUsername: string;
    title: string;
    content: string;
    isShared: boolean;
    createdAt: string;
    updatedAt: string;
}

type NoteFormData = {
    title: string;
    content: string;
    isShared: boolean;
};

const emptyForm: NoteFormData = {
    title: "",
    content: "",
    isShared: false,
};

export default function NotesPage() {
    const { username } = useAuth();
    const [notes, setNotes] = useState<Note[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState("");
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingNote, setEditingNote] = useState<Note | null>(null);
    const [formData, setFormData] = useState<NoteFormData>(emptyForm);

    useEffect(() => {
        fetchNotes();
    }, []);

    const fetchNotes = async () => {
        try {
            const response = await apiClient.get<Note[]>("/api/notes");
            setNotes(response);
        } catch (error) {
            console.error("Error fetching notes:", error);
        } finally {
            setLoading(false);
        }
    };

    const filteredNotes = notes.filter(
        (note) =>
            note.title.toLowerCase().includes(searchTerm.toLowerCase()) ||
            note.content.toLowerCase().includes(searchTerm.toLowerCase())
    );

    const currentUsername = username.trim().toLowerCase();

    const handleAddNew = () => {
        setEditingNote(null);
        setFormData(emptyForm);
        setIsModalOpen(true);
    };

    const handleEdit = (note: Note) => {
        setEditingNote(note);
        setFormData({
            title: note.title,
            content: note.content,
            isShared: note.isShared,
        });
        setIsModalOpen(true);
    };

    const handleCloseModal = () => {
        setIsModalOpen(false);
        setEditingNote(null);
        setFormData(emptyForm);
    };

    const handleDelete = async (id: string) => {
        if (!window.confirm("Bu notu silmek istediğinize emin misiniz?")) return;

        try {
            await apiClient.delete(`/api/notes/${id}`);
            setNotes((current) => current.filter((note) => note.id !== id));
        } catch (error) {
            console.error("Error deleting note:", error);
        }
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();

        try {
            if (editingNote) {
                const updatedNote = await apiClient.put<Note>(`/api/notes/${editingNote.id}`, formData);
                setNotes((current) =>
                    current.map((note) => (note.id === editingNote.id ? updatedNote : note))
                );
            } else {
                const newNote = await apiClient.post<Note>("/api/notes", formData);
                setNotes((current) => [newNote, ...current]);
            }

            handleCloseModal();
        } catch (error) {
            console.error("Error saving note:", error);
            alert("Not kaydedilirken bir hata oluştu. Lütfen tekrar deneyin.");
        }
    };

    return (
        <div className="mx-auto max-w-7xl p-6 text-slate-200">
            <div className="mb-8 flex flex-col items-center justify-between gap-4 md:flex-row">
                <div>
                    <h1 className="bg-gradient-to-r from-blue-400 to-cyan-300 bg-clip-text text-3xl font-bold text-transparent">
                        Notlar
                    </h1>
                    <p className="mt-1 text-slate-400">
                        Kişisel notlarını yönet, istersen ekip ile paylaş.
                    </p>
                </div>

                <div className="flex items-center gap-3">
                    <div className="relative">
                        <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                        <input
                            type="text"
                            placeholder="Notlarda ara..."
                            value={searchTerm}
                            onChange={(e) => setSearchTerm(e.target.value)}
                            className="w-64 rounded-lg border border-slate-700/50 bg-slate-800/50 py-2 pl-10 pr-4 text-sm transition-all focus:outline-none focus:ring-2 focus:ring-cyan-500/50"
                        />
                    </div>

                    <button
                        onClick={handleAddNew}
                        className="flex items-center gap-2 rounded-lg bg-gradient-to-r from-blue-600 to-cyan-600 px-4 py-2 font-medium text-white shadow-lg shadow-blue-900/20 transition-all hover:from-blue-500 hover:to-cyan-500"
                    >
                        <Plus className="h-4 w-4" />
                        Yeni Not
                    </button>
                </div>
            </div>

            {loading ? (
                <div className="flex h-64 items-center justify-center">
                    <div className="h-8 w-8 animate-spin rounded-full border-b-2 border-cyan-500"></div>
                </div>
            ) : filteredNotes.length === 0 ? (
                <div className="rounded-2xl border border-dashed border-slate-700/30 bg-slate-800/30 py-20 text-center">
                    <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-slate-800/50 p-4">
                        <StickyNote className="h-8 w-8 text-slate-500" />
                    </div>
                    <p className="text-lg text-slate-400">Henüz görüntülenecek not yok.</p>
                    <button onClick={handleAddNew} className="mt-2 font-medium text-cyan-400 hover:text-cyan-300">
                        İlk notunu oluştur
                    </button>
                </div>
            ) : (
                <div className="grid grid-cols-1 gap-6 md:grid-cols-2 lg:grid-cols-3">
                    {filteredNotes.map((note) => {
                        const ownerUsername = (note.ownerUsername || "").trim().toLowerCase();
                        const isOwner = ownerUsername === currentUsername;

                        return (
                            <div
                                key={note.id}
                                className="group rounded-xl border border-slate-700/50 bg-slate-800/40 p-5 shadow-lg shadow-black/20 transition-all duration-300 hover:-translate-y-1 hover:border-cyan-500/30 hover:bg-slate-800/60"
                            >
                                <div className="mb-3 flex items-start justify-between gap-3">
                                    <div className="min-w-0">
                                        <h3 className="line-clamp-1 text-lg font-semibold text-slate-200 transition-colors group-hover:text-cyan-300">
                                            {note.title}
                                        </h3>

                                        <div className="mt-2 flex flex-wrap items-center gap-2 text-xs">
                                            <span
                                                className={`inline-flex items-center gap-1 rounded-full px-2.5 py-1 font-medium ${
                                                    note.isShared
                                                        ? "bg-emerald-500/10 text-emerald-300 ring-1 ring-emerald-500/20"
                                                        : "bg-slate-700/70 text-slate-300 ring-1 ring-slate-600/60"
                                                }`}
                                            >
                                                {note.isShared ? <Globe2 className="h-3.5 w-3.5" /> : <Lock className="h-3.5 w-3.5" />}
                                                {note.isShared ? "Paylaşılsın" : "Sadece ben"}
                                            </span>

                                            <span className="inline-flex items-center gap-1 rounded-full bg-cyan-500/10 px-2.5 py-1 font-medium text-cyan-300 ring-1 ring-cyan-500/20">
                                                <UserRound className="h-3.5 w-3.5" />
                                                {isOwner ? "Benim notum" : note.ownerUsername}
                                            </span>
                                        </div>
                                    </div>

                                    {isOwner && (
                                        <div className="flex gap-1">
                                            <button
                                                onClick={() => handleEdit(note)}
                                                className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-700 hover:text-cyan-400"
                                                title="Düzenle"
                                            >
                                                <Edit className="h-4 w-4" />
                                            </button>
                                            <button
                                                onClick={() => handleDelete(note.id)}
                                                className="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-slate-700 hover:text-red-400"
                                                title="Sil"
                                            >
                                                <Trash2 className="h-4 w-4" />
                                            </button>
                                        </div>
                                    )}
                                </div>

                                <p className="mb-4 h-20 whitespace-pre-wrap text-sm text-slate-400 line-clamp-4">
                                    {note.content}
                                </p>

                                <div className="flex items-center justify-between border-t border-slate-700/50 pt-3 text-xs text-slate-500">
                                    <span>{new Date(note.updatedAt).toLocaleDateString("tr-TR")}</span>
                                    <span>
                                        {new Date(note.updatedAt).toLocaleTimeString("tr-TR", {
                                            hour: "2-digit",
                                            minute: "2-digit",
                                        })}
                                    </span>
                                </div>
                            </div>
                        );
                    })}
                </div>
            )}

            {isModalOpen && (
                <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-sm">
                    <div className="relative w-full max-w-lg rounded-2xl border border-slate-700 bg-slate-900 p-6 shadow-2xl">
                        <button
                            onClick={handleCloseModal}
                            className="absolute right-4 top-4 text-slate-400 transition-colors hover:text-white"
                        >
                            <X className="h-5 w-5" />
                        </button>

                        <h2 className="mb-6 flex items-center gap-2 text-xl font-bold">
                            {editingNote ? <Edit className="h-5 w-5 text-cyan-400" /> : <Plus className="h-5 w-5 text-cyan-400" />}
                            {editingNote ? "Notu Düzenle" : "Yeni Not Ekle"}
                        </h2>

                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div>
                                <label className="mb-1 block text-sm font-medium text-slate-400">
                                    Başlık
                                </label>
                                <input
                                    type="text"
                                    required
                                    value={formData.title}
                                    onChange={(e) => setFormData((current) => ({ ...current, title: e.target.value }))}
                                    className="w-full rounded-lg border border-slate-700 bg-slate-800 px-4 py-2.5 text-slate-200 transition-all placeholder-slate-600 focus:outline-none focus:ring-2 focus:ring-cyan-500/50"
                                    placeholder="Not başlığı..."
                                />
                            </div>

                            <div>
                                <label className="mb-1 block text-sm font-medium text-slate-400">
                                    İçerik
                                </label>
                                <textarea
                                    rows={6}
                                    value={formData.content}
                                    onChange={(e) => setFormData((current) => ({ ...current, content: e.target.value }))}
                                    className="w-full resize-none rounded-lg border border-slate-700 bg-slate-800 px-4 py-2.5 text-slate-200 transition-all placeholder-slate-600 focus:outline-none focus:ring-2 focus:ring-cyan-500/50"
                                    placeholder="Notunu buraya yaz..."
                                />
                            </div>

                            <div>
                                <label className="mb-2 block text-sm font-medium text-slate-400">
                                    Görünürlük
                                </label>
                                <div className="grid gap-3 sm:grid-cols-2">
                                    <button
                                        type="button"
                                        onClick={() => setFormData((current) => ({ ...current, isShared: true }))}
                                        className={`rounded-xl border px-4 py-3 text-left transition-all ${
                                            formData.isShared
                                                ? "border-emerald-400/50 bg-emerald-500/10 text-emerald-200"
                                                : "border-slate-700 bg-slate-800/80 text-slate-300 hover:border-slate-600"
                                        }`}
                                    >
                                        <span className="mb-1 inline-flex items-center gap-2 font-medium">
                                            <Globe2 className="h-4 w-4" />
                                            Paylaşılsın
                                        </span>
                                        <p className="text-xs text-inherit/80">
                                            Bu not diğer kullanıcıların not ekranında da görünür.
                                        </p>
                                    </button>

                                    <button
                                        type="button"
                                        onClick={() => setFormData((current) => ({ ...current, isShared: false }))}
                                        className={`rounded-xl border px-4 py-3 text-left transition-all ${
                                            !formData.isShared
                                                ? "border-cyan-400/50 bg-cyan-500/10 text-cyan-100"
                                                : "border-slate-700 bg-slate-800/80 text-slate-300 hover:border-slate-600"
                                        }`}
                                    >
                                        <span className="mb-1 inline-flex items-center gap-2 font-medium">
                                            <Lock className="h-4 w-4" />
                                            Sadece ben
                                        </span>
                                        <p className="text-xs text-inherit/80">
                                            Bu not yalnızca kendi hesabında görünür.
                                        </p>
                                    </button>
                                </div>
                            </div>

                            <div className="flex justify-end gap-3 pt-2">
                                <button
                                    type="button"
                                    onClick={handleCloseModal}
                                    className="rounded-lg px-4 py-2 text-slate-400 transition-colors hover:bg-slate-800 hover:text-white"
                                >
                                    İptal
                                </button>
                                <button
                                    type="submit"
                                    className="flex items-center gap-2 rounded-lg bg-gradient-to-r from-blue-600 to-cyan-600 px-6 py-2 font-medium text-white shadow-lg shadow-blue-900/20 transition-all hover:from-blue-500 hover:to-cyan-500"
                                >
                                    <Save className="h-4 w-4" />
                                    Kaydet
                                </button>
                            </div>
                        </form>
                    </div>
                </div>
            )}
        </div>
    );
}
