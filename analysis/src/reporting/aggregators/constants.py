"""
Shared constants for reporting aggregators.

Contains platform categorizations, topic keyword mappings, country codes,
and threshold values used across aggregator modules.
"""


# Platform categorization: map source_type to display category
SOCIAL_PLATFORMS = frozenset(['reddit_post', 'reddit_comment', 'social', 'x_post'])
NEWS_PLATFORMS = frozenset(['news', 'rss'])

# Confidence threshold for mild vs strong classification
STRONG_CONFIDENCE_THRESHOLD = 0.7

# Common political topics for keyword extraction
TOPIC_KEYWORDS = {
    'Immigration': [
        'immigration', 'border', 'migrant', 'asylum', 'deportation', 'illegal',
        'daca', 'visa', 'refugee', 'citizenship', 'undocumented', 'ice',
        'border wall', 'sanctuary city', 'caravan', 'title 42',
    ],
    'Economy': [
        'economy', 'inflation', 'jobs', 'unemployment', 'tax', 'tariff', 'recession',
        'gdp', 'fed', 'interest rate', 'stock market', 'wage', 'minimum wage',
        'debt ceiling', 'trade war', 'supply chain', 'cost of living',
    ],
    'Healthcare': [
        'healthcare', 'obamacare', 'medicare', 'insurance', 'hospital', 'medical',
        'medicaid', 'drug prices', 'pharma', 'mental health', 'pandemic',
        'vaccine', 'public health', 'prescription', 'affordable care',
    ],
    'Climate': [
        'climate', 'energy', 'green', 'carbon', 'emissions', 'fossil', 'renewable',
        'solar', 'wind', 'ev', 'electric vehicle', 'paris agreement',
        'epa', 'pollution', 'wildfire', 'natural disaster', 'oil',
    ],
    'Foreign Policy': [
        'foreign', 'russia', 'china', 'ukraine', 'military', 'nato', 'war', 'troops',
        'iran', 'israel', 'gaza', 'palestine', 'north korea', 'taiwan',
        'sanctions', 'diplomacy', 'pentagon', 'drone', 'intelligence',
    ],
    'Gun Policy': [
        'gun', 'firearm', 'second amendment', 'nra', 'shooting',
        'mass shooting', 'gun control', 'gun violence', 'background check',
        'assault weapon', 'concealed carry', 'red flag',
    ],
    'Abortion': [
        'abortion', 'roe', 'reproductive', 'pro-life', 'pro-choice',
        'dobbs', 'planned parenthood', 'contraception', 'fetal',
    ],
    'Education': [
        'education', 'school', 'student', 'college', 'university', 'teacher',
        'student loan', 'tuition', 'charter school', 'curriculum',
        'dei', 'critical race theory', 'book ban', 'homeschool',
    ],
    'Justice': [
        'justice', 'supreme court', 'judges', 'crime', 'police', 'prison',
        'criminal justice', 'bail reform', 'death penalty', 'sentencing',
        'fbi', 'doj', 'attorney general', 'civil rights', 'qualified immunity',
    ],
    'Technology': [
        'ai', 'artificial intelligence', 'social media', 'big tech', 'tiktok',
        'data privacy', 'antitrust', 'crypto', 'bitcoin', 'regulation',
        'section 230', 'deepfake', 'cybersecurity', 'surveillance',
    ],
    'Social Issues': [
        'lgbtq', 'transgender', 'same-sex', 'racial justice', 'blm',
        'woke', 'cancel culture', 'diversity', 'equity', 'inclusion',
        'affirmative action', 'discrimination', 'hate crime',
    ],
    'Democracy': [
        'election', 'voting', 'ballot', 'gerrymandering', 'filibuster',
        'electoral college', 'voter fraud', 'election integrity',
        'campaign finance', 'dark money', 'term limits', 'january 6',
    ],
    'Housing': [
        'housing', 'rent', 'mortgage', 'homelessness', 'affordable housing',
        'zoning', 'landlord', 'eviction', 'gentrification', 'real estate',
    ],
    'National Security': [
        'terrorism', 'homeland security', 'border security', 'fisa',
        'espionage', 'classified', 'national guard', 'veteran', 'va',
    ],
}


# =============================================================================
# Geographic Constants
# =============================================================================

# ISO 3166-1 alpha-2 to country name mapping (common countries)
COUNTRY_NAMES = {
    "US": "United States",
    "GB": "United Kingdom",
    "CA": "Canada",
    "AU": "Australia",
    "DE": "Germany",
    "FR": "France",
    "IN": "India",
    "BR": "Brazil",
    "MX": "Mexico",
    "RU": "Russia",
    "CN": "China",
    "JP": "Japan",
    "KR": "South Korea",
    "IT": "Italy",
    "ES": "Spain",
    "NL": "Netherlands",
    "SE": "Sweden",
    "NO": "Norway",
    "PL": "Poland",
    "UA": "Ukraine",
    "IL": "Israel",
    "SA": "Saudi Arabia",
    "AE": "United Arab Emirates",
    "TR": "Turkey",
    "ZA": "South Africa",
    "NG": "Nigeria",
    "EG": "Egypt",
    "AR": "Argentina",
    "CO": "Colombia",
    "PH": "Philippines",
    "ID": "Indonesia",
    "MY": "Malaysia",
    "SG": "Singapore",
    "TH": "Thailand",
    "VN": "Vietnam",
    "PK": "Pakistan",
    "BD": "Bangladesh",
    "IR": "Iran",
    "IQ": "Iraq",
}
