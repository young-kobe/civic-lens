# Frontend API Integration Walkthrough

## Overview
This walkthrough documents the successful integration of the frontend (React) with the new Rich Aggregator API (Python/FastAPI). We have moved from static mock data to dynamic, real-time data fetching for key dashboards.

## Changes Completed

### 1. API Services
- **`ui/src/services/api.ts`**: Created a centralized, typed API service to handle fetching:
  - Stories (`/api/stories`)
  - Favorability (`/api/favorability`)
  - Sentiment (`/api/sentiment`)
  - Bot Profiles (`/api/profiles`)
- **`ui/src/services/transformers.ts`**: Implemented data transformation logic to adapt backend API responses to frontend TypeScript interfaces, ensuring type safety and handling missing data gracefully.

### 2. Page Integrations
- **`StoryClusters.tsx`**:
  - Replaced `MOCK_CLUSTERS`.
  - Integrated `fetchStories` and `transformStories`.
  - Added loading and error states.
- **`GOPFavorability.tsx`**:
  - Replaced `MOCK_FAVORABILITY_DATA`.
  - Integrated `fetchFavorability` and `transformFavorability`.
  - Updated demographics to display "By Platform" using real data.
- **`PublicSentiment.tsx`**:
  - Replaced `MOCK_SENTIMENT_DATA`.
  - Integrated `fetchSentiment` and `transformPublicSentiment`.
  - Renamed type to `PublicSentimentData` to avoid conflicts.
- **`BotActivityProfiler.tsx`**:
  - Replaced `MOCK_BOT_DATA`.
  - Integrated `fetchBotProfiles` and `transformBotData`.

### 3. Configuration
- **`ui/vite.config.ts`**: Added proxy configuration to forward `/api` requests to `http://localhost:8000`, enabling seamless development without CORS issues.

## Verification Steps
To verify the changes locally:

1. **Start the Backend**:
   ```bash
   cd analysis
   python -m src.api.server
   ```
2. **Start the Frontend**:
   ```bash
   cd ui
   npm run dev
   ```
3. **Navigate to Dashboards**:
   - Go to `http://localhost:5173/` (or port shown).
   - Check **Story Clusters**: Ensure stories load with rich details (momentum, source mix).
   - Check **GOP Favorability**: Verify the "By Platform" breakdown appears.
   - Check **Public Sentiment**: Confirm sentiment scores and distribution are populated.
   - Check **Bot Activity**: View the bot profiles and metrics.

## Key Design Decisions
- **Transformer Pattern**: We decoupled API responses from UI types using `transformers.ts`. This allows the backend schema to evolve somewhat independently of the UI, with the transformer bridging the gap.
- **Proxying**: Using Vite's proxy avoids the need for complex CORS setup in development.
- **Strict Typing**: We enforced strict TypeScript types for API responses to catch mismatches early.
