import personelData from '../data/personel.json';

export interface PersonelRecord {
  no: string;
  ad: string;
  soyad: string;
  gorevKod: number;
  gorev: string;
  dogumYeri: string;
  dogumTarih: string;
  girisTarih: string;
  departmanKod: number;
  departman: string;
}

export interface TeamMember {
  no: string;
  name: string;
  surname: string;
  role: string;
  roleCode: number;
  birthPlace: string;
  birthDate: string;
  startDate: string;
}

export interface BirthdayInfo {
  nextBirthday: Date;
  daysUntil: number;
  ageTurning: number;
  label: string;
  shortLabel: string;
  isToday: boolean;
}

const allPersonel = personelData as PersonelRecord[];

const startDateOverrides: Record<string, string> = {
  P9632: '15/01/2008',
};

const teamMemberConfigs = [
  { no: 'P20516' },
  { no: 'P35448' },
  { no: 'P36914' },
  { no: 'P37859', name: 'NİSAN', surname: 'GÜRBÜZ' },
  { no: 'P34065' },
  { no: 'P34076' },
  { no: 'P36941', name: 'MUSTAFA', surname: 'KOCATEPE' },
  { no: 'P38540' },
  { no: 'P34767', name: 'ÜMMÜHAN', surname: 'KURT' },
  { no: 'P35690', name: 'ELİF', surname: 'KARAKAŞ' },
  { no: 'P37112', name: 'KADİR', surname: 'TURAN' },
  { no: 'P13947', name: 'ÜMİT', surname: 'SARILI' },
  { no: 'P32993' },
  { no: 'P9632' },
  { no: 'P38544', name: 'UĞUR', surname: 'HAMAMCI' },
  { no: 'P25763' },
  { no: 'P33672' },
  { no: 'P36311' },
  { no: 'P18265' },
  { no: 'P34642' },
  { no: 'P38215' },
] as const;

export function parseDate(value: string): Date {
  const [day, month, year] = value.split('/').map(Number);
  return new Date(year, month - 1, day);
}

export function formatName(value: string): string {
  return value
    .split(' ')
    .filter(Boolean)
    .map((word) => word.charAt(0) + word.slice(1).toLocaleLowerCase('tr-TR'))
    .join(' ');
}

function normalizeToDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

export function getBirthdayInfo(birthDate: string, now = new Date()): BirthdayInfo {
  const birth = parseDate(birthDate);
  const today = normalizeToDay(now);
  let nextBirthday = new Date(today.getFullYear(), birth.getMonth(), birth.getDate());
  if (nextBirthday < today) {
    nextBirthday = new Date(today.getFullYear() + 1, birth.getMonth(), birth.getDate());
  }

  const msPerDay = 1000 * 60 * 60 * 24;
  const daysUntil = Math.round((normalizeToDay(nextBirthday).getTime() - today.getTime()) / msPerDay);
  const ageTurning = nextBirthday.getFullYear() - birth.getFullYear();
  const isToday = daysUntil === 0;

  let shortLabel = '';
  let label = '';
  if (daysUntil === 0) {
    shortLabel = 'Bugün';
    label = `Bugün doğum günü, ${ageTurning} oluyor`;
  } else if (daysUntil === 1) {
    shortLabel = 'Yarın';
    label = `Yarın doğum günü, ${ageTurning} olacak`;
  } else {
    shortLabel = `${daysUntil} gün`;
    label = `${daysUntil} gün kaldı, ${ageTurning} olacak`;
  }

  return { nextBirthday, daysUntil, ageTurning, label, shortLabel, isToday };
}

export const teamMembers: TeamMember[] = teamMemberConfigs
  .map((config) => {
    const person = allPersonel.find((item) => item.no === config.no);
    if (!person) return null;

    return {
      no: person.no,
      name: config.name ?? person.ad,
      surname: config.surname ?? person.soyad,
      role: person.gorev,
      roleCode: person.gorevKod,
      birthPlace: person.dogumYeri,
      birthDate: person.dogumTarih,
      startDate: startDateOverrides[person.no] ?? person.girisTarih,
    };
  })
  .filter((member): member is TeamMember => member !== null);
