package util

import "testing"

func TestCanonicalizeURL(t *testing.T) {
	tests := []struct {
		input    string
		expected string
	}{
		{"https://Example.COM/Path/", "https://example.com/Path"},
		{"https://example.com:443/path", "https://example.com/path"},
		{"http://example.com:80/path", "http://example.com/path"},
		{"https://example.com/path#fragment", "https://example.com/path"},
		{"https://example.com/path?b=2&a=1", "https://example.com/path?a=1&b=2"},
		{"https://example.com/", "https://example.com/"},
	}

	for _, tc := range tests {
		got, err := CanonicalizeURL(tc.input)
		if err != nil {
			t.Errorf("CanonicalizeURL(%q) error: %v", tc.input, err)
			continue
		}
		if got != tc.expected {
			t.Errorf("CanonicalizeURL(%q) = %q, want %q", tc.input, got, tc.expected)
		}
	}
}

func TestExtractDomain(t *testing.T) {
	tests := []struct {
		input    string
		expected string
	}{
		{"https://www.example.com/path", "www.example.com"},
		{"http://Example.COM:8080/path", "example.com:8080"},
		{"https://sub.domain.co.uk/foo", "sub.domain.co.uk"},
	}

	for _, tc := range tests {
		got := ExtractDomain(tc.input)
		if got != tc.expected {
			t.Errorf("ExtractDomain(%q) = %q, want %q", tc.input, got, tc.expected)
		}
	}
}
