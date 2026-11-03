#!/bin/sh
set -eu

# Oracle runs /container-entrypoint-initdb.d/*.sql as SYS, so connect as the
# application user explicitly to create the tables in the media_app schema.
echo "Creating the media_app schema from database/schema.sql"

sqlplus -s -L "media_app/${APP_USER_PASSWORD}@//localhost/FREEPDB1" <<EOF
WHENEVER SQLERROR EXIT FAILURE
@/opt/media-tracker/schema.sql
EXIT
EOF
