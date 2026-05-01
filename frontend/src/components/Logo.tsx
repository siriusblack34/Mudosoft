import React from 'react';

interface LogoProps {
  /** Kutu boyutu (px). Default 32. */
  size?: number;
  /** Eski API uyumluluğu için korunuyor; logo PNG birebir kullanılır. */
  withBackground?: boolean;
  /** Ek className */
  className?: string;
  /** Eski API uyumluluğu için korunuyor. */
  idSuffix?: string;
}

const LOGO_SRC = '/logo.png';

const Logo: React.FC<LogoProps> = ({
  size = 32,
  className = '',
}) => {
  return (
    <img
      src={LOGO_SRC}
      alt="Orchestra"
      className={`block shrink-0 object-contain ${className}`}
      style={{ width: size, height: size }}
      draggable={false}
    />
  );
};

export default Logo;
