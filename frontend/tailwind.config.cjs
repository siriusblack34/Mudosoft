module.exports = {
  content: ['./index.html', './src/**/*.{ts,tsx,js,jsx}'],
  theme: {
    extend: {
      colors: {
        'ms-bg': '#050816',
        'ms-bg-soft': '#0c1220',
        'ms-panel': '#101828',
        'ms-border': '#1f2937',
        'ms-accent': '#22c55e',
        'ms-accent-soft': '#16a34a',
        'ms-danger': '#ef4444',
        'ms-text': '#e5e7eb',
        'ms-text-muted': '#9ca3af'
      },
      borderRadius: {
        xl: '0.75rem',
        '2xl': '1rem'
      }
    }
  },
  plugins: []
};