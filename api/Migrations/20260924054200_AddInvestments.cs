using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInvestments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketAssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nickname = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assets_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Assets_MarketAssets_MarketAssetId",
                        column: x => x.MarketAssetId,
                        principalTable: "MarketAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Fees = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "char(3)", nullable: false),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movements_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Movements_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PortfolioDaily",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    AverageCost = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    PriceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    FxRate = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    ValueBrl = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CostBasisBrl = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortfolioDaily", x => new { x.UserId, x.AssetId, x.Date });
                    table.ForeignKey(
                        name: "FK_PortfolioDaily_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PortfolioDaily_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_MarketAssetId",
                table: "Assets",
                column: "MarketAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Assets_UserId_MarketAssetId",
                table: "Assets",
                columns: new[] { "UserId", "MarketAssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movements_AssetId",
                table: "Movements",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_UserId_AssetId_Date",
                table: "Movements",
                columns: new[] { "UserId", "AssetId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioDaily_AssetId",
                table: "PortfolioDaily",
                column: "AssetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Movements");

            migrationBuilder.DropTable(
                name: "PortfolioDaily");

            migrationBuilder.DropTable(
                name: "Assets");
        }
    }
}
