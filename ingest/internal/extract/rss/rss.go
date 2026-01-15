package rss

import (
	"encoding/xml"
	"strings"
	"time"
)

// Feed represents a parsed RSS/Atom feed.
type Feed struct {
	Title   string
	Link    string
	Items   []Item
}

// Item represents a single feed item.
type Item struct {
	Title     string
	Link      string
	Published time.Time
	GUID      string
}

// Parse parses RSS or Atom XML content.
func Parse(data []byte) (*Feed, error) {
	// Try RSS 2.0 first
	var rss rss2Feed
	if err := xml.Unmarshal(data, &rss); err == nil && rss.Channel.Title != "" {
		return convertRSS2(rss), nil
	}

	// Try Atom
	var atom atomFeed
	if err := xml.Unmarshal(data, &atom); err == nil && atom.Title != "" {
		return convertAtom(atom), nil
	}

	// Try RSS 1.0 (RDF)
	var rdf rdfFeed
	if err := xml.Unmarshal(data, &rdf); err == nil && rdf.Channel.Title != "" {
		return convertRDF(rdf), nil
	}

	return nil, xml.Unmarshal(data, &rss) // Return the first error
}

// RSS 2.0 structures
type rss2Feed struct {
	XMLName xml.Name    `xml:"rss"`
	Channel rss2Channel `xml:"channel"`
}

type rss2Channel struct {
	Title string     `xml:"title"`
	Link  string     `xml:"link"`
	Items []rss2Item `xml:"item"`
}

type rss2Item struct {
	Title   string `xml:"title"`
	Link    string `xml:"link"`
	PubDate string `xml:"pubDate"`
	GUID    string `xml:"guid"`
}

func convertRSS2(r rss2Feed) *Feed {
	f := &Feed{
		Title: r.Channel.Title,
		Link:  r.Channel.Link,
	}
	for _, item := range r.Channel.Items {
		f.Items = append(f.Items, Item{
			Title:     item.Title,
			Link:      strings.TrimSpace(item.Link),
			Published: parseTime(item.PubDate),
			GUID:      item.GUID,
		})
	}
	return f
}

// Atom structures
type atomFeed struct {
	XMLName xml.Name   `xml:"feed"`
	Title   string     `xml:"title"`
	Links   []atomLink `xml:"link"`
	Entries []atomEntry `xml:"entry"`
}

type atomLink struct {
	Rel  string `xml:"rel,attr"`
	Href string `xml:"href,attr"`
}

type atomEntry struct {
	Title   string     `xml:"title"`
	Links   []atomLink `xml:"link"`
	Updated string     `xml:"updated"`
	ID      string     `xml:"id"`
}

func convertAtom(a atomFeed) *Feed {
	f := &Feed{Title: a.Title}
	for _, l := range a.Links {
		if l.Rel == "" || l.Rel == "alternate" {
			f.Link = l.Href
			break
		}
	}
	for _, entry := range a.Entries {
		item := Item{
			Title:     entry.Title,
			Published: parseTime(entry.Updated),
			GUID:      entry.ID,
		}
		for _, l := range entry.Links {
			if l.Rel == "" || l.Rel == "alternate" {
				item.Link = strings.TrimSpace(l.Href)
				break
			}
		}
		f.Items = append(f.Items, item)
	}
	return f
}

// RDF/RSS 1.0 structures
type rdfFeed struct {
	XMLName xml.Name   `xml:"RDF"`
	Channel rdfChannel `xml:"channel"`
	Items   []rdfItem  `xml:"item"`
}

type rdfChannel struct {
	Title string `xml:"title"`
	Link  string `xml:"link"`
}

type rdfItem struct {
	Title string `xml:"title"`
	Link  string `xml:"link"`
	Date  string `xml:"date"`
}

func convertRDF(r rdfFeed) *Feed {
	f := &Feed{
		Title: r.Channel.Title,
		Link:  r.Channel.Link,
	}
	for _, item := range r.Items {
		f.Items = append(f.Items, Item{
			Title:     item.Title,
			Link:      strings.TrimSpace(item.Link),
			Published: parseTime(item.Date),
		})
	}
	return f
}

var timeFormats = []string{
	time.RFC1123Z,
	time.RFC1123,
	time.RFC3339,
	"2006-01-02T15:04:05Z",
	"2006-01-02",
	"Mon, 02 Jan 2006 15:04:05 MST",
	"Mon, 02 Jan 2006 15:04:05 -0700",
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
