import React, { useState, useMemo } from 'react';
import { Search, Briefcase, MapPin, Calendar, Clock, Users2, Crown, Star, ChevronUp } from 'lucide-react';

interface Period {
  from: string;
  to?: string; // undefined = hâlâ devam ediyor
}

interface TeamMember {
  name: string;
  surname: string;
  role: string;
  roleCode: number;
  birthPlace: string;
  birthDate: string;
  startDate: string;
  periods?: Period[]; // birden fazla dönem varsa
}

const teamMembers: TeamMember[] = [
  { name: 'ÖNCÜ',       surname: 'ÖZMETE',   role: 'Genel Müdür Yardımcısı', roleCode: 55, birthPlace: 'ANKARA',   birthDate: '14/06/1984', startDate: '03/03/2014' },
  { name: 'ÖZGÜR',      surname: 'ALTUN',     role: 'Müdür',                  roleCode: 50, birthPlace: 'GÖNEN',    birthDate: '28/03/1984', startDate: '15/01/2008', periods: [{ from: '15/01/2008', to: '30/11/2022' }, { from: '03/01/2024' }] },
  { name: 'TUĞRUL',     surname: 'DÖNMEZ',    role: 'Yönetici',               roleCode: 53, birthPlace: 'KADİKÖY',  birthDate: '22/11/1995', startDate: '13/09/2021' },
  { name: 'GÖKHAN',     surname: 'BALTACI',   role: 'Kıdemli Uzman',          roleCode: 64, birthPlace: 'BAKIRKÖY', birthDate: '07/09/1993', startDate: '19/09/2016' },
  { name: 'İSA',        surname: 'ERASLAN',   role: 'Kıdemli Uzman',          roleCode: 64, birthPlace: 'ÇATAK',    birthDate: '05/11/1990', startDate: '17/07/2014' },
  { name: 'ÜMİT YASİN',surname: 'SARILI',    role: 'Kıdemli Uzman',          roleCode: 64, birthPlace: 'HENDEK',   birthDate: '01/02/1976', startDate: '07/06/2011' },
  { name: 'CİHAN',      surname: 'ÖZCAN',     role: 'Kıdemli Uzman',          roleCode: 64, birthPlace: 'BAKIRKÖY', birthDate: '07/03/1995', startDate: '08/02/2023' },
  { name: 'KADİR',      surname: 'GÜREL',     role: 'Kıdemli Uzman',          roleCode: 64, birthPlace: 'TUZLA',    birthDate: '06/01/1999', startDate: '23/05/2022' },
  { name: 'UĞUR',       surname: 'HAMAMCI',   role: 'Uzman Yardımcısı',       roleCode: 49, birthPlace: 'ERBAA',    birthDate: '02/09/2005', startDate: '03/11/2025' },
  { name: 'ESMA',       surname: 'YALINIZ',   role: 'Uzman Yardımcısı',       roleCode: 49, birthPlace: 'BAKIRKÖY', birthDate: '05/02/2003', startDate: '18/08/2025' },
  { name: 'KADİR',      surname: 'YILMAZ',    role: 'Stajyer',                roleCode: 52, birthPlace: 'ŞİŞLİ',   birthDate: '11/12/2001', startDate: '15/04/2024' },
];

function parseDate(d: string): Date {
  const [day, month, year] = d.split('/').map(Number);
  return new Date(year, month - 1, day);
}

function calcTenure(member: TeamMember): { label: string; totalDays: number } {
  const now = new Date();
  let totalMs = 0;

  if (member.periods && member.periods.length > 0) {
    for (const p of member.periods) {
      const from = parseDate(p.from);
      const to = p.to ? parseDate(p.to) : now;
      totalMs += to.getTime() - from.getTime();
    }
  } else {
    totalMs = now.getTime() - parseDate(member.startDate).getTime();
  }

  const totalDays = totalMs / (1000 * 60 * 60 * 24);
  const years = Math.floor(totalDays / 365.25);
  const months = Math.floor((totalDays % 365.25) / 30.44);

  let label: string;
  if (years < 1) {
    label = months <= 0 ? '< 1 ay' : `${months} ay`;
  } else if (months === 0) {
    label = `${years} yıl`;
  } else {
    label = `${years} yıl ${months} ay`;
  }

  return { label, totalDays };
}

function formatName(name: string): string {
  return name.split(' ').map(w => w.charAt(0) + w.slice(1).toLowerCase()).join(' ');
}

function getInitials(name: string, surname: string): string {
  return (name.charAt(0) + surname.charAt(0)).toUpperCase();
}

function getRoleTier(code: number): { color: string; bg: string; border: string; ring: string; level: number } {
  if (code === 55) return { color: 'text-amber-300',   bg: 'bg-amber-500/15',   border: 'border-amber-500/30',  ring: 'ring-amber-500/20',   level: 0 };
  if (code === 50) return { color: 'text-rose-300',    bg: 'bg-rose-500/15',    border: 'border-rose-500/30',   ring: 'ring-rose-500/20',    level: 1 };
  if (code === 53) return { color: 'text-cyan-300',    bg: 'bg-cyan-500/15',    border: 'border-cyan-500/30',   ring: 'ring-cyan-500/20',    level: 2 };
  if (code === 64) return { color: 'text-violet-300',  bg: 'bg-violet-500/15',  border: 'border-violet-500/30', ring: 'ring-violet-500/20',  level: 3 };
  if (code === 49) return { color: 'text-emerald-300', bg: 'bg-emerald-500/15', border: 'border-emerald-500/30',ring: 'ring-emerald-500/20', level: 4 };
  return           { color: 'text-slate-300',   bg: 'bg-slate-500/15',   border: 'border-slate-500/30',  ring: 'ring-slate-500/20',   level: 5 };
}

function getRoleIcon(code: number) {
  if (code === 55) return <Crown className="w-3.5 h-3.5" />;
  if (code === 50) return <Star className="w-3.5 h-3.5" />;
  if (code === 53) return <ChevronUp className="w-3.5 h-3.5" />;
  return null;
}

function getAvatarGradient(code: number): string {
  if (code === 55) return 'from-amber-600 to-yellow-500';
  if (code === 50) return 'from-rose-600 to-pink-500';
  if (code === 53) return 'from-cyan-600 to-teal-500';
  if (code === 64) return 'from-violet-600 to-purple-500';
  if (code === 49) return 'from-emerald-600 to-green-500';
  return 'from-slate-600 to-slate-500';
}

const TeamPage: React.FC = () => {
  const [search, setSearch] = useState('');

  const filtered = useMemo(() => {
    if (!search.trim()) return teamMembers;
    const q = search.toLowerCase();
    return teamMembers.filter(m =>
      `${m.name} ${m.surname} ${m.role} ${m.birthPlace}`.toLowerCase().includes(q)
    );
  }, [search]);

  const stats = useMemo(() => {
    const totalYears = teamMembers.reduce((sum, m) => {
      return sum + calcTenure(m).totalDays / 365.25;
    }, 0);
    return {
      total: teamMembers.length,
      avgTenure: (totalYears / teamMembers.length).toFixed(1),
      seniorCount: teamMembers.filter(m => m.roleCode === 64).length,
    };
  }, []);

  return (
    <div className="min-h-full p-6 space-y-6">
      {/* Header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
        <div>
          <div className="flex items-center gap-3 mb-1">
            <div className="h-10 w-10 rounded-xl bg-gradient-to-br from-violet-600 to-indigo-600 flex items-center justify-center shadow-lg shadow-violet-600/20">
              <Users2 className="w-5 h-5 text-white" />
            </div>
            <div>
              <h1 className="text-xl font-bold" style={{ color: 'var(--ms-text)' }}>
                Bilgi Teknolojileri
              </h1>
              <p className="text-xs font-medium" style={{ color: 'var(--ms-text-muted)' }}>
                Sistem Destek & Network Departmanı
              </p>
            </div>
          </div>
        </div>

        {/* Stats pills */}
        <div className="flex items-center gap-2">
          {[
            { label: 'Toplam', value: stats.total, icon: <Users2 className="w-3 h-3" /> },
            { label: 'Ort. Kıdem', value: `${stats.avgTenure} yıl`, icon: <Clock className="w-3 h-3" /> },
            { label: 'Kıdemli Uzman', value: stats.seniorCount, icon: <Star className="w-3 h-3" /> },
          ].map(s => (
            <div
              key={s.label}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-medium"
              style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text-muted)' }}
            >
              {s.icon}
              <span style={{ color: 'var(--ms-text)' }}>{s.value}</span>
              <span>{s.label}</span>
            </div>
          ))}
        </div>
      </div>

      {/* Search */}
      <div className="relative max-w-sm">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4" style={{ color: 'var(--ms-text-muted)' }} />
        <input
          type="text"
          placeholder="Kişi, unvan veya şehir ara..."
          value={search}
          onChange={e => setSearch(e.target.value)}
          className="w-full pl-9 pr-3 py-2 rounded-lg text-sm outline-none transition-all focus:ring-2 focus:ring-violet-500/30"
          style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text)' }}
        />
      </div>

      {/* Team grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-4">
        {filtered.map((member, idx) => {
          const tier = getRoleTier(member.roleCode);
          const icon = getRoleIcon(member.roleCode);
          const gradient = getAvatarGradient(member.roleCode);
          return (
            <div
              key={idx}
              className={`group relative rounded-xl p-4 transition-all duration-200 hover:scale-[1.02] hover:shadow-lg ring-1 ${tier.ring}`}
              style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}
            >
              {/* Top accent line */}
              <div className={`absolute top-0 left-4 right-4 h-[2px] rounded-b ${tier.bg.replace('/15', '/40')}`} />

              <div className="flex items-start gap-3.5">
                {/* Avatar */}
                <div className={`h-11 w-11 rounded-xl bg-gradient-to-br ${gradient} flex items-center justify-center shrink-0 shadow-md`}>
                  <span className="text-white font-bold text-sm">{getInitials(member.name, member.surname)}</span>
                </div>

                <div className="min-w-0 flex-1">
                  {/* Name */}
                  <div className="font-semibold text-[13px] leading-tight" style={{ color: 'var(--ms-text)' }}>
                    {formatName(member.name)} {formatName(member.surname)}
                  </div>

                  {/* Role badge */}
                  <div className={`inline-flex items-center gap-1 mt-1 px-2 py-0.5 rounded-md text-[10px] font-semibold ${tier.bg} ${tier.color} ${tier.border} border`}>
                    {icon}
                    {member.role}
                  </div>
                </div>
              </div>

              {/* Details */}
              <div className="mt-3.5 pt-3 space-y-1.5" style={{ borderTop: '1px solid var(--ms-border)' }}>
                <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                  <MapPin className="w-3 h-3 shrink-0" />
                  <span>{formatName(member.birthPlace)}</span>
                </div>
                <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                  <Calendar className="w-3 h-3 shrink-0" />
                  <span>{member.birthDate}</span>
                </div>
                <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                  <Briefcase className="w-3 h-3 shrink-0" />
                  <span>
                    {member.periods ? member.periods.map((p, i) => (
                      <span key={i}>
                        {i > 0 && <span className="mx-1 opacity-40">|</span>}
                        {p.from}{p.to ? ` → ${p.to}` : ' → ...'}
                      </span>
                    )) : member.startDate}
                    <span className={`ml-1.5 px-1.5 py-0.5 rounded text-[9px] font-semibold ${tier.bg} ${tier.color}`}>
                      {calcTenure(member).label}
                    </span>
                  </span>
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {filtered.length === 0 && (
        <div className="text-center py-16">
          <Users2 className="w-12 h-12 mx-auto mb-3" style={{ color: 'var(--ms-text-muted)', opacity: 0.3 }} />
          <p className="text-sm" style={{ color: 'var(--ms-text-muted)' }}>Sonuc bulunamadi</p>
        </div>
      )}

      {/* Org hierarchy mini */}
      <div className="rounded-xl p-4" style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}>
        <h3 className="text-xs font-semibold uppercase tracking-wider mb-3" style={{ color: 'var(--ms-text-muted)' }}>
          Organizasyon Yapisi
        </h3>
        <div className="flex flex-wrap items-center gap-2 text-[11px]">
          {[
            { role: 'Genel Müdür Yrd.', color: 'amber', code: 55 },
            { role: 'Müdür', color: 'rose', code: 50 },
            { role: 'Yönetici', color: 'cyan', code: 53 },
            { role: 'Kıdemli Uzman', color: 'violet', code: 64 },
            { role: 'Uzman Yardımcısı', color: 'emerald', code: 49 },
            { role: 'Stajyer', color: 'slate', code: 52 },
          ].map((r, i, arr) => {
            const count = teamMembers.filter(m => m.roleCode === r.code).length;
            const tier = getRoleTier(r.code);
            return (
              <React.Fragment key={r.code}>
                <div className={`flex items-center gap-1.5 px-2.5 py-1 rounded-lg ${tier.bg} ${tier.color} ${tier.border} border font-medium`}>
                  {getRoleIcon(r.code)}
                  <span>{r.role}</span>
                  <span className="opacity-60">({count})</span>
                </div>
                {i < arr.length - 1 && (
                  <span style={{ color: 'var(--ms-text-muted)', opacity: 0.3 }}>›</span>
                )}
              </React.Fragment>
            );
          })}
        </div>
      </div>
    </div>
  );
};

export default TeamPage;
