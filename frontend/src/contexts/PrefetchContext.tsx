import React, { createContext, useContext, useState, useEffect, useRef, useCallback } from 'react';
import { apiClient } from '../lib/apiClient';

// ─── Exported Types ───────────────────────────────────────────────────────────

export interface DiskDevice {
    deviceId: string;
    storeCode: number;
    storeName: string;
    deviceName: string;
    ipAddress: string;
    isOnline: boolean;
    status: 'online' | 'offline' | 'error' | 'unknown';
    diskCPercent: number | null;
    diskCTotalGB: number | null;
    diskCFreeGB: number | null;
    diskCUsedGB: number | null;
    diskDPercent: number | null;
    diskDTotalGB: number | null;
    diskDFreeGB: number | null;
    diskDUsedGB: number | null;
    errorMessage?: string;
}

export interface InboxStatus {
    deviceId: string;
    storeCode: number;
    storeName: string;
    ipAddress: string;
    isOnline: boolean;
    rdyCount: number;
    txtCount: number;
    tmp1Count: number;
    tmp2Count: number;
    disCount: number;
    proCount: number;
    seqCount: number;
    totalCount: number;
    status: 'clean' | 'dirty' | 'error' | 'offline' | 'unknown';
    errorMessage?: string;
}

export interface StockStatus {
    deviceId: string;
    storeCode: number;
    storeName: string;
    ipAddress: string;
    isOnline: boolean;
    plu0: number;
    plu10: number;
    plu20: number;
    plu30: number;
    total: number;
    status: 'clean' | 'dirty' | 'error' | 'offline' | 'unknown';
    errorMessage?: string;
}

export interface DbLogStatus {
    deviceId: string;
    storeCode: number;
    storeName: string;
    ipAddress: string;
    isOnline: boolean;
    exportLogCount: number;
    exportErrLogCount: number;
    total: number;
    status: 'clean' | 'dirty' | 'error' | 'offline' | 'unknown';
    errorMessage?: string;
}

// ─── Cache Entry ──────────────────────────────────────────────────────────────

export interface CacheEntry<T> {
    data: T | null;
    fetchedAt: Date | null;
    loading: boolean;
}

function emptyEntry<T>(): CacheEntry<T> {
    return { data: null, fetchedAt: null, loading: false };
}

// ─── Context Type ─────────────────────────────────────────────────────────────

interface PrefetchContextType {
    diskPc: CacheEntry<DiskDevice[]>;
    setDiskPc: (update: Partial<CacheEntry<DiskDevice[]>>) => void;

    diskKasa: CacheEntry<DiskDevice[]>;
    setDiskKasa: (update: Partial<CacheEntry<DiskDevice[]>>) => void;

    inboxCleanup: CacheEntry<InboxStatus[]>;
    setInboxCleanup: (update: Partial<CacheEntry<InboxStatus[]>>) => void;

    stockCleanup: CacheEntry<StockStatus[]>;
    setStockCleanup: (update: Partial<CacheEntry<StockStatus[]>>) => void;

    dbLogCleanup: CacheEntry<DbLogStatus[]>;
    setDbLogCleanup: (update: Partial<CacheEntry<DbLogStatus[]>>) => void;
}

const PrefetchContext = createContext<PrefetchContextType | null>(null);

// ─── Provider ─────────────────────────────────────────────────────────────────

const REFRESH_INTERVAL_MS = 10 * 60 * 1000; // 10 dakika

export const PrefetchProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const [diskPc, _setDiskPc]         = useState<CacheEntry<DiskDevice[]>>(emptyEntry);
    const [diskKasa, _setDiskKasa]     = useState<CacheEntry<DiskDevice[]>>(emptyEntry);
    const [inboxCleanup, _setInbox]    = useState<CacheEntry<InboxStatus[]>>(emptyEntry);
    const [stockCleanup, _setStock]    = useState<CacheEntry<StockStatus[]>>(emptyEntry);
    const [dbLogCleanup, _setDbLog]    = useState<CacheEntry<DbLogStatus[]>>(emptyEntry);

    const merge = <T,>(setter: React.Dispatch<React.SetStateAction<CacheEntry<T>>>) =>
        (update: Partial<CacheEntry<T>>) => setter(prev => ({ ...prev, ...update }));

    const setDiskPc       = useCallback(merge<DiskDevice[]>(_setDiskPc), []);
    const setDiskKasa     = useCallback(merge<DiskDevice[]>(_setDiskKasa), []);
    const setInboxCleanup = useCallback(merge<InboxStatus[]>(_setInbox), []);
    const setStockCleanup = useCallback(merge<StockStatus[]>(_setStock), []);
    const setDbLogCleanup = useCallback(merge<DbLogStatus[]>(_setDbLog), []);

    const abortRef = useRef<boolean>(false);

    const runAllScans = useCallback(() => {
        if (abortRef.current) return;

        // Tüm taramalar paralel başlar, her biri hazır olduğunda kendi cache'ini günceller
        const scanTimeout = 120_000; // Scan endpoint'leri uzun sürebilir

        setDiskPc({ loading: true });
        apiClient.post<DiskDevice[]>('/api/disk-status/check-all', undefined, scanTimeout)
            .then(res => { if (!abortRef.current) setDiskPc({ data: res, fetchedAt: new Date(), loading: false }); })
            .catch(() => { if (!abortRef.current) setDiskPc({ loading: false }); });

        setDiskKasa({ loading: true });
        apiClient.post<DiskDevice[]>('/api/disk-status/check-all-kasa', undefined, scanTimeout)
            .then(res => { if (!abortRef.current) setDiskKasa({ data: res, fetchedAt: new Date(), loading: false }); })
            .catch(() => { if (!abortRef.current) setDiskKasa({ loading: false }); });

        setInboxCleanup({ loading: true });
        apiClient.post<InboxStatus[]>('/api/inbox-cleanup/check-all', undefined, scanTimeout)
            .then(res => { if (!abortRef.current) setInboxCleanup({ data: res, fetchedAt: new Date(), loading: false }); })
            .catch(() => { if (!abortRef.current) setInboxCleanup({ loading: false }); });

        setStockCleanup({ loading: true });
        apiClient.post<StockStatus[]>('/api/stock-cleanup/check-all', {}, scanTimeout)
            .then(res => { if (!abortRef.current) setStockCleanup({ data: res, fetchedAt: new Date(), loading: false }); })
            .catch(() => { if (!abortRef.current) setStockCleanup({ loading: false }); });

        setDbLogCleanup({ loading: true });
        apiClient.post<DbLogStatus[]>('/api/db-log-cleanup/check-all', {}, scanTimeout)
            .then(res => { if (!abortRef.current) setDbLogCleanup({ data: res, fetchedAt: new Date(), loading: false }); })
            .catch(() => { if (!abortRef.current) setDbLogCleanup({ loading: false }); });
    }, []);

    useEffect(() => {
        abortRef.current = false;
        runAllScans();

        const interval = setInterval(runAllScans, REFRESH_INTERVAL_MS);
        return () => {
            abortRef.current = true;
            clearInterval(interval);
        };
    }, [runAllScans]);

    return (
        <PrefetchContext.Provider value={{
            diskPc, setDiskPc,
            diskKasa, setDiskKasa,
            inboxCleanup, setInboxCleanup,
            stockCleanup, setStockCleanup,
            dbLogCleanup, setDbLogCleanup,
        }}>
            {children}
        </PrefetchContext.Provider>
    );
};

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function usePrefetch(): PrefetchContextType {
    const ctx = useContext(PrefetchContext);
    if (!ctx) throw new Error('usePrefetch must be used within PrefetchProvider');
    return ctx;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Formats fetchedAt as "X dk önce" or "X sn önce" */
export function formatFetchAge(fetchedAt: Date | null): string {
    if (!fetchedAt) return '';
    const secs = Math.floor((Date.now() - fetchedAt.getTime()) / 1000);
    if (secs < 60) return `${secs} sn önce yüklendi`;
    const mins = Math.floor(secs / 60);
    return `${mins} dk önce yüklendi`;
}
