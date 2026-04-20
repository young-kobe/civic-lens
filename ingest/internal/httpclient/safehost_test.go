package httpclient

import "testing"

func TestValidateURL(t *testing.T) {
	cases := []struct {
		name    string
		in      string
		wantErr bool
	}{
		// Happy path.
		{"public https", "https://www.nytimes.com/", false},
		{"public http", "http://example.com/path", false},
		{"subdomain", "https://api.x.com/2/tweets/search/recent", false},

		// Scheme blocklist.
		{"file", "file:///etc/passwd", true},
		{"gopher", "gopher://host/1/", true},
		{"data url", "data:text/html,hello", true},
		{"javascript", "javascript:alert(1)", true},

		// Literal private / metadata addresses. DNS names that only RESOLVE
		// to private IPs (e.g. metadata.google.internal) are not caught —
		// validateURL does not resolve, to avoid a per-fetch DNS round-trip.
		// Documented gap; compensating control is the origin firewall.
		{"aws metadata", "http://169.254.169.254/latest/meta-data", true},
		{"loopback v4", "http://127.0.0.1:8000/api/run/full-pipeline", true},
		{"loopback alt", "http://127.127.127.127/", true},
		{"rfc1918 10", "http://10.0.0.5/", true},
		{"rfc1918 172", "http://172.16.0.1/", true},
		{"rfc1918 192", "http://192.168.1.1/", true},
		{"loopback v6", "http://[::1]/", true},
		{"link-local v6", "http://[fe80::1]/", true},
		{"unspecified v4", "http://0.0.0.0/", true},

		// Hostname blocklist (belt-and-suspenders; does NOT resolve).
		{"localhost", "http://localhost:8000/", true},
		{"localhost suffix", "http://foo.localhost/", true},
		{"mdns local", "http://printer.local/", true},

		// Malformed.
		{"empty host", "http:///", true},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			err := validateURL(tc.in)
			if (err != nil) != tc.wantErr {
				t.Errorf("validateURL(%q) err=%v, wantErr=%v", tc.in, err, tc.wantErr)
			}
		})
	}
}
