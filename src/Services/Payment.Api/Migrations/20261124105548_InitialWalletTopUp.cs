using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payment.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialWalletTopUp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Wallets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Balance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    LockedBalance = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Wallets", x => x.Id);
                    table.CheckConstraint("CK_Wallets_Balance", "[Balance] >= 0 AND [LockedBalance] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "TopUpPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopUpPayments", x => x.Id);
                    table.CheckConstraint("CK_TopUpPayments_Amount", "[Amount] BETWEEN 50000 AND 1000000000");
                    table.CheckConstraint("CK_TopUpPayments_Status", "([Status] = N'Pending' AND [CompletedAt] IS NULL) OR\n([Status] IN (N'SUCCESS', N'FAILED') AND [CompletedAt] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_TopUpPayments_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WalletId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,0)", precision: 18, scale: 0, nullable: false),
                    RefId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Transactions_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_Transactions_Wallets_WalletId",
                        column: x => x.WalletId,
                        principalTable: "Wallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedAt",
                table: "OutboxMessages",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TopUpPayments_ProviderTransactionId",
                table: "TopUpPayments",
                column: "ProviderTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TopUpPayments_WalletId_IdempotencyKey",
                table: "TopUpPayments",
                columns: new[] { "WalletId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_WalletId_CreatedAt_Id",
                table: "Transactions",
                columns: new[] { "WalletId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_WalletId_IdempotencyKey",
                table: "Transactions",
                columns: new[] { "WalletId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_UserId",
                table: "Wallets",
                column: "UserId",
                unique: true);

            migrationBuilder.Sql("""
                EXEC(N'
                    CREATE TRIGGER dbo.TR_Transactions_AppendOnly
                    ON dbo.Transactions AFTER UPDATE, DELETE
                    AS
                    BEGIN
                        SET NOCOUNT ON;
                        IF EXISTS (SELECT 1 FROM deleted)
                            THROW 51000, N''Sổ giao dịch chỉ cho phép ghi thêm.'', 1;
                    END;
                ');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "TopUpPayments");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Wallets");
        }
    }
}
