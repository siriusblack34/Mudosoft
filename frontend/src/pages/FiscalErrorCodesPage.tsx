import React, { useState, useMemo } from 'react';
import { Search, AlertTriangle, ChevronDown, ChevronUp, Printer } from 'lucide-react';

interface ErrorCode {
  code: string;
  hex?: string;
  explanation: string;
  solution: string;
  severity: 'info' | 'warning' | 'critical';
}

interface ErrorCategory {
  title: string;
  description: string;
  codes: ErrorCode[];
}

function getSeverity(explanation: string): 'info' | 'warning' | 'critical' {
  const lower = explanation.toLowerCase();
  if (
    lower.includes('bloke') || lower.includes('engel') ||
    lower.includes('kurtarılamaz') || lower.includes('değiştir') ||
    lower.includes('servis çağır') || lower.includes('arızalı')
  ) return 'critical';
  if (
    lower.includes('taşma') || lower.includes('alt taşma') ||
    lower.includes('hata') || lower.includes('geçersiz')
  ) return 'warning';
  return 'info';
}

const standardCodes: ErrorCode[] = [
  {
    code: '002', hex: '80900102',
    explanation: 'Taşma oluştu. İptal işlemi toplamı izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplam işlemi (cmd.D9h) çalıştırın, ardından işlemi sonlandırın (cmd.06h) veya işlemi iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '004', hex: '80900104',
    explanation: 'Taşma oluştu. İndirim işlemi toplamı izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplam işlemi (cmd.D9h) çalıştırın, ardından işlemi sonlandırın (cmd.06h) veya işlemi iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '005', hex: '80900105',
    explanation: 'Eksik sertifika. Dijital sertifika bulunamadı. İstek işlenmedi.',
    solution: 'Eksik sertifikayı cmd.92h komutu ile yükleyin.',
    severity: 'critical',
  },
  {
    code: '006', hex: '80900106',
    explanation: 'NTP fonksiyonu çalıştırılamadı. Olası nedenler: NTP sunucusu ile iletişim kurulamadı; kısa süre içinde birden fazla NTP çağrısı yapıldı; NTP sunucusu hizmet dışı. İstek işlenmedi.',
    solution: 'Ethernet kablosunu kontrol edin. Yeni NTP çağrısı için 4 saat bekleyin veya farklı bir NTP sunucusu ayarlayın.',
    severity: 'warning',
  },
  {
    code: '007', hex: '80900107',
    explanation: 'Taşma oluştu. Ödeme tipine göre ödeme işlemi toplamı izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplam işlemi (cmd.D9h) çalıştırın, ardından işlemi sonlandırın (cmd.06h) veya işlemi iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '008', hex: '80900108',
    explanation: 'Alt taşma oluştu. Mevcut işlem toplamı veya KDV numarasına göre işlem toplamlarından biri izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'İşlemi iptal edin veya toplamı izin verilen minimum değerin üzerine çıkarın.',
    severity: 'warning',
  },
  {
    code: '010', hex: '80900110',
    explanation: 'Alt taşma oluştu. İptal işlemi toplamı izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Toplam işlemi (cmd.D9h) çalıştırın, ardından işlemi sonlandırın (cmd.06h) veya işlemi iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '011', hex: '80900111',
    explanation: 'Alt taşma oluştu. Yerel para birimi cinsinden ödeme eşdeğeri toplamı (Tra_PayLocalCurrency) izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Toplam işlemi (cmd.D9h) çalıştırın, ardından işlemi sonlandırın (cmd.06h) veya işlemi iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '012', hex: '80900112',
    explanation: 'Alt taşma oluştu. İndirim işlemi toplamı izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Toplam işlemi (cmd.D9h) çalıştırın, ardından işlemi sonlandırın (cmd.06h) veya işlemi iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '013', hex: '80900113',
    explanation: 'Alt taşma oluştu. Döviz cinsinden ödeme toplamlarından biri izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Toplam işlemi (cmd.D9h) çalıştırın, ardından işlemi sonlandırın (cmd.06h) veya işlemi iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '014', hex: '80900114',
    explanation: 'Alt taşma oluştu. Yerel para birimi cinsinden ödeme eşdeğeri toplamlarından biri izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Toplam işlemi (cmd.D9h) çalıştırın, ardından işlemi sonlandırın (cmd.06h) veya işlemi iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '015', hex: '80900115',
    explanation: 'Alt taşma oluştu. Ödeme tipine göre ödeme işlemi toplamı negatif. İstek işlenmedi.',
    solution: 'Ödeme tipine göre ödeme toplamını >= 0 yapın (düzeltme tutarı ilgili ödeme tipi tutarından küçük veya eşit olmalıdır).',
    severity: 'warning',
  },
  {
    code: '016', hex: '80900116',
    explanation: 'Taşma oluştu. Günlük toplam veya KDV numarasına göre günlük toplamlardan biri, toplam isteği sırasında izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplamı izin verilen maksimum değere eşit veya altına getirin, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '017', hex: '80900117',
    explanation: 'Sertifikalar silindi. Gelir İdaresi veya TSM sertifikası silinmiş. Mali yazıcı bloke edildi.',
    solution: 'Teknisyen Servis Modunu etkinleştirerek mali yazıcının blokesini kaldırmalı ve yeni sertifika yüklemelidir.',
    severity: 'critical',
  },
  {
    code: '018', hex: '80900118',
    explanation: 'Taşma oluştu. İptal günlük toplamı, işlem sonu sırasında izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplamı izin verilen maksimum değere eşit veya altına getirin, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '019', hex: '80900119',
    explanation: 'Taşma oluştu. Yerel para birimi cinsinden ödeme günlük toplamı, işlem sonu sırasında izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplamı izin verilen maksimum değere eşit veya altına getirin, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '020', hex: '80900120',
    explanation: 'Taşma oluştu. İndirim günlük toplamı, işlem sonu sırasında izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplamı izin verilen maksimum değere eşit veya altına getirin, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '021', hex: '80900121',
    explanation: 'Taşma oluştu. Döviz cinsinden günlük ödeme toplamlarından biri, işlem sonu sırasında izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplamı izin verilen maksimum değere eşit veya altına getirin, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '022', hex: '80900122',
    explanation: 'Taşma oluştu. Yerel para birimi cinsinden günlük ödeme eşdeğeri toplamlarından biri, işlem sonu sırasında izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplamı izin verilen maksimum değere eşit veya altına getirin, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '023', hex: '80900123',
    explanation: 'Taşma oluştu. Günlük ödeme toplamı, işlem sonu sırasında izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplamı izin verilen maksimum değere eşit veya altına getirin, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '024', hex: '80900124',
    explanation: 'Kullanıcı toplam tutarı mali toplam tutarına eşit değil. Toplam isteğindeki değerler mali bellekte saklanan toplamlarla eşleşmiyor. İstek işlenmedi.',
    solution: 'Toplam hesaplama prosedürünü düzeltin, ardından işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h).',
    severity: 'critical',
  },
  {
    code: '025', hex: '80900125',
    explanation: 'Mali kural ihlali oluştu. İzin verilmeyen bir mesajda "toplam" kelimesi (veya eşdeğeri) kullanıldı veya ayrılmış bir karakter kullanıldı. İstek işlenmedi.',
    solution: 'Mali kural ihlalini düzeltin ve komutu tekrar deneyin.',
    severity: 'warning',
  },
  {
    code: '026', hex: '80900126',
    explanation: 'Alt taşma oluştu. Toplam isteği sırasında bir işlem tutarı negatif. Bu kod şu durumlarda geçerlidir: İşlem toplamı, KDV toplamı, KDV numarasına göre işlem toplamı, KDV numarasına göre KDV toplamı. İstek işlenmedi.',
    solution: 'Tutarı sıfıra eşit veya üzerine çıkarın, ardından işlemi sonlandırın veya iptal edin.',
    severity: 'warning',
  },
  {
    code: '027', hex: '8090061B',
    explanation: 'Taşma oluştu. Ara toplam üzerindeki indirim izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Tutarı izin verilen maksimum değerin altına düşürün, ardından işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '028', hex: '8090061C',
    explanation: 'Taşma oluştu. Ara toplam üzerindeki artış izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Tutarı izin verilen maksimum değerin altına düşürün, ardından işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '029', hex: '80900129',
    explanation: 'Ödeme tamamlanmadı. Ödeme toplamı işlem toplamından küçük (Tra_Payment < Tra_Tot). İstek işlenmedi.',
    solution: 'Ödeme komutunda düzeltme seçeneğini kullanın veya işlemi tamamlamak için ek bir ödeme komutu çalıştırın.',
    severity: 'warning',
  },
  {
    code: '030', hex: '80900130',
    explanation: 'Negatif ürün işlemi geçerli değil. Olası nedenler: ürün daha önce satılmamış; İNDİRİM ürün tutarı ürün tutarından küçük olmalı; sonuç ürün biriktirici sıfıra eşit veya altında ya da maksimum değerin üstünde; daha önce iptal edilen bir ürün için negatif işlem istendi. İstek işlenmedi.',
    solution: 'Negatif ürün veya artış ürün işlemini düzeltin.',
    severity: 'warning',
  },
  {
    code: '031', hex: '8090061F',
    explanation: 'Alt taşma oluştu. Bu satış işlemindeki ara toplam indirim işlemlerinin toplamı izin verilen minimum değerin altında VEYA düzeltme tutarı önceki indirim/artış tutarına eşit olmalıdır. İstek işlenmedi.',
    solution: 'Tutarı izin verilen minimum değere eşit veya üzerine çıkarın, ardından işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '032', hex: '80900620',
    explanation: 'Alt taşma oluştu. Bu satış işlemindeki ara toplam artış işlemlerinin toplamı izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Toplamı sıfıra eşit veya üzerine çıkarın, ardından işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '033', hex: '80900141',
    explanation: 'Sertifika seri numarası hatası. Sertifikadaki subject.serialnumber alanında tanımlanan seri numarası mali seri numarası ile eşleşmiyor. İstek işlenmedi.',
    solution: 'Sertifika yüklemek için kullanılan pfx dosyasının mali yazıcı seri numarasıyla eşleştiğinden emin olun ve cmd.92h komutunu tekrar deneyin.',
    severity: 'critical',
  },
  {
    code: '034', hex: '80900142',
    explanation: 'Alt taşma oluştu. Günlük iptal toplamı izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Toplamı izin verilen minimum değere eşit veya üzerine çıkarın, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '035', hex: '80900143',
    explanation: 'SSL sertifikasında geçersiz tarih. Sertifikanın "Geçerlilik Başlangıç" tarihi geçerli tarihten önce değil veya "Geçerlilik Bitiş" tarihi geçerli tarihten sonra değil. İstek işlenmedi.',
    solution: 'Sertifika yüklemek için kullanılan pfx dosyasının geçerli bir sertifika içerdiğinden emin olun ve cmd.92h komutunu tekrar deneyin.',
    severity: 'critical',
  },
  {
    code: '036', hex: '80900144',
    explanation: 'Alt taşma oluştu. Günlük indirim toplamı izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Toplamı izin verilen minimum değere eşit veya üzerine çıkarın, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '038', hex: '80900146',
    explanation: 'Belirtilen mali olmayan rapor tipi Mali Olmayan Rapor Tipleri Tablosuna yüklenmemiş VEYA belirtilen fiş tipi Fiş Tipleri Tablosuna yüklenmemiş. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin. Mali olmayan rapor tipi (cmd.2500h ile) veya fiş tipi (cmd.2501h ile) yüklenmelidir.',
    severity: 'warning',
  },
  {
    code: '039', hex: '80900627',
    explanation: 'Genel sıralama hatası.',
    solution: 'Uygulama programı sıralamasını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '040', hex: '80900628',
    explanation: 'Tutar alanı boş olduğunda KDV numarası alanı boş olmalı; tutar alanı boş olduğunda miktar alanı da boş olmalıdır. İstek işlenmedi.',
    solution: 'Uygulama programını düzeltin.',
    severity: 'info',
  },
  {
    code: '041', hex: '80900629',
    explanation: 'KDV oran tablosu yüklenmemiş. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin. İşlemlerden önce KDV Oranı Ayarla (cmd.20h) komutunu çalıştırın.',
    severity: 'warning',
  },
  {
    code: '042', hex: '8090062A',
    explanation: 'KDV oran tablosu uyuşmazlığı var. İstek işlenmedi.',
    solution: 'KDV oranlarını düzeltin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '044', hex: '8090062C',
    explanation: 'Tutar boş olmadığında miktar boş olamaz. İstek işlenmedi.',
    solution: 'Uygulama programını düzeltin.',
    severity: 'info',
  },
  {
    code: '045', hex: '8090062D',
    explanation: 'Belirtilen döviz, Döviz Tablosuna yüklenmemiş. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin. Dövizler (cmd.23h ile) yüklenmelidir.',
    severity: 'warning',
  },
  {
    code: '046', hex: '8090062E',
    explanation: 'Belirtilen ödeme tipi, Ödeme Tipleri Tablosuna yüklenmemiş. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin. Ödeme tipi (cmd.24h ile) yüklenmelidir.',
    severity: 'warning',
  },
  {
    code: '047', hex: '8090062F',
    explanation: 'Ondalık nokta zaten sıfırlanmış. İstek işlenmedi.',
    solution: 'Herhangi bir işlem gerekmez.',
    severity: 'info',
  },
  {
    code: '048', hex: '80900630',
    explanation: 'Taşma oluştu. Ara toplam indirim işlemlerinin günlük toplamı, işlem sonu sırasında izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplamı izin verilen maksimum değere eşit veya altına getirin, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '049', hex: '80900631',
    explanation: 'Taşma oluştu. Ara toplam artış işlemlerinin günlük toplamı izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplamı izin verilen maksimum değere eşit veya altına getirin, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '050', hex: '80900632',
    explanation: 'Alt taşma oluştu. Ara toplam indirim işlemlerinin günlük toplamı izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Toplamı izin verilen minimum değerin üzerine çıkarın, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '051', hex: '80900633',
    explanation: 'Alt taşma oluştu. Ara toplam artış işlemlerinin günlük toplamı izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Toplamı izin verilen minimum değerin üzerine çıkarın, işlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '052', hex: '80900634',
    explanation: 'İşlem toplamı 0 iken ara toplam üzerinde artış ve indirim (cmd.D9h) yapılamaz. İstek işlenmedi.',
    solution: 'İşlem toplamını ayarlayın veya işlemi iptal edin (cmd.06h).',
    severity: 'warning',
  },
  {
    code: '053', hex: '80900635',
    explanation: 'Tarih ve saat, mali bellekte saklanan son kapanışın tarih ve saatinden önceki bir değere sahip. İstek işlenmedi.',
    solution: 'Mali yazıcıya gönderilen tarih ve saati düzeltin (cmd.16h) veya mali yazıcı saati gerçek saatten izin verilen aralıktan fazla farklıysa servis çağırın.',
    severity: 'critical',
  },
  {
    code: '054', hex: '80900636',
    explanation: 'Ödeme aşaması tamamlandı. Para üstü zaten yazdırıldı. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'info',
  },
  {
    code: '055', hex: '80900203',
    explanation: 'Mali istek mesaj uzunluğu gerekli minimum değerden kısa. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'info',
  },
  {
    code: '056', hex: '80900150',
    explanation: 'Taşma oluştu. İşlem iptal sırasında iptal günlük toplamı izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Toplamı izin verilen maksimum değere eşit veya altına getirin, işlemi iptal edin (cmd.07h) ardından satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '057', hex: '80900151',
    explanation: 'Kasiyer parametreleri ayarlanmadan önce komut çalıştırıldı. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını düzelterek bu komuttan önce kasiyer parametrelerini (cmd.26h) ayarlayın.',
    severity: 'warning',
  },
  {
    code: '058', hex: '8090063A',
    explanation: 'Taşma oluştu. Borç tutarı biriktirici izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Ödeme tutarını düzeltin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '059', hex: '8090063B',
    explanation: 'Alt taşma oluştu. Borç tutarı biriktirici izin verilen minimum değerin altında. İstek işlenmedi.',
    solution: 'Ödeme tutarını düzeltin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '060', hex: '8090063C',
    explanation: 'Taşma oluştu. Yerel para birimi cinsinden ödeme eşdeğeri toplamı, ürün satış komutunda izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'İşlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '061', hex: '8090063D',
    explanation: 'Taşma oluştu. İşlem toplamı veya KDV numarasına göre işlem KDV toplamlarından biri, ürün satış komutunda izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'İşlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '062', hex: '8090063E',
    explanation: 'Taşma oluştu. Döviz cinsinden ödeme toplamlarından biri, ürün satış komutunda izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'İşlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '063', hex: '8090063F',
    explanation: 'Taşma oluştu. Yerel para birimi cinsinden ödeme eşdeğeri toplamlarından biri, ürün satış komutunda izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'İşlemi sonlandırın (cmd.06h) veya iptal edin (cmd.07h).',
    severity: 'warning',
  },
  {
    code: '064', hex: '80900127',
    explanation: 'Taşma oluştu. Uygulama programından gelen belirtilen alınan değer tutarı izin verilen maksimum tutarı aşıyor. İstek işlenmedi.',
    solution: 'Değeri düzeltin ve işlemi tekrar deneyin.',
    severity: 'warning',
  },
  {
    code: '065', hex: '80900201',
    explanation: 'Mali birime gönderilen istekteki mali komut baytı tanınmıyor. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '066', hex: '80900202',
    explanation: 'Mali birime gönderilen istekteki mali komut bayt uzantısı tanınmıyor. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '067', hex: '80900643',
    explanation: 'Komut başarıyla işlendi. Hata oluşmadı.',
    solution: 'Herhangi bir işlem gerekmez.',
    severity: 'info',
  },
  {
    code: '069', hex: '80900205',
    explanation: 'Mali fiş veya fatura fişi sırasında CR istasyonunda yazdırılabilecek maksimum olağan yazdırma satırı sayısı aşıldı. İstek işlenmedi.',
    solution: 'Olağan yazdırma satırlarını yazdırmadan önce işlemi sonlandırın veya iptal edin. Çevrimiçi yazıcı tanılama testi sırasında oluşursa, satış işlemi devam ettiği için test tamamlanamaz.',
    severity: 'warning',
  },
  {
    code: '070', hex: '80900646',
    explanation: 'Kredi kartı fişi satır besleme (cmd.C3h) komutunda kısmi satır besleme noktaları aralık dışında. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını kontrol edin.',
    severity: 'info',
  },
  {
    code: '071', hex: '80900302',
    explanation: 'Son kapanıştan bu yana izin verilenden fazla zaman geçti. Yeni satış işlemleri yapılamaz. İstek işlenmedi.',
    solution: 'Yeni satış işlemlerine izin vermek için: devam eden satış işlemini sonlandırın, kapanış raporu çalıştırın (cmd.13h).',
    severity: 'critical',
  },
  {
    code: '073', hex: '80900303',
    explanation: 'Günlük kapanış sayısı izin verilen maksimumu (3) aştı. İstek işlenmedi.',
    solution: 'Kapanış raporu yapmak için ertesi günü bekleyin.',
    severity: 'warning',
  },
  {
    code: '074', hex: '80900208',
    explanation: 'Kasiyer sayısı izin verilen maksimumu aştı. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '075', hex: '80900209',
    explanation: 'Satış işlemi sırasında bu noktada takılı belge üzerine yazdırma yapılamaz. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'info',
  },
  {
    code: '076', hex: '80900210',
    explanation: 'Geçersiz yazdırma istasyonu, geçersiz yönlendirme veya geçersiz yön. İstek işlenmedi.',
    solution: 'İstasyon, yönlendirme veya yönü düzeltin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '079', hex: '80900212',
    explanation: 'Takılı mali belge yazdırma sırasında CR istasyonunda satır besleme yapılamaz. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'info',
  },
  {
    code: '080', hex: '80900213',
    explanation: 'Satış işlemi sırasında bu noktada takılı belge üzerinde satır besleme yapılamaz. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'info',
  },
  {
    code: '081', hex: '80900651',
    explanation: 'Belirtilen CPI veya Yazdırma Modu geçerli değil. İstek işlenmedi.',
    solution: 'Geçerli bir CPI veya Yazdırma Modu belirtin.',
    severity: 'warning',
  },
  {
    code: '082', hex: '80900306',
    explanation: 'CR istasyonunda yazdırma isteği yapıldı ancak mali olmayan rapor/fiş DI istasyonunda başlatılmış VEYA DI istasyonunda yazdırma isteği yapıldı ancak mali olmayan rapor CR istasyonunda başlatılmış VEYA mali olmayan rapor devam etmezken mali olmayan rapor komutu çalıştırıldı. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '083', hex: '80900307',
    explanation: 'Mali bellek tanımlama/durum/ayar alanı okunurken kurtarılamaz hata oluştu. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '084', hex: '80900308',
    explanation: 'Genel hata. Sınırlı hata kodu sayısı nedeniyle bazı başarısız komutlar bu genel hatayı döndürür.',
    solution: 'Hatanın detayını öğrenmek için Genişletilmiş Hata Kodu Al (cmd.63h) komutu gönderin. "Genişletilmiş Hata Kodları" bölümüne bakın.',
    severity: 'critical',
  },
  {
    code: '085', hex: '80900309',
    explanation: 'EJ ortam başlatma tablosu dolu. İstek işlenmedi.',
    solution: 'Takılı EJ ortamını son kullanılan EJ ortamıyla değiştirin veya mali belleği değiştirin.',
    severity: 'critical',
  },
  {
    code: '086', hex: '80900401',
    explanation: 'Girilen şifre geçerli değil. İstek işlenmedi.',
    solution: 'Doğru şifreyi tekrar girin.',
    severity: 'warning',
  },
  {
    code: '087', hex: '80900657',
    explanation: 'Mali yazıcı tarafından alınan yazıcı komutu geçerli değil. İstek işlenmedi.',
    solution: 'Geçerli bir yazıcı komutu çalıştırın.',
    severity: 'warning',
  },
  {
    code: '089', hex: '80900312',
    explanation: 'Günlük kayıt tablosu dolu. Mali bellek raporu (cmd.15h) dışında tüm mali komutlar reddedilir. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '090', hex: '8090065A',
    explanation: 'İstenen kapanış numarası günlük kayıt tablosunda bulunamadı. İstek işlenmedi.',
    solution: 'Mali bellek raporu (cmd.15h) için geçerli bir kapanış numarası veya geçerli tarihler belirtin.',
    severity: 'warning',
  },
  {
    code: '091', hex: '80900314',
    explanation: 'Başlangıç mesajı yazdırılırken hata oluştu. İstek işlenmedi.',
    solution: 'Yazıcıyı kapatıp açın. Hata devam ederse servise gönderin.',
    severity: 'critical',
  },
  {
    code: '092', hex: '80900315',
    explanation: 'İstenen dahili tablo kaydı mali bellekte bulunamadı. İstek işlenmedi.',
    solution: 'Geçerli bir tablo girişi belirtin.',
    severity: 'warning',
  },
  {
    code: '093', hex: '80900316',
    explanation: 'RAMP Parametre Tablosu yüklenmemiş. İstek işlenmedi.',
    solution: 'Parametre Tablosunu yükleyin (Parametre Yükleme İsteği veya cmd.26h ile).',
    severity: 'warning',
  },
  {
    code: '094', hex: '80900317',
    explanation: 'RAMP İletişim Tablosu yüklenmemiş. İstek işlenmedi.',
    solution: 'İletişim Tablosunu yükleyin (Parametre Yükleme İsteği veya cmd.26h ile).',
    severity: 'warning',
  },
  {
    code: '095', hex: '80900425',
    explanation: 'Adres veya uzunluk verisi geçerli değil. Mühendislik döküm komutundaki adres aralığı geçersiz veya yanlış. İstek işlenmedi.',
    solution: 'Giriş verilerini düzeltin.',
    severity: 'warning',
  },
  {
    code: '096', hex: '80900140',
    explanation: 'Sayısal alan geçersiz karakterler içeriyor. İstek işlenmedi.',
    solution: 'Değeri düzeltin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '097', hex: '80900410',
    explanation: 'NVRAM hatalı veya mali bellekle eşleşmiyor.',
    solution: 'Yazıcıyı servise gönderin. NVRAM temizleme gereklidir. Not: Yalnızca yetkili kullanıcı CE jumperını taşıyabilir veya servis moduna girebilir.',
    severity: 'critical',
  },
  {
    code: '098', hex: '80900411',
    explanation: 'NVRAM geri yüklendi veya komut servis modunda çalıştırılamıyor. İstek işlenmedi.',
    solution: 'Normal çalışmaya geri dönmek için CE jumperını çıkarın veya servis modunu devre dışı bırakın.',
    severity: 'warning',
  },
  {
    code: '099', hex: '80900318',
    explanation: 'Onarım işlemleri tablosu dolu.',
    solution: 'Bir sonraki arıza durumunda mali yazıcıyı değiştirin.',
    severity: 'critical',
  },
  {
    code: '100', hex: '80900329',
    explanation: 'Mali bellekten okuma sırasında hata oluştu. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '101', hex: '80900326',
    explanation: 'Mali belleğe yazma sırasında kurtarılamaz hata oluştu. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '102', hex: '80900327',
    explanation: 'RAMP İş İstasyonu Parametreleri ayarlanmamış. İstek işlenmedi.',
    solution: 'İş İstasyonu Parametrelerini ayarlayın (cmd.26h ile).',
    severity: 'warning',
  },
  {
    code: '103', hex: '80900421',
    explanation: 'Alfabetik/Sayısal alanlarda geçersiz veri (örnek: boş olamaz, sıfır olmalı, sıfır olamaz, aralık dışında vb.). İstek işlenmedi.',
    solution: 'Giriş verilerini düzeltin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '104', hex: '80900360',
    explanation: 'Barkod verisi null ile sonlandırılmalıdır. İstek işlenmedi.',
    solution: 'Barkod verisini düzeltin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '105', hex: '80900361',
    explanation: 'Barkod boyutu geçersiz. İstek işlenmedi.',
    solution: 'Barkod boyutunu düzeltin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '106', hex: '80900363',
    explanation: 'Mikrokod dahili hatası. İstek işlenmedi.',
    solution: 'Yazıcıyı kapatıp açın. Sorun devam ederse servise gönderin.',
    severity: 'critical',
  },
  {
    code: '107', hex: '8090066B',
    explanation: 'NVRAM Olaylar tablosunda bütünlük hatası bulundu. Mali yazıcı bloke edildi. İstek işlenmedi.',
    solution: 'Servis Modunu etkinleştirin ve tüm tablonun bütünlüğünü kontrol etmek için cmd.D000h-D004h (Olay Bilgileri) komutlarını çalıştırın.',
    severity: 'critical',
  },
  {
    code: '108', hex: '80900328',
    explanation: 'Mali bellekteki mağaza/vergi mükellefi tanımlama tablosu dolu. İstek işlenmedi.',
    solution: 'Servis çağırın. Yeni mağaza/vergi mükellefi tanımlama tablosu için mali tabanın değiştirilmesi gerekir.',
    severity: 'critical',
  },
  {
    code: '109', hex: '80900324',
    explanation: 'Mali bellek bağlı değil. Mali birim işleme devam edemiyor.',
    solution: 'Yazıcıyı servise gönderin. Servis sırasında mali işlemci kartındaki kablo bağlantılarını kontrol edin. Mali bellek yeniden bağlandığında CE jumper prosedürü gereklidir.',
    severity: 'critical',
  },
  {
    code: '110', hex: '80900131',
    explanation: 'Mali Olmayan Rapor sonlandırılırken taşma/alt taşma oluştu. Olası nedenler: rapor numarasına göre günlük MO rapor toplamı maksimumu aşıyor; günlük nakit toplam maksimumu aşıyor; tahsilat tutarı günlük nakit toplamından büyük. İstek işlenmedi.',
    solution: 'Duruma göre: tutarı düzeltin veya Mali Olmayan Raporu farklı bir komut uzantısıyla sonlandırın.',
    severity: 'warning',
  },
  {
    code: '111', hex: '80900132',
    explanation: 'Taşma oluştu. Belirli bir günlük fiş toplamı, işlem sonu sırasında izin verilen maksimum değeri aşıyor. İstek işlenmedi.',
    solution: 'Satış dönemini kapatın (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '112', hex: '80900670',
    explanation: 'Mali yazıcı sıfırlandı.',
    solution: 'Herhangi bir işlem gerekmez.',
    severity: 'info',
  },
  {
    code: '113', hex: '80900341',
    explanation: 'İki güç açma sıfırlamasından sonra kurtarılamaz yazıcı hatası oluştu. İstek işlenmedi.',
    solution: 'Yazıcıyı kapatıp açın. Sorun devam ederse servise gönderin.',
    severity: 'critical',
  },
  {
    code: '114', hex: '80900363',
    explanation: 'Yazıcı iletişim hatası oluştu. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '115', hex: '80900701',
    explanation: 'Şifre kullanım maksimum süresi (30 dakika) doldu. İstek işlenmedi.',
    solution: 'Yeni mali yazıcı anahtarı oluşturun ve Servis Modu prosedürünü yeniden başlatın.',
    severity: 'warning',
  },
  {
    code: '116', hex: '80900702',
    explanation: 'Geçersiz şifre ile maksimum deneme sayısı aşıldı. İstek işlenmedi.',
    solution: 'Geçerli şifreyi girin ve cmd.0202h (Servis Şifresi Etkinleştir) komutunu tekrar gönderin.',
    severity: 'warning',
  },
  {
    code: '117', hex: '80900703',
    explanation: 'Geçersiz şifre. İstek işlenmedi.',
    solution: 'Geçerli şifreyi girin ve cmd.0202h (Servis Şifresi Etkinleştir) komutunu tekrar gönderin.',
    severity: 'warning',
  },
  {
    code: '118', hex: '80900704',
    explanation: 'Mali bellek ile iletişim hatası. İstek işlenmedi.',
    solution: 'Yazıcıyı kapatıp açın. Sorun devam ederse servise gönderin.',
    severity: 'critical',
  },
  {
    code: '119', hex: '80900677',
    explanation: 'Bu komut yalnızca yazdırma veya grafik indirme komut seti (Barkodlar) içinde gönderilebilir.',
    solution: 'Uygulama programı sıralamasını kontrol edin.',
    severity: 'info',
  },
  {
    code: '120', hex: '80900678',
    explanation: 'Komut çalıştırılırken yazıcı kartı zaman aşımı oluştu. İstek işlenmedi.',
    solution: 'Yazıcıyı kapatıp açın. Sorun devam ederse servise gönderin.',
    severity: 'critical',
  },
  {
    code: '121', hex: '80900679',
    explanation: 'İstenen ID sayısı çok büyük, istenen ID sayısı 0 veya ID geçersiz. İstek işlenmedi.',
    solution: 'Değeri düzeltin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '123', hex: '8090067B',
    explanation: 'Geçersiz boyut. Byte 4, 72\'den büyük. Yazdırma veya grafik indirme sırasında bu komut gönderilemez. İstek işlenmedi.',
    solution: 'Değeri düzeltin ve cmd.CAh (00h, 01h veya 02h uzantı) komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '124', hex: '8090067C',
    explanation: 'Aynı numaralı grafik zaten yazıcı flash belleğinde mevcut. İstek işlenmedi.',
    solution: 'Grafik numarasını düzeltin veya cmd.CA10h ile tüm grafikleri silin, ardından cmd.CA02h komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '125', hex: '8090067D',
    explanation: 'Geçersiz grafik numarası. İstek işlenmedi.',
    solution: 'Grafik numarasını düzeltin ve cmd.CAh (uzantı 02h, 11h veya 12h) komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '126', hex: '8090067E',
    explanation: 'Mali bellekteki FV birikmiş işlem toplamları tablosu dolu. İstek işlenmedi.',
    solution: 'Servis çağırın. Yeni FV birikmiş işlem toplamları tablosu için mali tabanın değiştirilmesi gerekir. NOT: Satış işlemi devam ediyorsa önce sonlandırın veya iptal edin.',
    severity: 'critical',
  },
  {
    code: '128', hex: '80900320',
    explanation: 'Mali bellek seri numarası atanmamış. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '129', hex: '80900321',
    explanation: 'Mali birim mali modda değil. İstek işlenmedi.',
    solution: 'Mali modu ayarlamak için servis çağırın.',
    severity: 'critical',
  },
  {
    code: '131', hex: '80900323',
    explanation: 'Ekranlar bağlı değil. İstek işlenmedi.',
    solution: 'Ekranları bağlayın. Sorun devam ederse servis çağırın.',
    severity: 'warning',
  },
  {
    code: '132', hex: '80900684',
    explanation: 'Departman isimleri yüklenmemiş. İstek işlenmedi.',
    solution: 'Departman isimleri yükle (cmd.22h) komutunu kullanarak ülkeye özgü prosedürleri izleyin.',
    severity: 'warning',
  },
  {
    code: '134', hex: '80900325',
    explanation: 'Mali birim dahili donanım hatası algıladı. İstek işlenmedi.',
    solution: 'Sorunun nedenini belirlemek için yazıcı testini çalıştırın. Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '135', hex: '80900220',
    explanation: 'Komut satış dönemi dışında geçerli değil. İstek işlenmedi.',
    solution: 'X-raporu çalıştırın (cmd.14h).',
    severity: 'warning',
  },
  {
    code: '136', hex: '80900221',
    explanation: 'Satış işlemi devam etmezken fiş ile ilgili satış işlemi komutu çalıştırıldı. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını düzeltin.',
    severity: 'warning',
  },
  {
    code: '138', hex: '80900223',
    explanation: 'Fiş devam etmezken fiş komutu çalıştırıldı. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını düzeltin.',
    severity: 'warning',
  },
  {
    code: '140', hex: '80900225',
    explanation: 'Mağaza başlığı yazdırılmadan önce mali fiş komutu çalıştırıldı. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını düzeltin.',
    severity: 'warning',
  },
  {
    code: '141', hex: '80900226',
    explanation: 'Toplam komutu başarıyla çalıştırılmadan önce izin verilmeyen bir komut çalıştırıldı. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını düzeltin.',
    severity: 'warning',
  },
  {
    code: '142', hex: '80900227',
    explanation: 'İşlem ödeme prosedürü devam etmiyor. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını düzeltin.',
    severity: 'warning',
  },
  {
    code: '144', hex: '80900229',
    explanation: 'Mağaza başlığı ayarlanmadan önce komut çalıştırıldı. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını düzelterek bu komuttan önce mağaza başlıklarını (cmd.D7h) ayarlayın.',
    severity: 'warning',
  },
  {
    code: '145', hex: '80900691',
    explanation: 'Servis modu aktif değilken komut kabul edilmez. cmd.FDh için CE Jumper aktif değilken komut kabul edilmez. İstek işlenmedi.',
    solution: 'Servis moduna girin veya CE jumperını etkinleştirin ve komutu tekrar deneyin.',
    severity: 'warning',
  },
  {
    code: '146', hex: '80900692',
    explanation: 'Mağaza/vergi mükellefi tanımlaması ayarlanmamış. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını düzelterek bu komuttan önce mağaza/vergi mükellefi tanımlamasını (cmd.1Eh) ayarlayın.',
    severity: 'warning',
  },
  {
    code: '153', hex: '80900699',
    explanation: 'Rol oturum açmamış veya oturum açan rol bu komutu çalıştırmaya yetkili değil. İstek işlenmedi.',
    solution: 'Rol oturumu açın veya oturum açan rolü değiştirin, ardından komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '155', hex: '8090069B',
    explanation: 'Aynı anda yalnızca bir rol oturum açabilir. İstek işlenmedi.',
    solution: 'Mevcut rolün oturumunu kapatın, ardından yeni rolün oturumunu açın.',
    severity: 'warning',
  },
  {
    code: '156', hex: '8090069C',
    explanation: 'Şifre öncekinden farklı olmalıdır. İstek işlenmedi.',
    solution: 'Şifreyi değiştirin ve komutu tekrar çalıştırın.',
    severity: 'info',
  },
  {
    code: '157', hex: '8090069D',
    explanation: 'Kredi kartı fişi yazdırma işlemi doğru şekilde devam etmiyor. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını düzeltin.',
    severity: 'warning',
  },
  {
    code: '158', hex: '8090069E',
    explanation: 'Tarih ve saat uygulama programı tarafından ayarlanmamış. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını düzeltin.',
    severity: 'warning',
  },
  {
    code: '160', hex: '80900330',
    explanation: 'Mali belleğe seri numarası atanmış. İstek işlenmedi.',
    solution: 'Herhangi bir işlem gerekmez.',
    severity: 'info',
  },
  {
    code: '161', hex: '80900331',
    explanation: 'Mali birim mali modda. İstek işlenmedi.',
    solution: 'Herhangi bir işlem gerekmez.',
    severity: 'info',
  },
  {
    code: '164', hex: '80900350',
    explanation: 'Güç açma sırası devam ediyor. İstek işlenmedi.',
    solution: 'Herhangi bir işlem gerekmez.',
    severity: 'info',
  },
  {
    code: '165', hex: '809006A5',
    explanation: 'Bu komut uzantısını çalıştırmak için izin verilen sayı aşıldı. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '167', hex: '80900230',
    explanation: 'Satış dönemi devam ederken istenen komut çalıştırılamaz. İstek işlenmedi.',
    solution: 'Satış dönemini kapatın (cmd.13h), ardından komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '168', hex: '80900231',
    explanation: 'Satış işlemi devam ederken bu belge ile ilgili olmayan bir komut gönderildi. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '170', hex: '80900233',
    explanation: 'Fiş devam ederken bu fiş ile ilgili olmayan bir komut gönderildi. İstek işlenmedi.',
    solution: 'Mali fiş tamamlandıktan sonra komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '172', hex: '80900235',
    explanation: 'Mağaza başlığı yazdırıldıktan sonra yalnızca satış işlemi komutuna izin verilir. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '173', hex: '80900236',
    explanation: 'Ara toplam/toplam komutu çalıştırıldıktan sonra komut sırası geçerli değil. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '174', hex: '80900237',
    explanation: 'Ödeme aşaması devam ediyor. İstek işlenmedi.',
    solution: 'Ödeme işlemi tamamlandıktan sonra isteği tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '175', hex: '80900238',
    explanation: 'RC 1012 açıklamasına bakın (Komut şifreli değil).',
    solution: 'Komutu şifreleyin.',
    severity: 'warning',
  },
  {
    code: '176', hex: '80900239',
    explanation: 'Mali birim dahili donanım hatası algıladı. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '177', hex: '809006B1',
    explanation: 'Tek CE jumper işleminden sonra günlük değerler ilgili EJ ortamından geri yüklenmedi. İstek işlenmedi.',
    solution: 'Mali yazıcıyı kapatın, takılı EJ ortamının doğru olduğunu kontrol edin ve geri yükleme işleminin yapılması için yazıcıyı açın.',
    severity: 'critical',
  },
  {
    code: '178', hex: '809006B2',
    explanation: 'Satış dönemi kapanışı (kapanış raporu) gerekli. İstek işlenmedi.',
    solution: 'Satış dönemi kapanışını gönderin (cmd.13h).',
    severity: 'warning',
  },
  {
    code: '179', hex: '809006B3',
    explanation: 'RC 1011 açıklamasına bakın (Komut özyinelemeli olamaz).',
    solution: 'Uygulama programını düzeltin.',
    severity: 'warning',
  },
  {
    code: '180', hex: '809006B4',
    explanation: 'Mali EPROM hatalı. EPROM seri numarası atanmış ancak şablon bulunamadı. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '181', hex: '809006B5',
    explanation: 'NVRAM hatalı. cmd.FBh\'ye dönüş.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '182', hex: '809006B6',
    explanation: 'İşlem sonlandırma (cmd.06h) sırasında hata oluştu. İstek işlenmedi.',
    solution: 'İşlem sonlandırma (cmd.06h) komutunu tekrar çalıştırın. Çevrimiçi yazıcı tanılama testi sırasında oluşursa, işlemi tamamlamak için cmd.06h çalıştırılmalıdır.',
    severity: 'warning',
  },
  {
    code: '183', hex: '809006B7',
    explanation: 'İşlem iptal (cmd.07h) sırasında hata oluştu. İstek işlenmedi.',
    solution: 'İşlem iptal (cmd.07h) komutunu tekrar çalıştırın. Çevrimiçi yazıcı tanılama testi sırasında oluşursa, işlemi tamamlamak için cmd.07h çalıştırılmalıdır.',
    severity: 'warning',
  },
  {
    code: '184', hex: '809006B8',
    explanation: 'Mali Olmayan Rapor çalıştırılırken izin verilmeyen bir komut istendi. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '185', hex: '809006B9',
    explanation: 'Yazıcı mantık kartında EPROM yükleme hatası oluştu. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '186', hex: '809006BA',
    explanation: 'NVRAM mali mod bayrağı ile EPROM işareti arasında uyuşmazlık. İstek işlenmedi.',
    solution: 'CE jumperını takın ve yazıcıyı yeniden başlatın. Sorun devam ederse servise gönderin.',
    severity: 'critical',
  },
  {
    code: '187', hex: '809006BB',
    explanation: 'Mali bellekten okunan blok boş. İstek işlenmedi.',
    solution: 'Uygulama programını kontrol edin.',
    severity: 'warning',
  },
  {
    code: '189', hex: '809006BD',
    explanation: 'Farklı bir kredi kartı fişi yazdırma modu zaten devam ediyor, seçilen mod sonlandırılamaz. Yazıcı bloke edildi. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını kontrol edin. Teknisyen müdahale etmelidir.',
    severity: 'critical',
  },
  {
    code: '191', hex: '809006BF',
    explanation: 'RC 1010 açıklamasına bakın (Geçersiz CRC32).',
    solution: 'Uygulama programını düzeltin.',
    severity: 'warning',
  },
  {
    code: '192', hex: '80900524',
    explanation: 'Komut yazıcı mantık kartı tarafından reddedildi. İstek işlenmedi.',
    solution: 'Aygıt sürücüsü programlama hatası kontrol edin.',
    severity: 'critical',
  },
  {
    code: '194', hex: '80900521',
    explanation: 'Baskı kafası başlangıç pozisyonu hatası oluştu. (4690 OS\'ta bu hata, başlangıç hataları dışında diğer yazıcı sorunları için de raporlanabilir.) İstek işlenmedi.',
    solution: 'Sorun devam ederse yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '200', hex: '8090070D',
    explanation: 'CR yazıcı kapağı açık veya CR kağıt bitti. İstek işlenmedi.',
    solution: 'CR kapağını kapatın veya CR kağıdının doğru takıldığından emin olun. Sorun devam ederse servise gönderin. Durumu belirlemek için: mali durumda byte 0 bit 6 ayarlıysa cmd.F803h ile tam durum alın.',
    severity: 'critical',
  },
  {
    code: '201', hex: '80900528',
    explanation: 'DI yazıcı kapağı açık. İstek işlenmedi.',
    solution: 'DI kapağını kapatın. Sorun devam ederse servise gönderin.',
    severity: 'warning',
  },
  {
    code: '202', hex: '80900527',
    explanation: 'Takılı belge hazır değil. İstek işlenmedi.',
    solution: 'Belgeyi çıkarıp tekrar takmayı deneyin. Sorun devam ederse servise gönderin.',
    severity: 'warning',
  },
  {
    code: '204', hex: '80900711',
    explanation: 'Yazıcı dahili hatası. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: '205', hex: '80900526',
    explanation: 'Yazıcı tuşuna basılı. İstek işlenmedi.',
    solution: 'Basılı tuşu bırakın. Tuş basılı değilse servise gönderin.',
    severity: 'warning',
  },
  {
    code: '208', hex: '809006D0',
    explanation: 'İndirilen grafik, logo veya ayarlanan karakter bozuk. İstek işlenmedi.',
    solution: 'İndirilen grafik bozuksa cmd.CA10h ile tüm grafikleri silin, ardından cmd.CA02h komutunu tekrar çalıştırın. Grafik veya karakter bozuksa servise gönderin.',
    severity: 'warning',
  },
  {
    code: '210', hex: '809006D2',
    explanation: 'Yazıcı DI boğaz bölümü açık. İstek işlenmedi.',
    solution: 'Boğaz bölümünü kapatın ve yazdırma komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: '214', hex: '80900527',
    explanation: 'Kağıt besleme hatası oluştu. İstek işlenmedi.',
    solution: 'Kağıdın doğru takıldığından emin olun.',
    severity: 'warning',
  },
  {
    code: '235', hex: '809006EB',
    explanation: 'EPROM yükleme hatası. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
];

const specialCodes: ErrorCode[] = [
  {
    code: '1005', hex: undefined,
    explanation: 'Z-Raporu mesajı, Gelir İdaresi sunucularına bağlantı olmadan izin verilen maksimum takvim günü sayısı boyunca gönderilmedi (Parametre Yükleme mesajında tanımlanmış). Mali yazıcı bloke edildi. İstek işlenmedi.',
    solution: 'Teknisyen servis modunu etkinleştirerek yazıcının blokesini kaldırmalı ve normal çalışma durumuna döndürmelidir.',
    severity: 'critical',
  },
  {
    code: '1006', hex: undefined,
    explanation: 'Mali yazıcı bloke edildi, çünkü TSM "Mali Yazıcıyı Bloke Et" görevini Görev Zamanlayıcı Mesajında gönderdi ve görev çalıştırıldı. İstek işlenmedi.',
    solution: 'Teknisyen servis modunu etkinleştirerek yazıcının blokesini kaldırmalı ve normal çalışma durumuna döndürmelidir.',
    severity: 'critical',
  },
  {
    code: '1010', hex: undefined,
    explanation: 'Geçersiz CRC32. İstek işlenmedi.',
    solution: 'Uygulama programını düzeltin.',
    severity: 'warning',
  },
  {
    code: '1011', hex: undefined,
    explanation: 'Komut özyinelemeli olamaz. İstek işlenmedi.',
    solution: 'Uygulama programını düzeltin.',
    severity: 'warning',
  },
  {
    code: '1012', hex: undefined,
    explanation: 'Komut şifrelenmemiş. İstek işlenmedi.',
    solution: 'Komutu şifreleyin.',
    severity: 'warning',
  },
];

const extendedCodes: ErrorCode[] = [
  {
    code: 'E-001', hex: '01h',
    explanation: 'EJ ortamı takılı değil. İstek işlenmedi.',
    solution: 'EJ ortamını takın ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-002', hex: '02h',
    explanation: 'EJ ortamında yeterli boş alan yok. İstek işlenmedi.',
    solution: 'Mevcut EJ dosyasını kapatmak için cmd.13h çalıştırın veya açık mali fiş varsa kapatın, ardından EJ dosyasını kapatın.',
    severity: 'warning',
  },
  {
    code: 'E-003', hex: '03h',
    explanation: 'Flash bellek aktarım hatası. İstek işlenmedi.',
    solution: 'Komutu tekrar deneyin. Hata devam ederse EJ ortamını değiştirin, CE jumperını etkinleştirin ve yazıcıyı yeniden başlatın.',
    severity: 'critical',
  },
  {
    code: 'E-004', hex: '04h',
    explanation: 'Açık EJ dosyası varken EJ ortamı değiştirildi.',
    solution: 'Eski EJ ortamını tekrar takın.',
    severity: 'warning',
  },
  {
    code: 'E-005', hex: '05h',
    explanation: 'Dahili hata. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: 'E-006', hex: '06h',
    explanation: 'Dahili hata. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: 'E-007', hex: '07h',
    explanation: 'Dahili hata. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: 'E-008', hex: '08h',
    explanation: 'EJ ortamı yanlış biçimde. İstek işlenmedi.',
    solution: 'IPL işlemi sırasında EJ ortamını biçimlendirin.',
    severity: 'warning',
  },
  {
    code: 'E-009', hex: '09h',
    explanation: 'Dahili hata. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: 'E-010', hex: '0Ah',
    explanation: 'Dahili hata. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: 'E-011', hex: '0Bh',
    explanation: 'EJ dosya adı zaten mevcut. İstek işlenmedi.',
    solution: 'EJ dosya adını değiştirin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-012', hex: '0Ch',
    explanation: 'Geçersiz EJ dosya adı. İstek işlenmedi.',
    solution: 'EJ dosya adını değiştirin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-013', hex: '0Dh',
    explanation: 'EJ dosya adında geçersiz karakter. Dosya adları yalnızca A-Z, a-z, 0-9, \'-\' ve \'_\' karakterlerini içerebilir. İstek işlenmedi.',
    solution: 'EJ dosya adını değiştirin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-014', hex: '0Eh',
    explanation: 'Anahtar uzunluğu veya komut uzunluğu geçersiz. İstek işlenmedi.',
    solution: 'Uygulama programı sıralamasını kontrol edin.',
    severity: 'warning',
  },
  {
    code: 'E-015', hex: '0Fh',
    explanation: 'Belirtilen algoritma bilinmiyor. İstek işlenmedi.',
    solution: 'Giriş verilerini düzeltin ve Genel/Özel Anahtar Ayarla (cmd.66h) komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-016', hex: '10h',
    explanation: 'Belirtilen anahtar, mikrokod tarafından işlenemeyecek kadar uzun. İstek işlenmedi.',
    solution: 'Giriş verilerini düzeltin ve Genel/Özel Anahtar Ayarla (cmd.66h) komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-017', hex: '11h',
    explanation: 'Belirtilen uzunluk geçersiz. İstek işlenmedi.',
    solution: 'Giriş verilerini düzeltin ve Genel/Özel Anahtar Ayarla (cmd.66h) komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-018', hex: '12h',
    explanation: 'Genel ve özel anahtar ayarlama komutu sırasında geçersiz sıralama. İstek işlenmedi.',
    solution: 'Giriş verilerini düzeltin ve Genel/Özel Anahtar Ayarla (cmd.66h) komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-019', hex: '13h',
    explanation: 'Anahtar asal veya alt asal değeri geçersiz. İstek işlenmedi.',
    solution: 'Giriş verilerini düzeltin ve Genel/Özel Anahtar Ayarla (cmd.66h) komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-020', hex: '14h',
    explanation: 'Anahtar tabanı geçersiz. İstek işlenmedi.',
    solution: 'Giriş verilerini düzeltin ve Genel/Özel Anahtar Ayarla (cmd.66h) komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-021', hex: '15h',
    explanation: 'Genel ve özel anahtar geçerli bir çift oluşturmuyor. İstek işlenmedi.',
    solution: 'Giriş verilerini düzeltin ve Genel/Özel Anahtar Ayarla (cmd.66h) komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-022', hex: '16h',
    explanation: 'Anahtar asal ve alt asal geçerli bir çift oluşturmuyor. İstek işlenmedi.',
    solution: 'Giriş verilerini düzeltin ve Genel/Özel Anahtar Ayarla (cmd.66h) komutunu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-024', hex: '18h',
    explanation: 'Anahtar ayarlanmamış. İstek işlenmedi.',
    solution: 'Genel/Özel Anahtar Ayarla (cmd.66h) komutunu çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-025', hex: '19h',
    explanation: 'Anahtar bozuk. İstek işlenmedi.',
    solution: 'Mali belleği (FM) değiştirin.',
    severity: 'critical',
  },
  {
    code: 'E-026', hex: '1Ah',
    explanation: 'Dahili hata. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: 'E-027', hex: '1Bh',
    explanation: 'Dosya bulunamadı. EJ ortamında belirtilen dosya adıyla eşleşen dosya yok veya başka dosya kalmamış. İstek işlenmedi.',
    solution: 'Doğru dosya adını belirtin.',
    severity: 'warning',
  },
  {
    code: 'E-028', hex: '1Ch',
    explanation: 'Geçersiz dosya adı karakterleri. Belirtilen dosya adı geçersiz karakterler içeriyor. İstek işlenmedi.',
    solution: 'Uygulama programını düzeltin.',
    severity: 'warning',
  },
  {
    code: 'E-029', hex: '1Dh',
    explanation: 'EJ dosyası zaten açık. Bir EJ dosyası halihazırda açık. İstek işlenmedi.',
    solution: 'Uygulama programını düzeltin.',
    severity: 'warning',
  },
  {
    code: 'E-030', hex: '1Eh',
    explanation: 'EJ dosyası açık değil. Açık bir EJ dosyası yok. İstek işlenmedi.',
    solution: 'Uygulama programını düzeltin.',
    severity: 'warning',
  },
  {
    code: 'E-031', hex: '1Fh',
    explanation: 'Dahili hata. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: 'E-032', hex: '20h',
    explanation: 'Geçersiz EJ dosya öznitelikleri. İstenen EJ dosya özniteliği geçersiz. İstek işlenmedi.',
    solution: 'Uygulama programını düzeltin.',
    severity: 'warning',
  },
  {
    code: 'E-034', hex: '22h',
    explanation: 'Sıkıştırma türü bilinmiyor. İstek işlenmedi.',
    solution: 'Yazıcıyı servise gönderin.',
    severity: 'critical',
  },
  {
    code: 'E-035', hex: '23h',
    explanation: 'CR istasyonundaki mali veya mali olmayan belge içindeki yazdırma satırları sırasında EJ ortamında yeterli alan yok. İstek işlenmedi.',
    solution: 'Mevcut belgeyi iptal edin/sonlandırın, satış dönemini kapatın (cmd.13h), EJ dosyasını aktarın ve aktarımı onaylayın (cmd.6Fh).',
    severity: 'critical',
  },
  {
    code: 'E-036', hex: '24h',
    explanation: 'EJ dosyası yazmak için yeterli alan yok (ör: EJ dosyası aç, satış dönemi aç veya yeni belge aç). İstek işlenmedi.',
    solution: 'Gerekirse satış dönemini kapatın (cmd.13h), EJ dosyasını aktarın ve aktarımı onaylayın (cmd.6Fh).',
    severity: 'critical',
  },
  {
    code: 'E-037', hex: '25h',
    explanation: 'EJ dosyası okuma sıralaması geçersiz.',
    solution: 'Sıralama "0" (ilk blok), "n" (son okunan bloku tekrar oku) veya "n + 1" (sonraki bloku oku) olmalıdır.',
    severity: 'warning',
  },
  {
    code: 'E-038', hex: '26h',
    explanation: 'EJ ortamı donanım hatası. Tanılama testini geçemedi.',
    solution: 'Yeni EJ ortamı takın.',
    severity: 'critical',
  },
  {
    code: 'E-040', hex: '28h',
    explanation: 'EJ ortamı donanım hatası.',
    solution: 'Komutu tekrar çalıştırın. Sorun devam ederse EJ ortamını çıkarıp tekrar takın ve komutu tekrar çalıştırın.',
    severity: 'critical',
  },
  {
    code: 'E-041', hex: '29h',
    explanation: 'Belirtilen arşiv öznitelik durumu geçersiz.',
    solution: 'Doğru arşiv öznitelik durumunu seçin ve komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-043', hex: '2Bh',
    explanation: 'EJ ortamı hazır değil.',
    solution: 'Komutu tekrar çalıştırın.',
    severity: 'warning',
  },
  {
    code: 'E-044', hex: '2Ch',
    explanation: 'Sıkıştırma türü tanınmıyor. İstek işlenmedi.',
    solution: 'Geçerli bir sıkıştırma türü kullanın.',
    severity: 'warning',
  },
  {
    code: 'E-045', hex: '2Dh',
    explanation: 'Sıkıştırılmış dosya bozuk. İstek işlenmedi.',
    solution: 'Dosyayı yeniden oluşturun.',
    severity: 'critical',
  },
  {
    code: 'E-046', hex: '2Eh',
    explanation: 'Geçersiz imza. İstek işlenmedi.',
    solution: 'İmzayı kontrol edin ve düzeltin.',
    severity: 'critical',
  },
  {
    code: 'E-049', hex: '31h',
    explanation: 'EJ ortamı tanınmıyor. İstek işlenmedi.',
    solution: 'EJ ortamını tekrar takın ve komutu çalıştırın. Sorun devam ederse EJ ortamını değiştirin, hala devam ederse EJ ortam sürücüsünü değiştirmek için servis çağırın.',
    severity: 'critical',
  },
  {
    code: 'E-068', hex: '44h',
    explanation: 'Bir EJ dosyasının doğrulaması başarısız oldu. Dosya bozuk.',
    solution: 'Dosyayı yeniden oluşturmayı deneyin veya servis çağırın.',
    severity: 'critical',
  },
  {
    code: 'E-096', hex: '60h',
    explanation: 'Geçersiz USB adresi. Cihaz sahte adres kullanıyor, cihaz bağlantısı kesilmiş veya CİHAZ ADRESİ KAYDET komutunda yanlış adres gönderilmiş olabilir. İstek işlenmedi.',
    solution: 'Doğru adres değerleri: kayıt silme için 00h, cihaz kaydetme için 40h-7Fh.',
    severity: 'warning',
  },
  {
    code: 'E-097', hex: '61h',
    explanation: 'Geçersiz rapor. Belirtilen rapor ID\'si bu cihaz için mevcut değil. İstek işlenmedi.',
    solution: 'Doğru rapor ID\'si belirtin.',
    severity: 'warning',
  },
  {
    code: 'E-098', hex: '62h',
    explanation: 'Farklı yazma veya okuma uzunluğu. Bu rapor ID\'si için yazma/okuma uzunluğu doğru uzunluktan farklı. İstek işlenmedi.',
    solution: 'Doğru uzunluk değerini kullanın.',
    severity: 'warning',
  },
  {
    code: 'E-099', hex: '63h',
    explanation: 'Okuma hatası. Önceki paketin okunması READ PACKET CONTINUATION komutuyla tamamlanmadan READ DEVICES, READ INPUT REPORT veya READ FEATURE REPORT komutlarıyla bilgi okuma girişimi. İstek işlenmedi.',
    solution: 'Önce önceki okuma işlemini tamamlayın.',
    severity: 'warning',
  },
  {
    code: 'E-100', hex: '64h',
    explanation: 'Cihaz adresi zaten kayıtlı. Başka bir kayıtlı cihazla aynı adrese sahip bir cihazı kaydetme girişimi. İstek işlenmedi.',
    solution: 'Farklı bir adres kullanın.',
    severity: 'warning',
  },
  {
    code: 'E-101', hex: '65h',
    explanation: 'Tek bir CİHAZ ADRESİ KAYDET komutunda kayıt edilecek çok fazla cihaz. İstek işlenmedi.',
    solution: 'Cihazları birden fazla komutla kaydedin.',
    severity: 'warning',
  },
  {
    code: 'E-102', hex: '66h',
    explanation: 'READ INPUT REPORT komutuna yanıt yok. Bu hata, cihazdan olay almadan önce bu komutu gönderme girişimini gösterir. İstek işlenmedi.',
    solution: 'Cihazdan olay beklendikten sonra komutu gönderin.',
    severity: 'warning',
  },
  {
    code: 'E-103', hex: '67h',
    explanation: 'Yazma arabelleği dolu. Çok fazla WRITE PACKET CONTINUATION komutu gönderildi. Yazma arabelleği geçersiz kılındı. İstek işlenmedi.',
    solution: 'Daha az paket gönderin veya önceki yazmayı tamamlayın.',
    severity: 'warning',
  },
  {
    code: 'E-104', hex: '68h',
    explanation: 'WRITE OUTPUT REPORT veya WRITE PACKET CONTINUATION komutlarında yazma için tamamlanmamış arabellek. İstek işlenmedi.',
    solution: 'Tüm verileri gönderin.',
    severity: 'warning',
  },
  {
    code: 'E-105', hex: '69h',
    explanation: 'Önceki READ FEATURE REPORT veya READ DEVICES INFORMATION komutu olmadan READ PACKET CONTINUATION gönderme girişimi, veya önceki WRITE OUTPUT REPORT komutu olmadan WRITE PACKET CONTINUATION gönderme girişimi. İstek işlenmedi.',
    solution: 'Doğru komut sıralamasını izleyin.',
    severity: 'warning',
  },
  {
    code: 'E-112', hex: '70h',
    explanation: 'Geçersiz arabellek bilgisi. Arabellekte kaydedilen veri geçerli bir mikrokoda karşılık gelmiyor. İstek işlenmedi.',
    solution: 'Geçerli bir mikrokod kullanarak tekrar deneyin.',
    severity: 'warning',
  },
  {
    code: 'E-113', hex: '71h',
    explanation: 'Flash yazma aktarım hatası. Yazma işlemi sırasında donanım hatası oluştu. Flash bellek hasar görmüş ve değiştirilmelidir. İstek tamamen işlenmedi.',
    solution: 'Flash belleği değiştirin.',
    severity: 'critical',
  },
  {
    code: 'E-128', hex: '80h',
    explanation: 'Takılı EJ ortamı geçersiz. EJ ortam seri numarası FM seri numarasıyla aynı değil VEYA EJ ortam numarası mali yazıcı EJ ortam numarasından küçük. İstek işlenmedi.',
    solution: 'Mali yazıcıda son kullanılan EJ ortamını takın veya yeni bir EJ ortamı takın.',
    severity: 'critical',
  },
  {
    code: 'E-129', hex: '81h',
    explanation: 'Harici EJ dosyasının doğrulaması başarısız oldu. Dosya bozuk. İstek işlenmedi.',
    solution: 'Dosyayı yeniden oluşturmayı deneyin veya servis çağırın.',
    severity: 'critical',
  },
];

const categories: ErrorCategory[] = [
  {
    title: 'Standart Hata Kodları (002 - 235)',
    description: 'Temel mali yazıcı hata kodları. İşlem taşmaları, mali bellek hataları, yazıcı donanım hataları, komut sıralama ihlalleri ve sertifika sorunlarını kapsar.',
    codes: standardCodes,
  },
  {
    title: 'Özel Kodlar (1005 - 1012) — Yalnızca Linux/Windows',
    description: 'Mali yazıcı blokeleme, CRC hataları, özyinelemeli komutlar ve şifreleme gereksinimleri ile ilgili kodlar. 4690 OS tarafından desteklenmez.',
    codes: specialCodes,
  },
  {
    title: 'Genişletilmiş Hata Kodları (E-001 - E-129)',
    description: 'Genel hata RC 084 alındıktan sonra cmd.63h ile elde edilen detaylı hata kodları. Ağırlıklı olarak EJ (Elektronik Jurnal) ortam hataları, kriptografik anahtar hataları, USB cihaz hataları ve flash bellek sorunlarını kapsar.',
    codes: extendedCodes,
  },
];

const severityConfig = {
  info: { bg: 'bg-blue-500/10', border: 'border-blue-500/30', text: 'text-blue-400', label: 'Bilgi' },
  warning: { bg: 'bg-amber-500/10', border: 'border-amber-500/30', text: 'text-amber-400', label: 'Uyarı' },
  critical: { bg: 'bg-red-500/10', border: 'border-red-500/30', text: 'text-red-400', label: 'Kritik' },
};

const FiscalErrorCodesPage: React.FC = () => {
  const [search, setSearch] = useState('');
  const [expandedCategory, setExpandedCategory] = useState<number | null>(0);
  const [severityFilter, setSeverityFilter] = useState<string>('all');

  const filteredCategories = useMemo(() => {
    const q = search.toLowerCase().trim();
    return categories.map(cat => ({
      ...cat,
      codes: cat.codes.filter(c => {
        const matchSearch = !q ||
          c.code.toLowerCase().includes(q) ||
          (c.hex?.toLowerCase().includes(q) ?? false) ||
          c.explanation.toLowerCase().includes(q) ||
          c.solution.toLowerCase().includes(q);
        const matchSeverity = severityFilter === 'all' || c.severity === severityFilter;
        return matchSearch && matchSeverity;
      }),
    }));
  }, [search, severityFilter]);

  const totalFiltered = filteredCategories.reduce((sum, c) => sum + c.codes.length, 0);
  const totalAll = categories.reduce((sum, c) => sum + c.codes.length, 0);

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-ms-text flex items-center gap-3">
            <div className="p-2 rounded-xl bg-violet-600/20">
              <Printer className="w-6 h-6 text-violet-400" />
            </div>
            Mali Hata Kodları
          </h1>
          <p className="text-ms-text-muted text-sm mt-1">
            Toshiba 4610/4690 Mali Yazıcı Donanım Hata Kodları — Toplam {totalAll} kod
          </p>
        </div>
      </div>

      {/* Search & Filter Bar */}
      <div className="flex flex-col sm:flex-row gap-3">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-zinc-500" />
          <input
            type="text"
            placeholder="Hata kodu, hex kodu veya açıklama ara..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full pl-10 pr-4 py-2.5 bg-ms-bg-soft border border-ms-border rounded-lg text-sm text-ms-text placeholder:text-zinc-600 focus:outline-none focus:ring-1 focus:ring-violet-500 focus:border-violet-500"
          />
        </div>
        <div className="flex gap-2">
          {[
            { key: 'all', label: 'Tümü' },
            { key: 'critical', label: 'Kritik' },
            { key: 'warning', label: 'Uyarı' },
            { key: 'info', label: 'Bilgi' },
          ].map(f => (
            <button
              key={f.key}
              onClick={() => setSeverityFilter(f.key)}
              className={`px-3 py-2 text-xs font-medium rounded-lg border transition-colors ${
                severityFilter === f.key
                  ? 'bg-violet-600/20 border-violet-500 text-violet-400'
                  : 'bg-ms-bg-soft border-ms-border text-ms-text-muted hover:text-ms-text hover:border-zinc-600'
              }`}
            >
              {f.label}
            </button>
          ))}
        </div>
      </div>

      {/* Result count */}
      {(search || severityFilter !== 'all') && (
        <div className="text-xs text-ms-text-muted">
          {totalFiltered} / {totalAll} sonuç gösteriliyor
        </div>
      )}

      {/* Categories */}
      <div className="space-y-4">
        {filteredCategories.map((cat, catIdx) => (
          <div key={catIdx} className="bg-ms-bg-soft border border-ms-border rounded-xl overflow-hidden">
            {/* Category Header */}
            <button
              onClick={() => setExpandedCategory(expandedCategory === catIdx ? null : catIdx)}
              className="w-full flex items-center justify-between px-5 py-4 hover:bg-zinc-800/40 transition-colors"
            >
              <div className="text-left">
                <h2 className="text-sm font-semibold text-ms-text">{cat.title}</h2>
                <p className="text-xs text-ms-text-muted mt-0.5">{cat.description}</p>
              </div>
              <div className="flex items-center gap-3 shrink-0 ml-4">
                <span className="text-xs text-violet-400 font-medium bg-violet-500/10 px-2.5 py-1 rounded-full">
                  {cat.codes.length} kod
                </span>
                {expandedCategory === catIdx ? (
                  <ChevronUp className="w-4 h-4 text-zinc-500" />
                ) : (
                  <ChevronDown className="w-4 h-4 text-zinc-500" />
                )}
              </div>
            </button>

            {/* Codes Table */}
            {expandedCategory === catIdx && cat.codes.length > 0 && (
              <div className="border-t border-ms-border">
                <div className="divide-y divide-ms-border/50">
                  {cat.codes.map((code) => {
                    const sev = severityConfig[code.severity];
                    return (
                      <div key={code.code} className="px-5 py-4 hover:bg-zinc-800/20 transition-colors">
                        <div className="flex items-start gap-4">
                          {/* Code badge */}
                          <div className="shrink-0 pt-0.5">
                            <div className={`px-3 py-1.5 rounded-lg text-xs font-mono font-bold ${sev.bg} ${sev.border} ${sev.text} border`}>
                              {code.code.startsWith('E-') ? `Mali Hata ${code.code}` : `Mali Hata ${code.code}`}
                            </div>
                            {code.hex && (
                              <div className="text-[10px] text-zinc-600 font-mono text-center mt-1">
                                {code.hex}
                              </div>
                            )}
                          </div>

                          {/* Content */}
                          <div className="flex-1 min-w-0 space-y-2">
                            {/* Explanation */}
                            <div>
                              <div className="text-[10px] uppercase tracking-wider text-zinc-600 font-semibold mb-0.5">Sorun</div>
                              <p className="text-sm text-ms-text leading-relaxed">{code.explanation}</p>
                            </div>
                            {/* Solution */}
                            <div>
                              <div className="text-[10px] uppercase tracking-wider text-zinc-600 font-semibold mb-0.5">Çözüm</div>
                              <p className="text-sm text-emerald-400/90 leading-relaxed">{code.solution}</p>
                            </div>
                          </div>

                          {/* Severity badge */}
                          <div className="shrink-0">
                            <span className={`inline-flex items-center gap-1 px-2 py-1 rounded text-[10px] font-semibold ${sev.bg} ${sev.text}`}>
                              {code.severity === 'critical' && <AlertTriangle className="w-3 h-3" />}
                              {sev.label}
                            </span>
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {expandedCategory === catIdx && cat.codes.length === 0 && (
              <div className="border-t border-ms-border px-5 py-8 text-center text-sm text-ms-text-muted">
                Arama kriterlerine uygun hata kodu bulunamadı.
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
};

export default FiscalErrorCodesPage;
