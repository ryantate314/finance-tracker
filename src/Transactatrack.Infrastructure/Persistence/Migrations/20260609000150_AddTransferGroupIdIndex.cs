using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transactatrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransferGroupIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_TransferGroupId",
                table: "Transactions",
                column: "TransferGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_TransferGroupId",
                table: "Transactions");
        }
    }
}
