# Civic Lens - UI Module

This is the frontend for Civic Lens, built with **React** (Vite). It provides a dashboard to visualize story clusters and outlet profiles.

## Architecture
-   **Framework**: React 18 + Vite
-   **State/API**: Standard `fetch` + React Hooks
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
