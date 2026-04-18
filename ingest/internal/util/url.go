package util

import (
	"net/url"
	"strings"
)

// CanonicalizeURL normalizes a URL for deduplication.
// - Lowercases scheme and host
// - Removes default ports
// - Sorts query parameters
// - Removes fragments
// - Removes trailing slashes on paths (except root)
func CanonicalizeURL(rawURL string) (string, error) {
	u, err := url.Parse(rawURL)
	if err != nil {
		return "", err
	}

	// Lowercase scheme and host
	u.Scheme = strings.ToLower(u.Scheme)
	u.Host = strings.ToLower(u.Host)

	// Remove default ports
	if (u.Scheme == "http" && strings.HasSuffix(u.Host, ":80")) ||
		(u.Scheme == "https" && strings.HasSuffix(u.Host, ":443")) {
		u.Host = strings.TrimSuffix(u.Host, ":80")
		u.Host = strings.TrimSuffix(u.Host, ":443")
	}

	// Remove fragment
	u.Fragment = ""

	// Remove trailing slash (except for root path)
	if u.Path != "/" && strings.HasSuffix(u.Path, "/") {
		u.Path = strings.TrimSuffix(u.Path, "/")
	}

	// Sort query parameters
	if u.RawQuery != "" {
		q := u.Query()
		u.RawQuery = q.Encode()
	}

	return u.String(), nil
}

// ExtractDomain returns the host portion of a URL.
func ExtractDomain(rawURL string) string {
	u, err := url.Parse(rawURL)
	if err != nil {
		return ""
	}
	return strings.ToLower(u.Host)
}
