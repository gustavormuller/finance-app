START TRANSACTION;
ALTER TABLE "StagedTransactions" ADD "CategorySource" integer NOT NULL DEFAULT 0;

CREATE TABLE "AiAnalyses" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Month" char(7) NOT NULL,
    "Status" integer NOT NULL,
    "Content" text,
    "Error" character varying(500),
    "PromptVersion" character varying(20) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "StartedAt" timestamp with time zone,
    "CompletedAt" timestamp with time zone,
    CONSTRAINT "PK_AiAnalyses" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiAnalyses_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "AiUsage" (
    "Id" uuid NOT NULL,
    "UserId" uuid NOT NULL,
    "Month" char(7) NOT NULL,
    "Purpose" integer NOT NULL,
    "Provider" character varying(20) NOT NULL,
    "Model" character varying(100) NOT NULL,
    "InputTokens" integer NOT NULL,
    "OutputTokens" integer NOT NULL,
    "CostBrl" numeric(10,4) NOT NULL,
    "Succeeded" boolean NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_AiUsage" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_AiUsage_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_AiAnalyses_UserId_Month" ON "AiAnalyses" ("UserId", "Month");

CREATE INDEX "IX_AiUsage_UserId_Month" ON "AiUsage" ("UserId", "Month");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260924082437_AddAi', '10.0.12');

COMMIT;


