import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],

  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },

  server: {
    port: 5173,
    // En développement, le front tourne à part et parle au Host .NET. En production il est
    // servi depuis wwwroot par ce même Host, donc en même origine : aucun CORS nulle part.
    proxy: {
      '/api': { target: 'http://127.0.0.1:8080', changeOrigin: false },
      '/healthz': { target: 'http://127.0.0.1:8080', changeOrigin: false },
    },
  },

  build: {
    // La sortie atterrit directement dans le Host : `dotnet publish` produit un binaire
    // unique qui sert l'interface en statique (ADR-0001).
    outDir: '../src/HomelabHub.Host/wwwroot',
    emptyOutDir: true,
    sourcemap: false,
  },
})
