#!/bin/bash

# Lade die Umgebungsvariablen aus der .env-Datei
set -a
source .env
set +a

# Liste aller Tabellen in der Datenbank abrufen und löschen
docker exec -i btcadvisor-postgres-db psql -U sa -d postgres <<EOF
DO \$\$
DECLARE
    table_name text;
BEGIN
    FOR table_name IN
        SELECT tablename
        FROM pg_tables
        WHERE schemaname = 'public'
    LOOP
        EXECUTE 'DROP TABLE IF EXISTS ' || quote_ident(table_name) || ' CASCADE';
        RAISE NOTICE 'Tabelle % gelöscht', table_name;
    END LOOP;
END
\$\$;

DO \$\$
DECLARE
    seq_name text;
BEGIN
    FOR seq_name IN
        SELECT sequencename
        FROM pg_sequences
        WHERE schemaname = 'public'
    LOOP
        EXECUTE 'DROP SEQUENCE IF EXISTS ' || quote_ident(seq_name) || ' CASCADE';
        RAISE NOTICE 'Sequenz % gelöscht', seq_name;
    END LOOP;
END
\$\$;
EOF

echo "Datenbank wurde geleert."
