-- =========================================================
-- Media Tracker - purge smoke-test data
--
-- Run with "Run Script" (not "Run Statement") as media_app.
-- Stop the API first, or it will hold connections open.
--
-- Deletes ONLY the rows created by the endpoint smoke tests.
-- media_id 41 and 61 ("Breaking Bad") predate the tests and are left alone.
-- =========================================================

-- ---------------------------------------------------------------
-- STEP 1 - preview. Read this output before running STEP 2.
-- ---------------------------------------------------------------
SELECT media_id   AS MediaId,
       title      AS Title,
       media_type AS MediaType,
       tmdb_id    AS TmdbId
FROM media
ORDER BY media_id;

-- ---------------------------------------------------------------
-- STEP 2 - delete the test rows.
--
-- ON DELETE CASCADE takes seasons, episodes, episode_progress,
-- media_genres, watch_status and ratings with each media row,
-- so nothing else needs deleting.
-- ---------------------------------------------------------------
DELETE FROM media
WHERE title IN ('Progress Test Show', 'Cascade Ep', 'Cascade Season')
   OR tmdb_id = 95396;   -- the Severance row, matched on tmdb_id so an edited title still catches it

-- ---------------------------------------------------------------
-- STEP 3 - commit. SQL Developer will not do this for you.
-- ---------------------------------------------------------------
COMMIT;

-- ---------------------------------------------------------------
-- STEP 4 - verify. Expect 2 rows, both "Breaking Bad".
-- ---------------------------------------------------------------
SELECT media_id AS MediaId, title AS Title, media_type AS MediaType
FROM media
ORDER BY media_id;

-- ---------------------------------------------------------------
-- STEP 5 - orphan check. Expect 0 rows in every result.
--    Should be impossible given the cascades; this proves it.
-- ---------------------------------------------------------------
SELECT 'seasons' AS TableName, COUNT(*) AS Orphans
  FROM seasons s WHERE NOT EXISTS (SELECT 1 FROM media m WHERE m.media_id = s.media_id)
UNION ALL
SELECT 'episodes', COUNT(*)
  FROM episodes e WHERE NOT EXISTS (
      SELECT 1 FROM seasons s JOIN media m ON m.media_id = s.media_id
       WHERE s.season_id = e.season_id)
UNION ALL
SELECT 'episode_progress', COUNT(*)
  FROM episode_progress ep WHERE NOT EXISTS (
      SELECT 1 FROM episodes e WHERE e.episode_id = ep.episode_id)
UNION ALL
SELECT 'media_genres', COUNT(*)
  FROM media_genres mg WHERE NOT EXISTS (SELECT 1 FROM media m WHERE m.media_id = mg.media_id)
UNION ALL
SELECT 'watch_status', COUNT(*)
  FROM watch_status ws WHERE NOT EXISTS (SELECT 1 FROM media m WHERE m.media_id = ws.media_id)
UNION ALL
SELECT 'ratings', COUNT(*)
  FROM ratings r WHERE NOT EXISTS (SELECT 1 FROM media m WHERE m.media_id = r.media_id);

-- ---------------------------------------------------------------
-- OPTIONAL - test genres left behind, if STEP 6 shows any.
--    Only run this if the names look like test data.
-- ---------------------------------------------------------------
SELECT genre_id AS GenreId, name AS Name FROM genres ORDER BY genre_id;

-- DELETE FROM genres WHERE name LIKE 'TempGenre%';
