START TRANSACTION;
CREATE TABLE "Benchmarks" (
    "Code" character varying(20) NOT NULL,
    "Date" date NOT NULL,
    "Value" numeric(18,8) NOT NULL,
    CONSTRAINT "PK_Benchmarks" PRIMARY KEY ("Code", "Date")
);

CREATE TABLE "MarketAssets" (
    "Id" uuid NOT NULL,
    "Ticker" character varying(20) NOT NULL,
    "Name" character varying(200) NOT NULL,
    "Class" integer NOT NULL,
    "Currency" char(3) NOT NULL,
    "Provider" integer NOT NULL,
    "ProviderSymbol" character varying(50) NOT NULL,
    "IsActive" boolean NOT NULL,
    "LastSyncedAt" timestamp with time zone,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_MarketAssets" PRIMARY KEY ("Id")
);

CREATE TABLE "SyncRuns" (
    "Id" uuid NOT NULL,
    "StartedAt" timestamp with time zone NOT NULL,
    "FinishedAt" timestamp with time zone,
    "Trigger" integer NOT NULL,
    "Status" integer NOT NULL,
    "Summary" jsonb NOT NULL,
    CONSTRAINT "PK_SyncRuns" PRIMARY KEY ("Id")
);

CREATE TABLE "Prices" (
    "MarketAssetId" uuid NOT NULL,
    "Date" date NOT NULL,
    "Close" numeric(18,8) NOT NULL,
    CONSTRAINT "PK_Prices" PRIMARY KEY ("MarketAssetId", "Date"),
    CONSTRAINT "FK_Prices_MarketAssets_MarketAssetId" FOREIGN KEY ("MarketAssetId") REFERENCES "MarketAssets" ("Id") ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_MarketAssets_Provider_ProviderSymbol" ON "MarketAssets" ("Provider", "ProviderSymbol");

CREATE INDEX "IX_SyncRuns_StartedAt" ON "SyncRuns" ("StartedAt" DESC);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260924033238_AddMarketData', '10.0.12');

COMMIT;

