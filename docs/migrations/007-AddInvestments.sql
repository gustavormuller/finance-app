START TRANSACTION;
CREATE TABLE "Assets" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "MarketAssetId" uuid NOT NULL,
    "Nickname" character varying(100),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Assets" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Assets_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Assets_MarketAssets_MarketAssetId" FOREIGN KEY ("MarketAssetId") REFERENCES "MarketAssets" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "Movements" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "AssetId" uuid NOT NULL,
    "Date" date NOT NULL,
    "Kind" integer NOT NULL,
    "Quantity" numeric(18,8) NOT NULL,
    "UnitPrice" numeric(18,8) NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Fees" numeric(18,2) NOT NULL,
    "Currency" char(3) NOT NULL,
    "Notes" character varying(300),
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_Movements" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_Movements_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Movements_Assets_AssetId" FOREIGN KEY ("AssetId") REFERENCES "Assets" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "PortfolioDaily" (
    "UserId" uuid NOT NULL,
    "AssetId" uuid NOT NULL,
    "Date" date NOT NULL,
    "Quantity" numeric(18,8) NOT NULL,
    "AverageCost" numeric(18,8) NOT NULL,
    "Price" numeric(18,8) NOT NULL,
    "PriceDate" date NOT NULL,
    "FxRate" numeric(18,8) NOT NULL,
    "ValueBrl" numeric(18,2) NOT NULL,
    "CostBasisBrl" numeric(18,2) NOT NULL,
    CONSTRAINT "PK_PortfolioDaily" PRIMARY KEY ("UserId", "AssetId", "Date"),
    CONSTRAINT "FK_PortfolioDaily_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_PortfolioDaily_Assets_AssetId" FOREIGN KEY ("AssetId") REFERENCES "Assets" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_Assets_MarketAssetId" ON "Assets" ("MarketAssetId");

CREATE UNIQUE INDEX "IX_Assets_UserId_MarketAssetId" ON "Assets" ("UserId", "MarketAssetId");

CREATE INDEX "IX_Movements_AssetId" ON "Movements" ("AssetId");

CREATE INDEX "IX_Movements_UserId_AssetId_Date" ON "Movements" ("UserId", "AssetId", "Date");

CREATE INDEX "IX_PortfolioDaily_AssetId" ON "PortfolioDaily" ("AssetId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260924054200_AddInvestments', '10.0.12');

COMMIT;

