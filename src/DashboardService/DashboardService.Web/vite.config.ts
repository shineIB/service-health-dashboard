import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Dev-only proxy: dashboard-api serves the built SPA itself in every other environment
// (same origin, no CORS needed there), but `npm run dev` runs on Vite's own port, so
// requests to /api and /health need to be forwarded to a locally running dashboard-api.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": "http://localhost:5040",
      "/health": "http://localhost:5040",
      "/version": "http://localhost:5040",
    },
  },
});
