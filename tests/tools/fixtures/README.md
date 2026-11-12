# Fixtures for the district geometry tool

`cb_2024_us_cd119_20m.zip` is a hand-made sample. It is not Census data. The coordinates are invented rectangles.
It has the same zip member name and the same field names as the Census file that
`tools/render-district-geometry.py` reads, so the tool runs on it.

- Sample district 1 and 2 of Colorado (`STATEFP` 08, `CD119FP` 01 and 02). They share the border at longitude -105.
  Each side lists the 4 points of that border, in opposite directions, as the Census file does.
- The at-large district of Alaska (`STATEFP` 02, `CD119FP` 00). Its ring has 16 points, so the test can check the simplification.

`cb_2024_us_cd119_20m.kml` is the unzipped member, kept so that a reader can see the input.
The zip holds the same bytes with a fixed timestamp.
Its sha256 is `80d277eec50cdc966550e9e333c8288f1c2cf411eacf4e6225f42110eeb7edc2`.
The test patches the pinned size and hash of the tool to the values of this zip. The real pinned values reject it.

Run the tests with `python3 -m unittest discover -s tests/tools`.
