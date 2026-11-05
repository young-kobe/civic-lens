# Districts

## Purpose

The districts feature captures one public file of the U.S. Census Bureau and keeps it as tables. The file maps each 2020 ZIP Code Tabulation Area (ZCTA) to the districts of the 119th Congress. A ZCTA is a Census area that the Bureau builds from ZIP codes. A ZCTA is not a USPS ZIP code. Each row cites the capture it came from.

The view `public_api.zip_districts` serves these tables to the public read path. The route `/v1/zip-districts/{zcta}` joins it with the release names; see [search](search.md#api). The home page ZIP form calls that route.

## Decisions

- The source is the U.S. Census Bureau relationship file `tab20_cd11920_zcta520_natl.txt`. It is public domain under 17 U.S.C. 105. It needs no credential. It has the 119th Congress districts only.
- The file is pipe delimited UTF-8 with a byte order mark. It is about 6.2 MB with about 40,400 data rows. One ZCTA can lie in up to 4 districts.
- The source is an `api` source with `apiParser` `censusZctaDistricts`. The shipped source `census-zcta-districts` is disabled, like the other `api` sources. It adds no collector mode and no wire contract change. The collector keeps the bytes as an ordinary capture.
- The source checks once a week. `checkIntervalSeconds` is 604800, which is the longest interval that `CollectionConfiguration` allows. The file changes rarely, so the check is cheap.
- A snapshot is one header row in `roster_snapshots`, with the data rows below it. The header holds the attempt ID of the capture. Every row holds that attempt ID. A row is never updated. `RosterSnapshotRules` stays the one owner of the header and of the hash rule; see [roster](roster.md#decisions).
- A capture whose hash matches any snapshot of the source adds only its attempt row. Preparation checks the hash before it parses.
- The newest snapshot follows the rule of `RosterSnapshotRules.ReadLatestSnapshotAttemptIdAsync`. The newest captured attempt of the source names the body. The oldest snapshot of that body is the newest snapshot. A file that changes and changes back reads as the earlier snapshot. The view `public_api.zip_districts` repeats this rule in SQL for each source that owns ZCTA rows, so it needs no source ID.
- The view shows `congress`, which is 119. The file holds the 119th Congress only, so the migration `ZipDistrictCongress` writes the number as a constant. A source for another Congress needs a new migration.
- The view shows `land_share`, which is the land of the overlap divided by the land of the ZCTA in the snapshot. The tables do not store the ZCTA total, so the view sums it. The ZCTA total leaves out the dropped rows. A page lists the districts of a ZCTA by `land_share`, largest first.
- A data row keeps the ZCTA with 5 digits, the state, the district, and `AREALAND_PART` as the land area of the overlap in square meters. The land area is a `bigint`.
- The state is the two letter code that the roster uses. `StateFips` is the one owner of the map from the state FIPS code to this code. It covers the 50 states, DC, Puerto Rico, and the four island areas. An unknown FIPS code fails preparation.
- The district is an integer, as in `roster_terms.district`. The Census codes `00` for an at-large district and `98` for a delegate district. The roster gives both the district 0. So the parser maps both codes to 0. The district column is never null, because it is part of the key and the file lists House districts only. The Senate has no district.
- The parser drops rows with a blank ZCTA, rows with `AREALAND_PART` 0, and rows with the district `ZZ`. The district `ZZ` means water with no district. A dropped row is still checked for its shape.
- A header that differs from the Census header fails preparation. A malformed row fails preparation. Preparation then blocks as invalid data and writes nothing.
- The capture read limit of `CaptureBodyReader` is 10,000,000 bytes and stays as it is. The shipped source sets `maxBytes` 8,000,000, `maxRequests` 2, and `timeoutSeconds` 60. The shipped job policy allows 2 attempts, so a job reserves 16,000,000 bytes.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it.

- A district source has no credential, no validator, no coverage list, and no Congress options, and it stays eligible with no covered people. Enforced by `CollectionConfiguration`. Tests: `ZctaDistrictSourceConfigurationTests.AWeeklyCheckValidatesAndASourceCoversNoPeopleSoItStaysEligible`, `ZctaDistrictSourceConfigurationTests.ACredentialOrAValidatorIsRejectedBecauseTheFileIsPublicAndDownloadedInFull`.
- A configuration has at most one district source. Enforced by `CollectionConfiguration`. Test: `ZctaDistrictSourceConfigurationTests.ASecondDistrictSourceIsRejectedBecauseEachKindHasOneSource`.
- The source checks once a week and ships disabled. Enforced by the shipped configuration `config/federal-coverage.json`. Tests: `ZctaDistrictSourceConfigurationTests.TheShippedSourceIsOffByDefaultAndChecksOnceAWeek`, `PostgresZctaDistrictStoreTests.TheWeeklyCheckCreatesADistrictJobAndSchedulesTheNextCheckOneWeekLater`.
- A district job fetches the source URL and no other URL. Enforced by `CollectionJobDefinition`. Test: `ZctaDistrictSourceConfigurationTests.ADistrictJobIsBoundToTheSourceUrlAndTheDistrictResource`.
- A district job is parsed by `ZctaDistrictParser` and admitted by its own store, never by a roster store. Enforced by `JobPreparation`. Test: `JobPreparationTests.ADistrictJobIsParsedAndPassedToItsOwnStoreAndNotToTheRosterStores`.
- A capture whose body matches any snapshot of the source is not parsed and writes no rows. Enforced by `RosterSnapshotRules.HasSnapshotOfCaptureAsync` and `JobPreparation`. Tests: `PostgresZctaDistrictStoreTests.ACaptureWithTheBodyOfAnEarlierSnapshotIsKnownSoPreparationAddsOnlyItsAttemptRow`, `JobPreparationTests.AKnownDistrictCaptureIsNotParsedAndWritesNothing`.
- The parser keeps one row for each ZCTA and district, and drops a blank ZCTA, zero land, and the district `ZZ`. Enforced by `ZctaDistrictParser`. Test: `ZctaDistrictParserTests.ParseKeepsOneRowForEachZctaAndDistrictAndDropsBlankZeroLandAndNoDistrictRows`.
- The state and the district have the form of the roster term rows, so a join on both finds the member. Enforced by `ZctaDistrictParser` and `StateFips`. Tests: `ZctaDistrictParserTests.StateAndDistrictMatchTheTermFormOfTheRosterSoALaterJoinFindsTheMember`, `ZctaDistrictParserTests.AnAtLargeDistrictAndADelegateDistrictBothReadAsDistrictZeroLikeTheRoster`.
- An unknown state FIPS code fails preparation. Enforced by `StateFips`. Test: `ZctaDistrictParserTests.AnUnknownStateFipsCodeIsRejectedEvenWhenTheRowIsOtherwiseValid`.
- A header that differs from the Census header fails preparation. Enforced by `ZctaDistrictParser`. Test: `ZctaDistrictParserTests.AHeaderThatDiffersFromTheCensusHeaderIsRejected`.
- A malformed row, a repeated ZCTA and district, or a file with no kept row fails preparation. Enforced by `ZctaDistrictParser`. Tests: `ZctaDistrictParserTests.AMalformedRowIsRejected`, `ZctaDistrictParserTests.AZctaAndDistrictListedTwiceAreRejected`, `ZctaDistrictParserTests.AFileWithOnlyDroppedRowsIsRejectedSoAnEmptySnapshotIsNeverStored`.
- A ZCTA and a district appear at most once in a snapshot. Enforced by the key `PK_zcta_districts`. Test: `PostgresZctaDistrictStoreTests.ARowWithAZctaAndDistrictAlreadyInTheSnapshotIsRefusedByTheKey`.
- A row has a header row. Enforced by the foreign key `FK_zcta_districts_roster_snapshots_attempt_id`. Test: `PostgresZctaDistrictStoreTests.ARowWithoutAHeaderIsRefusedSoNoSnapshotExistsWithoutIt`.
- A ZCTA has 5 digits and the land area is positive. Enforced by `ck_zcta_districts_zcta` and `ck_zcta_districts_land`. Tests: `PostgresZctaDistrictStoreTests.AFailedInsertLeavesNoPartialSnapshot`, `PostgresZctaDistrictStoreTests.AZeroLandRowIsRefusedByTheDatabase`.
- The view `zip_districts` shows the rows of the newest snapshot only, by the rule of `RosterSnapshotRules`. Enforced by the SQL of the migration `PublicApiZipDistricts`. Tests: `PublicApiViewsTests.TheZipViewFollowsTheRosterRuleForTheNewestSnapshotWhenTheFileChangesAndChangesBack`, `PublicApiViewsTests.TheZipViewReadsTheOldestSnapshotWhenTwoSnapshotsHoldOneBodyLikeTheRosterRule`, `PublicApiViewsTests.TheZipViewIgnoresSnapshotsOfOtherSourcesAndShowsOneRowForEachZctaStateAndDistrict`.
- The view `zip_districts` names the Congress of its rows. Enforced by the SQL of the migration `ZipDistrictCongress`. Test: `PublicApiViewsTests.TheZipViewNamesTheCongressOfTheDistrictFileSoARouteCanFilterByIt`.
- The view `zip_districts` shows no attempt ID and no hash. Enforced by the column list of the view. Test: `PublicApiViewsTests.TheZipViewShowsNoRowsBeforeTheFirstSnapshotAndHidesCaptureIdentifiers`.
- The land shares of a ZCTA add up to 1. Enforced by the window sum `PARTITION BY attempt_id, zcta` in the SQL of the migration `PublicApiZipDistricts`. Test: `PublicApiViewsTests.TheZipViewGivesTheLandOfTheOverlapAndTheShareOfTheZctaLandSoALargestFirstPageNeedsNoMoreMath`.
- A snapshot is complete or absent. Enforced by one `SaveChanges` in `PostgresZctaDistrictStore.AdmitAsync`. Test: `PostgresZctaDistrictStoreTests.AFailedInsertLeavesNoPartialSnapshot`.
- Old snapshots stay when a changed capture arrives. Enforced by the insert-only store. Test: `PostgresZctaDistrictStoreTests.AChangedCaptureAppendsASnapshotAndTheOldRowsStay`.

## Behavior

### Source configuration

The source sets `mode` `api`, `apiParser` `censusZctaDistricts`, and `checkIntervalSeconds` 604800. It sets no `credential`, no `coverage`, no `minimumCongress`, and no `backfillFrom`. It sets no ETag and no Last-Modified. A 304 would leave the attempt without a capture body. The origin is `www2.census.gov`.

### Capture and admission

The scheduler makes one root job for the source URL with `apiResource` `zctaDistricts`. Preparation first asks `HasSnapshotOfCaptureAsync`. It is true when any snapshot of the same source has the same capture hash. Then preparation stops, and the capture adds only its attempt row. Otherwise `ZctaDistrictParser` parses the body and `AdmitAsync` inserts the header row and the data rows in one save.

Each weekly check downloads the whole file. An unchanged file adds an attempt row and nothing else.

## Storage and contracts

- The generated [districts schema](schema/districts.md) lists the table, its columns, its key, and its checks.
- `zcta_districts` has one row for each ZCTA, state, district, and snapshot. The key is the attempt ID, the ZCTA, the state, and the district. It holds the land area of the overlap in square meters.
- `roster_snapshots` is the header table. See [roster](roster.md#storage-and-contracts).
- The view `public_api.zip_districts` has one row for each ZCTA, state, and district of the newest snapshot. The columns are `zcta`, `state`, `district`, `land_area_square_meters`, `land_share`, and `congress`. `PublicApiGrants` grants the view owner SELECT on the columns that the view reads in `zcta_districts`, `roster_snapshots`, and `collection_attempts`. See [publication](publication.md#public-api-views).
- `IZctaDistrictStore` is the port. `PostgresZctaDistrictStore` implements it. It has `HasSnapshotOfCaptureAsync` and `AdmitAsync` only.

## Code map

| Concern | Files |
|---|---|
| Parsing | `src/CivicLens.Application/Districts/ZctaDistrictParser.cs`, `src/CivicLens.Application/Districts/StateFips.cs`, `src/CivicLens.Application/Districts/ZctaDistrict.cs` |
| Store port | `src/CivicLens.Application/Districts/IZctaDistrictStore.cs` |
| Preparation | `src/CivicLens.Application/Collection/Processing/JobPreparation.cs` |
| Eligibility and validation | `src/CivicLens.Application/Collection/CollectionConfiguration.cs` |
| Store and rows | `src/CivicLens.Infrastructure/Districts/PostgresZctaDistrictStore.cs`, `src/CivicLens.Infrastructure/Districts/Persistence/ZctaDistrictRow.cs`, `src/CivicLens.Infrastructure/Districts/Persistence/ZctaDistrictRowConfiguration.cs`, `src/CivicLens.Infrastructure/Roster/RosterSnapshotRules.cs` |
| Public view | `src/CivicLens.Infrastructure/Collection/Migrations/20261104013000_PublicApiZipDistricts.cs`, `src/CivicLens.Infrastructure/Collection/Migrations/20261105153622_ZipDistrictCongress.cs`, `src/CivicLens.Infrastructure/Publication/PublicApiGrants.cs`, `docs/architecture/public-api-columns.txt`, `tests/CivicLens.Tests/Infrastructure/PublicApi/PublicApiViewsTests.cs` |
| Sources | `config/federal-coverage.json` |

## Limits and planned work

- The file has the districts of the 119th Congress. The 2025 and 2026 mid-decade maps can differ for the 120th Congress. The 120th Congress needs a new file URL and a new source then.
- The House member of DC and of Guam, the U.S. Virgin Islands, American Samoa, and the Northern Mariana Islands is a Delegate. The House member of Puerto Rico is the Resident Commissioner. `NonStateSeats` in Publication.Contracts owns the six codes, their names, and this title. `UsStates`, `ZipDistrictStore`, and the home page ZIP form read it. `StateFips` keeps only the FIPS numbers. Tests: `NonStateSeatsTests.OnlyPuertoRicoHasAResidentCommissionerAndTheOtherFiveSendADelegate`, `NonStateSeatsTests.NoOtherSourceFileKeepsItsOwnListOfNonStateNamesOrOffices`, `PublishedHomePageTests.TheZipFormCallsTheServiceAtTheConfiguredAddressAndStaysHiddenUntilScriptRuns`.
- A ZCTA is not a USPS ZIP code. A ZIP code can lie in no ZCTA, and a ZCTA can cover parts of several ZIP codes. A reader must say this to a citizen.
- Rows with a blank ZCTA, zero land, or the district `ZZ` are not stored. Their land area is not counted.
- The Census names the file for the 2020 ZCTAs. A change of the Census column list fails preparation until a person updates the header in `ZctaDistrictParser`.
- The view shows the newest snapshot of every source that owns ZCTA rows. A second district source, such as one for the 120th Congress, would show two snapshots for one ZCTA. The owner has not decided the fix.
