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

# Fixed sentiment confidence for the deterministic trivial-content
# short-circuit (prompt rule 6: mentions/links/hashtags only -> low
# confidence). Matches the old analyzer.py's NEUTRAL/0.5 trivial-content value.
TRIVIAL_CONTENT_CONFIDENCE = 0.5
