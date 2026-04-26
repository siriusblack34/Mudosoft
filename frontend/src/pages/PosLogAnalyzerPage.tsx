import React, { useCallback, useMemo, useState } from 'react';
import {
  ArrowLeft,
  Braces,
  CalendarDays,
  ChevronDown,
  Clock3,
  FileText,
  ListTree,
  ScanLine,
  Search,
  Upload,
  Wallet,
} from 'lucide-react';

type LineKind = 'barcode' | 'item' | 'payment' | 'customer' | 'receipt_end' | 'completed' | 'voided' | 'error' | 'other';

interface ParsedLine {
  index: number;
  lineNumber: number;
  raw: string;
  timestamp?: Date;
  level?: string;
  message: string;
  kind: LineKind;
  detectedBarcode?: string;
}

interface SaleItem {
  barcode: string;
  price: number;
  timestamp?: Date;
  source: 'item_sale' | 'insert_sale';
}

interface Payment {
  description: string;
  amount: number;
  timestamp?: Date;
}

interface SaleSegment {
  receiptBarcode: string;
  startIndex: number;
  endIndex: number;
  barcodeFirstIndex: number;
  barcodeLastIndex: number;
  startLineNumber: number;
  endLineNumber: number;
  startTime?: Date;
  endTime?: Date;
  status: 'completed' | 'voided' | 'open';
  customerCode?: string;
  items: SaleItem[];
  payments: Payment[];
  lines: ParsedLine[];
  summary: string[];
  startReason: string;
  endReason: string;
}

interface InterpretedBlock {
  type: 'sale' | 'other';
  key: string;
  title: string;
  lines: ParsedLine[];
  summary: string[];
  sale?: SaleSegment;
}

interface LogAnalysis {
  lines: ParsedLine[];
  sales: SaleSegment[];
  interpretedBlocks: InterpretedBlock[];
  logStartTime?: Date;
  logEndTime?: Date;
}

const BARCODE_REGEX =
  /(Generated Barcode by system|GetDocBarcode for Invoice Saved|Genereted receipt barkod by System)\s*=\s*(\d{10,})/i;
const ITEM_SALE_REGEX = /ITEM SALE --> Barcode:(\S+)\s+Unit Price:(\d+)\s+X\s+\d+\[Label Price:\s*(\d+)\]/i;
const INSERT_ITEM_REGEX = /insertSalesTrx -> Saving Unit Price,\s*Barcode:\s*(\S+)\s*->\s*Price:\s*(\d+)/i;
const PAYMENT_REGEX = /Payment type received with provision:\s*Desc=(.+?)\s+GeniusNo=\d+\s+Amount=([0-9.]+)/i;
const CUSTOMER_REGEX = /mdmScreenNext start with\s+(\d+)/i;
const LOG_TEXT_ENCODINGS = ['utf-8', 'windows-1254', 'iso-8859-9'] as const;

function scoreDecodedText(text: string): number {
  const replacementPenalty = (text.match(/\uFFFD/g) || []).length * 80;
  const mojibakePenalty = (text.match(/[ÃÄÅ�]/g) || []).length * 25;
  const controlPenalty = (text.match(/[\x00-\x08\x0B\x0C\x0E-\x1F]/g) || []).length * 40;
  const turkishBonus = (text.match(/[çğıİöşüÇĞÖŞÜ]/g) || []).length * 3;
  return turkishBonus - replacementPenalty - mojibakePenalty - controlPenalty;
}

function decodeLogBuffer(buffer: ArrayBuffer): string {
  let bestText = '';
  let bestScore = Number.NEGATIVE_INFINITY;

  for (const encoding of LOG_TEXT_ENCODINGS) {
    try {
      const decoded = new TextDecoder(encoding).decode(buffer);
      const score = scoreDecodedText(decoded);
      if (score > bestScore) {
        bestScore = score;
        bestText = decoded;
      }
    } catch {
      // Unsupported decoder in current browser, skip.
    }
  }

  if (bestText) return bestText;
  return new TextDecoder('utf-8').decode(buffer);
}

function parseTimestamp(raw: string): Date | undefined {
  const cleaned = raw.replace(/\s+[A-Z]{2,5}\s+/, ' ');
  const parsed = new Date(cleaned);
  if (!Number.isNaN(parsed.getTime())) return parsed;

  const match = raw.match(/\w+\s+(\w+)\s+(\d+)\s+(\d+):(\d+):(\d+)\s+\w+\s+(\d+)/);
  if (!match) return undefined;

  const months: Record<string, number> = { Jan: 0, Feb: 1, Mar: 2, Apr: 3, May: 4, Jun: 5, Jul: 6, Aug: 7, Sep: 8, Oct: 9, Nov: 10, Dec: 11 };
  const month = months[match[1]];
  if (month == null) return undefined;

  return new Date(+match[6], month, +match[2], +match[3], +match[4], +match[5]);
}

function detectLineKind(message: string): { kind: LineKind; barcode?: string } {
  const barcodeMatch = message.match(BARCODE_REGEX);
  if (barcodeMatch) return { kind: 'barcode', barcode: barcodeMatch[2] };
  if (ITEM_SALE_REGEX.test(message) || INSERT_ITEM_REGEX.test(message)) return { kind: 'item' };
  if (PAYMENT_REGEX.test(message)) return { kind: 'payment' };
  if (CUSTOMER_REGEX.test(message) || /customer_code\s*=/i.test(message) || /RECEIPT_CUSTOMER_INFO_MSG/i.test(message)) return { kind: 'customer' };
  if (/RECEIPT_END endDocument method will progress|endDocuMent function start/i.test(message)) return { kind: 'receipt_end' };
  if (/#### Document successfully completed ####/i.test(message)) return { kind: 'completed' };
  if (/RECEIPT_IS_VOIDED|## Document voided successfully ##|RETURN_RECEIPT_IS_VOIDED/i.test(message)) return { kind: 'voided' };
  if (/Exception|ERROR_|NumberFormatException|NullPointerException|ClassCastException|Connection error|Connection refused/i.test(message)) return { kind: 'error' };
  return { kind: 'other' };
}

function parseRawLine(raw: string, index: number): ParsedLine {
  const trimmed = raw.trimEnd();
  const match = trimmed.match(/^\[(.+?)\]\s+\[(\w+)\]\s+\[.*?\]\s+Global:\s*(.*)$/);

  if (!match) {
    return { index, lineNumber: index + 1, raw, message: trimmed, kind: 'other' };
  }

  const timestamp = parseTimestamp(match[1]);
  const message = match[3].trim();
  const detected = detectLineKind(message);

  return {
    index,
    lineNumber: index + 1,
    raw,
    timestamp,
    level: match[2],
    message,
    kind: detected.kind,
    detectedBarcode: detected.barcode,
  };
}

function isHardBoundary(line: ParsedLine): boolean {
  return line.kind === 'completed' || line.kind === 'voided';
}

function isStrongStart(line: ParsedLine): boolean {
  return line.kind === 'customer' || line.kind === 'item' || line.kind === 'payment' || line.kind === 'receipt_end';
}

function findBoundaryReason(line: ParsedLine | undefined, fallback: string): string {
  if (!line) return fallback;
  if (line.kind === 'customer') return 'Musteri veya fis baslangici satiri';
  if (line.kind === 'item') return 'Ilk urun satiri';
  if (line.kind === 'payment') return 'Odeme satiri';
  if (line.kind === 'receipt_end') return 'Fis kapatma akisi';
  if (line.kind === 'completed') return 'Fis basariyla tamamlandi';
  if (line.kind === 'voided') return 'Fis iptal edildi';
  if (line.kind === 'barcode') return 'Receipt barcode satiri';
  return fallback;
}

function extractItems(lines: ParsedLine[]): SaleItem[] {
  const directItems: SaleItem[] = [];
  const fallbackItems: SaleItem[] = [];

  for (const line of lines) {
    const itemMatch = line.message.match(ITEM_SALE_REGEX);
    if (itemMatch) {
      directItems.push({ barcode: itemMatch[1], price: parseInt(itemMatch[2], 10) / 100, timestamp: line.timestamp, source: 'item_sale' });
      continue;
    }

    const insertMatch = line.message.match(INSERT_ITEM_REGEX);
    if (insertMatch) {
      fallbackItems.push({ barcode: insertMatch[1], price: parseInt(insertMatch[2], 10) / 100, timestamp: line.timestamp, source: 'insert_sale' });
    }
  }

  return directItems.length > 0 ? directItems : fallbackItems;
}

function extractPayments(lines: ParsedLine[]): Payment[] {
  return lines
    .map((line) => {
      const match = line.message.match(PAYMENT_REGEX);
      if (!match) return null;
      return { description: match[1].trim(), amount: parseFloat(match[2]), timestamp: line.timestamp };
    })
    .filter((payment): payment is NonNullable<typeof payment> => payment !== null);
}

function extractCustomerCode(lines: ParsedLine[]): string | undefined {
  for (const line of lines) {
    const customerMatch = line.message.match(CUSTOMER_REGEX);
    if (customerMatch) return customerMatch[1];

    const basicMatch = line.message.match(/customer_code\s*=\s*(\S+)/i);
    if (basicMatch && basicMatch[1] !== '0') return basicMatch[1];
  }
  return undefined;
}

function firstTimestamp(lines: ParsedLine[]): Date | undefined {
  return lines.find((line) => line.timestamp)?.timestamp;
}

function lastTimestamp(lines: ParsedLine[]): Date | undefined {
  for (let i = lines.length - 1; i >= 0; i -= 1) {
    if (lines[i].timestamp) return lines[i].timestamp;
  }
  return undefined;
}

const formatTime = (date?: Date) =>
  date
    ? date.toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
    : '-';

const formatDateTime = (date?: Date) => (date ? date.toLocaleString('tr-TR') : '-');

const formatPrice = (value: number) =>
  value.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' TL';

const statusStyles = {
  completed: 'text-emerald-300 bg-emerald-500/10 border-emerald-500/20',
  voided: 'text-amber-300 bg-amber-500/10 border-amber-500/20',
  open: 'text-sky-300 bg-sky-500/10 border-sky-500/20',
};

const lineKindStyles: Record<LineKind, string> = {
  barcode: 'border-l-cyan-400 bg-cyan-500/5',
  item: 'border-l-emerald-400 bg-emerald-500/5',
  payment: 'border-l-violet-400 bg-violet-500/5',
  customer: 'border-l-amber-400 bg-amber-500/5',
  receipt_end: 'border-l-blue-400 bg-blue-500/5',
  completed: 'border-l-emerald-500 bg-emerald-500/10',
  voided: 'border-l-rose-400 bg-rose-500/10',
  error: 'border-l-red-400 bg-red-500/10',
  other: 'border-l-zinc-700',
};

function buildSaleSummary(sale: SaleSegment): string[] {
  const itemsText =
    sale.items.length > 0
      ? `${sale.items.length} urun var, toplam gorunen tutar ${formatPrice(sale.items.reduce((sum, item) => sum + item.price, 0))}.`
      : 'Urun satiri yakalanmadi, sadece kapanis veya veritabani satirlari bulundu.';

  const paymentText =
    sale.payments.length > 0
      ? `Odeme: ${sale.payments.map((payment) => `${payment.description} ${formatPrice(payment.amount)}`).join(', ')}.`
      : 'Odeme satiri bulunmadi.';

  const statusText =
    sale.status === 'completed'
      ? 'Satis basariyla tamamlanmis gorunuyor.'
      : sale.status === 'voided'
        ? 'Satis iptal veya void ile sonlanmis.'
        : 'Satisin net kapanis satiri bulunamadi; log acik veya eksik olabilir.';

  return [
    `Baslangic: ${findBoundaryReason(sale.lines[0], 'Tahmini baslangic')}.`,
    `Bitis: ${sale.endReason}.`,
    itemsText,
    paymentText,
    statusText,
  ];
}

function parseLog(text: string): LogAnalysis {
  const rawLines = text.split(/\r?\n/);
  const lines = rawLines.map(parseRawLine);
  const barcodeMap = new Map<string, { firstIndex: number; lastIndex: number }>();

  lines.forEach((line) => {
    if (!line.detectedBarcode) return;
    const existing = barcodeMap.get(line.detectedBarcode);
    if (existing) {
      existing.lastIndex = line.index;
      return;
    }
    barcodeMap.set(line.detectedBarcode, { firstIndex: line.index, lastIndex: line.index });
  });

  const sales = Array.from(barcodeMap.entries())
    .map(([receiptBarcode, range]) => {
      let previousBoundary = -1;
      for (let cursor = range.firstIndex - 1; cursor >= 0; cursor -= 1) {
        const line = lines[cursor];
        if (isHardBoundary(line) || (line.kind === 'barcode' && line.detectedBarcode !== receiptBarcode)) {
          previousBoundary = cursor;
          break;
        }
      }

      const startSearchFrom = previousBoundary + 1;
      let startIndex = Math.max(startSearchFrom, range.firstIndex - 40);
      for (let cursor = startSearchFrom; cursor <= range.firstIndex; cursor += 1) {
        if (isStrongStart(lines[cursor])) {
          startIndex = cursor;
          break;
        }
      }

      let nextBarcodeIndex = lines.length - 1;
      for (let cursor = range.lastIndex + 1; cursor < lines.length; cursor += 1) {
        if (lines[cursor].kind === 'barcode' && lines[cursor].detectedBarcode !== receiptBarcode) {
          nextBarcodeIndex = cursor - 1;
          break;
        }
      }

      let endIndex = nextBarcodeIndex;
      for (let cursor = range.lastIndex; cursor < lines.length; cursor += 1) {
        if (isHardBoundary(lines[cursor])) {
          endIndex = cursor;
          break;
        }
      }

      return {
        receiptBarcode,
        barcodeFirstIndex: range.firstIndex,
        barcodeLastIndex: range.lastIndex,
        startIndex,
        endIndex,
      };
    })
    .sort((left, right) => left.startIndex - right.startIndex || left.barcodeFirstIndex - right.barcodeFirstIndex);

  for (let i = 0; i < sales.length; i += 1) {
    const current = sales[i];
    const next = sales[i + 1];
    if (next) current.endIndex = Math.min(current.endIndex, next.startIndex - 1);
    if (i > 0) current.startIndex = Math.max(current.startIndex, sales[i - 1].endIndex + 1);
    current.endIndex = Math.max(current.endIndex, current.barcodeLastIndex, current.startIndex);
  }

  const normalizedSales: SaleSegment[] = sales.map((sale) => {
    const saleLines = lines.slice(sale.startIndex, sale.endIndex + 1);
    const status = saleLines.some((line) => line.kind === 'voided')
      ? 'voided'
      : saleLines.some((line) => line.kind === 'completed')
        ? 'completed'
        : 'open';

    const customerCode = extractCustomerCode(saleLines);
    const items = extractItems(saleLines);
    const payments = extractPayments(saleLines);
    const startTime = firstTimestamp(saleLines);
    const endTime = lastTimestamp(saleLines);
    const startReason = findBoundaryReason(saleLines[0], 'Tahmini baslangic');
    const endReason = findBoundaryReason(saleLines[saleLines.length - 1], 'Tahmini bitis');

    const segment: SaleSegment = {
      receiptBarcode: sale.receiptBarcode,
      startIndex: sale.startIndex,
      endIndex: sale.endIndex,
      barcodeFirstIndex: sale.barcodeFirstIndex,
      barcodeLastIndex: sale.barcodeLastIndex,
      startLineNumber: saleLines[0]?.lineNumber ?? sale.startIndex + 1,
      endLineNumber: saleLines[saleLines.length - 1]?.lineNumber ?? sale.endIndex + 1,
      startTime,
      endTime,
      status,
      customerCode,
      items,
      payments,
      lines: saleLines,
      summary: [],
      startReason,
      endReason,
    };

    segment.summary = buildSaleSummary(segment);
    return segment;
  });

  const interpretedBlocks: InterpretedBlock[] = [];
  let cursor = 0;

  normalizedSales.forEach((sale, index) => {
    if (cursor < sale.startIndex) {
      const otherLines = lines.slice(cursor, sale.startIndex);
      if (otherLines.some((line) => line.raw.trim())) {
        interpretedBlocks.push({
          type: 'other',
          key: `other-${cursor}`,
          title: 'Satis disi sistem satirlari',
          lines: otherLines,
          summary: ['Bu bolum satisa baglanmayan sistem, baglanti veya arka plan satirlarini icerir.'],
        });
      }
    }

    interpretedBlocks.push({
      type: 'sale',
      key: `${sale.receiptBarcode}-${index}`,
      title: `Satis ${index + 1} - ${sale.receiptBarcode}`,
      lines: sale.lines,
      summary: sale.summary,
      sale,
    });

    cursor = sale.endIndex + 1;
  });

  if (cursor < lines.length) {
    const otherLines = lines.slice(cursor);
    if (otherLines.some((line) => line.raw.trim())) {
      interpretedBlocks.push({
        type: 'other',
        key: `other-tail-${cursor}`,
        title: 'Satis disi sistem satirlari',
        lines: otherLines,
        summary: ['Bu son bolum herhangi bir receipt barcode ile eslestirilemeyen satirlari gosterir.'],
      });
    }
  }

  return {
    lines,
    sales: normalizedSales,
    interpretedBlocks,
    logStartTime: firstTimestamp(lines),
    logEndTime: lastTimestamp(lines),
  };
}

const MetricCard: React.FC<{ label: string; value: number; icon: React.ReactNode }> = ({ label, value, icon }) => (
  <div className="rounded-2xl border border-zinc-800 bg-zinc-950 px-4 py-3 text-sm">
    <div className="mb-1 flex items-center gap-2 text-zinc-500">
      {icon}
      <span>{label}</span>
    </div>
    <div className="text-lg font-semibold text-white">{value}</div>
  </div>
);

const MiniBadge: React.FC<{ icon: React.ReactNode; text: string }> = ({ icon, text }) => (
  <span className="inline-flex items-center gap-1 rounded-full bg-zinc-900 px-3 py-1">
    {icon}
    {text}
  </span>
);

const InfoPair: React.FC<{ label: string; value: string }> = ({ label, value }) => (
  <div className="rounded-2xl bg-zinc-900 px-3 py-2">
    <p className="text-xs text-zinc-500">{label}</p>
    <p className="mt-1 text-sm text-zinc-200">{value}</p>
  </div>
);

const LogLineRow: React.FC<{ line: ParsedLine }> = ({ line }) => (
  <div className={`rounded-2xl border-l-4 px-3 py-2 text-sm ${lineKindStyles[line.kind]}`}>
    <div className="flex flex-wrap items-center gap-2 text-xs text-zinc-500">
      <span className="font-mono">#{line.lineNumber}</span>
      {line.timestamp && <span>{formatDateTime(line.timestamp)}</span>}
      {line.detectedBarcode && <span className="rounded-full bg-cyan-500/10 px-2 py-0.5 text-cyan-200">{line.detectedBarcode}</span>}
      {line.kind !== 'other' && <span className="rounded-full bg-zinc-900 px-2 py-0.5 uppercase tracking-wide">{line.kind}</span>}
    </div>
    <p className="mt-1 break-all font-mono text-[12px] leading-5 text-zinc-200">{line.raw || line.message}</p>
  </div>
);

const PosLogAnalyzerPage: React.FC = () => {
  const [analysis, setAnalysis] = useState<LogAnalysis | null>(null);
  const [fileName, setFileName] = useState('');
  const [loading, setLoading] = useState(false);
  const [search, setSearch] = useState('');
  const [selectedBarcode, setSelectedBarcode] = useState<string | null>(null);
  const [activeView, setActiveView] = useState<'sales' | 'interpreted'>('sales');

  const loadFile = useCallback((file: File) => {
    setLoading(true);
    setFileName(file.name);

    const reader = new FileReader();
    reader.onload = (event) => {
      const buffer = event.target?.result as ArrayBuffer;
      const content = buffer ? decodeLogBuffer(buffer) : '';
      const result = parseLog(content);
      setAnalysis(result);
      setSelectedBarcode(null);
      setActiveView('sales');
      setSearch('');
      setLoading(false);
    };
    reader.onerror = () => setLoading(false);
    reader.readAsArrayBuffer(file);
  }, []);

  const handleFile = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const file = event.target.files?.[0];
      if (file) loadFile(file);
    },
    [loadFile],
  );

  const handleDrop = useCallback(
    (event: React.DragEvent<HTMLDivElement>) => {
      event.preventDefault();
      const file = event.dataTransfer.files?.[0];
      if (file) loadFile(file);
    },
    [loadFile],
  );

  const filteredSales = useMemo(() => {
    if (!analysis) return [];
    const query = search.trim().toLowerCase();
    if (!query) return analysis.sales;
    return analysis.sales.filter((sale) => {
      if (sale.receiptBarcode.toLowerCase().includes(query)) return true;
      if (sale.customerCode?.toLowerCase().includes(query)) return true;
      if (sale.items.some((item) => item.barcode.toLowerCase().includes(query))) return true;
      if (sale.payments.some((payment) => payment.description.toLowerCase().includes(query))) return true;
      return false;
    });
  }, [analysis, search]);

  const selectedSale = useMemo(
    () => analysis?.sales.find((sale) => sale.receiptBarcode === selectedBarcode) ?? null,
    [analysis, selectedBarcode],
  );

  if (!analysis) {
    return (
      <div className="p-6">
        <div
          onDrop={handleDrop}
          onDragOver={(event) => event.preventDefault()}
          onClick={() => document.getElementById('pos-log-file-input')?.click()}
          className="mx-auto max-w-4xl rounded-[28px] border border-dashed border-zinc-600 bg-[radial-gradient(circle_at_top,_rgba(56,189,248,0.12),_transparent_45%),linear-gradient(135deg,rgba(24,24,27,0.96),rgba(39,39,42,0.9))] p-12 text-center transition-colors hover:border-cyan-400/60"
        >
          {loading ? (
            <div className="flex flex-col items-center gap-4">
              <div className="h-10 w-10 animate-spin rounded-full border-2 border-cyan-400 border-t-transparent" />
              <p className="text-zinc-300">Log analiz ediliyor, receipt barkodlari cikartiliyor...</p>
            </div>
          ) : (
            <>
              <div className="mx-auto mb-5 flex h-16 w-16 items-center justify-center rounded-2xl bg-cyan-500/10 text-cyan-300">
                <Upload className="h-8 w-8" />
              </div>
              <h1 className="text-3xl font-semibold tracking-tight text-white">Kasa Log Analizi</h1>
              <p className="mx-auto mt-3 max-w-2xl text-sm leading-6 text-zinc-400">
                Logu yuklediginizde sistem aninda <span className="text-cyan-300">RECEIPT_BARCODE</span> satirlarini bulur,
                satislari ayirir, tarih-saat listesi cikarir ve tiklayinca o satisin baslangictan bitise tum satirlarini gosterir.
              </p>
              <p className="mt-6 text-sm text-zinc-500">Surukleyip birakin veya tiklayip `.log` / `.txt` secin.</p>
            </>
          )}
          <input id="pos-log-file-input" type="file" accept=".log,.txt" className="hidden" onChange={handleFile} />
        </div>
      </div>
    );
  }

  return (
    <div className="p-6">
      <div className="mx-auto max-w-7xl space-y-4">
        <div className="rounded-[28px] border border-zinc-800 bg-[linear-gradient(135deg,rgba(10,10,10,0.96),rgba(24,24,27,0.94))] p-5">
          <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
            <div>
              <h1 className="text-2xl font-semibold text-white">Kasa Log Analizi</h1>
              <p className="mt-1 text-sm text-zinc-400">
                <FileText className="mr-1 inline h-4 w-4" />
                {fileName}
              </p>
              <p className="mt-2 text-sm text-zinc-500">
                {formatDateTime(analysis.logStartTime)} - {formatDateTime(analysis.logEndTime)}
              </p>
            </div>

            <div className="flex flex-wrap gap-2">
              <MetricCard label="Tespit edilen satis" value={analysis.sales.length} icon={<ScanLine className="h-4 w-4" />} />
              <MetricCard label="Toplam satir" value={analysis.lines.length} icon={<ListTree className="h-4 w-4" />} />
              <MetricCard
                label="Tamamlanan satis"
                value={analysis.sales.filter((sale) => sale.status === 'completed').length}
                icon={<CalendarDays className="h-4 w-4" />}
              />
              <button
                onClick={() => {
                  setAnalysis(null);
                  setFileName('');
                  setSearch('');
                  setSelectedBarcode(null);
                }}
                className="rounded-2xl border border-zinc-700 px-4 py-2 text-sm text-zinc-200 transition-colors hover:border-cyan-400/50 hover:text-white"
              >
                Yeni log yukle
              </button>
            </div>
          </div>
        </div>

        <div className="flex flex-wrap gap-2">
          <button
            onClick={() => setActiveView('sales')}
            className={`rounded-2xl px-4 py-2 text-sm transition-colors ${
              activeView === 'sales' ? 'bg-cyan-500/15 text-cyan-200' : 'bg-zinc-900 text-zinc-400 hover:text-white'
            }`}
          >
            Barkod listesi
          </button>
          <button
            onClick={() => setActiveView('interpreted')}
            className={`rounded-2xl px-4 py-2 text-sm transition-colors ${
              activeView === 'interpreted' ? 'bg-cyan-500/15 text-cyan-200' : 'bg-zinc-900 text-zinc-400 hover:text-white'
            }`}
          >
            Yorumlu tam log
          </button>
        </div>

        {activeView === 'sales' && !selectedSale && (
          <div className="space-y-4">
            <div className="relative max-w-xl">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-zinc-500" />
              <input
                value={search}
                onChange={(event) => setSearch(event.target.value)}
                placeholder="Receipt barcode, urun barkodu, musteri veya odeme tipi ara..."
                className="w-full rounded-2xl border border-zinc-800 bg-zinc-950 px-10 py-3 text-sm text-white outline-none transition-colors placeholder:text-zinc-500 focus:border-cyan-500"
              />
            </div>

            <div className="grid gap-3">
              {filteredSales.length === 0 && (
                <div className="rounded-3xl border border-zinc-800 bg-zinc-950 p-8 text-center text-zinc-500">Sonuc bulunamadi.</div>
              )}

              {filteredSales.map((sale) => (
                <button
                  key={sale.receiptBarcode}
                  onClick={() => setSelectedBarcode(sale.receiptBarcode)}
                  className="group rounded-[24px] border border-zinc-800 bg-zinc-950 p-4 text-left transition-colors hover:border-cyan-500/40 hover:bg-zinc-900"
                >
                  <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
                    <div className="space-y-2">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="rounded-full bg-cyan-500/10 px-3 py-1 font-mono text-xs text-cyan-200">{sale.receiptBarcode}</span>
                        <span className={`rounded-full border px-3 py-1 text-xs ${statusStyles[sale.status]}`}>
                          {sale.status === 'completed' ? 'Tamamlandi' : sale.status === 'voided' ? 'Iptal' : 'Acik / Eksik'}
                        </span>
                        {sale.customerCode && (
                          <span className="rounded-full bg-amber-500/10 px-3 py-1 text-xs text-amber-200">Musteri {sale.customerCode}</span>
                        )}
                      </div>

                      <div className="flex flex-wrap gap-x-5 gap-y-2 text-sm text-zinc-400">
                        <span className="flex items-center gap-2">
                          <Clock3 className="h-4 w-4 text-zinc-500" />
                          Baslangic: {formatDateTime(sale.startTime)}
                        </span>
                        <span className="flex items-center gap-2">
                          <CalendarDays className="h-4 w-4 text-zinc-500" />
                          Bitis: {formatDateTime(sale.endTime)}
                        </span>
                        <span>Satir: {sale.startLineNumber} - {sale.endLineNumber}</span>
                      </div>
                    </div>

                    <div className="flex flex-wrap gap-2 text-xs text-zinc-300">
                      <MiniBadge icon={<Braces className="h-3.5 w-3.5" />} text={`${sale.lines.length} satir`} />
                      <MiniBadge icon={<ScanLine className="h-3.5 w-3.5" />} text={`${sale.items.length} urun`} />
                      <MiniBadge icon={<Wallet className="h-3.5 w-3.5" />} text={`${sale.payments.length} odeme`} />
                    </div>
                  </div>

                  <div className="mt-3 flex flex-wrap gap-2 text-xs text-zinc-500">
                    {sale.summary.slice(0, 2).map((entry) => (
                      <span key={entry} className="rounded-full bg-zinc-900 px-3 py-1">
                        {entry}
                      </span>
                    ))}
                  </div>
                </button>
              ))}
            </div>
          </div>
        )}

        {activeView === 'sales' && selectedSale && (
          <div className="space-y-4">
            <button
              onClick={() => setSelectedBarcode(null)}
              className="inline-flex items-center gap-2 rounded-2xl border border-zinc-800 bg-zinc-950 px-4 py-2 text-sm text-zinc-300 transition-colors hover:border-cyan-500/40 hover:text-white"
            >
              <ArrowLeft className="h-4 w-4" />
              Geri don, tum satis barkodlarini goster
            </button>

            <div className="rounded-[28px] border border-zinc-800 bg-zinc-950 p-5">
              <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
                <div className="space-y-3">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="rounded-full bg-cyan-500/10 px-3 py-1 font-mono text-sm text-cyan-200">{selectedSale.receiptBarcode}</span>
                    <span className={`rounded-full border px-3 py-1 text-xs ${statusStyles[selectedSale.status]}`}>
                      {selectedSale.status === 'completed' ? 'Tamamlandi' : selectedSale.status === 'voided' ? 'Iptal' : 'Acik / Eksik'}
                    </span>
                  </div>

                  <div className="grid gap-2 text-sm text-zinc-400 md:grid-cols-2">
                    <InfoPair label="Baslangic" value={formatDateTime(selectedSale.startTime)} />
                    <InfoPair label="Bitis" value={formatDateTime(selectedSale.endTime)} />
                    <InfoPair label="Baslangic satiri" value={String(selectedSale.startLineNumber)} />
                    <InfoPair label="Bitis satiri" value={String(selectedSale.endLineNumber)} />
                    <InfoPair label="Baslangic nedeni" value={selectedSale.startReason} />
                    <InfoPair label="Bitis nedeni" value={selectedSale.endReason} />
                    <InfoPair label="Musteri" value={selectedSale.customerCode ?? '-'} />
                  </div>
                </div>

                <div className="min-w-[260px] space-y-2 rounded-3xl bg-zinc-900/80 p-4">
                  {selectedSale.summary.map((entry) => (
                    <p key={entry} className="text-sm leading-6 text-zinc-300">
                      {entry}
                    </p>
                  ))}
                </div>
              </div>
            </div>

            {(selectedSale.items.length > 0 || selectedSale.payments.length > 0) && (
              <div className="grid gap-4 lg:grid-cols-2">
                <div className="rounded-[24px] border border-zinc-800 bg-zinc-950 p-4">
                  <h2 className="mb-3 text-sm font-medium text-white">Urunler</h2>
                  <div className="space-y-2 text-sm">
                    {selectedSale.items.length === 0 && <p className="text-zinc-500">Urun satiri bulunmadi.</p>}
                    {selectedSale.items.map((item, index) => (
                      <div key={`${item.barcode}-${index}`} className="flex items-center justify-between rounded-2xl bg-zinc-900 px-3 py-2">
                        <div>
                          <p className="font-mono text-cyan-200">{item.barcode}</p>
                          <p className="text-xs text-zinc-500">{item.source === 'item_sale' ? 'Satis satiri' : 'DB kayit satiri'}</p>
                        </div>
                        <div className="text-right">
                          <p className="text-white">{formatPrice(item.price)}</p>
                          <p className="text-xs text-zinc-500">{formatTime(item.timestamp)}</p>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="rounded-[24px] border border-zinc-800 bg-zinc-950 p-4">
                  <h2 className="mb-3 text-sm font-medium text-white">Odemeler</h2>
                  <div className="space-y-2 text-sm">
                    {selectedSale.payments.length === 0 && <p className="text-zinc-500">Odeme satiri bulunmadi.</p>}
                    {selectedSale.payments.map((payment, index) => (
                      <div key={`${payment.description}-${index}`} className="flex items-center justify-between rounded-2xl bg-zinc-900 px-3 py-2">
                        <div>
                          <p className="text-white">{payment.description}</p>
                          <p className="text-xs text-zinc-500">{formatTime(payment.timestamp)}</p>
                        </div>
                        <p className="text-cyan-200">{formatPrice(payment.amount)}</p>
                      </div>
                    ))}
                  </div>
                </div>
              </div>
            )}

            <div className="rounded-[28px] border border-zinc-800 bg-zinc-950 p-4">
              <h2 className="mb-4 text-sm font-medium text-white">Satisin tum satirlari</h2>
              <div className="max-h-[72vh] space-y-1 overflow-y-auto pr-1">
                {selectedSale.lines.map((line) => (
                  <LogLineRow key={`${selectedSale.receiptBarcode}-${line.lineNumber}`} line={line} />
                ))}
              </div>
            </div>
          </div>
        )}

        {activeView === 'interpreted' && (
          <div className="space-y-4">
            {analysis.interpretedBlocks.map((block) => {
              const isSale = block.type === 'sale' && block.sale;
              return (
                <details
                  key={block.key}
                  open={Boolean(isSale)}
                  className="overflow-hidden rounded-[26px] border border-zinc-800 bg-zinc-950"
                >
                  <summary className="flex cursor-pointer list-none items-center justify-between gap-3 px-5 py-4 text-sm text-zinc-200">
                    <div>
                      <p className="font-medium text-white">{block.title}</p>
                      <p className="mt-1 text-xs text-zinc-500">
                        {block.lines[0]?.lineNumber ?? '-'} - {block.lines[block.lines.length - 1]?.lineNumber ?? '-'} satirlari
                      </p>
                    </div>
                    <div className="flex items-center gap-2 text-xs text-zinc-400">
                      {isSale && (
                        <span className={`rounded-full border px-3 py-1 ${statusStyles[block.sale.status]}`}>
                          {block.sale.status === 'completed' ? 'Tamamlandi' : block.sale.status === 'voided' ? 'Iptal' : 'Acik / Eksik'}
                        </span>
                      )}
                      <span className="rounded-full bg-zinc-900 px-3 py-1">{block.lines.length} satir</span>
                      <ChevronDown className="h-4 w-4" />
                    </div>
                  </summary>

                  <div className="border-t border-zinc-800 px-5 py-4">
                    <div className="mb-4 flex flex-wrap gap-2">
                      {block.summary.map((entry) => (
                        <span key={entry} className="rounded-full bg-zinc-900 px-3 py-1 text-xs text-zinc-300">
                          {entry}
                        </span>
                      ))}
                    </div>

                    <div className="max-h-[60vh] space-y-1 overflow-y-auto pr-1">
                      {block.lines.map((line) => (
                        <LogLineRow key={`${block.key}-${line.lineNumber}`} line={line} />
                      ))}
                    </div>
                  </div>
                </details>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
};

export default PosLogAnalyzerPage;
