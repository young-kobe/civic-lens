# Civic Lens - UI Module

This is the frontend for Civic Lens, built with **React + Vite + TypeScript**. It provides a
dashboard visualizing sampled political discourse: sentiment + GOP favorability, bot activity,
propaganda techniques, and narrative (claim-cluster) overlays. It consumes the API only — no
direct DB access.

## Architecture
-   **Framework**: React 18 + Vite + TypeScript
-   **State/API**: Standard `fetch` + React Hooks against `/api/v1`
-   **Visualization**: Recharts

## Workflows

### 1. Setup
Install Node.js dependencies:
```bash
npm install
```

### 2. Development
Start the development server:
```bash
npm run dev
```
The app will be available at `http://localhost:5173`.

> **Note**: The Vite config proxies `/api` requests to `http://localhost:8000`. Ensure the Python backend is running.

### 3. Building for Production
Create a production build:
```bash
npm run build
```
Output will be in `dist/`.
