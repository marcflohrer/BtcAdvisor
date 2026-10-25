-- Dubletten in IndicatorError bereinigen (Primärschlüssel: Id)
DELETE FROM "IndicatorError"
WHERE "Id" IN (
    SELECT "Id"
    FROM (
        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Id" ORDER BY "Id") as rnum
        FROM "IndicatorError"
    ) t
    WHERE t.rnum > 1
);

-- Dubletten in IndicatorResults bereinigen (Primärschlüssel: Id)
DELETE FROM "IndicatorResults"
WHERE "Id" IN (
    SELECT "Id"
    FROM (
        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Id" ORDER BY "Id") as rnum
        FROM "IndicatorResults"
    ) t
    WHERE t.rnum > 1
);

-- Dubletten in ActionLogs bereinigen (Primärschlüssel: Id)
DELETE FROM "ActionLogs"
WHERE "Id" IN (
    SELECT "Id"
    FROM (
        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Id" ORDER BY "Id") as rnum
        FROM "ActionLogs"
    ) t
    WHERE t.rnum > 1
);

-- Dubletten in BuyActionLogs bereinigen (Primärschlüssel: Id)
DELETE FROM "BuyActionLogs"
WHERE "Id" IN (
    SELECT "Id"
    FROM (
        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Id" ORDER BY "Id") as rnum
        FROM "BuyActionLogs"
    ) t
    WHERE t.rnum > 1
);

-- Dubletten in SellActionLogs bereinigen (Primärschlüssel: Id)
DELETE FROM "SellActionLogs"
WHERE "Id" IN (
    SELECT "Id"
    FROM (
        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Id" ORDER BY "Id") as rnum
        FROM "SellActionLogs"
    ) t
    WHERE t.rnum > 1
);

-- Dubletten in IndicatorData bereinigen (Primärschlüssel: Id)
DELETE FROM "IndicatorData"
WHERE "Id" IN (
    SELECT "Id"
    FROM (
        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Id" ORDER BY "Id") as rnum
        FROM "IndicatorData"
    ) t
    WHERE t.rnum > 1
);

-- Dubletten in ShortSellerStates bereinigen (Primärschlüssel: Id)
DELETE FROM "ShortSellerStates"
WHERE "Id" IN (
    SELECT "Id"
    FROM (
        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Id" ORDER BY "Id") as rnum
        FROM "ShortSellerStates"
    ) t
    WHERE t.rnum > 1
);

-- Dubletten in ShortSellerPhaseNotifications bereinigen (Primärschlüssel: Id)
DELETE FROM "ShortSellerPhaseNotifications"
WHERE "Id" IN (
    SELECT "Id"
    FROM (
        SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "Id" ORDER BY "Id") as rnum
        FROM "ShortSellerPhaseNotifications"
    ) t
    WHERE t.rnum > 1
);

-- Dubletten in StockCandles bereinigen (Primärschlüssel: CandleID)
DELETE FROM "StockCandles"
WHERE "CandleID" IN (
    SELECT "CandleID"
    FROM (
        SELECT "CandleID", ROW_NUMBER() OVER (PARTITION BY "CandleID" ORDER BY "CandleID") as rnum
        FROM "StockCandles"
    ) t
    WHERE t.rnum > 1
);
