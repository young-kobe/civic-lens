# Civic Lens Dashboard UI - Implementation Walkthrough

This document summarizes the complete UI redesign implementation for Civic Lens, an analytics dashboard for understanding media narratives, public sentiment, and coordinated/bot-driven behavior.

---

## What Was Built

### Design System

A comprehensive CSS design system was created in [index.css](file:///c:/Users/kobey/civic-lens/ui/src/index.css):

| Category | Implementation |
|----------|----------------|
| **Typography** | Inter font family, scale from 0.75rem to 2.5rem |
| **Colors** | Neutral palette (50-900), single accent (blue-600), semantic colors (positive/negative/neutral/warning) |
| **Spacing** | 8px base grid, tokens from space-1 (4px) to space-16 (64px) |
| **Shadows** | 3-tier shadow system (sm, md, lg) |
| **Components** | Pre-styled cards, buttons, badges, tables, forms, navigation tabs |

### Component Architecture

All components are written in **TypeScript** with proper type definitions in [types.ts](file:///c:/Users/kobey/civic-lens/ui/src/types.ts).

#### Common Components

| Component | Purpose |
|-----------|---------|
| [Card.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/common/Card.tsx) | Universal card container with title/subtitle/note pattern |
| [MetricCard.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/common/MetricCard.tsx) | KPI display with large value and trend indicator |
| [Tabs.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/common/Tabs.tsx) | Navigation tabs with minimal bottom-border active indicator |
| [GlobalFilters.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/common/GlobalFilters.tsx) | Persistent time/source/geography filter bar |
| [ConfidenceBadge.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/common/ConfidenceBadge.tsx) | Trust indicator showing coverage/confidence levels |
| [MethodPopover.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/common/MethodPopover.tsx) | Expandable methodology explanation |
| [ExportMenu.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/common/ExportMenu.tsx) | Dropdown for PNG/PDF/JSON/CSV export |
| [LoadingState.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/common/LoadingState.tsx) | Skeleton loading patterns |
| [EmptyState.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/common/EmptyState.tsx) | Empty data display |
| [ErrorState.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/common/ErrorState.tsx) | Error display with retry |

#### Chart Components

| Component | Purpose |
|-----------|---------|
| [Sparkline.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/charts/Sparkline.tsx) | Mini trend line for inline displays |
| [StackedBar.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/charts/StackedBar.tsx) | Source mix chart by outlet type |
| [SentimentBar.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/charts/SentimentBar.tsx) | Horizontal positive/negative/neutral bar |
| [TrendStrip.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/charts/TrendStrip.tsx) | Annotated trend line with inflection markers |
| [Heatmap.tsx](file:///c:/Users/kobey/civic-lens/ui/src/components/charts/Heatmap.tsx) | Day/hour activity heatmap |

---

## Page Implementations

### Tab 1: Story Clusters
[StoryClusters.tsx](file:///c:/Users/kobey/civic-lens/ui/src/pages/StoryClusters.tsx)

**Layout**: Split panel with cluster list (left, 320px) and detail view (right).

**Features**:
- Searchable cluster list with article count and momentum indicators
- Narrative summary with key claims
- Key entities by type (people, organizations, locations)
- Source mix visualization
- Volume timeline sparkline
- Representative articles with "why included" reasons

---

### Tab 2: Public Sentiment
[PublicSentiment.tsx](file:///c:/Users/kobey/civic-lens/ui/src/pages/PublicSentiment.tsx)

**Layout**: Full-width stacked cards.

**Features**:
- Net sentiment score header with volume and confidence
- Breakdowns by topic, platform (labeled as "sampled" where appropriate), and time window
- 5-point intensity distribution (strong positive to strong negative)
- Method transparency panel explaining data sources and limitations

---

### Tab 3: GOP Favorability (Infographic)
[GOPFavorability.tsx](file:///c:/Users/kobey/civic-lens/ui/src/pages/GOPFavorability.tsx)

**Layout**: Infographic-style vertical flow.

**Features**:
- Hero metric with dark gradient background
- 30-day trend strip with annotation markers
- Small multiples for age, region, and party ID breakouts
- Polling vs online sentiment comparison with explicit caveats
- Data footnote explaining sources and exclusions

---

### Tab 4: Bot Activity Profiler
[BotActivityProfiler.tsx](file:///c:/Users/kobey/civic-lens/ui/src/pages/BotActivityProfiler.tsx)

**Layout**: Metrics row + narrative cards + behavioral analysis.

**Features**:
- **Warning banner** emphasizing calibrated language ("suspected", "likely")
- Overview metrics: automation rate, coordination index, flagged accounts
- Expandable narrative amplification cards with:
  - Why flagged (synchronized bursts, text similarity, posting patterns)
  - Example posts, hashtags, phrases, targets
- Coordination indicators summary
- Behavioral signals:
  - Account age distribution
  - Posting cadence heatmap
  - Text similarity distribution
  - Link domain concentration

> [!IMPORTANT]
> All bot activity content uses calibrated language per project invariants. Statements are framed as "suspected" or "likely" rather than definitive claims.

---

## Running the Application

```powershell
# Install dependencies (if npm is in PATH)
cd ui
npm install

# Start development server
npm run dev
```

The UI will be available at `http://localhost:3000` and proxies API requests to `http://localhost:8000`.

---

## File Structure

```
ui/
  src/
    index.css           # Design system
    types.ts            # TypeScript type definitions
    App.tsx             # Main app shell with navigation
    main.tsx            # Entry point
    
    components/
      common/           # Reusable UI components
        Card.tsx
        MetricCard.tsx
        Tabs.tsx
        GlobalFilters.tsx
        ConfidenceBadge.tsx
        MethodPopover.tsx
        ExportMenu.tsx
        LoadingState.tsx
        EmptyState.tsx
        ErrorState.tsx
        index.ts
        
      charts/           # Visualization components
        Sparkline.tsx
        StackedBar.tsx
        SentimentBar.tsx
        TrendStrip.tsx
        Heatmap.tsx
        index.ts
        
    pages/              # Tab page components
      StoryClusters.tsx
      PublicSentiment.tsx
      GOPFavorability.tsx
      BotActivityProfiler.tsx
      index.ts
```

---

## Design Decisions

1. **Single accent color** (blue-600) for emphasis; everything else grayscale + semantic colors
2. **Card-based layout** with consistent Title | Subtitle | Content | Note structure
3. **Method transparency** via popovers on every chart explaining data sources and limitations
4. **Proxy labeling**: Reddit and social data explicitly marked as "sampled"
5. **Calibrated language**: Bot activity uses "suspected", "likely", never definitive claims
6. **Trust cues**: Confidence badges, sample sizes, and source counts shown throughout

---

## Mock Data

All pages currently use mock data defined at the top of each page file. To connect to the real API:

1. Replace mock data with `fetch('/api/...')` calls
2. Handle loading and error states (already implemented)
3. Transform API responses to match TypeScript interfaces in `types.ts`
