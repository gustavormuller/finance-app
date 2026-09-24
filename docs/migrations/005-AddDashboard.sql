START TRANSACTION;
ALTER TABLE "Accounts" ADD "OpeningBalance" numeric(18,2) NOT NULL DEFAULT 0.0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260924025212_AddDashboard', '10.0.12');

COMMIT;

