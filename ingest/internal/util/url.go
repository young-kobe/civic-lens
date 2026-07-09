package util

import (
	"net/url"
	"strings"

	"golang.org/x/net/publicsuffix"
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

// RegistrableDomain reduces a host to its effective-TLD-plus-one using the
// public suffix list (e.g. "www.example.co.uk:443" -> "example.co.uk"). Any
// port is stripped first. On failure (bare hostname, IP literal, unknown
// suffix) it returns the cleaned host unchanged so callers still get a
// deterministic comparison key. It is used to decide whether a page-declared
// canonical URL belongs to the same publisher as the URL we actually fetched.
func RegistrableDomain(host string) string {
	host = strings.ToLower(strings.TrimSpace(host))
	if host == "" {
		return ""
	}
	// Strip a trailing :port. IPv6 literals (which contain multiple colons)
	// are left alone — they never carry a public-suffix registrable domain.
	if strings.Count(host, ":") == 1 {
		host = host[:strings.LastIndex(host, ":")]
	}
	if etld1, err := publicsuffix.EffectiveTLDPlusOne(host); err == nil {
		return etld1
	}
	return host
}

// SameRegistrableDomain reports whether two hosts share a registrable domain.
func SameRegistrableDomain(hostA, hostB string) bool {
	a := RegistrableDomain(hostA)
	return a != "" && a == RegistrableDomain(hostB)
}
