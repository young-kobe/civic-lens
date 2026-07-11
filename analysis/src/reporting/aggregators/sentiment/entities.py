"""
Per-entity routing and the sampled-author consolidation pass.

Resolves each sentiment row's tier + entity via the shared entity_routing
module and buckets it into the sentiment-specific per-entity accumulator
(news outlets / verified officials / general public), then folds sub-floor
sampled X authors back into the "Other X users" catch-all after the row pass.

Imports the sample collector from ``samples`` (the leaf); the orchestrating
``aggregator`` imports the routing entry points from here.
"""

from __future__ import annotations

from typing import Any, Dict, Optional

from analysis.src.reporting.aggregators.sentiment.samples import (
    MAX_SAMPLES_PER_ENTITY,
    _collect_entity_sample,
    _insert_capped,
)
from analysis.src.reporting.entity_registry import (
    CATCH_ALL_OUTLETS, CATCH_ALL_SUBREDDITS, CATCH_ALL_VERIFIED_OFFICIALS,
    CATCH_ALL_X_USERS,
    account_profile_dict, canonicalize_handle, catch_all_profile,
    resolve_entity, sampled_account_profile, verified_officials_profile,
)


# --------------------------------------------------------------------------- #
#  Module constants                                                           #
# --------------------------------------------------------------------------- #

# Floors for promoting an unmatched public-tier X author out of the
# "Other X users" catch-all into their own named card. Both must hold:
# enough posts in the window to be a real voice in the sample, and enough
# followers that the card highlights an account with an audience rather
# than a private citizen. Everything below the floors (and beyond the card
# cap) stays pooled in the catch-all — counted, never dropped.
MIN_SAMPLED_AUTHOR_POSTS = 3
MIN_SAMPLED_AUTHOR_FOLLOWERS = 1000
MAX_SAMPLED_AUTHOR_CARDS = 12


# --------------------------------------------------------------------------- #
#  Entity routing wrapper                                                     #
# --------------------------------------------------------------------------- #

def _route_and_record(
    accum: Dict[str, Any],
    registry,
    source_type: Optional[str],
    domain_or_subreddit: Optional[str],
    x_handle: Optional[str],
    doc_id: int,
    label: str,
    conf: float,
    data: Dict[str, Any],
    title: Optional[str],
    published_at: Any,
    ident: Optional[str],
    text: Optional[str],
    is_official_tier: bool = False,
    account: Optional[Dict[str, Any]] = None,
    topic: Optional[str] = None,
    engagement: Optional[Dict[str, int]] = None,
    author: Optional[Dict[str, Any]] = None,
) -> Optional[str]:
    """Resolve the row's tier + entity via the shared entity_routing
    module, then bucket the row into the right per-entity accumulator
    (sentiment-specific shape).

    ``is_official_tier`` carries the ingestor's x_posts_raw provenance flag
    so a post fetched via the verified-officials timeline lands in the
    officials tier even when its stored handle doesn't match the editorial
    registry (audit D-4).

    ``account`` is the author's curated account_profiles classification
    (tier / full_name / party / office_title / account_type), or None for
    unclassified authors. A classified X author who isn't in the editorial
    registry gets a named per-account card (kind='account') instead of
    disappearing into the "Other X users" catch-all; the
    ``elected_official`` tier additionally upgrades the row to the
    officials column.

    Returns the tier label ('news' | 'officials' | 'public') for use by
    the per-topic three-way split; None for unknown source_types.
    """
    tier, entity = resolve_entity(
        registry, source_type, domain_or_subreddit, x_handle,
        is_official_tier=is_official_tier,
    )
    if tier is None:
        return None

    account_key = None
    if (
        source_type == "x_post" and entity is None and account is not None
        and x_handle and account.get("tier") in ("elected_official", "affiliated")
    ):
        account_key = canonicalize_handle(x_handle)
        if account["tier"] == "elected_official":
            tier = "officials"

    if tier == "news":
        bucket_dict = accum["by_news_outlet"]
        if entity is not None:
            key, profile, kind = entity.domain, entity.profile_dict(), "outlet"
        else:
            key = CATCH_ALL_OUTLETS
            profile = catch_all_profile(
                CATCH_ALL_OUTLETS, "Other news outlets",
                "News docs whose domain is not in the tracked outlet registry.",
            )
            kind = "catch_all"
    elif tier == "officials":
        bucket_dict = accum["by_official"]
        if entity is not None:
            key, profile, kind = entity.handle, entity.profile_dict(), "official"
        elif account_key is not None:
            key = account_key
            profile = account_profile_dict(
                account_key, account["tier"], account.get("full_name"),
                account.get("party"), account.get("office_title"),
                account.get("account_type"),
            )
            kind = "account"
        else:
            # Routed to officials purely by the is_official_tier provenance
            # flag (verified-officials timeline pull) with no editorial entity
            # to render — bucket into the shared verified-officials catch-all.
            key = CATCH_ALL_VERIFIED_OFFICIALS
            profile = verified_officials_profile()
            kind = "catch_all"
    else:  # public
        bucket_dict = accum["by_general_public"]
        if entity is not None:
            key, profile, kind = entity.subreddit, entity.profile_dict(), "subreddit"
        elif account_key is not None:
            key = account_key
            profile = account_profile_dict(
                account_key, account["tier"], account.get("full_name"),
                account.get("party"), account.get("office_title"),
                account.get("account_type"),
            )
            kind = "account"
        elif source_type == "x_post" and author and x_handle:
            # Unmatched author with a stored profile: bucket per handle so
            # active-enough voices become named cards. The consolidation
            # pass (_consolidate_sampled_authors) folds everyone below the
            # post/follower floors back into the catch-all.
            key = canonicalize_handle(x_handle) or x_handle.lower()
            profile = sampled_account_profile(
                key,
                display_name=author.get("display_name"),
                bio=author.get("bio"),
                followers_count=author.get("followers_count"),
            )
            kind = "account"
        elif source_type == "x_post":
            key = CATCH_ALL_X_USERS
            profile = _other_x_users_profile()
            kind = "catch_all"
        else:
            key = CATCH_ALL_SUBREDDITS
            profile = catch_all_profile(
                CATCH_ALL_SUBREDDITS, "Other subreddits",
                "Reddit posts whose subreddit is not in the tracked subreddit registry.",
            )
            kind = "catch_all"

    _init_entity_bucket(bucket_dict, key, kind, profile)
    if kind == "account" and profile.get("accountType") == "sampled":
        # Mark for the consolidation pass and track the follower floor.
        bucket_dict[key]["sampled"] = True
        followers = (author or {}).get("followers_count") or 0
        bucket_dict[key]["followers"] = max(
            bucket_dict[key].get("followers", 0), followers,
        )
    _collect_entity_sample(
        bucket_dict[key], doc_id, label, conf, data,
        title, source_type, published_at, domain_or_subreddit, ident, text, x_handle,
        topic=topic, engagement=engagement, author=author,
    )
    return tier


def _other_x_users_profile() -> Dict[str, Any]:
    """The public-tier X catch-all profile — shared by the routing branch
    and the consolidation pass so the blurb can't drift between them."""
    return catch_all_profile(
        CATCH_ALL_X_USERS, "Other X users",
        "X posts whose author is not in the tracked officials registry, the "
        "curated political-accounts list, or active enough in this window "
        "for an individual card.",
    )


def _consolidate_sampled_authors(bucket: Dict[str, Dict[str, Any]]) -> None:
    """Fold sub-floor sampled-author buckets back into the catch-all.

    During the row pass every unmatched public-tier X author gets a
    per-handle bucket (we can't know their window totals mid-stream).
    Afterwards, only authors clearing BOTH floors — MIN_SAMPLED_AUTHOR_POSTS
    and MIN_SAMPLED_AUTHOR_FOLLOWERS — keep a named card, capped at
    MAX_SAMPLED_AUTHOR_CARDS by volume. Everyone else's counts, per-topic
    cells, and samples merge into "Other X users": pooled, never dropped.
    """
    sampled = [(k, v) for k, v in bucket.items() if v.get("sampled")]
    if not sampled:
        return
    qualifying = sorted(
        (
            (k, v) for k, v in sampled
            if v["volume"] >= MIN_SAMPLED_AUTHOR_POSTS
            and v.get("followers", 0) >= MIN_SAMPLED_AUTHOR_FOLLOWERS
        ),
        key=lambda kv: -kv[1]["volume"],
    )
    keep = {k for k, _ in qualifying[:MAX_SAMPLED_AUTHOR_CARDS]}
    demoted = [(k, v) for k, v in sampled if k not in keep]
    if not demoted:
        return

    if CATCH_ALL_X_USERS not in bucket:
        _init_entity_bucket(
            bucket, CATCH_ALL_X_USERS, "catch_all", _other_x_users_profile(),
        )
    catch = bucket[CATCH_ALL_X_USERS]
    for key, stats in demoted:
        for label_key in ("positive", "negative", "neutral"):
            catch[label_key] += stats[label_key]
        catch["volume"] += stats["volume"]
        catch["engagement_total"] += stats.get("engagement_total", 0)
        for topic, counts in stats["by_topic"].items():
            target = catch["by_topic"].setdefault(
                topic, {"positive": 0, "negative": 0, "neutral": 0, "mixed": 0},
            )
            for label_key, n in counts.items():
                target[label_key] = target.get(label_key, 0) + n
        for sample in stats["samples"]:
            _insert_capped(catch["samples"], sample, MAX_SAMPLES_PER_ENTITY)
        del bucket[key]


def _init_entity_bucket(
    bucket: Dict[str, Dict[str, Any]],
    key: str,
    kind: str,
    profile: Dict[str, Any],
) -> None:
    """Create a fresh per-entity accumulator if ``key`` isn't present."""
    if key in bucket:
        return
    bucket[key] = {
        "kind": kind, "profile": profile,
        "positive": 0, "negative": 0, "neutral": 0, "volume": 0,
        "samples": [],
        # Summed engagement (likes + reposts + replies + quotes) across ALL
        # of the entity's posts in the window — powers the officials column's
        # engagement-weighted default sort. 0 for news (no engagement signal).
        "engagement_total": 0,
        # Per-topic stance counts for the entity's own posts — powers the
        # topic-scoped expressed score in the profile modal.
        "by_topic": {},
    }
