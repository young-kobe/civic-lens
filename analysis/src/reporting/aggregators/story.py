"""
Story cluster aggregator.

Aggregates document clusters with momentum, timeline, and source mix data.
Supports filtering by content type (articles vs social posts).
"""

import datetime
import json
import time
from typing import Any, Dict, List, Optional, Set

from analysis.src.reporting.aggregators.base import (
    get_connection,
    get_time_cutoff,
    get_bot_flagged_doc_ids,
)
from analysis.src.reporting.aggregators.constants import (
    SOCIAL_PLATFORMS, ARTICLE_SOURCE_TYPES,
    SOURCE_DISPLAY_NAMES, SOURCE_TYPE_MAP,
)
from analysis.src.reporting.models import (
    MomentumData,
    SourceMixItem,
    TimelinePoint,
    ArticlePreview,
    StoryCluster,
)


class StoryAggregator:
    """Aggregates story clusters with rich metadata."""
    
    def __init__(self, db_path: str):
        self.db_path = db_path
    
    def get_stories(
        self,
        time_window: str = "24h",
        content_type: str = "all",
    ) -> List[StoryCluster]:
        """
        Returns rich cluster data matching frontend expectations.
        Excludes bot-flagged content.
        
        Args:
            time_window: Filter by time window (24h, 7d, 30d, 90d, all)
            content_type: Filter by content type ('all', 'articles', 'social')
        """
        bot_docs = get_bot_flagged_doc_ids(self.db_path)
        cutoff = get_time_cutoff(time_window)
        
        with get_connection(self.db_path) as conn:
            cursor = conn.cursor()
            raw_clusters = self._fetch_cluster_data(cursor, bot_docs, cutoff)
            
        return self._format_story_clusters(raw_clusters, content_type)

    def _fetch_cluster_data(
        self, 
        cursor, 
        bot_docs: Set[int], 
        cutoff: Optional[int] = None
    ) -> Dict[int, Dict[str, Any]]:
        """Fetch and organize cluster data from database."""
        if cutoff:
            cursor.execute("""
                SELECT 
                    c.cluster_id, 
                    c.name, 
                    c.summary,
                    d.doc_id, 
                    d.published_at, 
                    d.source_type, 
                    d.ident, 
                    d.title,
                    d.metadata_json,
                    d.text
                FROM clusters c
                JOIN cluster_assignments ca ON c.cluster_id = ca.cluster_id
                JOIN docs d ON ca.doc_id = d.doc_id
                WHERE d.published_at >= ?
            """, (cutoff,))
        else:
            cursor.execute("""
                SELECT 
                    c.cluster_id, 
                    c.name, 
                    c.summary,
                    d.doc_id, 
                    d.published_at, 
                    d.source_type, 
                    d.ident, 
                    d.title,
                    d.metadata_json,
                    d.text
                FROM clusters c
                JOIN cluster_assignments ca ON c.cluster_id = ca.cluster_id
                JOIN docs d ON ca.doc_id = d.doc_id
            """)
        
        clusters: Dict[int, Dict[str, Any]] = {}
        now = time.time()
        
        for row in cursor.fetchall():
            (c_id, c_name, c_summary, d_id, pub_at,
             src_type, ident, title, meta_json, text) = row
            
            if d_id in bot_docs:
                continue
                
            if c_id not in clusters:
                clusters[c_id] = {
                    "id": c_id,
                    "title": c_name,
                    "summary_text": c_summary,
                    "doc_count": 0,
                    "docs": [],
                    "sources": {},
                    "timeline": {},
                    "now": now,
                    "source_types_seen": set(),
                }
            
            cluster = clusters[c_id]
            cluster["doc_count"] += 1
            cluster["source_types_seen"].add(src_type or "other")
            
            # For X posts without titles, use truncated text
            display_title = title
            if not display_title and src_type == "x_post" and text:
                display_title = text[:80] + ("..." if len(text) > 80 else "")
            
            # Parse snippet from metadata or text
            snippet = self._extract_snippet(meta_json, text)
            
            cluster["docs"].append({
                "id": d_id,
                "title": display_title or "Untitled",
                "source": ident,
                "snippet": snippet,
                "published_at": pub_at,
                "source_type": src_type,
            })
            
            # Track source mix
            source_type = src_type or 'other'
            cluster["sources"][source_type] = cluster["sources"].get(source_type, 0) + 1
            
            # Track timeline by day
            if pub_at:
                day = datetime.datetime.fromtimestamp(pub_at).strftime('%a')
                cluster["timeline"][day] = cluster["timeline"].get(day, 0) + 1
        
        return clusters

    def _extract_snippet(self, meta_json: Optional[str], text: Optional[str] = None) -> str:
        """Extract description snippet from metadata JSON or text."""
        if meta_json:
            try:
                desc = json.loads(meta_json).get('description', '')
                if desc:
                    return desc
            except json.JSONDecodeError:
                pass
        # Fall back to first 150 chars of text for social posts
        if text:
            return text[:150] + ("..." if len(text) > 150 else "")
        return ''

    def _classify_content_type(self, source_types: set) -> str:
        """Determine content type based on source types in the cluster."""
        has_articles = bool(source_types & ARTICLE_SOURCE_TYPES)
        has_social = bool(source_types & SOCIAL_PLATFORMS)
        
        if has_articles and has_social:
            return "mixed"
        elif has_articles:
            return "articles"
        elif has_social:
            return "social"
        return "mixed"

    def _generate_social_cluster_title(self, docs: List[Dict[str, Any]]) -> str:
        """Generate a meaningful title for social-only clusters."""
        # Use the most recent post's first meaningful words
        sorted_docs = sorted(docs, key=lambda x: x["published_at"] or 0, reverse=True)
        for doc in sorted_docs[:3]:
            title = doc.get("title", "")
            if title and title != "Untitled" and len(title) > 10:
                # Truncate to a reasonable title length
                if len(title) > 60:
                    return title[:57] + "..."
                return title
        return "Social Discussion"

    def _format_story_clusters(
        self,
        clusters: Dict[int, Dict[str, Any]],
        content_type: str = "all",
    ) -> List[StoryCluster]:
        """Format raw cluster data into StoryCluster response objects."""
        results = []
        
        for c_id, c in clusters.items():
            cluster_content_type = self._classify_content_type(c["source_types_seen"])
            
            # Filter by content type if specified.
            # For UI tabs (All / Articles / Social Posts), we want strict filtering:
            if content_type == "articles" and cluster_content_type != "articles":
                continue
            if content_type == "social" and cluster_content_type != "social":
                continue
            
            momentum = self._compute_momentum(c["docs"], c["now"])
            source_mix = self._format_source_mix(c["sources"])
            timeline = self._format_timeline(c["timeline"])
            articles = self._format_articles(c["docs"])
            summary_points = [
                d["title"] for d in sorted(
                    c["docs"], key=lambda x: x["published_at"] or 0, reverse=True
                )[:3]
            ]
            
            # Generate meaningful title for social-only clusters
            title = c["title"]
            if not title or title == "Unnamed Cluster":
                if cluster_content_type == "social":
                    title = self._generate_social_cluster_title(c["docs"])
                else:
                    title = "Unnamed Cluster"
            
            cluster = StoryCluster(
                id=c_id,
                title=title,
                articleCount=c["doc_count"],
                momentum=momentum,
                primarySources=list(c["sources"].keys())[:3],
                summary=summary_points,
                keyClaims=[],
                entities={"people": [], "organizations": [], "locations": []},
                sourceMix=source_mix,
                timeline=timeline,
                articles=articles,
                contentType=cluster_content_type,
            )
            results.append(cluster)
        
        return sorted(results, key=lambda x: x.articleCount, reverse=True)[:20]

    def _compute_momentum(self, docs: List[Dict[str, Any]], now: float) -> MomentumData:
        """Compute momentum (24h delta) for a cluster."""
        one_day_ago = now - 86400
        
        count_24h = sum(1 for d in docs if d["published_at"] and d["published_at"] > one_day_ago)
        count_prev_24h = sum(
            1 for d in docs 
            if d["published_at"] and d["published_at"] <= one_day_ago and d["published_at"] > (one_day_ago - 86400)
        )
        
        if count_prev_24h > 0:
            delta = ((count_24h - count_prev_24h) / count_prev_24h) * 100
        elif count_24h > 0:
            delta = 100.0  # New emerging topic
        else:
            delta = 0.0
        
        return MomentumData(delta24h=round(delta, 1))

    def _format_source_mix(self, sources: Dict[str, int]) -> List[SourceMixItem]:
        """Format source distribution into SourceMixItem list."""
        return [
            SourceMixItem(
                name=SOURCE_DISPLAY_NAMES.get(k, k.replace('_', ' ').title()),
                value=v,
                type=SOURCE_TYPE_MAP.get(k, 'other'),
            )
            for k, v in sources.items()
        ]

    def _format_timeline(self, timeline_data: Dict[str, int]) -> List[TimelinePoint]:
        """Format timeline data with proper day ordering."""
        days_order = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]
        return [
            TimelinePoint(date=day, value=timeline_data[day])
            for day in days_order if day in timeline_data
        ]

    def _format_articles(self, docs: List[Dict[str, Any]]) -> List[ArticlePreview]:
        """Format top 3 recent articles as previews."""
        sorted_docs = sorted(docs, key=lambda x: x["published_at"] or 0, reverse=True)
        return [
            ArticlePreview(
                id=d["id"],
                title=d["title"],
                source=d["source"],
                snippet=d["snippet"][:100] + "..." if len(d["snippet"]) > 100 else d["snippet"],
                reason="Recent coverage",
            )
            for d in sorted_docs[:3]
        ]
