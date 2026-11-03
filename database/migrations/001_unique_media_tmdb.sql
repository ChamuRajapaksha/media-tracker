-- =========================================================
-- Media Tracker - Migration 001: unique TMDB imports
-- Run this entire script as media_app with Run Script.
--
-- STEP 2 DELETES ROWS. Review the STEP 1 output first, and
-- take a backup if the database holds data you care about.
-- Children (seasons, episodes, progress, genres, status,
-- rating) are removed by ON DELETE CASCADE.
-- =========================================================

-- STEP 1: list the duplicates that STEP 2 will collapse.
SELECT media_type,
       tmdb_id,
       COUNT(*) AS copies,
       MIN(media_id) AS kept_media_id,
       LISTAGG(TO_CHAR(media_id), ',') WITHIN GROUP (ORDER BY media_id) AS all_media_ids
FROM media
WHERE tmdb_id IS NOT NULL
GROUP BY media_type, tmdb_id
HAVING COUNT(*) > 1
ORDER BY media_type, tmdb_id;

-- STEP 2: keep the lowest media_id per (media_type, tmdb_id).
DELETE FROM media m
WHERE m.tmdb_id IS NOT NULL
  AND m.media_id > (
      SELECT MIN(dup.media_id)
      FROM media dup
      WHERE dup.media_type = m.media_type
        AND dup.tmdb_id = m.tmdb_id
  );

-- STEP 3: a function-based unique index rather than a plain one.
-- Oracle does not store index entries whose keys are all NULL, so rows with a
-- NULL tmdb_id (hand-created media) stay unconstrained and can repeat freely.
CREATE UNIQUE INDEX uq_media_tmdb ON media (
    CASE WHEN tmdb_id IS NOT NULL THEN media_type END,
    CASE WHEN tmdb_id IS NOT NULL THEN tmdb_id END
);

COMMIT;
