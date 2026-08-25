\if :{?SEARCH_PERFORMANCE_DATABASE_CONFIRMED}
\else
\echo 'Set SEARCH_PERFORMANCE_DATABASE_CONFIRMED to YES_DEDICATED_DATABASE.'
\quit
\endif

SELECT CASE
  WHEN :'SEARCH_PERFORMANCE_DATABASE_CONFIRMED' = 'YES_DEDICATED_DATABASE' THEN 1
  ELSE 1 / 0
END;

SET synchronous_commit = off;

INSERT INTO users
    (id, username_normalized, display_name, password_hash, role, status,
     failed_login_count, lock_type, created_at, updated_at)
SELECT
    md5('performance-user-' || number)::uuid,
    'PERFORMANCE_USER_' || number,
    'Performance User ' || number,
    'performance-only-not-authenticatable',
    'Member',
    'Active',
    0,
    'None',
    TIMESTAMPTZ '2026-01-01 00:00:00Z',
    TIMESTAMPTZ '2026-01-01 00:00:00Z'
FROM generate_series(1, 10) AS number;

INSERT INTO file_entries
    (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
     size, status, file_version, created_at, updated_at)
SELECT
    md5('performance-root-' || number)::uuid,
    md5('performance-user-' || number)::uuid,
    NULL,
    'FOLDER',
    'Files',
    'performance/users/' || number || '/files',
    NULL,
    0,
    'ACTIVE',
    1,
    TIMESTAMPTZ '2026-01-01 00:00:00Z',
    TIMESTAMPTZ '2026-01-01 00:00:00Z'
FROM generate_series(1, 10) AS number;

INSERT INTO file_entries
    (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
     size, status, file_version, created_at, updated_at)
SELECT
    md5('performance-folder-' || owner_number || '-' || folder_number)::uuid,
    md5('performance-user-' || owner_number)::uuid,
    md5('performance-root-' || owner_number)::uuid,
    'FOLDER',
    'performance-folder-' || folder_number,
    'performance/users/' || owner_number || '/files/folder-' || folder_number,
    NULL,
    0,
    'ACTIVE',
    1,
    TIMESTAMPTZ '2026-01-01 00:00:00Z',
    TIMESTAMPTZ '2026-01-01 00:00:00Z'
FROM generate_series(1, 10) AS owner_number
CROSS JOIN generate_series(1, 100) AS folder_number;

INSERT INTO file_entries
    (id, owner_user_id, parent_id, entry_type, name, relative_path, mime_type,
     size, status, missing_detected_at, missing_last_checked_at,
     missing_observation_id, file_version, created_at, updated_at)
SELECT
    md5('performance-file-' || number)::uuid,
    md5('performance-user-' || owner_number)::uuid,
    md5('performance-folder-' || owner_number || '-' || folder_number)::uuid,
    'FILE',
    'performance-file-' || lpad(number::text, 6, '0') || extension,
    'performance/users/' || owner_number || '/files/folder-' || folder_number ||
      '/file-' || lpad(number::text, 6, '0') || extension,
    mime_type,
    1048576 + number,
    status,
    CASE WHEN status = 'ACTIVE' THEN NULL ELSE TIMESTAMPTZ '2026-06-01 00:00:00Z' END,
    CASE WHEN status = 'ACTIVE' THEN NULL ELSE TIMESTAMPTZ '2026-06-01 00:05:00Z' END,
    CASE WHEN status = 'ACTIVE' THEN NULL ELSE md5('performance-observation-' || number)::uuid END,
    1,
    TIMESTAMPTZ '2026-01-01 00:00:00Z',
    TIMESTAMPTZ '2026-06-01 00:05:00Z'
FROM (
    SELECT
        number,
        ((number - 1) % 10) + 1 AS owner_number,
        ((number - 1) % 100) + 1 AS folder_number,
        CASE number % 6
            WHEN 0 THEN '.jpg'
            WHEN 1 THEN '.mp4'
            WHEN 2 THEN '.flac'
            WHEN 3 THEN '.pdf'
            WHEN 4 THEN '.zip'
            ELSE '.bin'
        END AS extension,
        CASE number % 6
            WHEN 0 THEN 'image/jpeg'
            WHEN 1 THEN 'video/mp4'
            WHEN 2 THEN 'audio/flac'
            WHEN 3 THEN 'application/pdf'
            WHEN 4 THEN 'application/zip'
            ELSE 'application/octet-stream'
        END AS mime_type,
        CASE
            WHEN number % 2000 = 0 THEN 'MISSING'
            WHEN number % 1000 = 0 THEN 'MISSING_CANDIDATE'
            ELSE 'ACTIVE'
        END AS status
    FROM generate_series(1, 298990) AS number
) AS generated;

INSERT INTO shares (id, target_entry_id, owner_user_id, created_at, updated_at)
SELECT
    md5('performance-share-' || owner_number || '-' || folder_number)::uuid,
    md5('performance-folder-' || owner_number || '-' || folder_number)::uuid,
    md5('performance-user-' || owner_number)::uuid,
    TIMESTAMPTZ '2026-01-01 00:00:00Z',
    TIMESTAMPTZ '2026-01-01 00:00:00Z'
FROM generate_series(1, 9) AS owner_number
CROSS JOIN generate_series(1, 5) AS folder_number;

INSERT INTO share_members (share_id, user_id, permission, created_at, updated_at)
SELECT
    md5('performance-share-' || owner_number || '-' || folder_number)::uuid,
    md5('performance-user-10')::uuid,
    (ARRAY['VIEWER', 'CONTRIBUTOR', 'EDITOR', 'MANAGER'])[folder_number],
    TIMESTAMPTZ '2026-01-01 00:00:00Z',
    TIMESTAMPTZ '2026-01-01 00:00:00Z'
FROM generate_series(1, 9) AS owner_number
CROSS JOIN generate_series(1, 4) AS folder_number;

INSERT INTO shares (id, target_entry_id, owner_user_id, created_at, updated_at)
SELECT
    md5('performance-direct-share-' || owner_number)::uuid,
    md5('performance-file-' || owner_number)::uuid,
    md5('performance-user-' || owner_number)::uuid,
    TIMESTAMPTZ '2026-01-01 00:00:00Z',
    TIMESTAMPTZ '2026-01-01 00:00:00Z'
FROM generate_series(1, 9) AS owner_number;

INSERT INTO share_members (share_id, user_id, permission, created_at, updated_at)
SELECT
    md5('performance-direct-share-' || owner_number)::uuid,
    md5('performance-user-10')::uuid,
    'VIEWER',
    TIMESTAMPTZ '2026-01-01 00:00:00Z',
    TIMESTAMPTZ '2026-01-01 00:00:00Z'
FROM generate_series(1, 9) AS owner_number;

ANALYZE users;
ANALYZE file_entries;
ANALYZE shares;
ANALYZE share_members;

SELECT count(*) AS synthetic_file_entries FROM file_entries;
