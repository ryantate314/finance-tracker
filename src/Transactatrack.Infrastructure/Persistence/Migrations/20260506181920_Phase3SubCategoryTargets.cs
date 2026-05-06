using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transactatrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3SubCategoryTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubCategoryId",
                table: "Transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetSubCategoryId",
                table: "CategoryRules",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SubCategoryId",
                table: "Transactions",
                column: "SubCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryRules_TargetSubCategoryId",
                table: "CategoryRules",
                column: "TargetSubCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_CategoryRules_SubCategories_TargetSubCategoryId",
                table: "CategoryRules",
                column: "TargetSubCategoryId",
                principalTable: "SubCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_SubCategories_SubCategoryId",
                table: "Transactions",
                column: "SubCategoryId",
                principalTable: "SubCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CategoryRules_SubCategories_TargetSubCategoryId",
                table: "CategoryRules");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_SubCategories_SubCategoryId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_SubCategoryId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_CategoryRules_TargetSubCategoryId",
                table: "CategoryRules");

            migrationBuilder.DropColumn(
                name: "SubCategoryId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TargetSubCategoryId",
                table: "CategoryRules");
        }
    }
}
