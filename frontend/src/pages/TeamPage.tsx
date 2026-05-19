import React, { useMemo, useState } from 'react';
import { Search, Briefcase, MapPin, Calendar, Clock, Users2, Crown, Star, ChevronUp, Gift, Lock } from 'lucide-react';
import { TeamMember, formatName, getBirthdayInfo, parseDate, teamMembers } from '../lib/btTeam';
import { useAuth } from '../contexts/AuthContext';

function calcTenure(member: TeamMember): { label: string; totalDays: number } {
  const now = new Date();
  const totalMs = now.getTime() - parseDate(member.startDate).getTime();
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

function getInitials(name: string, surname: string): string {
  return (name.charAt(0) + surname.charAt(0)).toUpperCase();
}

function getRoleTier(code: number): { color: string; bg: string; border: string; ring: string; level: number } {
  if (code === 55) return { color: 'text-amber-300', bg: 'bg-amber-500/15', border: 'border-amber-500/30', ring: 'ring-amber-500/20', level: 0 };
  if (code === 50) return { color: 'text-rose-300', bg: 'bg-rose-500/15', border: 'border-rose-500/30', ring: 'ring-rose-500/20', level: 1 };
  if (code === 53) return { color: 'text-cyan-300', bg: 'bg-cyan-500/15', border: 'border-cyan-500/30', ring: 'ring-cyan-500/20', level: 2 };
  if (code === 64) return { color: 'text-violet-300', bg: 'bg-violet-500/15', border: 'border-violet-500/30', ring: 'ring-violet-500/20', level: 3 };
  if (code === 44) return { color: 'text-blue-300', bg: 'bg-blue-500/15', border: 'border-blue-500/30', ring: 'ring-blue-500/20', level: 4 };
  if (code === 49) return { color: 'text-emerald-300', bg: 'bg-emerald-500/15', border: 'border-emerald-500/30', ring: 'ring-emerald-500/20', level: 5 };
  if (code === 52) return { color: 'text-slate-300', bg: 'bg-slate-500/15', border: 'border-slate-500/30', ring: 'ring-slate-500/20', level: 6 };
  return { color: 'text-indigo-300', bg: 'bg-indigo-500/15', border: 'border-indigo-500/30', ring: 'ring-indigo-500/20', level: 7 };
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
  if (code === 44) return 'from-blue-600 to-sky-500';
  if (code === 49) return 'from-emerald-600 to-green-500';
  if (code === 52) return 'from-slate-600 to-gray-500';
  return 'from-indigo-600 to-blue-500';
}

const sortedTeamMembers: TeamMember[] = [...teamMembers].sort((a, b) => {
  const tierDiff = getRoleTier(a.roleCode).level - getRoleTier(b.roleCode).level;
  if (tierDiff !== 0) return tierDiff;
  return `${a.name} ${a.surname}`.localeCompare(`${b.name} ${b.surname}`, 'tr');
});

const TeamPage: React.FC = () => {
  const { isAdmin } = useAuth();
  const [search, setSearch] = useState('');

  const filtered = useMemo(() => {
    if (!search.trim()) return sortedTeamMembers;
    const q = search.toLocaleLowerCase('tr-TR');
    return sortedTeamMembers.filter((member) => {
      // Non-admin'lerin aramasına doğum yeri girmiyor.
      const haystack = isAdmin
        ? `${member.name} ${member.surname} ${member.role} ${member.birthPlace}`
        : `${member.name} ${member.surname} ${member.role}`;
      return haystack.toLocaleLowerCase('tr-TR').includes(q);
    });
  }, [search, isAdmin]);

  const stats = useMemo(() => {
    const totalYears = sortedTeamMembers.reduce((sum, member) => sum + calcTenure(member).totalDays / 365.25, 0);
    return {
      total: sortedTeamMembers.length,
      avgTenure: sortedTeamMembers.length ? (totalYears / sortedTeamMembers.length).toFixed(1) : '0.0',
    };
  }, []);

  // Non-admin için: gösterilebilecek istatistikler
  const visibleStats = isAdmin
    ? [
        { label: 'Toplam', value: stats.total, icon: <Users2 className="w-3 h-3" /> },
        { label: 'Ort. Kıdem', value: `${stats.avgTenure} yıl`, icon: <Clock className="w-3 h-3" /> },
      ]
    : [{ label: 'Toplam', value: stats.total, icon: <Users2 className="w-3 h-3" /> }];

  return (
    <div className="min-h-full p-6 space-y-6">
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

        <div className="flex items-center gap-2">
          {visibleStats.map((stat) => (
            <div
              key={stat.label}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[11px] font-medium"
              style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text-muted)' }}
            >
              {stat.icon}
              <span style={{ color: 'var(--ms-text)' }}>{stat.value}</span>
              <span>{stat.label}</span>
            </div>
          ))}
        </div>
      </div>

      <div className="relative max-w-sm">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4" style={{ color: 'var(--ms-text-muted)' }} />
        <input
          type="text"
          placeholder={isAdmin ? "Kişi, unvan veya şehir ara..." : "Kişi veya unvan ara..."}
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          className="w-full pl-9 pr-3 py-2 rounded-lg text-sm outline-none transition-all focus:ring-2 focus:ring-violet-500/30"
          style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)', color: 'var(--ms-text)' }}
        />
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4 gap-4">
        {filtered.map((member) => {
          const tier = getRoleTier(member.roleCode);
          const icon = getRoleIcon(member.roleCode);
          const gradient = getAvatarGradient(member.roleCode);
          const birthday = getBirthdayInfo(member.birthDate);
          const birthdayBadgeClass = birthday.isToday
            ? 'bg-amber-500/20 text-amber-200 border-amber-400/40'
            : birthday.daysUntil <= 7
              ? 'bg-rose-500/15 text-rose-200 border-rose-400/30'
              : birthday.daysUntil <= 30
                ? 'bg-cyan-500/15 text-cyan-200 border-cyan-400/30'
                : 'bg-slate-500/15 text-slate-200 border-slate-400/25';

          return (
            <div
              key={member.no}
              className={`group relative rounded-xl p-4 transition-all duration-200 hover:scale-[1.02] hover:shadow-lg ring-1 ${tier.ring}`}
              style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}
            >
              <div className={`absolute top-0 left-4 right-4 h-[2px] rounded-b ${tier.bg.replace('/15', '/40')}`} />

              <div className="flex items-start gap-3.5">
                <div className={`h-11 w-11 rounded-xl bg-gradient-to-br ${gradient} flex items-center justify-center shrink-0 shadow-md`}>
                  <span className="text-white font-bold text-sm">{getInitials(member.name, member.surname)}</span>
                </div>

                <div className="min-w-0 flex-1">
                  <div className="font-semibold text-[13px] leading-tight" style={{ color: 'var(--ms-text)' }}>
                    {formatName(member.name)} {formatName(member.surname)}
                  </div>

                  <div className="mt-1 flex flex-wrap items-center gap-1.5">
                    <div className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] font-semibold ${tier.bg} ${tier.color} ${tier.border} border`}>
                      {icon}
                      {member.role}
                    </div>
                    <div
                      className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[10px] font-semibold border ${birthdayBadgeClass}`}
                      title={isAdmin ? birthday.label : `${birthday.shortLabel} kaldı`}
                    >
                      <Gift className="w-3 h-3" />
                      {birthday.shortLabel}
                    </div>
                  </div>
                </div>
              </div>

              <div className="mt-3.5 pt-3 space-y-1.5" style={{ borderTop: '1px solid var(--ms-border)' }}>
                {isAdmin ? (
                  <>
                    <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                      <MapPin className="w-3 h-3 shrink-0" />
                      <span>{formatName(member.birthPlace)}</span>
                    </div>
                    <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                      <Calendar className="w-3 h-3 shrink-0" />
                      <span>{member.birthDate}</span>
                    </div>
                    <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                      <Gift className="w-3 h-3 shrink-0" />
                      <span>{birthday.label}</span>
                    </div>
                    <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                      <Briefcase className="w-3 h-3 shrink-0" />
                      <span>
                        {member.startDate}
                        <span className={`ml-1.5 px-1.5 py-0.5 rounded text-[9px] font-semibold ${tier.bg} ${tier.color}`}>
                          {calcTenure(member).label}
                        </span>
                      </span>
                    </div>
                  </>
                ) : (
                  <>
                    <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                      <Gift className="w-3 h-3 shrink-0" />
                      <span>
                        {birthday.isToday
                          ? 'Bugün doğum günü 🎉'
                          : birthday.daysUntil === 1
                            ? 'Yarın doğum günü'
                            : `Doğum gününe ${birthday.daysUntil} gün`}
                      </span>
                    </div>
                    <div className="flex items-center gap-2 text-[11px]" style={{ color: 'var(--ms-text-muted)' }}>
                      <Briefcase className="w-3 h-3 shrink-0" />
                      <span>
                        {member.startDate}
                        <span className={`ml-1.5 px-1.5 py-0.5 rounded text-[9px] font-semibold ${tier.bg} ${tier.color}`}>
                          {calcTenure(member).label}
                        </span>
                      </span>
                    </div>
                  </>
                )}
              </div>
            </div>
          );
        })}
      </div>

      {filtered.length === 0 && (
        <div className="text-center py-16">
          <Users2 className="w-12 h-12 mx-auto mb-3" style={{ color: 'var(--ms-text-muted)', opacity: 0.3 }} />
          <p className="text-sm" style={{ color: 'var(--ms-text-muted)' }}>Sonuç bulunamadı</p>
        </div>
      )}

      <div className="rounded-xl p-4" style={{ background: 'var(--ms-bg-soft)', border: '1px solid var(--ms-border)' }}>
        <h3 className="text-xs font-semibold uppercase tracking-wider mb-3" style={{ color: 'var(--ms-text-muted)' }}>
          Organizasyon Yapısı
        </h3>
        <div className="flex flex-wrap items-center gap-2 text-[11px]">
          {[
            { role: 'Genel Müdür Yrd.', code: 55 },
            { role: 'Müdür', code: 50 },
            { role: 'Yönetici', code: 53 },
            { role: 'Kıdemli Uzman', code: 64 },
            { role: 'Uzman Yardımcısı', code: 49 },
            { role: 'Uzman', code: 44 },
            { role: 'Stajyer', code: 52 },
          ].map((role, index, items) => {
            const count = sortedTeamMembers.filter((member) => member.roleCode === role.code).length;
            const tier = getRoleTier(role.code);

            return (
              <React.Fragment key={role.code}>
                <div className={`flex items-center gap-1.5 px-2.5 py-1 rounded-lg ${tier.bg} ${tier.color} ${tier.border} border font-medium`}>
                  {getRoleIcon(role.code)}
                  <span>{role.role}</span>
                  <span className="opacity-60">({count})</span>
                </div>
                {index < items.length - 1 && (
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
