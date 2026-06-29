import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Build output goes to ../wwwroot so the ASP.NET Core host serves it directly.
export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../wwwroot",
    emptyOutDir: true,
  },
  server: {
    // Dev: proxy API + SSE to the running ASP.NET backend.
    proxy: {
      "/api": { target: "http://localhost:8080", changeOrigin: true },
    },
  },
});
