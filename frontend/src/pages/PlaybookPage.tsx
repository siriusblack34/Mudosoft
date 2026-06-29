import { useEffect, useState } from 'react';
import { apiClient } from '../lib/apiClient';
import {
  Zap, Plus, Trash2, Play, Power, ChevronDown, ChevronUp,
  Clock, CheckCircle, XCircle, AlertTriangle, AlertOctagon,
  Server, WifiOff, Cpu, HardDrive, Layers, Activity,
  RotateCcw, Terminal, Mail, X,
} from 'lucide-react';

interface PlaybookListItem {
  id: number;
  name: string;
  description?: string;
  triggerType: string;
  isEnabled: boolean;
  createdBy: string;
  createdAt: string;
  updatedAt: string;
  stepCount: number;
  lastExecutionAt?: string;
  lastExecutionStatus?: string;
}

interface PlaybookStep {
  id?: number;
  stepOrder: number;
  actionType: string;
  actionPayloadJson?: string;
  delaySeconds: number;
  description?: string;
}

interface PlaybookExecution {
  id: number;
  hostname?: string;
  storeCode?: string;
  status: string;
  startedAt: string;
  completedAt?: string;
  resultSummary?: string;
  triggerReason: string;
}

interface PlaybookDetail {
  id: number;
  name: string;
  description?: string;
  triggerType: string;
  triggerConditionJson?: string;
  isEnabled: boolean;
  createdBy: string;
  steps: PlaybookStep[];
  recentExecutions: PlaybookExecution[];
}

interface FieldDef {
  key: string;
  label: string;
  type: 'text' | 'number';
  placeholder?: string;
  hint?: string;
  min?: number;
  max?: number;
}

interface TriggerDef {
  label: string;
  shortLabel: string;
  icon: React.ComponentType<{ className?: string }>;
  color: string;
  ringColor: string;
  selBg: string;
  description: string;
  fields: FieldDef[];
  defaultCondition: string;
}

interface ActionDef {
  label: string;
  icon: React.ComponentType<{ className?: string }>;
  color: string;
  description: string;
  fields: FieldDef[];
  defaultPayload: string;
}

const TRIGGERS: Record<string, TriggerDef> = {
  ServiceDown: {
    label: 'Servis Çöktüğünde',
    shortLabel: 'Servis Çöktü',
    icon: Server,
    color: 'text-red-500',
    ringColor: 'ring-red-400',
    selBg: 'bg-red-50 dark:bg-red-900/20 border-red-400 dark:border-red-600',
    description: 'İzlenen Windows servisi durduğunda tetiklenir (5 dk\'da bir kontrol)',
    fields: [{ key: 'serviceName', label: 'Servis Adı', type: 'text', placeholder: 'MSSQL$SQLEXPRESS', hint: 'Boş bırakılırsa tüm kritik servisler izlenir' }],
    defaultCondition: '{"serviceName": "Genius3"}',
  },
  DeviceOffline: {
    label: 'Cihaz Offline Olduğunda',
    shortLabel: 'Cihaz Offline',
    icon: WifiOff,
    color: 'text-slate-400',
    ringColor: 'ring-slate-400',
    selBg: 'bg-slate-100 dark:bg-slate-700/60 border-slate-400',
    description: 'Agent çalıştıran bir cihaz offline görünürse tetiklenir',
    fields: [],
    defaultCondition: '{}',
  },
  CpuHigh: {
    label: 'CPU Yüksek Olduğunda',
    shortLabel: 'CPU Yüksek',
    icon: Cpu,
    color: 'text-orange-500',
    ringColor: 'ring-orange-400',
    selBg: 'bg-orange-50 dark:bg-orange-900/20 border-orange-400',
    description: 'CPU kullanımı belirtilen eşiği aşarsa tetiklenir',
    fields: [{ key: 'threshold', label: 'Eşik (%)', type: 'number', placeholder: '90', min: 1, max: 100 }],
    defaultCondition: '{"threshold": 90}',
  },
  DiskFull: {
    label: 'Disk Dolduğunda',
    shortLabel: 'Disk Doldu',
    icon: HardDrive,
    color: 'text-amber-500',
    ringColor: 'ring-amber-400',
    selBg: 'bg-amber-50 dark:bg-amber-900/20 border-amber-400',
    description: 'Disk kullanımı belirtilen eşiği aşarsa tetiklenir',
    fields: [{ key: 'threshold', label: 'Eşik (%)', type: 'number', placeholder: '90', min: 1, max: 100 }],
    defaultCondition: '{"threshold": 90}',
  },
  MemoryHigh: {
    label: 'RAM Yüksek Olduğunda',
    shortLabel: 'RAM Yüksek',
    icon: Layers,
    color: 'text-purple-500',
    ringColor: 'ring-purple-400',
    selBg: 'bg-purple-50 dark:bg-purple-900/20 border-purple-400',
    description: 'RAM kullanımı belirtilen eşiği aşarsa tetiklenir',
    fields: [{ key: 'threshold', label: 'Eşik (%)', type: 'number', placeholder: '85', min: 1, max: 100 }],
    defaultCondition: '{"threshold": 85}',
  },
  HealthScoreLow: {
    label: 'Sağlık Skoru Düşük',
    shortLabel: 'Sağlık Düşük',
    icon: Activity,
    color: 'text-rose-500',
    ringColor: 'ring-rose-400',
    selBg: 'bg-rose-50 dark:bg-rose-900/20 border-rose-400',
    description: 'Cihaz sağlık skoru eşiğin altına düşerse tetiklenir',
    fields: [{ key: 'threshold', label: 'Eşik (0–100)', type: 'number', placeholder: '40', min: 0, max: 100 }],
    defaultCondition: '{"threshold": 40}',
  },
  AgentSilent: {
    label: 'Agent Yanıtsız',
    shortLabel: 'Agent Sessiz',
    icon: Clock,
    color: 'text-sky-500',
    ringColor: 'ring-sky-400',
    selBg: 'bg-sky-50 dark:bg-sky-900/20 border-sky-400',
    description: 'Online görünen cihaz belirtilen süre boyunca heartbeat göndermezse tetiklenir',
    fields: [{ key: 'minutesSilent', label: 'Sessizlik Süresi (dakika)', type: 'number', placeholder: '60', min: 5 }],
    defaultCondition: '{"minutesSilent": 60}',
  },
};

const ACTIONS: Record<string, ActionDef> = {
  RestartService: {
    label: 'Servisi Yeniden Başlat',
    icon: RotateCcw,
    color: 'text-sky-600',
    description: 'Windows servisini uzaktan yeniden başlatır (WMI/DCOM)',
    fields: [{ key: 'serviceName', label: 'Servis Adı', type: 'text', placeholder: 'MSSQL$SQLEXPRESS' }],
    defaultPayload: '{"serviceName": "Genius3"}',
  },
  KillProcess: {
    label: 'Process Sonlandır',
    icon: AlertOctagon,
    color: 'text-red-600',
    description: 'Belirtilen process\'i uzaktan zorla sonlandırır (taskkill /F)',
    fields: [{ key: 'processName', label: 'Process Adı (.exe)', type: 'text', placeholder: 'Genius3.exe' }],
    defaultPayload: '{"processName": "Genius3.exe"}',
  },
  RestartDevice: {
    label: 'Cihazı Yeniden Başlat',
    icon: Power,
    color: 'text-amber-600',
    description: 'Hedef cihazı WMI üzerinden uzaktan yeniden başlatır (shutdown /r)',
    fields: [],
    defaultPayload: '{}',
  },
  ClearTempFiles: {
    label: 'Temp Dosyaları Temizle',
    icon: Trash2,
    color: 'text-slate-500',
    description: 'Windows %TEMP% dizinindeki geçici dosyaları siler (disk alanı kazanımı)',
    fields: [],
    defaultPayload: '{}',
  },
  RunScript: {
    label: 'Script / Komut Çalıştır',
    icon: Terminal,
    color: 'text-green-600',
    description: 'Uzak cihazda WMI üzerinden komut satırı çalıştırır',
    fields: [{ key: 'script', label: 'Komut', type: 'text', placeholder: 'net stop Genius3 && net start Genius3' }],
    defaultPayload: '{"script": ""}',
  },
  SendAlert: {
    label: 'Uyarı E-postası Gönder',
    icon: Mail,
    color: 'text-indigo-600',
    description: 'Tüm Admin kullanıcılarına otomatik e-posta bildirimi gönderir',
    fields: [{ key: 'message', label: 'Mesaj', type: 'text', placeholder: 'Cihaz sorunu tespit edildi.' }],
    defaultPayload: '{"message": "Cihaz sorunu tespit edildi."}',
  },
  Wait: {
    label: 'Bekle',
    icon: Clock,
    color: 'text-slate-400',
    description: 'Bir sonraki adımdan önce belirtilen süre kadar bekler',
    fields: [{ key: 'seconds', label: 'Süre (saniye)', type: 'number', placeholder: '60', min: 1 }],
    defaultPayload: '{"seconds": 60}',
  },
};

const STATUS_ICON: Record<string, React.ReactNode> = {
  Success: <CheckCircle className="h-4 w-4 text-green-500" />,
  Failed: <XCircle className="h-4 w-4 text-red-500" />,
  Partial: <AlertTriangle className="h-4 w-4 text-amber-500" />,
  Running: <Clock className="h-4 w-4 text-sky-500 animate-spin" />,
};

function parseJson(json: string | undefined): Record<string, unknown> {
  if (!json) return {};
  try { return JSON.parse(json) as Record<string, unknown>; }
  catch { return {}; }
}

function makeEmptyForm() {
  return {
    name: '',
    description: '',
    triggerType: 'ServiceDown',
    triggerConditionJson: TRIGGERS.ServiceDown.defaultCondition,
    isEnabled: true,
    steps: [{
      stepOrder: 1,
      actionType: 'RestartService',
      actionPayloadJson: ACTIONS.RestartService.defaultPayload,
      delaySeconds: 0,
      description: '',
    }] as PlaybookStep[],
  };
}

export default function PlaybookPage() {
  const [playbooks, setPlaybooks] = useState<PlaybookListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [expandedId, setExpandedId] = useState<number | null>(null);
  const [expandedDetail, setExpandedDetail] = useState<PlaybookDetail | null>(null);

  const [showForm, setShowForm] = useState(false);
  const [editId, setEditId] = useState<number | null>(null);
  const [form, setForm] = useState(makeEmptyForm());
  const [formLoading, setFormLoading] = useState(false);
  const [formError, setFormError] = useState('');

  useEffect(() => { loadPlaybooks(); }, []);

  async function loadPlaybooks() {
    setLoading(true);
    try {
      const data = await apiClient.get<PlaybookListItem[]>('/api/playbooks');
      setPlaybooks(data ?? []);
    } catch { /* ignore */ }
    setLoading(false);
  }

  async function loadDetail(id: number) {
    if (expandedId === id) { setExpandedId(null); setExpandedDetail(null); return; }
    setExpandedId(id);
    try {
      const data = await apiClient.get<PlaybookDetail>(`/api/playbooks/${id}`);
      setExpandedDetail(data);
    } catch { setExpandedDetail(null); }
  }

  async function toggleEnabled(id: number, e: React.MouseEvent) {
    e.stopPropagation();
    try {
      await apiClient.patch(`/api/playbooks/${id}/toggle`);
      await loadPlaybooks();
    } catch { /* ignore */ }
  }

  async function deletePlaybook(id: number, e: React.MouseEvent) {
    e.stopPropagation();
    if (!confirm('Bu playbook silinsin mi?')) return;
    try {
      await apiClient.delete(`/api/playbooks/${id}`);
      if (expandedId === id) { setExpandedId(null); setExpandedDetail(null); }
      await loadPlaybooks();
    } catch { alert('Silinemedi.'); }
  }

  async function runPlaybook(id: number, e: React.MouseEvent) {
    e.stopPropagation();
    if (!confirm('Bu playbook şimdi manuel olarak çalıştırılsın mı?')) return;
    try {
      await apiClient.post(`/api/playbooks/${id}/run`, {});
      alert('Playbook çalıştırılıyor. Birkaç saniye içinde tamamlanacak.');
      setTimeout(() => { if (expandedId === id) loadDetail(id); }, 3000);
    } catch { alert('Çalıştırılamadı.'); }
  }

  function openCreate() {
    setEditId(null);
    setForm(makeEmptyForm());
    setFormError('');
    setShowForm(true);
  }

  function openEdit(p: PlaybookListItem, e: React.MouseEvent) {
    e.stopPropagation();
    setEditId(p.id);
    apiClient.get<PlaybookDetail>(`/api/playbooks/${p.id}`).then(d => {
      setForm({
        name: d.name,
        description: d.description ?? '',
        triggerType: d.triggerType,
        triggerConditionJson: d.triggerConditionJson ?? '{}',
        isEnabled: d.isEnabled,
        steps: d.steps.map(s => ({ ...s })),
      });
    });
    setFormError('');
    setShowForm(true);
  }

  function selectTrigger(triggerType: string) {
    setForm(f => ({
      ...f,
      triggerType,
      triggerConditionJson: TRIGGERS[triggerType]?.defaultCondition ?? '{}',
    }));
  }

  function updateConditionField(key: string, value: string | number) {
    setForm(f => {
      const current = parseJson(f.triggerConditionJson);
      current[key] = value;
      return { ...f, triggerConditionJson: JSON.stringify(current) };
    });
  }

  function addStep() {
    setForm(f => ({
      ...f,
      steps: [...f.steps, {
        stepOrder: f.steps.length + 1,
        actionType: 'RestartService',
        actionPayloadJson: ACTIONS.RestartService.defaultPayload,
        delaySeconds: 0,
        description: '',
      }],
    }));
  }

  function removeStep(idx: number) {
    setForm(f => ({
      ...f,
      steps: f.steps.filter((_, i) => i !== idx).map((s, i) => ({ ...s, stepOrder: i + 1 })),
    }));
  }

  function updateStepAction(idx: number, actionType: string) {
    setForm(f => {
      const steps = [...f.steps];
      steps[idx] = { ...steps[idx], actionType, actionPayloadJson: ACTIONS[actionType]?.defaultPayload ?? '{}' };
      return { ...f, steps };
    });
  }

  function updateStepPayloadField(idx: number, key: string, value: string | number) {
    setForm(f => {
      const steps = [...f.steps];
      const current = parseJson(steps[idx].actionPayloadJson ?? '{}');
      current[key] = value;
      steps[idx] = { ...steps[idx], actionPayloadJson: JSON.stringify(current) };
      return { ...f, steps };
    });
  }

  function updateStepDelay(idx: number, delaySeconds: number) {
    setForm(f => {
      const steps = [...f.steps];
      steps[idx] = { ...steps[idx], delaySeconds };
      return { ...f, steps };
    });
  }

  function updateStepDescription(idx: number, description: string) {
    setForm(f => {
      const steps = [...f.steps];
      steps[idx] = { ...steps[idx], description };
      return { ...f, steps };
    });
  }

  async function submitForm() {
    setFormLoading(true);
    setFormError('');
    try {
      const payload = {
        name: form.name,
        description: form.description || undefined,
        triggerType: form.triggerType,
        triggerConditionJson: form.triggerConditionJson || undefined,
        isEnabled: form.isEnabled,
        steps: form.steps.map(s => ({
          actionType: s.actionType,
          actionPayloadJson: s.actionPayloadJson || undefined,
          delaySeconds: s.delaySeconds,
          description: s.description || undefined,
        })),
      };

      if (editId) {
        await apiClient.put(`/api/playbooks/${editId}`, payload);
      } else {
        await apiClient.post('/api/playbooks', payload);
      }

      setShowForm(false);
      await loadPlaybooks();
    } catch (e: unknown) {
      setFormError((e as Error)?.message || 'Kaydedilemedi.');
    } finally {
      setFormLoading(false);
    }
  }

  const triggerDef = TRIGGERS[form.triggerType];
  const conditionData = parseJson(form.triggerConditionJson);

  return (
    <div className="p-4 md:p-6 max-w-4xl mx-auto">
      {/* Header */}
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold text-slate-900 dark:text-white flex items-center gap-2">
            <Zap className="h-6 w-6 text-sky-500" />
            Remediation Playbook'lar
          </h1>
          <p className="text-sm text-slate-500 dark:text-slate-400 mt-0.5">
            Otomatik müdahale kuralları — servis düşünce, cihaz offline olunca
          </p>
        </div>
        <button
          onClick={openCreate}
          className="flex items-center gap-2 px-4 py-2 bg-sky-600 text-white rounded-lg text-sm font-medium hover:bg-sky-700 transition-colors"
        >
          <Plus className="h-4 w-4" />
          Yeni Playbook
        </button>
      </div>

      {loading ? (
        <div className="text-center py-16 text-slate-500">Yükleniyor...</div>
      ) : playbooks.length === 0 ? (
        <div className="text-center py-20 bg-white dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-700">
          <Zap className="h-12 w-12 mx-auto mb-3 text-slate-300 dark:text-slate-600" />
          <p className="text-slate-600 dark:text-slate-400 font-medium">Henüz playbook yok</p>
          <p className="text-sm text-slate-500 dark:text-slate-500 mt-1">Yukarıdan ilk playbook'unuzu oluşturun.</p>
          <button onClick={openCreate} className="mt-4 px-4 py-2 bg-sky-600 text-white rounded-lg text-sm font-medium hover:bg-sky-700 transition-colors">
            Playbook Oluştur
          </button>
        </div>
      ) : (
        <div className="space-y-3">
          {playbooks.map(p => {
            const tDef = TRIGGERS[p.triggerType];
            const TIcon = tDef?.icon;
            return (
              <div key={p.id} className="bg-white dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-700 overflow-hidden">
                <div
                  className="flex items-center gap-3 px-4 py-3 cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-700/40 transition-colors"
                  onClick={() => loadDetail(p.id)}
                >
                  <div className={`w-2.5 h-2.5 rounded-full flex-shrink-0 ${p.isEnabled ? 'bg-green-500' : 'bg-slate-400'}`} />
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 flex-wrap">
                      <span className="font-semibold text-slate-900 dark:text-white truncate">{p.name}</span>
                      <span className={`text-xs px-2 py-0.5 rounded-full whitespace-nowrap flex items-center gap-1 bg-slate-100 dark:bg-slate-700 text-slate-600 dark:text-slate-300`}>
                        {TIcon && <TIcon className={`h-3 w-3 ${tDef?.color ?? ''}`} />}
                        {tDef?.label ?? p.triggerType}
                      </span>
                    </div>
                    {p.description && <div className="text-xs text-slate-500 dark:text-slate-400 truncate mt-0.5">{p.description}</div>}
                  </div>
                  <div className="flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400 flex-shrink-0">
                    <span>{p.stepCount} adım</span>
                    {p.lastExecutionAt && (
                      <>
                        <span>·</span>
                        {STATUS_ICON[p.lastExecutionStatus ?? ''] ?? null}
                        <span>{new Date(p.lastExecutionAt).toLocaleDateString('tr-TR')}</span>
                      </>
                    )}
                  </div>
                  <div className="flex items-center gap-1" onClick={e => e.stopPropagation()}>
                    <button
                      onClick={e => openEdit(p, e)}
                      className="px-2 py-1 rounded text-xs text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-700 transition-colors"
                    >
                      Düzenle
                    </button>
                    <button
                      onClick={e => runPlaybook(p.id, e)}
                      className="p-1.5 rounded hover:bg-sky-50 dark:hover:bg-sky-900/20 text-slate-400 hover:text-sky-600 dark:hover:text-sky-400 transition-colors"
                      title="Manuel çalıştır"
                    >
                      <Play className="h-4 w-4" />
                    </button>
                    <button
                      onClick={e => toggleEnabled(p.id, e)}
                      className={`p-1.5 rounded transition-colors ${p.isEnabled ? 'hover:bg-amber-50 dark:hover:bg-amber-900/20 text-amber-500' : 'hover:bg-green-50 dark:hover:bg-green-900/20 text-slate-400 hover:text-green-600'}`}
                      title={p.isEnabled ? 'Devre dışı bırak' : 'Etkinleştir'}
                    >
                      <Power className="h-4 w-4" />
                    </button>
                    <button
                      onClick={e => deletePlaybook(p.id, e)}
                      className="p-1.5 rounded hover:bg-red-50 dark:hover:bg-red-900/20 text-slate-400 hover:text-red-500 transition-colors"
                      title="Sil"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                  {expandedId === p.id ? <ChevronUp className="h-4 w-4 text-slate-400 flex-shrink-0" /> : <ChevronDown className="h-4 w-4 text-slate-400 flex-shrink-0" />}
                </div>

                {expandedId === p.id && expandedDetail && (
                  <div className="border-t border-slate-100 dark:border-slate-700 px-4 py-4">
                    <div className="mb-4">
                      <div className="text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wide mb-2">Adımlar</div>
                      <div className="space-y-1.5">
                        {expandedDetail.steps.map(step => {
                          const aDef = ACTIONS[step.actionType];
                          const AIcon = aDef?.icon ?? Zap;
                          return (
                            <div key={step.id} className="flex items-start gap-3 text-sm bg-slate-50 dark:bg-slate-700/40 rounded-lg px-3 py-2">
                              <span className="w-5 h-5 rounded-full bg-sky-100 dark:bg-sky-900/40 text-sky-700 dark:text-sky-300 text-xs font-bold flex items-center justify-center flex-shrink-0 mt-0.5">
                                {step.stepOrder}
                              </span>
                              <AIcon className={`h-4 w-4 flex-shrink-0 mt-0.5 ${aDef?.color ?? 'text-slate-400'}`} />
                              <div className="flex-1 min-w-0">
                                <span className="font-medium text-slate-700 dark:text-slate-200">{aDef?.label ?? step.actionType}</span>
                                {step.description && <span className="text-slate-500 dark:text-slate-400 ml-2 text-xs">— {step.description}</span>}
                                {step.actionPayloadJson && step.actionPayloadJson !== '{}' && (
                                  <code className="block text-xs text-slate-400 dark:text-slate-500 font-mono mt-0.5">{step.actionPayloadJson}</code>
                                )}
                              </div>
                              {step.delaySeconds > 0 && (
                                <span className="text-xs text-slate-400 flex items-center gap-1 flex-shrink-0">
                                  <Clock className="h-3 w-3" />{step.delaySeconds}s
                                </span>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    </div>

                    {expandedDetail.recentExecutions.length > 0 && (
                      <div>
                        <div className="text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wide mb-2">Son Çalışmalar</div>
                        <div className="space-y-1">
                          {expandedDetail.recentExecutions.slice(0, 5).map(ex => (
                            <div key={ex.id} className="flex items-start gap-2 text-xs text-slate-600 dark:text-slate-300 bg-slate-50 dark:bg-slate-700/40 rounded px-3 py-2">
                              {STATUS_ICON[ex.status] ?? null}
                              <div className="flex-1 min-w-0">
                                <span className="font-medium">{ex.hostname ?? ex.storeCode ?? 'Manuel'}</span>
                                <span className="text-slate-400 ml-2">{ex.triggerReason}</span>
                                {ex.resultSummary && <div className="text-slate-400 dark:text-slate-500 mt-0.5 truncate">{ex.resultSummary}</div>}
                              </div>
                              <span className="text-slate-400 whitespace-nowrap">
                                {new Date(ex.startedAt).toLocaleString('tr-TR', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })}
                              </span>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}

      {/* Form Modal */}
      {showForm && (
        <div className="fixed inset-0 z-50 overflow-y-auto">
          <div className="min-h-full flex items-start justify-center p-4 py-8">
            <div className="fixed inset-0 bg-black/60" onClick={() => setShowForm(false)} />
            <div className="relative bg-white dark:bg-slate-800 rounded-2xl shadow-2xl w-full max-w-2xl flex flex-col">

              {/* Modal header */}
              <div className="flex items-center justify-between px-6 py-4 border-b border-slate-200 dark:border-slate-700 flex-shrink-0">
                <h3 className="text-lg font-semibold text-slate-900 dark:text-white">
                  {editId ? 'Playbook Düzenle' : 'Yeni Playbook'}
                </h3>
                <button
                  onClick={() => setShowForm(false)}
                  className="p-1.5 rounded-lg hover:bg-slate-100 dark:hover:bg-slate-700 text-slate-400 transition-colors"
                >
                  <X className="h-5 w-5" />
                </button>
              </div>

              {/* Modal body — scrollable */}
              <div className="overflow-y-auto max-h-[72vh] px-6 py-5 space-y-5">
                {formError && (
                  <div className="p-3 bg-red-50 dark:bg-red-900/20 text-red-600 dark:text-red-400 text-sm rounded-lg border border-red-200 dark:border-red-800">
                    {formError}
                  </div>
                )}

                {/* Name + Description */}
                <div className="space-y-3">
                  <div>
                    <label className="block text-xs font-semibold text-slate-600 dark:text-slate-400 mb-1.5">İsim *</label>
                    <input
                      type="text"
                      value={form.name}
                      onChange={e => setForm(f => ({ ...f, name: e.target.value }))}
                      placeholder="Genius3 Yeniden Başlatma"
                      className="w-full border border-slate-300 dark:border-slate-600 rounded-lg px-3 py-2 text-sm bg-white dark:bg-slate-700 text-slate-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-sky-500"
                    />
                  </div>
                  <div>
                    <label className="block text-xs font-semibold text-slate-600 dark:text-slate-400 mb-1.5">Açıklama</label>
                    <input
                      type="text"
                      value={form.description}
                      onChange={e => setForm(f => ({ ...f, description: e.target.value }))}
                      placeholder="Bu playbook ne zaman ve neden çalışır?"
                      className="w-full border border-slate-300 dark:border-slate-600 rounded-lg px-3 py-2 text-sm bg-white dark:bg-slate-700 text-slate-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-sky-500"
                    />
                  </div>
                </div>

                {/* Trigger selection */}
                <div>
                  <label className="block text-xs font-semibold text-slate-600 dark:text-slate-400 mb-2">Tetikleyici *</label>
                  <div className="grid grid-cols-4 gap-2">
                    {Object.entries(TRIGGERS).map(([key, t]) => {
                      const Icon = t.icon;
                      const selected = form.triggerType === key;
                      return (
                        <button
                          key={key}
                          type="button"
                          onClick={() => selectTrigger(key)}
                          className={`flex flex-col items-center gap-1.5 p-3 rounded-xl border-2 text-center transition-all ${
                            selected
                              ? `${t.selBg} border-current`
                              : 'border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-700/50 hover:border-slate-300 dark:hover:border-slate-500'
                          }`}
                        >
                          <Icon className={`h-5 w-5 ${selected ? t.color : 'text-slate-400 dark:text-slate-500'}`} />
                          <span className={`text-xs font-medium leading-tight ${selected ? 'text-slate-900 dark:text-white' : 'text-slate-500 dark:text-slate-400'}`}>
                            {t.shortLabel}
                          </span>
                        </button>
                      );
                    })}
                  </div>

                  {/* Trigger condition fields */}
                  <div className="mt-3 p-3 rounded-xl bg-slate-50 dark:bg-slate-700/40 border border-slate-200 dark:border-slate-600">
                    <p className="text-xs text-slate-500 dark:text-slate-400 mb-2">{triggerDef?.description}</p>
                    {triggerDef?.fields.length > 0 && (
                      <div className="grid grid-cols-2 gap-3">
                        {triggerDef.fields.map(field => (
                          <div key={field.key}>
                            <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1">{field.label}</label>
                            <input
                              type={field.type}
                              value={(conditionData[field.key] as string | number) ?? ''}
                              min={field.min}
                              max={field.max}
                              placeholder={field.placeholder}
                              onChange={e => updateConditionField(field.key, field.type === 'number' ? (parseInt(e.target.value) || 0) : e.target.value)}
                              className="w-full border border-slate-300 dark:border-slate-500 rounded-lg px-3 py-1.5 text-sm bg-white dark:bg-slate-700 text-slate-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-sky-500"
                            />
                            {field.hint && <p className="text-xs text-slate-400 mt-0.5">{field.hint}</p>}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </div>

                {/* Steps */}
                <div>
                  <div className="flex items-center justify-between mb-2">
                    <label className="text-xs font-semibold text-slate-600 dark:text-slate-400">Adımlar *</label>
                    <button
                      type="button"
                      onClick={addStep}
                      className="text-xs text-sky-600 dark:text-sky-400 hover:underline flex items-center gap-1"
                    >
                      <Plus className="h-3 w-3" /> Adım Ekle
                    </button>
                  </div>

                  <div className="space-y-2.5">
                    {form.steps.map((step, idx) => {
                      const aDef = ACTIONS[step.actionType];
                      const AIcon = aDef?.icon ?? Zap;
                      const payloadData = parseJson(step.actionPayloadJson ?? '{}');

                      return (
                        <div key={idx} className="border border-slate-200 dark:border-slate-600 rounded-xl overflow-hidden">
                          {/* Step header */}
                          <div className="flex items-center gap-2 px-3 py-2.5 bg-slate-50 dark:bg-slate-700/50">
                            <span className="w-6 h-6 rounded-full bg-sky-100 dark:bg-sky-900/40 text-sky-700 dark:text-sky-300 text-xs font-bold flex items-center justify-center flex-shrink-0">
                              {idx + 1}
                            </span>
                            <AIcon className={`h-4 w-4 flex-shrink-0 ${aDef?.color ?? 'text-slate-400'}`} />
                            <select
                              value={step.actionType}
                              onChange={e => updateStepAction(idx, e.target.value)}
                              className="flex-1 border-0 bg-transparent text-sm font-medium text-slate-900 dark:text-white focus:ring-0 focus:outline-none cursor-pointer"
                            >
                              {Object.entries(ACTIONS).map(([k, a]) => (
                                <option key={k} value={k}>{a.label}</option>
                              ))}
                            </select>
                            {form.steps.length > 1 && (
                              <button
                                type="button"
                                onClick={() => removeStep(idx)}
                                className="p-1 text-slate-400 hover:text-red-500 transition-colors ml-auto flex-shrink-0"
                              >
                                <Trash2 className="h-3.5 w-3.5" />
                              </button>
                            )}
                          </div>

                          {/* Step body */}
                          <div className="px-3 py-3 space-y-2.5 bg-white dark:bg-slate-800/40">
                            {aDef?.description && (
                              <p className="text-xs text-slate-400 dark:text-slate-500">{aDef.description}</p>
                            )}

                            {aDef?.fields.map(field => (
                              <div key={field.key}>
                                <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1">{field.label}</label>
                                <input
                                  type={field.type}
                                  value={(payloadData[field.key] as string | number) ?? ''}
                                  min={field.min}
                                  placeholder={field.placeholder}
                                  onChange={e => updateStepPayloadField(idx, field.key, field.type === 'number' ? (parseInt(e.target.value) || 0) : e.target.value)}
                                  className="w-full border border-slate-200 dark:border-slate-600 rounded-lg px-2.5 py-1.5 text-sm bg-white dark:bg-slate-700 text-slate-900 dark:text-white focus:outline-none focus:ring-2 focus:ring-sky-500"
                                />
                              </div>
                            ))}

                            <input
                              type="text"
                              value={step.description ?? ''}
                              onChange={e => updateStepDescription(idx, e.target.value)}
                              placeholder="Adım açıklaması (isteğe bağlı)"
                              className="w-full border border-slate-200 dark:border-slate-600 rounded-lg px-2.5 py-1.5 text-xs bg-white dark:bg-slate-700 text-slate-500 dark:text-slate-400 focus:outline-none focus:ring-1 focus:ring-sky-500"
                            />

                            <div className="flex items-center gap-2">
                              <Clock className="h-3.5 w-3.5 text-slate-400 flex-shrink-0" />
                              <span className="text-xs text-slate-500 dark:text-slate-400">Başlamadan önce bekle</span>
                              <input
                                type="number"
                                value={step.delaySeconds}
                                min={0}
                                onChange={e => updateStepDelay(idx, parseInt(e.target.value) || 0)}
                                className="w-16 border border-slate-200 dark:border-slate-600 rounded px-2 py-0.5 text-xs bg-white dark:bg-slate-700 text-slate-900 dark:text-white focus:outline-none"
                              />
                              <span className="text-xs text-slate-400">saniye</span>
                            </div>
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>

                {/* Enabled toggle */}
                <label className="flex items-center gap-3 cursor-pointer p-3 rounded-xl bg-slate-50 dark:bg-slate-700/30 border border-slate-200 dark:border-slate-600">
                  <input
                    type="checkbox"
                    checked={form.isEnabled}
                    onChange={e => setForm(f => ({ ...f, isEnabled: e.target.checked }))}
                    className="w-4 h-4 accent-sky-600"
                  />
                  <div>
                    <span className="text-sm font-medium text-slate-700 dark:text-slate-200">Etkin</span>
                    <span className="text-xs text-slate-500 dark:text-slate-400 ml-2">— otomatik tetiklemeye dahil et (5 dakikada bir kontrol edilir)</span>
                  </div>
                </label>
              </div>

              {/* Modal footer */}
              <div className="flex gap-2 px-6 py-4 border-t border-slate-200 dark:border-slate-700 flex-shrink-0">
                <button
                  type="button"
                  onClick={() => setShowForm(false)}
                  className="flex-1 px-4 py-2 border border-slate-300 dark:border-slate-600 rounded-lg text-sm text-slate-700 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
                >
                  İptal
                </button>
                <button
                  type="button"
                  onClick={submitForm}
                  disabled={formLoading || !form.name.trim()}
                  className="flex-1 px-4 py-2 bg-sky-600 text-white rounded-lg text-sm font-medium hover:bg-sky-700 disabled:opacity-50 transition-colors"
                >
                  {formLoading ? 'Kaydediliyor...' : editId ? 'Güncelle' : 'Kaydet'}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
