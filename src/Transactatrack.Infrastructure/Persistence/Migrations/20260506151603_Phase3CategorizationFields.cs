using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transactatrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3CategorizationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppliedRuleId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategorizationSource",
                table: "Transactions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CategorizedUtc",
                table: "Transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LlmConfidence",
                table: "Transactions",
                type: "numeric(3,2)",
                precision: 3,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LlmModel",
                table: "Transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "Transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LlmRowsDone",
                table: "ImportBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LlmRowsTotal",
                table: "ImportBatches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LlmStatus",
                table: "ImportBatches",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "AmountMax",
                table: "CategoryRules",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AmountMin",
                table: "CategoryRules",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AppliedRuleId",
                table: "Transactions",
                column: "AppliedRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_FamilyId_NeedsReview",
                table: "Transactions",
                columns: new[] { "FamilyId", "NeedsReview" });

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_CategoryRules_AppliedRuleId",
                table: "Transactions",
                column: "AppliedRuleId",
                principalTable: "CategoryRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_CategoryRules_AppliedRuleId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AppliedRuleId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_FamilyId_NeedsReview",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "AppliedRuleId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategorizationSource",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "CategorizedUtc",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LlmConfidence",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LlmModel",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LlmRowsDone",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "LlmRowsTotal",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "LlmStatus",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AmountMax",
                table: "CategoryRules");

            migrationBuilder.DropColumn(
                name: "AmountMin",
                table: "CategoryRules");
        }
    }
}
