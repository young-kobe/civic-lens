"""
Shared constants for Civic Lens analysis engines.

Contains keyword lists, entity sets, and threshold values used by
sentiment, favorability, and bot detection analyzers.
"""


# Proximity window: only count stance keywords within this many words of an entity
# (STRONG_CONFIDENCE_THRESHOLD lives in reporting/aggregators/constants.py —
# single source of truth for the aggregator-owned threshold.)
PROXIMITY_WINDOW = 50


# =============================================================================
# Sentiment Analysis Constants
# =============================================================================

POSITIVE_WORDS = frozenset([
    # General positive
    "good", "great", "excellent", "amazing", "love", "support", "agree",
    "wonderful", "fantastic", "brilliant", "outstanding", "positive",
    "beneficial", "successful", "effective", "impressive", "promising",
    "strong", "robust", "healthy", "progress", "improve", "win", "victory",
    "approve", "praise", "commend", "celebrate", "achieve", "accomplish",
    # Political positive
    "bipartisan", "consensus", "historic", "landmark", "breakthrough",
    "reform", "prosperity", "growth", "unity", "compromise", "mandate",
    "landslide", "coalition", "alliance", "delivered", "progress",
    "hero", "patriot", "freedom", "liberty", "proud", "brave",
    "champion", "triumph", "endorsed", "rally", "momentum",
    "optimistic", "hopeful", "empowered", "transformative", "visionary",
    # Internet slang / social media positive
    "based", "goat", "goated", "w", "dub", "fire", "lit", "bussin",
    "valid", "king", "queen", "chad", "gigachad", "sigma",
    "peak", "clutch", "iconic", "slaps", "banger", "elite",
    "respect", "lets go", "lfg", "huge", "massive", "legend",
    "underrated", "lowkey great", "no cap", "hits different",
    "real one", "blessed", "redemption arc", "cooking", "cooked",
    "common w", "gigabased", "built different", "fax", "spitting",
])

NEGATIVE_WORDS = frozenset([
    # General negative
    "bad", "terrible", "hate", "awful", "stupid", "wrong", "disagree",
    "fake", "failure", "disaster", "corrupt", "scandal", "crisis",
    "dangerous", "harmful", "threat", "attack", "destroy", "collapse",
    "reject", "oppose", "condemn", "criticize", "blame", "accuse",
    "incompetent", "unacceptable", "outrageous", "shocking", "disgrace",
    # Political negative
    "gridlock", "shutdown", "stalemate", "polarization", "dysfunction",
    "partisan", "obstruct", "veto", "filibuster", "deficit",
    "indicted", "convicted", "impeached", "authoritarian", "extremist",
    "insurrection", "fascist", "radical", "divisive", "chaotic",
    "reckless", "unfit", "fraud", "lies", "liar", "coverup",
    "abuse", "tyranny", "rigged", "betrayal", "hostile",
    # Internet slang / social media negative
    "cringe", "cope", "copium", "ratio", "ratioed", "l", "mid",
    "clown", "clownshow", "brainrot", "braindead", "npc",
    "shill", "grifter", "grift", "boomer", "doomer", "doompost",
    "unhinged", "delusional", "yikes", "oof", "sus",
    "trash", "garbage", "common l", "massive l", "down bad",
    "rent free", "coping", "seething", "malding", "touch grass",
    "smooth brain", "bot", "astroturf", "psyop",
    "clown world", "honk", "dead", "get rekt",
    # Derogatory / inflammatory
    "satanist", "satanists", "pedophile", "pedophiles", "groomer", "groomers",
    "evil", "demon", "demons", "traitor", "traitors", "scum", "filth",
    "vermin", "thugs", "wicked", "degenerate", "degenerates", "sick",
    "disgusting", "vile", "pathetic", "worthless", "moron", "idiot",
])

INTENSIFIERS = frozenset([
    "very", "extremely", "incredibly", "absolutely", "completely",
    "totally", "utterly", "highly", "deeply", "strongly",
])

NEGATORS = frozenset([
    "not", "no", "never", "neither", "nobody", "nothing", "nowhere",
    "hardly", "barely", "scarcely", "without", "isn't", "aren't",
    "wasn't", "weren't", "won't", "wouldn't", "couldn't", "shouldn't",
])


# =============================================================================
# GOP Favorability Constants
# =============================================================================

GOP_ENTITIES = frozenset([
    # Party names
    "republican", "republicans", "gop", "republican party",
    # Major figures (keep updated)
    "trump", "donald trump", "desantis", "ron desantis",
    "mccarthy", "kevin mccarthy", "mcconnell", "mitch mcconnell",
    "pence", "mike pence", "haley", "nikki haley",
    "cruz", "ted cruz", "rubio", "marco rubio",
    "graham", "lindsey graham", "jordan", "jim jordan",
    # Generic references
    "conservatives", "conservative", "right-wing", "maga",
])

FAVORABLE_INDICATORS = frozenset([
    # Direct support
    "support", "supports", "backed", "backs", "endorses", "praised",
    "commended", "applauded", "championed", "defended", "advocates",
    "agree", "agrees", "approve", "approves", "celebrates", "credits",
    "admires", "respects", "stands with", "aligned with", "pro-",
    # Achievement/success language
    "won", "wins", "victory", "triumph", "leads", "leading",
    "accomplished", "delivered", "achieved", "succeeded", "landslide",
    "mandate", "historic", "landmark", "breakthrough", "momentum",
    # Positive characterization
    "hero", "patriot", "brave", "strong leader", "visionary",
    "promises kept", "freedom", "liberty", "proud", "bold",
    "reform", "prosperity", "coalition", "alliance", "bipartisan",
    "effective", "decisive", "principled", "transparent",
])

UNFAVORABLE_INDICATORS = frozenset([
    # Direct opposition
    "oppose", "opposes", "criticized", "criticizes", "condemned",
    "attacked", "attacks", "slammed", "blasted", "denounced",
    "rejected", "rejects", "blame", "blames", "accuses", "accused",
    "against", "anti-", "failed", "scandal", "corrupt", "dangerous",
    # Legal/institutional
    "indicted", "convicted", "impeached", "charged", "investigated",
    "subpoenaed", "lawsuit", "violation", "obstruction",
    # Negative characterization
    "authoritarian", "extremist", "insurrection", "fascist", "radical",
    "divisive", "chaotic", "reckless", "unfit", "disgrace",
    "fraud", "lies", "liar", "coverup", "abuse of power",
    "tyranny", "rigged", "betrayal", "hostile", "incompetent",
    # Failure language
    "lost", "defeat", "defeated", "losing", "collapsed", "backfired",
    "abandoned", "broken promise", "dysfunction", "gridlock",
])


# =============================================================================
# Bot Detection Constants (walkthrough 040 rework)
#
# The old SPAM_KEYWORDS list targeted 2018-era spambots ("buy now", "viagra").
# Modern propaganda-driver accounts use LLM-generated political text that hits
# zero of those keywords. The signal lists below target LLM-generated text
# and account-level automation instead.
# =============================================================================

# Low-volume retained spam signal. Still useful for pure spam / affiliate bots
# that slip through, but no longer the headline signal.
SPAM_KEYWORDS = frozenset([
    "buy now", "click here", "limited time offer", "free gift",
    "act now", "exclusive deal", "100% free",
])

# Hedge-phrase list — LLM output over-indexes on cautious, acknowledge-both-
# sides phrasing. Casual political social-media writing uses these at a
# much lower rate. Match case-insensitively as substrings.
LLM_HEDGE_PHRASES = frozenset([
    "it's important to note",
    "it is important to note",
    "it's worth noting",
    "it is worth noting",
    "while it's true that",
    "while it is true that",
    "one could argue",
    "it could be argued",
    "studies have shown",
    "research suggests",
    "research indicates",
    "on one hand",
    "on the other hand",
    "that being said",
    "with that said",
    "having said that",
    "as an ai language model",
    "i'm just a language model",
    "i am an ai",
    "there are many perspectives",
    "various factors",
    "a complex issue",
    "a nuanced issue",
    "a multifaceted issue",
    "multifaceted topic",
    "in conclusion",
    "to summarize",
    "to sum up",
    "in summary",
    "it is essential to",
    "it's essential to",
    "it is crucial to",
    "it's crucial to",
])

# Typographic tells — characters LLMs use at unnatural rates in social-media
# writing (where most humans fall back to ASCII apostrophes / hyphens).
LLM_TYPOGRAPHIC_TELLS = (
    "\u2014",  # em-dash
    "\u2018",  # left single smart quote
    "\u2019",  # right single smart quote
    "\u201C",  # left double smart quote
    "\u201D",  # right double smart quote
    "\u2026",  # horizontal ellipsis
)

# Patterns suggesting coordinated behavior (retained; aggregator uses these labels)
COORDINATION_PATTERNS = [
    ("repetitive_text", "Text contains highly repetitive phrases"),
    ("url_density", "Unusually high URL/link density"),
    ("hashtag_spam", "Excessive hashtag usage"),
    ("timing_burst", "Posted in coordination burst window"),
    ("llm_text_style", "Text style matches LLM-generated patterns"),
]


# =============================================================================
# Evidence-span validation constants (Postgres redesign Phase 5,
# engine/validation.py — the unified validator; see that module and
# docs/audit-trail/analysis/2026-07-22-pg-analysis-plumbing.md for the
# majority-precedent rationale behind these specific values).
# =============================================================================

MIN_EVIDENCE_WORDS = 4
UNVERIFIED_EVIDENCE_CONFIDENCE_CAP = 0.3
MIN_CLAIM_WORDS = 4
MAX_CLAIM_WORDS = 20


# =============================================================================
# Text engine constants (Postgres redesign Phase 6, engine/text.py -- the
# unified sentiment+favorability engine). Additive section.
# =============================================================================

# Character budget for the doc text the LLM sees, clamped at a sentence
# boundary by text_prep.truncate_at_sentence. Matches the old analyzer.py's
# TEXT_ANALYSIS_MAX_CHARS (that module keeps its own copy; untouched, live).
TEXT_ANALYSIS_MAX_CHARS = 2000


# =============================================================================
# Wave 2 engine constants (Postgres redesign Phase 6, engine/{targets,
# propaganda,claims}.py). Consolidated here 2026-07-23 -- each of those
# modules originally kept these module-local to avoid concurrent edits to
# this file while landing in parallel; now that all three are in, task-name
# strings and the numeric budgets/caps/defaults move here per the owner's
# constants-consolidation rule, matching the precedent set above by
# TEXT_ANALYSIS_MAX_CHARS. Private lookup/mapping
# tables tightly coupled to one engine's own logic (targets.py's _STANCE_MAP,
# propaganda.py's _DDL_PROPAGANDA_TECHNIQUES enum-gate frozenset) stay
# module-local: they are read by exactly one function each, and moving them
# here would separate a lookup table from its only reader without making it
# any more shared. Compiled regexes and presentational strings (propaganda.py's
# _WORD_RE, _PRE_FILTER_REASONING) stay local for the same reason -- this file
# holds plain data constants, not implementation detail or narrative text.
# =============================================================================

TARGETS_TASK = "targets"
PROPAGANDA_TASK = "propaganda"
CLAIMS_TASK = "claims"

# Schema instructs the LLM to extract at most 4 targets; enforced defensively
# in engine/targets.py too (ported from old engine/target_extractor.py's
# MAX_TARGETS).
MAX_TARGETS = 4

# Character budget for the doc text the LLM sees in engine/targets.py,
# clamped at a sentence boundary (matches old target_extractor.py's
# TARGET_TEXT_MAX_CHARS).
TARGET_TEXT_MAX_CHARS = 2000

# Character budget for the text engine/propaganda.py's LLM sees -- unchanged
# from the old detector (propaganda techniques surface in the opening
# rhetoric: headline + first 2-3 paragraphs, not paragraph 12).
PROPAGANDA_TEXT_MAX_CHARS = 800

# Schema caps engine/propaganda.py's techniques at 5; enforced defensively
# here too.
MAX_PROPAGANDA_TECHNIQUES = 5

# engine/propaganda.py's loaded-language pre-filter, ported verbatim from the
# old detector's _has_loaded_language: union of the negative-word and
# intensifier lexicons. If the scored window contains none of these, the six
# starter techniques (all of which ride on loaded vocabulary) are
# overwhelmingly unlikely -- skip the LLM call and record a deterministic
# zero-technique result instead of a silent skip, so the doc is marked done
# and never re-queued for this.
PROPAGANDA_LOADED_LEXICON = NEGATIVE_WORDS | INTENSIFIERS

# Number of characters from the start of the (title + text) doc that
# engine/propaganda.py's pre-filter scans for loaded language.
PROPAGANDA_PRE_FILTER_SCAN_CHARS = 600

# Character budget for the doc text engine/claims.py's LLM sees, clamped at a
# sentence boundary. Matches the old claim_extractor.py's CLAIM_TEXT_MAX_CHARS.
CLAIM_TEXT_MAX_CHARS = 2000

# Prompt rule 2 caps engine/claims.py's model at 3 claims; enforced
# defensively here too in case a backend ignores the instruction.
MAX_CLAIMS_PER_DOC = 3

# Run confidence engine/claims.py uses for a doc with zero surviving claims --
# old convention (job_runner.py's run_claim_extraction: `sum(confidences)/
# len(confidences) if confidences else 0.0`).
ZERO_CLAIMS_CONFIDENCE = 0.0


# =============================================================================
# Wave 3 engine constants (Postgres redesign Phase 6, engine/{bot_detection,
# account_tier,narrative_clustering}.py). Consolidated here 2026-07-23,
# following the Wave 2 precedent above (task-identifying strings and
# numeric budgets/caps/thresholds move; private lookup/gate values,
# compiled patterns, and presentational strings stay module-local since
# moving them would only separate a table from its single reader).
#
# Stayed local, with the rule applied: bot_detection.py's
# _GOVERNMENT_VERIFIED_TYPE/_BUSINESS_VERIFIED_TYPE (de-bias gate values
# read by exactly one function, _aggregate_score -- the same role
# targets.py's _STANCE_MAP plays, just single-valued instead of a dict) and
# its compiled _SENTENCE_SPLIT_RE/_NOISE_INDICATOR_PATTERNS; account_tier.py's
# SQL strings (this file holds data constants, not query text);
# narrative_clustering.py's _STOPWORDS (a private lookup table read by
# exactly one function, tokenize_claim) and its compiled _TOKEN_RE, plus
# the EmbedFn type alias (not a data constant of the kind this file holds).
# =============================================================================

BOT_TASK = "bot"

# Prompt-input truncation -- matches old engine/bot.py's `text[:1500]` verbatim.
BOT_PROMPT_TEXT_MAX_CHARS = 1500

# Account/text thresholds from old bot.py's _compute_signals/_aggregate_score.
NEW_ACCOUNT_AGE_DAYS = 7  # generic "new account" indicator, any platform
X_NEW_ACCOUNT_AGE_DAYS = 90  # X-specific stricter new-account flag
X_LOW_FOLLOWERS_THRESHOLD = 50
FOLLOW_RATIO_ANOMALY_MIN_FOLLOWING = 1000
FOLLOW_RATIO_ANOMALY_MAX_FOLLOWER_SHARE = 0.1

# corpus.author_tier / corpus.classification_method enum values
# engine/account_tier.py writes -- named here so a future consumer (tests,
# serving-layer rollups) reads the same literal rather than re-typing it.
ELECTED_OFFICIAL_TIER = "elected_official"
AFFILIATED_TIER = "affiliated"
CURATED_LIST_METHOD = "curated_list"

# Fragmentation fix (engine/narrative_clustering.py): a narrative persists
# only once >= this many DISTINCT docs support it.
MIN_NARRATIVE_SUPPORT = 2

# How far back narrative_clustering.py's _load_pending_claims looks (by
# analysis.claims.created_at). Matches the old clusterer's
# CLAIM_LOOKBACK_SECONDS (30 days).
CLAIM_LOOKBACK_DAYS = 30

# analysis.narratives.name truncation width (old clusterer used the same
# literal for its `name` column).
NARRATIVE_NAME_MAX_CHARS = 120


# =============================================================================
# Lean derivation constants (Postgres redesign Phase 7, engine/
# lean_derivation.py -- the deterministic author/narrative political-lean
# gate). See that module for the full three-way gate this feeds.
# =============================================================================

# Below this many pooled directional stance samples, lean is 'unknown'
# (insufficient evidence) regardless of how one-sided they are.
LEAN_MIN_SAMPLE_COUNT = 5

# At/above LEAN_MIN_SAMPLE_COUNT, the majority side's share of samples must
# reach this threshold for a decided lean ('democrat'/'republican'); below
# it, the outcome is 'mixed' (balanced evidence is a finding, not ignorance).
LEAN_SHARE_THRESHOLD = 0.7

# Sample count at which lean_confidence stops scaling up with more evidence
# (confidence = lean_share * min(1.0, samples / this)).
LEAN_CONFIDENCE_SATURATION_SAMPLES = 20
