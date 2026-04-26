import React from 'react';

interface LogoProps {
  /** Kutu boyutu (px). Default 32. */
  size?: number;
  /** Arka plan + köşe yuvarlatma göster. Default true. */
  withBackground?: boolean;
  /** Ek className */
  className?: string;
  /** Benzersiz gradient ID (aynı sayfada birden fazla logo varsa çakışma önler) */
  idSuffix?: string;
}

/**
 * MudoSoft Signal Mark — RMM bağlamında nabız/sinyal hattı formunda
 * stilize edilmiş "M" silueti. Dalga formu + aktif sinyal noktası.
 */
const Logo: React.FC<LogoProps> = ({
  size = 32,
  withBackground = true,
  className = '',
  idSuffix = 'default',
}) => {
  const gradId = `logo-grad-${idSuffix}`;
  const glowId = `logo-glow-${idSuffix}`;
  const inner = (
    <svg viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg" className="w-full h-full">
      <defs>
        <linearGradient id={gradId} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#38bdf8" />
          <stop offset="100%" stopColor="#818cf8" />
        </linearGradient>
        <radialGradient id={glowId} cx="0.5" cy="0.5" r="0.5">
          <stop offset="0%" stopColor="#38bdf8" stopOpacity="0.7" />
          <stop offset="100%" stopColor="#38bdf8" stopOpacity="0" />
        </radialGradient>
      </defs>
      {/* signal pulse dot glow */}
      <circle cx="27" cy="20" r="5" fill={`url(#${glowId})`} />
      {/* pulse line — baseline + M-formunda çift tepe + baseline */}
      <path
        d="M3 20 L8.5 20 L12 8 L16 24 L20 8 L23.5 20 L29 20"
        stroke={`url(#${gradId})`}
        strokeWidth="2.4"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      {/* active signal indicator */}
      <circle cx="27" cy="20" r="1.8" fill="#38bdf8" />
    </svg>
  );

  if (!withBackground) {
    return (
      <div className={className} style={{ width: size, height: size }}>
        {inner}
      </div>
    );
  }

  return (
    <div
      className={`flex items-center justify-center shrink-0 ${className}`}
      style={{
        width: size,
        height: size,
        borderRadius: size * 0.28,
        background: 'linear-gradient(135deg, #0f1e35 0%, #1c2e4a 100%)',
        boxShadow: '0 4px 14px rgba(56, 189, 248, 0.25), inset 0 1px 0 rgba(255, 255, 255, 0.06)',
        border: '1px solid rgba(56, 189, 248, 0.2)',
      }}
    >
      <div style={{ width: size * 0.7, height: size * 0.7 }}>{inner}</div>
    </div>
  );
};

export default Logo;
