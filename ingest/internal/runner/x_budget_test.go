package runner

import "testing"

// TestCeilDiv pins the rounding rule Record relies on: costs are always
// rounded UP to the next whole cent, never truncated, so OverBudget cannot
// release a call past the ceiling due to undercounted spend.
func TestCeilDiv(t *testing.T) {
	cases := []struct {
		a, b, want int
	}{
		{0, 10, 0},
		{-5, 10, 0},
		{1, 10, 1},    // 0.1c rounds up to 1c
		{20, 10, 2},   // exact division stays exact
		{25, 10, 3},   // 2.5c rounds up to 3c, never truncates to 2
		{305, 10, 31}, // 30.5c rounds up to 31c
	}
	for _, c := range cases {
		if got := ceilDiv(c.a, c.b); got != c.want {
			t.Errorf("ceilDiv(%d, %d) = %d, want %d", c.a, c.b, got, c.want)
		}
	}
}

// TestXBudgetTracker_OverBudgetHonoursCeiling exercises the pure comparison
// OverBudget makes between estimated spend and the configured ceiling,
// without needing a live database.
func TestXBudgetTracker_OverBudgetHonoursCeiling(t *testing.T) {
	tr := &XBudgetTracker{ceiling: 40, estimated: 0}
	if tr.OverBudget() {
		t.Fatal("fresh tracker at 0 cents reported OverBudget")
	}
	tr.estimated = 70
	if !tr.OverBudget() {
		t.Fatalf("want OverBudget=true at %d cents / %d cap", tr.estimated, tr.ceiling)
	}
}

// TestXBudgetTracker_CeilingZeroDisables asserts a ceiling of 0 means
// "disabled" regardless of accumulated spend.
func TestXBudgetTracker_CeilingZeroDisables(t *testing.T) {
	tr := &XBudgetTracker{ceiling: 0, estimated: 999999}
	if tr.OverBudget() {
		t.Fatal("ceiling=0 should disable the guard")
	}
}
