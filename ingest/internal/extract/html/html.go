package html

import (
	"bytes"
	"regexp"
	"strings"
	"time"

	"golang.org/x/net/html"
)

// Metadata represents extracted HTML metadata.
type Metadata struct {
	Title         string
	CanonicalURL  string
	PublishedTime time.Time
	Description   string
	Outlinks      []string
}

// Extract parses HTML and extracts metadata and links.
func Extract(data []byte, baseURL string) (*Metadata, error) {
	doc, err := html.Parse(bytes.NewReader(data))
	if err != nil {
		return nil, err
	}

	m := &Metadata{}
	var outlinks []string

	var extractNode func(*html.Node)
	extractNode = func(n *html.Node) {
		if n.Type == html.ElementNode {
			switch n.Data {
			case "title":
				if n.FirstChild != nil && m.Title == "" {
					m.Title = strings.TrimSpace(n.FirstChild.Data)
				}
			case "link":
				rel := getAttr(n, "rel")
				href := getAttr(n, "href")
				if rel == "canonical" && href != "" {
					m.CanonicalURL = resolveURL(baseURL, href)
				}
			case "meta":
				name := getAttr(n, "name")
				property := getAttr(n, "property")
				content := getAttr(n, "content")

				switch {
				case property == "og:title" && m.Title == "":
					m.Title = content
				case property == "og:url" && m.CanonicalURL == "":
					m.CanonicalURL = content
				case property == "article:published_time" && content != "":
					m.PublishedTime = parseTime(content)
				case name == "description" || property == "og:description":
					if m.Description == "" {
						m.Description = content
					}
				case name == "pubdate" || name == "date":
					if m.PublishedTime.IsZero() {
						m.PublishedTime = parseTime(content)
					}
				}
			case "a":
				href := getAttr(n, "href")
				if href != "" && !strings.HasPrefix(href, "#") && !strings.HasPrefix(href, "javascript:") {
					resolved := resolveURL(baseURL, href)
					if resolved != "" && (strings.HasPrefix(resolved, "http://") || strings.HasPrefix(resolved, "https://")) {
						outlinks = append(outlinks, resolved)
					}
				}
			case "time":
				datetime := getAttr(n, "datetime")
				if m.PublishedTime.IsZero() && datetime != "" {
					m.PublishedTime = parseTime(datetime)
				}
			}
		}

		for c := n.FirstChild; c != nil; c = c.NextSibling {
			extractNode(c)
		}
	}

	extractNode(doc)
	m.Outlinks = dedupeLinks(outlinks)

	return m, nil
}

func getAttr(n *html.Node, key string) string {
	for _, a := range n.Attr {
		if a.Key == key {
			return a.Val
		}
	}
	return ""
}

// Simple URL resolver (handles relative paths)
func resolveURL(base, href string) string {
	href = strings.TrimSpace(href)
	if strings.HasPrefix(href, "http://") || strings.HasPrefix(href, "https://") {
		return href
	}
	if strings.HasPrefix(href, "//") {
		return "https:" + href
	}
	if strings.HasPrefix(href, "/") {
		// Extract scheme and host from base
		re := regexp.MustCompile(`^(https?://[^/]+)`)
		if m := re.FindString(base); m != "" {
			return m + href
		}
	}
	// Relative path - append to base directory
	if idx := strings.LastIndex(base, "/"); idx > 8 { // after "https://"
		return base[:idx+1] + href
	}
	return base + "/" + href
}

func dedupeLinks(links []string) []string {
	seen := make(map[string]bool)
	var result []string
	for _, l := range links {
		if !seen[l] {
			seen[l] = true
			result = append(result, l)
		}
	}
	return result
}

var timeFormats = []string{
	time.RFC3339,
	"2006-01-02T15:04:05Z",
	"2006-01-02T15:04:05-07:00",
	"2006-01-02",
	"January 2, 2006",
	"Jan 2, 2006",
}

func parseTime(s string) time.Time {
	s = strings.TrimSpace(s)
	for _, format := range timeFormats {
		if t, err := time.Parse(format, s); err == nil {
			return t
		}
	}
	return time.Time{}
}
