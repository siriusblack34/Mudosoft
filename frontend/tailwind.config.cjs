module.exports = {
  content: ['./index.html', './src/**/*.{ts,tsx,js,jsx}'],
  theme: {
    extend: {
      fontFamily: {
        sans: ['Inter', 'system-ui', 'sans-serif'],
      },
      colors: {
        // Ana arka plan renkleri
        'ms-bg':         '#09090b', // zinc-950
        'ms-bg-soft':    '#18181b', // zinc-900
        'ms-panel':      '#1c1c1f', // zinc-900 variant
        'ms-border':     '#27272a', // zinc-800
        // Accent — Electric Violet
        'ms-accent':     '#8b5cf6', // violet-500
        'ms-accent-soft':'#7c3aed', // violet-700
        // Durum renkleri
        'ms-success':    '#22c55e', // green-500
        'ms-danger':     '#ef4444', // red-500
        'ms-warning':    '#f59e0b', // amber-500
        'ms-info':       '#3b82f6', // blue-500
        // Metin renkleri
        'ms-text':       '#fafafa', // zinc-50
        'ms-text-muted': '#a1a1aa', // zinc-400
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
