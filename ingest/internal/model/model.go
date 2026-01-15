package model

// PageState represents the state of a page in the frontier.
type PageState int

const (
	StateQueued   PageState = 0
	StateInflight PageState = 1
	StateDone     PageState = 2
	StateFailed   PageState = 3
)

// Page represents a URL in the crawl frontier.
type Page struct {
	URLCanon     string
	URLRaw       string
	Domain       string
	State        PageState
	Priority     int
	Retries      int
	NextFetchAt  int64  // Unix timestamp
	InflightAt   int64  // Unix timestamp when claimed
	HTTPStatus   int
	ContentSHA256 string
	ETag         string
	LastModified string
	LastError    string
}

// ArticleRaw represents extracted article metadata.
type ArticleRaw struct {
	URLCanon          string
	Domain            string
	FetchedAt         int64
	PublishedAt       int64
	Title             string
	RawHash           string
	ExtractionVersion string
}

// RedditPost represents a Reddit post.
type RedditPost struct {
	Fullname          string
	Subreddit         string
	CreatedUTC        int64
	FetchedAt         int64
	Title             string
	Body              string
	Score             int
	NumComments       int
	RawHash           string
	ExtractionVersion string
}

// RedditComment represents a Reddit comment.
type RedditComment struct {
	Fullname          string
	PostFullname      string
	Subreddit         string
	CreatedUTC        int64
	FetchedAt         int64
	Body              string
	Score             int
	RawHash           string
	ExtractionVersion string
}

// CrawlResult is the result of fetching one page.
type CrawlResult struct {
	Page       *Page
	Body       []byte
	StatusCode int
	Error      error
	Outlinks   []string
}
