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
	URLCanon      string
	URLRaw        string
	Domain        string
	State         PageState
	Priority      int
	Retries       int
	NextFetchAt   int64 // Unix timestamp
	InflightAt    int64 // Unix timestamp when claimed
	HTTPStatus    int
	ContentSHA256 string
	ETag          string
	LastModified  string
	LastError     string
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

// XPost represents an X (Twitter) post.
type XPost struct {
	TweetID                string
	AuthorID               string
	ConversationID         string
	CreatedAt              int64
	FetchedAt              int64
	Text                   string
	Lang                   string
	RetweetCount           int
	ReplyCount             int
	LikeCount              int
	QuoteCount             int
	PlaceID                string
	PlaceCountryCode       string
	PlaceFullName          string
	ContextAnnotationsJSON string
	InReplyToUserID        string
	ReferencedTweetID      string
	ReferencedTweetType    string
	RawHash                string
	ExtractionVersion      string
}

// XUser represents an X (Twitter) user.
type XUser struct {
	UserID          string
	Username        string
	Name            string
	Location        string
	Description     string
	CreatedAt       int64
	FetchedAt       int64
	FollowersCount  int
	FollowingCount  int
	TweetCount      int
	ListedCount     int
	Verified        bool
	VerifiedType    string
	ProfileImageURL string
	Protected       bool
	RawHash         string
}
