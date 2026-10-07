-- Demo data seed for UX exploration. Idempotent: clears prior seeded products and comparison
-- groups before re-inserting, so it can be run repeatedly against the same user.
DO $$
DECLARE
    uid uuid := 'fa7ee8db-a9bf-4a97-8290-60cd85fbb0b4';
    now_ts timestamptz := now();
    rec record;
    pid uuid;
    urlid uuid;
    days int := 75;
    d int;
    t double precision;
    px numeric(18,2);
    prev_px numeric(18,2);
    last_px numeric(18,2);
    cg_id uuid;
    -- "store" is stamped straight into ProductUrls.StoreId, so it must be a value the scraper can
    -- actually produce: ScrapingService does `storeConfig?.Id ?? "generic"`, and the only registered
    -- configs are amazon and ebay (CodeStoreConfigProvider). A Walmart/Target/Best Buy URL therefore
    -- scrapes as "generic" — those ids were never reachable in production. The URLs stay as they are;
    -- tracking those sites works, it just goes through the generic selector set.
    defs jsonb := '[
      {"name":"Apple AirPods Pro (2nd Gen)","store":"amazon","url":"https://www.amazon.com/dp/B0CHWRXH8B","high":249,"low":189,"fav":true,"anom":false},
      {"name":"Sony PlayStation 5 Slim Console","store":"generic","url":"https://www.bestbuy.com/site/ps5-slim/6566688.p","high":499,"low":449,"fav":false,"anom":false},
      {"name":"Kindle Paperwhite (16 GB)","store":"amazon","url":"https://www.amazon.com/dp/B08KTZ8249","high":159,"low":139,"fav":false,"anom":false},
      {"name":"Samsung 65\" Class CU8000 4K TV","store":"generic","url":"https://www.walmart.com/ip/samsung-cu8000/123456","high":599,"low":397,"fav":false,"anom":true},
      {"name":"Nike Air Max 90","store":"ebay","url":"https://www.ebay.com/itm/nike-air-max-90/204567","high":130,"low":119,"fav":false,"anom":false},
      {"name":"Apple MacBook Air 13\" M3","store":"amazon","url":"https://www.amazon.com/dp/B0CX23V2ZK","high":1099,"low":999,"fav":true,"anom":false},
      {"name":"Dyson V15 Detect Cordless Vacuum","store":"generic","url":"https://www.target.com/p/dyson-v15-detect/-/A-84561234","high":749,"low":549,"fav":false,"anom":false},
      {"name":"Instant Pot Duo 7-in-1 (6 Qt)","store":"generic","url":"https://www.walmart.com/ip/instant-pot-duo/567890","high":89,"low":59,"fav":false,"anom":false}
    ]'::jsonb;
BEGIN
    -- clean previously seeded demo rows (keep the manually-created Sony product, we backfill it)
    DELETE FROM "Products" WHERE "UserId" = uid AND "Name" <> 'Sony WH-1000XM5 Headphones';

    -- drop prior comparison groups so a re-run doesn't hit the (UserId, Name) unique constraint.
    -- null any surviving references (e.g. the kept Sony product) first to satisfy the FK.
    UPDATE "Products" SET "ComparisonGroupId" = NULL WHERE "UserId" = uid AND "ComparisonGroupId" IS NOT NULL;
    DELETE FROM "ComparisonGroups" WHERE "UserId" = uid;

    -- backfill the manually-created Sony product with a URL + history
    SELECT "Id" INTO pid FROM "Products" WHERE "UserId" = uid AND "Name" = 'Sony WH-1000XM5 Headphones' LIMIT 1;
    IF pid IS NOT NULL THEN
        DELETE FROM "PricePoints" WHERE "ProductId" = pid;
        DELETE FROM "ProductUrls" WHERE "ProductId" = pid;
        urlid := gen_random_uuid();
        INSERT INTO "ProductUrls"("Id","Url","StoreId","CurrentPrice","Currency","LastCheckedAt","FailureCount","Status","SuspiciousCount","IsOutOfStock","SelectorType","ProductId","CreatedAt","UpdatedAt")
        VALUES (urlid,'https://www.amazon.com/dp/B09XS7JWHH','amazon',278,'USD',now_ts,0,0,0,false,0,pid,now_ts,now_ts);
        prev_px := NULL; last_px := NULL;
        FOR d IN 0..days-1 LOOP
            t := d::double precision/(days-1);
            px := round((348 - (348-278)*t + (348-278)*0.07*sin(d*0.7))::numeric, 2);
            IF d = days-1 THEN px := 278; END IF;
            INSERT INTO "PricePoints"("Id","Price","Currency","RecordedAt","ProductId","ProductUrlId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(), px, 'USD', now_ts - ((days-1-d) || ' days')::interval, pid, urlid, now_ts, now_ts);
            prev_px := last_px; last_px := px;
        END LOOP;
        UPDATE "Products" SET "CurrentPrice"=last_px,"PreviousPrice"=prev_px,"ImageUrl"='https://m.media-amazon.com/images/I/61vJtKbAssL._AC_SL1500_.jpg',"UpdatedAt"=now_ts WHERE "Id"=pid;
    END IF;

    -- seeded products
    FOR rec IN SELECT * FROM jsonb_to_recordset(defs) AS x(name text, store text, url text, high numeric, low numeric, fav boolean, anom boolean) LOOP
        pid := gen_random_uuid();
        urlid := gen_random_uuid();
        INSERT INTO "Products"("Id","Name","Currency","Status","IsFavourite","HasPriceAnomaly","UserId","CreatedAt","UpdatedAt","CheckIntervalMinutes")
        VALUES (pid, rec.name, 'USD', 1, rec.fav, rec.anom, uid, now_ts - (days || ' days')::interval, now_ts, 60);
        INSERT INTO "ProductUrls"("Id","Url","StoreId","CurrentPrice","Currency","LastCheckedAt","FailureCount","Status","SuspiciousCount","IsOutOfStock","SelectorType","ProductId","CreatedAt","UpdatedAt")
        VALUES (urlid, rec.url, rec.store, rec.low, 'USD', now_ts - interval '2 hours', 0, 0, 0, false, 0, pid, now_ts, now_ts);
        prev_px := NULL; last_px := NULL;
        FOR d IN 0..days-1 LOOP
            t := d::double precision/(days-1);
            px := round((rec.high - (rec.high-rec.low)*t + (rec.high-rec.low)*0.08*sin(d*0.55))::numeric, 2);
            IF rec.anom AND d = days-2 THEN px := round(rec.low*0.55, 2); END IF; -- anomaly spike-down
            IF d = days-1 THEN px := rec.low; END IF;
            INSERT INTO "PricePoints"("Id","Price","Currency","RecordedAt","ProductId","ProductUrlId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(), px, 'USD', now_ts - ((days-1-d) || ' days')::interval, pid, urlid, now_ts, now_ts);
            prev_px := last_px; last_px := px;
        END LOOP;
        UPDATE "Products" SET "CurrentPrice"=last_px,"PreviousPrice"=prev_px,"UpdatedAt"=now_ts - interval '2 hours' WHERE "Id"=pid;

        -- alerts on a couple of products
        IF rec.name LIKE 'Apple AirPods%' THEN
            INSERT INTO "Alerts"("Id","TargetPrice","ReferencePrice","Currency","Condition","IsActive","TriggerCount","ProductId","UserId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(), 199, 249, 'USD', 0, true, 1, pid, uid, now_ts, now_ts);
        END IF;
        IF rec.name LIKE 'Dyson%' THEN
            INSERT INTO "Alerts"("Id","TargetPrice","ReferencePrice","Currency","Condition","IsActive","TriggerCount","ProductId","UserId","CreatedAt","UpdatedAt")
            VALUES (gen_random_uuid(), 500, 749, 'USD', 0, true, 0, pid, uid, now_ts, now_ts);
        END IF;
    END LOOP;

    -- comparison group of the three Apple products
    cg_id := gen_random_uuid();
    INSERT INTO "ComparisonGroups"("Id","Name","UserId","CreatedAt","UpdatedAt")
    VALUES (cg_id, 'Apple Wishlist', uid, now_ts, now_ts);
    UPDATE "Products" SET "ComparisonGroupId"=cg_id
    WHERE "UserId"=uid AND "Name" IN ('Apple AirPods Pro (2nd Gen)','Apple MacBook Air 13" M3','Sony WH-1000XM5 Headphones');
END $$;

SELECT count(*) AS products FROM "Products" WHERE "UserId"='fa7ee8db-a9bf-4a97-8290-60cd85fbb0b4';
SELECT count(*) AS price_points FROM "PricePoints";
SELECT count(*) AS alerts FROM "Alerts";
