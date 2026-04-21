package httpclient

import (
	"fmt"
	"net"
	"net/url"
	"strings"
)

// validateURL rejects URLs that point at link-local, loopback, multicast, or
// private-range hosts, or use schemes other than http/https. This blocks
// SSRF via hostile RSS feeds that redirect to cloud-metadata endpoints
// (169.254.169.254), the loopback admin API (127.0.0.1:8000), or file://
// / gopher:// / etc. (audit §1.10).
//
// Called at two points: (a) the first Fetch() of a URL admitted from the
// frontier — guards against a hostile seeds.yaml or DB tamper — and (b) every
// CheckRedirect hop, which is the real-world attack path since an initial
// request to a trusted news domain can redirect anywhere.
func validateURL(urlStr string) error {
	u, err := url.Parse(urlStr)
	if err != nil {
		return fmt.Errorf("parse url: %w", err)
	}
	if u.Scheme != "http" && u.Scheme != "https" {
		return fmt.Errorf("disallowed scheme %q", u.Scheme)
	}
	host := u.Hostname()
	if host == "" {
		return fmt.Errorf("empty host")
	}
	// A literal IP is the interesting case. A DNS name that resolves to a
	// private IP is a real attack too but requires resolving at validation
	// time — doing that here would add a network round-trip per fetch. The
	// proxy tier (Caddy + CF) already sits in front of the crawler, and
	// the blast radius of the crawler hitting a private name that *also*
	// serves content is tiny compared to the literal-IP case, so we accept
	// that gap for v1 and document it.
	if ip := net.ParseIP(host); ip != nil {
		if ip.IsLoopback() || ip.IsPrivate() || ip.IsLinkLocalUnicast() ||
			ip.IsLinkLocalMulticast() || ip.IsMulticast() || ip.IsUnspecified() {
			return fmt.Errorf("disallowed host IP %q", host)
		}
	}
	// Cheap belt-and-suspenders check for common hostile hostnames.
	lower := strings.ToLower(host)
	if lower == "localhost" || strings.HasSuffix(lower, ".localhost") ||
		strings.HasSuffix(lower, ".local") {
		return fmt.Errorf("disallowed hostname %q", host)
	}
	return nil
}
