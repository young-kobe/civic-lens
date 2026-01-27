import sqlite3
import os

DB_PATH = r"C:\Users\kobey\civic-lens\data\civic_lens.db"

def inspect():
    if not os.path.exists(DB_PATH):
        print(f"Error: DB not found at {DB_PATH}")
        return

    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()

    print("--- schema_version ---")
    try:
        cursor.execute("SELECT * FROM schema_version order by version desc")
        for row in cursor.fetchall():
            print(row)
    except Exception as e:
        print(f"Error reading schema_version: {e}")

    print("\n--- docs table schema ---")
    try:
        cursor.execute("SELECT sql FROM sqlite_master WHERE type='table' AND name='docs'")
        row = cursor.fetchone()
        if row:
            print(row[0])
        else:
            print("Table 'docs' not found")
    except Exception as e:
        print(f"Error reading docs schema: {e}")

    conn.close()

if __name__ == "__main__":
    inspect()
