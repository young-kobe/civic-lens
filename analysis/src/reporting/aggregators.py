"""
Aggregator for Civic Lens reporting.

Provides filtered aggregations that exclude bot-flagged content from:
- Public sentiment metrics
- GOP favorability metrics  
- Story clusters

Satisfies requirement: bot-flagged content should not appear in sentiment,
favorability, or cluster API returns.
"""

from typing import Any, Dict, List, Optional, Set
import sqlite3
import json
from analysis.src.common.logger import get_logger

logger = get_logger(__name__)


class Aggregator:
    """
    Aggregates analysis results with bot filtering.
    
    All public-facing metrics exclude documents flagged as 'bot'.
    """
    
    def __init__(self, db_path: str):
        self.db_path = db_path
        
    def _get_conn(self):
        return sqlite3.connect(self.db_path)
    
    def _get_bot_flagged_doc_ids(self) -> Set[int]:
        """
        Get all doc_ids that have been flagged as 'bot'.
        
        These documents are excluded from sentiment, favorability, and cluster aggregations.
        """
        conn = self._get_conn()
        cursor = conn.cursor()
        
        cursor.execute("""
            SELECT doc_id, output_json
            FROM ai_outputs
            WHERE task_type = 'bot_detection'
        """)
        
        bot_docs = set()
        for doc_id, output_json in cursor.fetchall():
            try:
                data = json.loads(output_json)
                # Exclude if labeled as 'bot' (not 'suspicious' - those may be human)
                if data.get('label') == 'bot' or data.get('is_bot') is True:
                    bot_docs.add(doc_id)
            except json.JSONDecodeError:
                continue
        
        logger.info(f"Found {len(bot_docs)} bot-flagged documents to exclude")
        return bot_docs

    def get_outlet_profiles(self) -> List[Dict[str, Any]]:
        """
        Aggregates sentiment and bot scores by domain/subreddit.
        Includes ALL content (bots included) for transparency in outlet analysis.
        """
        conn = self._get_conn()
        cursor = conn.cursor()
        
        cursor.execute("""
            SELECT d.domain_or_subreddit, a.task_type, a.output_json
            FROM ai_outputs a
            JOIN docs d ON a.doc_id = d.doc_id
            WHERE a.task_type IN ('sentiment', 'bot_detection')
        """)
        
        rows = cursor.fetchall()
        profiles = {}
        
        for domain, task, output_json in rows:
            if domain not in profiles:
                profiles[domain] = {
                    "sentiment_score": 0, 
                    "sentiment_count": 0, 
                    "bot_flags": 0, 
                    "total_scanned": 0
                }
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            if task == 'sentiment':
                label = data.get('label')
                val = 1 if label == 'POSITIVE' else (-1 if label == 'NEGATIVE' else 0)
                profiles[domain]["sentiment_score"] += val
                profiles[domain]["sentiment_count"] += 1
                
            elif task == 'bot_detection':
                label = data.get('label')
                if label in ['bot', 'suspicious']:
                    profiles[domain]["bot_flags"] += 1
                profiles[domain]["total_scanned"] += 1
                
        results = []
        for domain, stats in profiles.items():
            avg_sentiment = stats["sentiment_score"] / stats["sentiment_count"] if stats["sentiment_count"] > 0 else 0
            bot_rate = stats["bot_flags"] / stats["total_scanned"] if stats["total_scanned"] > 0 else 0
            
            results.append({
                "outlet": domain,
                "avg_sentiment": round(avg_sentiment, 3),
                "bot_rate": round(bot_rate, 3),
                "volume": stats["sentiment_count"]
            })
            
        return results

    def get_stories(self) -> List[Dict[str, Any]]:
        """
        Returns clusters with article counts, EXCLUDING bot-flagged content.
        """
        return self.get_stories_filtered()
    
    def get_stories_filtered(self) -> List[Dict[str, Any]]:
        """
        Returns clusters with article counts, excluding bot-flagged documents.
        """
        conn = self._get_conn()
        cursor = conn.cursor()
        
        # Get bot-flagged docs
        bot_docs = self._get_bot_flagged_doc_ids()
        
        # Get all cluster assignments
        cursor.execute("""
            SELECT c.cluster_id, c.name, ca.doc_id
            FROM clusters c
            JOIN cluster_assignments ca ON c.cluster_id = ca.cluster_id
        """)
        
        # Aggregate excluding bots
        clusters = {}
        for cluster_id, name, doc_id in cursor.fetchall():
            if doc_id in bot_docs:
                continue
            
            if cluster_id not in clusters:
                clusters[cluster_id] = {"id": cluster_id, "title": name, "doc_count": 0}
            clusters[cluster_id]["doc_count"] += 1
        
        # Sort by count and limit
        results = sorted(clusters.values(), key=lambda x: x["doc_count"], reverse=True)[:50]
        
        # Rename for API compatibility
        for r in results:
            r["article_count"] = r.pop("doc_count")
        
        return results

    def get_public_sentiment(self) -> Dict[str, Any]:
        """
        Aggregate sentiment EXCLUDING bot-flagged content.
        
        Returns sentiment distribution with explicit labeling that this represents
        "sampled platform discourse" per invariant requirements.
        """
        conn = self._get_conn()
        cursor = conn.cursor()
        
        bot_docs = self._get_bot_flagged_doc_ids()
        
        cursor.execute("""
            SELECT a.doc_id, a.output_json, a.confidence, d.source_type, d.domain_or_subreddit
            FROM ai_outputs a
            JOIN docs d ON a.doc_id = d.doc_id
            WHERE a.task_type = 'sentiment'
        """)
        
        # Aggregate by label
        distribution = {"POSITIVE": 0, "NEGATIVE": 0, "NEUTRAL": 0, "MIXED": 0}
        by_platform = {}
        total_confidence = 0.0
        count = 0
        excluded_bot_count = 0
        
        for doc_id, output_json, confidence, source_type, domain in cursor.fetchall():
            # Exclude bots
            if doc_id in bot_docs:
                excluded_bot_count += 1
                continue
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            label = data.get('label', 'NEUTRAL')
            if label in distribution:
                distribution[label] += 1
            
            # By platform
            platform = source_type or 'unknown'
            if platform not in by_platform:
                by_platform[platform] = {"POSITIVE": 0, "NEGATIVE": 0, "NEUTRAL": 0, "MIXED": 0}
            if label in by_platform[platform]:
                by_platform[platform][label] += 1
            
            total_confidence += confidence or 0.5
            count += 1
        
        # Calculate net sentiment
        total = sum(distribution.values())
        net_score = 0
        if total > 0:
            net_score = (distribution["POSITIVE"] - distribution["NEGATIVE"]) / total
        
        return {
            "disclaimer": "Represents sampled platform discourse, not verified population sentiment",
            "sample_size": count,
            "excluded_bot_content": excluded_bot_count,
            "net_score": round(net_score, 3),
            "avg_confidence": round(total_confidence / count, 3) if count > 0 else 0,
            "distribution": {
                "positive": distribution["POSITIVE"],
                "negative": distribution["NEGATIVE"],
                "neutral": distribution["NEUTRAL"],
                "mixed": distribution["MIXED"],
            },
            "by_platform": {
                platform: {
                    "positive": counts["POSITIVE"],
                    "negative": counts["NEGATIVE"],
                    "neutral": counts["NEUTRAL"],
                    "mixed": counts["MIXED"],
                }
                for platform, counts in by_platform.items()
            }
        }

    def get_gop_favorability(self) -> Dict[str, Any]:
        """
        Aggregate GOP favorability EXCLUDING bot-flagged content.
        
        Returns favorability metrics with explicit labeling as proxy data.
        """
        conn = self._get_conn()
        cursor = conn.cursor()
        
        bot_docs = self._get_bot_flagged_doc_ids()
        
        cursor.execute("""
            SELECT a.doc_id, a.output_json, a.confidence, d.source_type
            FROM ai_outputs a
            JOIN docs d ON a.doc_id = d.doc_id
            WHERE a.task_type = 'favorability'
        """)
        
        distribution = {"favorable": 0, "unfavorable": 0, "neutral": 0, "mixed": 0}
        total_confidence = 0.0
        count = 0
        excluded_bot_count = 0
        entity_mentions = {}
        
        for doc_id, output_json, confidence, source_type in cursor.fetchall():
            if doc_id in bot_docs:
                excluded_bot_count += 1
                continue
            
            try:
                data = json.loads(output_json)
            except json.JSONDecodeError:
                continue
            
            stance = data.get('overall_gop_stance', 'neutral')
            if stance in distribution:
                distribution[stance] += 1
            
            # Track entity mentions
            for entity in data.get('gop_entities_found', []):
                entity_mentions[entity] = entity_mentions.get(entity, 0) + 1
            
            total_confidence += confidence or 0.5
            count += 1
        
        # Calculate net favorability
        total = sum(distribution.values())
        net_favorability = 0
        if total > 0:
            net_favorability = (distribution["favorable"] - distribution["unfavorable"]) / total
        
        # Top mentioned entities
        top_entities = sorted(entity_mentions.items(), key=lambda x: x[1], reverse=True)[:10]
        
        return {
            "disclaimer": "Proxy metric based on sampled media/social discourse, not polling data",
            "sample_size": count,
            "excluded_bot_content": excluded_bot_count,
            "net_favorability": round(net_favorability, 3),
            "avg_confidence": round(total_confidence / count, 3) if count > 0 else 0,
            "distribution": {
                "favorable": distribution["favorable"],
                "unfavorable": distribution["unfavorable"],
                "neutral": distribution["neutral"],
                "mixed": distribution["mixed"],
            },
            "top_entities_mentioned": [
                {"entity": entity, "mention_count": cnt}
                for entity, cnt in top_entities
            ]
        }
