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
        'ms-accent':     '#8b5cf6',
        'ms-accent-soft':'#7c3aed',
        'ms-success':    '#22c55e',
        'ms-danger':     '#ef4444',
        'ms-warning':    '#f59e0b',
        'ms-info':       '#3b82f6',
        'ms-text':       'var(--ms-text)',
        'ms-text-muted': 'var(--ms-text-muted)',
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
