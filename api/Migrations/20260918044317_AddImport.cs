using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Finance.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalId",
                table: "Transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ImportBatchId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedDescription",
                table: "Transactions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CsvTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Delimiter = table.Column<char>(type: "char(1)", nullable: false),
                    HasHeader = table.Column<bool>(type: "boolean", nullable: false),
                    Culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DateFormat = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SignMode = table.Column<int>(type: "integer", nullable: false),
                    DateColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AmountColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DebitColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreditColumn = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DescriptionColumns = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CsvTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CsvTemplates_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    CommittedCount = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatches_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportBatches_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StagedTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Currency = table.Column<string>(type: "char(3)", nullable: true),
                    RawDescription = table.Column<string>(type: "text", nullable: false),
                    NormalizedDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Included = table.Column<bool>(type: "boolean", nullable: false),
                    Issues = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StagedTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StagedTransactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StagedTransactions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StagedTransactions_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ImportBatchId",
                table: "Transactions",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_AccountId_ExternalId",
                table: "Transactions",
                columns: new[] { "UserId", "AccountId", "ExternalId" },
                unique: true,
                filter: "\"ExternalId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_UserId_Date_NormalizedDescription",
                table: "Transactions",
                columns: new[] { "UserId", "Date", "NormalizedDescription" });

            migrationBuilder.CreateIndex(
                name: "IX_CsvTemplates_UserId_Name",
                table: "CsvTemplates",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_AccountId",
                table: "ImportBatches",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_UserId_CreatedAt",
                table: "ImportBatches",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_UserId_Staged",
                table: "ImportBatches",
                column: "UserId",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_StagedTransactions_CategoryId",
                table: "StagedTransactions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StagedTransactions_ImportBatchId_RowNumber",
                table: "StagedTransactions",
                columns: new[] { "ImportBatchId", "RowNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StagedTransactions_UserId",
                table: "StagedTransactions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_ImportBatches_ImportBatchId",
                table: "Transactions",
                column: "ImportBatchId",
                principalTable: "ImportBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_ImportBatches_ImportBatchId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "CsvTemplates");

            migrationBuilder.DropTable(
                name: "StagedTransactions");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ImportBatchId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_AccountId_ExternalId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_UserId_Date_NormalizedDescription",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ExternalId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ImportBatchId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "NormalizedDescription",
                table: "Transactions");
        }
    }
}
