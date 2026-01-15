from typing import List, Dict, Any
from analysis.src.common.logger import get_logger
import math

logger = get_logger(__name__)

class ContentClusterer:
    """
    Groups documents into stories based on text similarity.
    Uses TF-IDF + Cosine Similarity (Custom implementation to avoid heavy sklearn dep in prototype).
    """
    
    def __init__(self):
        pass

    def _tokenize(self, text: str) -> List[str]:
        return text.lower().split()

    def _compute_tf_idf(self, docs: List[Dict[str, Any]]) -> List[Dict[str, float]]:
        # 1. Compute DF (Document Frequency)
        doc_freq = {}
        total_docs = len(docs)
        
        for doc in docs:
            text = doc.get("text", "")
            words = set(self._tokenize(text))
            for w in words:
                doc_freq[w] = doc_freq.get(w, 0) + 1
                
        # 2. Compute TF-IDF for each doc
        vectors = []
        for doc in docs:
            text = doc.get("text", "")
            tokens = self._tokenize(text)
            term_freq = {}
            for t in tokens:
                term_freq[t] = term_freq.get(t, 0) + 1
            
            vec = {}
            for t, count in term_freq.items():
                tf = count / len(tokens) if tokens else 0
                idf = math.log(total_docs / (doc_freq.get(t, 1)))
                vec[t] = tf * idf
            vectors.append(vec)
            
        return vectors

    def _cosine_sim(self, v1: Dict[str, float], v2: Dict[str, float]) -> float:
        intersection = set(v1.keys()) & set(v2.keys())
        numerator = sum(v1[x] * v2[x] for x in intersection)
        
        sum1 = sum(v1[x]**2 for x in v1)
        sum2 = sum(v2[x]**2 for x in v2)
        denominator = math.sqrt(sum1) * math.sqrt(sum2)
        
        if not denominator:
            return 0.0
        return numerator / denominator

    def cluster_documents(self, docs: List[Dict[str, Any]], threshold: float = 0.3) -> List[Dict[str, Any]]:
        """
        Returns a list of clusters.
        Each cluster has 'name', 'doc_ids', 'summary'.
        """
        if not docs:
            return []
            
        vectors = self._compute_tf_idf(docs)
        clusters = [] # List of {doc_indices: [], vector_sum: {}, count: 0}
        
        # Simple single-pass clustering (Leader Algorithm)
        for i, vec in enumerate(vectors):
            best_cluster_idx = -1
            best_sim = -1.0
            
            for c_idx, cluster in enumerate(clusters):
                # Compare with centroid (simplified: comparing with first doc just for prototype speed)
                # Ideally, keep a centroid vector.
                sim = self._cosine_sim(vec, vectors[cluster['doc_indices'][0]])
                if sim > best_sim:
                    best_sim = sim
                    best_cluster_idx = c_idx
            
            if best_sim >= threshold:
                clusters[best_cluster_idx]['doc_indices'].append(i)
            else:
                # New cluster
                clusters.append({'doc_indices': [i]})
                
        # Format output
        results = []
        for c in clusters:
            first_doc = docs[c['doc_indices'][0]]
            results.append({
                "name": first_doc.get("title", "Untitled Cluster"),
                "doc_ids": [docs[idx]['doc_id'] for idx in c['doc_indices']],
                "size": len(c['doc_indices'])
            })
            
        logger.info(f"Clustered {len(docs)} documents into {len(results)} clusters.")
        return results
