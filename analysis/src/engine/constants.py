"""
Shared constants for Civic Lens analysis engines.

Contains keyword lists, entity sets, and threshold values used by
sentiment, favorability, and bot detection analyzers.
"""


# Confidence threshold for mild vs strong classification
STRONG_CONFIDENCE_THRESHOLD = 0.7

# Proximity window: only count stance keywords within this many words of an entity
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
# Bot Detection Constants
# =============================================================================

# Spam/bot keywords commonly found in automated content
SPAM_KEYWORDS = frozenset([
    "buy now", "click here", "subscribe", "limited time offer",
    "crypto", "bitcoin", "make money", "viagra", "free gift",
    "act now", "don't miss", "exclusive deal", "earn money",
    "work from home", "100% free", "amazing offer", "urgent",
])

# Patterns suggesting coordinated behavior
COORDINATION_PATTERNS = [
    ("repetitive_text", "Text contains highly repetitive phrases"),
    ("url_density", "Unusually high URL/link density"),
    ("hashtag_spam", "Excessive hashtag usage"),
    ("timing_burst", "Posted in coordination burst window"),
]
