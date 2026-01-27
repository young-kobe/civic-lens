package models

// SearchResponse represents the X API v2 search response.
type SearchResponse struct {
	Data     []TweetData `json:"data"`
	Includes *Includes   `json:"includes,omitempty"`
	Meta     *Meta       `json:"meta,omitempty"`
	Errors   []APIError  `json:"errors,omitempty"`
}

// TweetData represents a tweet from the API.
type TweetData struct {
	ID                 string              `json:"id"`
	Text               string              `json:"text"`
	AuthorID           string              `json:"author_id"`
	CreatedAt          string              `json:"created_at"`
	Lang               string              `json:"lang"`
	ConversationID     string              `json:"conversation_id"`
	InReplyToUserID    string              `json:"in_reply_to_user_id"`
	PublicMetrics      *PublicMetrics      `json:"public_metrics"`
	Geo                *Geo                `json:"geo"`
	ContextAnnotations []ContextAnnotation `json:"context_annotations"`
	ReferencedTweets   []ReferencedTweet   `json:"referenced_tweets"`
}

// PublicMetrics contains engagement counts.
type PublicMetrics struct {
	RetweetCount int `json:"retweet_count"`
	ReplyCount   int `json:"reply_count"`
	LikeCount    int `json:"like_count"`
	QuoteCount   int `json:"quote_count"`
}

// Geo contains place information.
type Geo struct {
	PlaceID string `json:"place_id"`
}

// ContextAnnotation contains topic/entity annotations.
type ContextAnnotation struct {
	Domain struct {
		ID          string `json:"id"`
		Name        string `json:"name"`
		Description string `json:"description"`
	} `json:"domain"`
	Entity struct {
		ID          string `json:"id"`
		Name        string `json:"name"`
		Description string `json:"description"`
	} `json:"entity"`
}

// ReferencedTweet indicates a reply, quote, or retweet.
type ReferencedTweet struct {
	Type string `json:"type"` // replied_to, quoted, retweeted
	ID   string `json:"id"`
}

// Includes contains expanded objects.
type Includes struct {
	Users  []UserData  `json:"users"`
	Places []PlaceData `json:"places"`
	Tweets []TweetData `json:"tweets"`
}

// UserData represents a user from the API.
type UserData struct {
	ID              string       `json:"id"`
	Username        string       `json:"username"`
	Name            string       `json:"name"`
	Location        string       `json:"location"`
	Description     string       `json:"description"`
	CreatedAt       string       `json:"created_at"`
	Verified        bool         `json:"verified"`
	VerifiedType    string       `json:"verified_type"`
	Protected       bool         `json:"protected"`
	PublicMetrics   *UserMetrics `json:"public_metrics"`
	ProfileImageURL string       `json:"profile_image_url"`
}

// UserMetrics contains user engagement counts.
type UserMetrics struct {
	FollowersCount int `json:"followers_count"`
	FollowingCount int `json:"following_count"`
	TweetCount     int `json:"tweet_count"`
	ListedCount    int `json:"listed_count"`
}

// PlaceData represents a place from the API.
type PlaceData struct {
	ID          string `json:"id"`
	FullName    string `json:"full_name"`
	Country     string `json:"country"`
	CountryCode string `json:"country_code"`
}

// Meta contains pagination info.
type Meta struct {
	NewestID    string `json:"newest_id"`
	OldestID    string `json:"oldest_id"`
	ResultCount int    `json:"result_count"`
	NextToken   string `json:"next_token"`
}

// APIError represents an API error.
type APIError struct {
	Title  string `json:"title"`
	Type   string `json:"type"`
	Detail string `json:"detail"`
	Status int    `json:"status"`
}
