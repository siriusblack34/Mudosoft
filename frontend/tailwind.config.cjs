module.exports = {
  darkMode: 'class',
  content: ['./index.html', './src/**/*.{ts,tsx,js,jsx}'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
        mono: ['JetBrains Mono', 'ui-monospace', 'monospace'],
      },
      colors: {
        // CSS-variable driven theme colors
        'ms-bg':         'var(--ms-bg)',
        'ms-bg-soft':    'var(--ms-bg-soft)',
        'ms-panel':      'var(--ms-panel)',
        'ms-border':     'var(--ms-border)',
        'ms-accent':     '#38bdf8',      // sky-400 — Deep Ocean primary accent
        'ms-accent-soft':'#0ea5e9',      // sky-500
        'ms-success':    '#22c55e',
        'ms-danger':     '#ef4444',
        'ms-warning':    '#f59e0b',
        'ms-info':       '#3b82f6',
        'ms-text':       'var(--ms-text)',
        'ms-text-muted': 'var(--ms-text-muted)',
        // ── Brand palette (Deep Ocean): sky → indigo gradient
        // violet/fuchsia tüm kod tabanında yaygın kullanıldığı için
        // bu paletleri Deep Ocean renklerine remap ediyoruz. Tek
        // merkezi değişiklikle bütün site yeniden skinlenir.
        violet: {
          50:  '#f0f9ff', 100: '#e0f2fe', 200: '#bae6fd', 300: '#7dd3fc',
          400: '#38bdf8', 500: '#0ea5e9', 600: '#0284c7', 700: '#0369a1',
          800: '#075985', 900: '#0c4a6e', 950: '#082f49',
        },
        fuchsia: {
          50:  '#eef2ff', 100: '#e0e7ff', 200: '#c7d2fe', 300: '#a5b4fc',
          400: '#818cf8', 500: '#6366f1', 600: '#4f46e5', 700: '#4338ca',
          800: '#3730a3', 900: '#312e81', 950: '#1e1b4b',
        },
      },
      borderRadius: {
        xl:  '0.75rem',
        '2xl': '1rem',
        '3xl': '1.5rem',
      },
      keyframes: {
        shimmer: {
          '0%':   { transform: 'translateX(-100%)' },
          '100%': { transform: 'translateX(100%)' },
        },
        'fade-in': {
          from: { opacity: '0', transform: 'translateY(8px)' },
          to:   { opacity: '1', transform: 'translateY(0)' },
        },
        'slide-in': {
          from: { opacity: '0', transform: 'translateX(-8px)' },
          to:   { opacity: '1', transform: 'translateX(0)' },
        },
      },
      animation: {
        shimmer:   'shimmer 2s infinite',
        'fade-in': 'fade-in 0.3s ease-out forwards',
        'slide-in':'slide-in 0.2s ease-out forwards',
      },
    },
  },
  plugins: [],
};
