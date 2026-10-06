using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddYahooHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdjustedClose",
                table: "Prices",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HistoryLoadedAt",
                table: "MarketAssets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PricesRevisedFrom",
                table: "MarketAssets",
                type: "date",
                nullable: true);

            // An asset already synced keeps its history. A null stamp means "reload whole and
            // replace", which on CoinGecko would drop everything older than 365 days.
            migrationBuilder.Sql("""UPDATE "MarketAssets" SET "HistoryLoadedAt" = "LastSyncedAt";""");

            // IVVB11 moves from brapi's close to Yahoo's adjusted close. Without rows, the next
            // sync loads it whole from Yahoo instead of appending to brapi's series.
            migrationBuilder.Sql("""DELETE FROM "Benchmarks" WHERE "Code" = 'IVVB11';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdjustedClose",
                table: "Prices");

            migrationBuilder.DropColumn(
                name: "HistoryLoadedAt",
                table: "MarketAssets");

            migrationBuilder.DropColumn(
                name: "PricesRevisedFrom",
                table: "MarketAssets");
        }
    }
}
