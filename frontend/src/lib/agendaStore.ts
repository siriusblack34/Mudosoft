import { apiClient } from './apiClient';

export type AgendaStatus = 'Takipte' | 'Planlandi' | 'Tamamlandi';
export type AgendaPriority = 'Dusuk' | 'Orta' | 'Yuksek';
export type AgendaCategory = 'Duyuru' | 'Altyapi' | 'Guvenlik' | 'Magaza Talebi' | 'Bakim' | 'Proje';

export interface AgendaItem {
  id: string;
  title: string;
  content: string;
  status: AgendaStatus;
  priority: AgendaPriority;
  category: AgendaCategory;
  dueDate: string;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
}

export type AgendaFormData = {
  title: string;
  content: string;
  status: AgendaStatus;
  priority: AgendaPriority;
  category: AgendaCategory;
  dueDate: string;
};

export const AGENDA_UPDATED_EVENT = 'ms-agenda-updated';

export const STATUS_OPTIONS: AgendaStatus[] = ['Takipte', 'Planlandi', 'Tamamlandi'];
export const PRIORITY_OPTIONS: AgendaPriority[] = ['Yuksek', 'Orta', 'Dusuk'];
export const CATEGORY_OPTIONS: AgendaCategory[] = ['Duyuru', 'Altyapi', 'Guvenlik', 'Magaza Talebi', 'Bakim', 'Proje'];

export const EMPTY_AGENDA_FORM: AgendaFormData = {
  title: '',
  content: '',
  status: 'Takipte',
  priority: 'Orta',
  category: 'Duyuru',
  dueDate: '',
};

interface AgendaApiItem {
  id: string;
  title: string;
  content: string;
  status: AgendaStatus;
  priority: AgendaPriority;
  category: AgendaCategory;
  dueDate: string | null;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
}

interface AgendaApiRequest {
  title: string;
  content: string;
  status: AgendaStatus;
  priority: AgendaPriority;
  category: AgendaCategory;
  dueDate: string | null;
}

function normalizeDueDate(value: string | null) {
  if (!value) return '';
  return value.slice(0, 10);
}

function toAgendaItem(item: AgendaApiItem): AgendaItem {
  return {
    ...item,
    dueDate: normalizeDueDate(item.dueDate),
  };
}

function toAgendaRequest(formData: AgendaFormData): AgendaApiRequest {
  return {
    title: formData.title,
    content: formData.content,
    status: formData.status,
    priority: formData.priority,
    category: formData.category,
    dueDate: formData.dueDate || null,
  };
}

export function notifyAgendaUpdated() {
  if (typeof window !== 'undefined') {
    window.dispatchEvent(new CustomEvent(AGENDA_UPDATED_EVENT));
  }
}

export async function fetchAgendaItems(): Promise<AgendaItem[]> {
  const items = await apiClient.get<AgendaApiItem[]>('/api/agenda');
  return sortAgendaItems(items.map(toAgendaItem));
}

export async function createAgendaItem(formData: AgendaFormData): Promise<AgendaItem> {
  const item = await apiClient.post<AgendaApiItem>('/api/agenda', toAgendaRequest(formData));
  notifyAgendaUpdated();
  return toAgendaItem(item);
}

export async function updateAgendaItem(id: string, formData: AgendaFormData): Promise<AgendaItem> {
  const item = await apiClient.put<AgendaApiItem>(`/api/agenda/${encodeURIComponent(id)}`, toAgendaRequest(formData));
  notifyAgendaUpdated();
  return toAgendaItem(item);
}

export async function deleteAgendaItem(id: string): Promise<void> {
  await apiClient.delete<void>(`/api/agenda/${encodeURIComponent(id)}`);
  notifyAgendaUpdated();
}

export function sortAgendaItems(items: AgendaItem[]) {
  const statusOrder: Record<AgendaStatus, number> = {
    Takipte: 0,
    Planlandi: 1,
    Tamamlandi: 2,
  };

  const priorityOrder: Record<AgendaPriority, number> = {
    Yuksek: 0,
    Orta: 1,
    Dusuk: 2,
  };

  return [...items].sort((a, b) => {
    const statusCompare = statusOrder[a.status] - statusOrder[b.status];
    if (statusCompare !== 0) return statusCompare;

    const priorityCompare = priorityOrder[a.priority] - priorityOrder[b.priority];
    if (priorityCompare !== 0) return priorityCompare;

    if (a.dueDate && b.dueDate) {
      const dueCompare = new Date(a.dueDate).getTime() - new Date(b.dueDate).getTime();
      if (dueCompare !== 0) return dueCompare;
    }

    if (a.dueDate && !b.dueDate) return -1;
    if (!a.dueDate && b.dueDate) return 1;

    return new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime();
  });
}
