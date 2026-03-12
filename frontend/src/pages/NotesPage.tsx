import React, { useState, useEffect } from "react";
import { Plus, Search, Trash2, Edit, Save, X, StickyNote } from "lucide-react";
import { apiClient } from "../lib/apiClient";

export interface Note {
    id: string;
    title: string;
    content: string;
    createdAt: string;
    updatedAt: string;
}

export default function NotesPage() {
    const [notes, setNotes] = useState<Note[]>([]);
    const [loading, setLoading] = useState(true);
    const [searchTerm, setSearchTerm] = useState("");
    const [isModalOpen, setIsModalOpen] = useState(false);
    const [editingNote, setEditingNote] = useState<Note | null>(null);
    const [formData, setFormData] = useState({ title: "", content: "" });

    useEffect(() => {
        fetchNotes();
    }, []);

    const fetchNotes = async () => {
        try {
            const notes = await apiClient.get<Note[]>("/api/notes");
            setNotes(notes);
        } catch (error) {
            console.error("Error fetching notes:", error);
        } finally {
            setLoading(false);
        }
    };

    const handleSearch = (e: React.ChangeEvent<HTMLInputElement>) => {
        setSearchTerm(e.target.value);
    };

    const filteredNotes = notes.filter(
        (note) =>
            note.title.toLowerCase().includes(searchTerm.toLowerCase()) ||
            note.content.toLowerCase().includes(searchTerm.toLowerCase())
    );

    const handleAddNew = () => {
        setEditingNote(null);
        setFormData({ title: "", content: "" });
        setIsModalOpen(true);
    };

    const handleEdit = (note: Note) => {
        setEditingNote(note);
        setFormData({ title: note.title, content: note.content });
        setIsModalOpen(true);
    };

    const handleDelete = async (id: string) => {
        if (!window.confirm("Bu notu silmek istediğinize emin misiniz?")) return;

        try {
            await apiClient.delete(`/api/notes/${id}`);
            setNotes(notes.filter((n) => n.id !== id));
        } catch (error) {
            console.error("Error deleting note:", error);
        }
    };

    const handleSubmit = async (e: React.FormEvent) => {
        e.preventDefault();
        try {
            if (editingNote) {
                // 🔧 FIX: ID'yi de gönderiyoruz (Backend düzeltilse de garanti olsun)
                await apiClient.put(`/api/notes/${editingNote.id}`, { ...formData, id: editingNote.id });
                setNotes(
                    notes.map((n) =>
                        n.id === editingNote.id ? { ...n, ...formData, updatedAt: new Date().toISOString() } : n
                    )
                );
            } else {
                const newNote = await apiClient.post<Note>("/api/notes", formData);
                setNotes([newNote, ...notes]);
            }
            setIsModalOpen(false);
        } catch (error) {
            console.error("Error saving note:", error);
            alert("Not kaydedilirken bir hata oluştu! Lütfen tekrar deneyin.");
        }
    };

    return (
        <div className="p-6 max-w-7xl mx-auto text-slate-200">
            <div className="flex flex-col md:flex-row justify-between items-center mb-8 gap-4">
                <div>
                    <h1 className="text-3xl font-bold bg-gradient-to-r from-blue-400 to-cyan-300 bg-clip-text text-transparent">
                        Notlar
                    </h1>
                    <p className="text-slate-400 mt-1">
                        Kişisel notlarınızı ve hatırlatmalarınızı yönetin
                    </p>
                </div>
                <div className="flex items-center gap-3">
                    <div className="relative">
                        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" />
                        <input
                            type="text"
                            placeholder="Notlarda ara..."
                            value={searchTerm}
                            onChange={handleSearch}
                            className="pl-10 pr-4 py-2 bg-slate-800/50 border border-slate-700/50 rounded-lg focus:outline-none focus:ring-2 focus:ring-cyan-500/50 text-sm w-64 transition-all"
                        />
                    </div>
                    <button
                        onClick={handleAddNew}
                        className="flex items-center gap-2 px-4 py-2 bg-gradient-to-r from-blue-600 to-cyan-600 hover:from-blue-500 hover:to-cyan-500 text-white rounded-lg transition-all shadow-lg shadow-blue-900/20 font-medium"
                    >
                        <Plus className="w-4 h-4" />
                        Yeni Not
                    </button>
                </div>
            </div>

            {loading ? (
                <div className="flex justify-center items-center h-64">
                    <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-cyan-500"></div>
                </div>
            ) : filteredNotes.length === 0 ? (
                <div className="text-center py-20 bg-slate-800/30 rounded-2xl border border-slate-700/30 border-dashed">
                    <div className="bg-slate-800/50 p-4 rounded-full w-16 h-16 mx-auto mb-4 flex items-center justify-center">
                        <StickyNote className="w-8 h-8 text-slate-500" />
                    </div>
                    <p className="text-slate-400 text-lg">Henüz hiç notunuz yok.</p>
                    <button onClick={handleAddNew} className="text-cyan-400 hover:text-cyan-300 mt-2 font-medium">
                        İlk notunuzu oluşturun
                    </button>
                </div>
            ) : (
                <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                    {filteredNotes.map((note) => (
                        <div
                            key={note.id}
                            className="group bg-slate-800/40 backdrop-blur-sm border border-slate-700/50 hover:border-cyan-500/30 rounded-xl p-5 hover:bg-slate-800/60 transition-all duration-300 hover:-translate-y-1 shadow-lg shadow-black/20"
                        >
                            <div className="flex justify-between items-start mb-3">
                                <h3 className="font-semibold text-lg text-slate-200 line-clamp-1 group-hover:text-cyan-300 transition-colors">
                                    {note.title}
                                </h3>
                                <div className="flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                                    <button
                                        onClick={() => handleEdit(note)}
                                        className="p-1.5 hover:bg-slate-700 rounded-lg text-slate-400 hover:text-cyan-400 transition-colors"
                                    >
                                        <Edit className="w-4 h-4" />
                                    </button>
                                    <button
                                        onClick={() => handleDelete(note.id)}
                                        className="p-1.5 hover:bg-slate-700 rounded-lg text-slate-400 hover:text-red-400 transition-colors"
                                    >
                                        <Trash2 className="w-4 h-4" />
                                    </button>
                                </div>
                            </div>
                            <p className="text-slate-400 text-sm mb-4 line-clamp-4 whitespace-pre-wrap h-20">
                                {note.content}
                            </p>
                            <div className="flex justify-between items-center text-xs text-slate-500 pt-3 border-t border-slate-700/50">
                                <span>{new Date(note.updatedAt).toLocaleDateString("tr-TR")}</span>
                                <span>{new Date(note.updatedAt).toLocaleTimeString("tr-TR", { hour: '2-digit', minute: '2-digit' })}</span>
                            </div>
                        </div>
                    ))}
                </div>
            )}

            {isModalOpen && (
                <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4 z-50">
                    <div className="bg-slate-900 border border-slate-700 rounded-2xl w-full max-w-lg shadow-2xl p-6 relative animate-in fade-in zoom-in duration-200">
                        <button
                            onClick={() => setIsModalOpen(false)}
                            className="absolute right-4 top-4 text-slate-400 hover:text-white transition-colors"
                        >
                            <X className="w-5 h-5" />
                        </button>

                        <h2 className="text-xl font-bold mb-6 flex items-center gap-2">
                            {editingNote ? <Edit className="w-5 h-5 text-cyan-400" /> : <Plus className="w-5 h-5 text-cyan-400" />}
                            {editingNote ? "Notu Düzenle" : "Yeni Not Ekle"}
                        </h2>

                        <form onSubmit={handleSubmit} className="space-y-4">
                            <div>
                                <label className="block text-sm font-medium text-slate-400 mb-1">
                                    Başlık
                                </label>
                                <input
                                    type="text"
                                    required
                                    value={formData.title}
                                    onChange={(e) => setFormData({ ...formData, title: e.target.value })}
                                    className="w-full bg-slate-800 border border-slate-700 rounded-lg px-4 py-2.5 focus:outline-none focus:ring-2 focus:ring-cyan-500/50 transition-all text-slate-200 placeholder-slate-600"
                                    placeholder="Not başlığı..."
                                />
                            </div>
                            <div>
                                <label className="block text-sm font-medium text-slate-400 mb-1">
                                    İçerik
                                </label>
                                <textarea
                                    rows={6}
                                    value={formData.content}
                                    onChange={(e) => setFormData({ ...formData, content: e.target.value })}
                                    className="w-full bg-slate-800 border border-slate-700 rounded-lg px-4 py-2.5 focus:outline-none focus:ring-2 focus:ring-cyan-500/50 transition-all text-slate-200 placeholder-slate-600 resize-none"
                                    placeholder="Notunuzu buraya yazın..."
                                />
                            </div>
                            <div className="flex justify-end gap-3 pt-2">
                                <button
                                    type="button"
                                    onClick={() => setIsModalOpen(false)}
                                    className="px-4 py-2 text-slate-400 hover:text-white hover:bg-slate-800 rounded-lg transition-colors"
                                >
                                    İptal
                                </button>
                                <button
                                    type="submit"
                                    className="px-6 py-2 bg-gradient-to-r from-blue-600 to-cyan-600 hover:from-blue-500 hover:to-cyan-500 text-white rounded-lg shadow-lg shadow-blue-900/20 font-medium transition-all flex items-center gap-2"
                                >
                                    <Save className="w-4 h-4" />
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
