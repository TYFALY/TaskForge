/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        background: {
          DEFAULT: 'hsl(240 10% 3.9%)',
          primary: 'hsl(240 6% 10%)',
          secondary: 'hsl(240 4% 16%)',
        },
        foreground: {
          DEFAULT: 'hsl(0 0% 98%)',
          muted: 'hsl(240 5% 65%)',
        },
        emerald: {
          glow: 'hsl(160 84% 39%)',
        },
        border: 'hsl(240 3.7% 15.9%)',
      },
      boxShadow: {
        'glow-emerald': '0 0 20px -5px hsl(160 84% 39% / 0.5)',
        'glow-sm': '0 0 10px -3px hsl(160 84% 39% / 0.4)',
      },
      animation: {
        'pulse-slow': 'pulse 3s cubic-bezier(0.4, 0, 0.6, 1) infinite',
      },
    },
  },
  plugins: [],
}