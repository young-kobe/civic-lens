package main

import (
	"database/sql"
	"fmt"
	"os"

	_ "modernc.org/sqlite"
)

func main() {
	dbPath := "../data/civic_lens.db"
	if len(os.Args) > 1 {
		dbPath = os.Args[1]
	}

	db, err := sql.Open("sqlite", dbPath)
	if err != nil {
		fmt.Printf("Error: %v\n", err)
		os.Exit(1)
	}
	defer db.Close()

	// ========== FRONTIER STATUS ==========
	fmt.Println("╔════════════════════════════════════════════════════════════════════╗")
	fmt.Println("║                       CIVIC LENS DATABASE STATUS                    ║")
	fmt.Println("╚════════════════════════════════════════════════════════════════════╝")

	var queued, inflight, done, failed int
	db.QueryRow(`
		SELECT 
			COALESCE(SUM(CASE WHEN state = 0 THEN 1 ELSE 0 END), 0),
			COALESCE(SUM(CASE WHEN state = 1 THEN 1 ELSE 0 END), 0),
			COALESCE(SUM(CASE WHEN state = 2 THEN 1 ELSE 0 END), 0),
			COALESCE(SUM(CASE WHEN state = 3 THEN 1 ELSE 0 END), 0)
		FROM pages
	`).Scan(&queued, &inflight, &done, &failed)

	fmt.Println("\n[CRAWLER FRONTIER]")
	fmt.Printf("   Queued:   %d URLs waiting to fetch\n", queued)
	fmt.Printf("   Inflight: %d currently being processed\n", inflight)
	fmt.Printf("   Done:     %d successfully crawled\n", done)
	fmt.Printf("   Failed:   %d errors\n", failed)

	// ========== REDDIT POSTS ==========
	var postCount int
	db.QueryRow("SELECT COUNT(*) FROM reddit_posts_raw").Scan(&postCount)

	fmt.Printf("\n[REDDIT POSTS]: %d total\n", postCount)

	// Group by subreddit
	subRows, _ := db.Query(`
		SELECT subreddit, COUNT(*) as cnt 
		FROM reddit_posts_raw 
		GROUP BY subreddit 
		ORDER BY cnt DESC
	`)
	if subRows != nil {
		fmt.Println("   By subreddit:")
		for subRows.Next() {
			var sub string
			var cnt int
			subRows.Scan(&sub, &cnt)
			fmt.Printf("     r/%-20s %d posts\n", sub, cnt)
		}
		subRows.Close()
	}

	// Top posts by score
	fmt.Println("\n   Top 10 by score:")
	rows, _ := db.Query(`
		SELECT subreddit, title, score, num_comments 
		FROM reddit_posts_raw 
		ORDER BY score DESC 
		LIMIT 10
	`)
	if rows != nil {
		for rows.Next() {
			var sub, title string
			var score, comments int
			rows.Scan(&sub, &title, &score, &comments)
			if len(title) > 55 {
				title = title[:52] + "..."
			}
			fmt.Printf("     [r/%-12s] ^ %5d  C %4d | %s\n", sub, score, comments, title)
		}
		rows.Close()
	}

	// ========== QUEUED URLS ==========
	if queued > 0 {
		fmt.Println("\n[SAMPLE QUEUED URLs]:")
		urlRows, _ := db.Query(`SELECT domain, url_raw FROM pages WHERE state = 0 LIMIT 10`)
		if urlRows != nil {
			for urlRows.Next() {
				var domain, url string
				urlRows.Scan(&domain, &url)
				if len(url) > 70 {
					url = url[:67] + "..."
				}
				fmt.Printf("     [%-20s] %s\n", domain, url)
			}
			urlRows.Close()
		}
	}

	// ========== RAW FILES ==========
	fmt.Println("\n[RAW STORAGE]:")
	if entries, err := os.ReadDir("../data/raw/sha256"); err == nil {
		fmt.Printf("   %d raw content files stored\n", len(entries))
	} else {
		fmt.Println("   No raw files yet")
	}

	fmt.Println()
}
