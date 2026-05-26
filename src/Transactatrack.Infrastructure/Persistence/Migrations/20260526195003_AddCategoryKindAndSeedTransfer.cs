using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transactatrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryKindAndSeedTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Categories",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "User");

            migrationBuilder.Sql(@"
                UPDATE ""Categories"" SET ""Kind"" = 'Transfer' WHERE LOWER(""Name"") = 'transfer';

                INSERT INTO ""Categories"" (""Id"", ""FamilyId"", ""CreatedUtc"", ""Name"", ""Kind"")
                SELECT gen_random_uuid(), f.""Id"", (NOW() AT TIME ZONE 'UTC'), 'Transfer', 'Transfer'
                FROM ""Families"" f
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Categories"" c
                    WHERE c.""FamilyId"" = f.""Id"" AND c.""Kind"" = 'Transfer'
                );

                UPDATE ""Transactions"" SET ""IsTransfer"" = TRUE
                WHERE ""CategoryId"" IN (SELECT ""Id"" FROM ""Categories"" WHERE ""Kind"" = 'Transfer');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Categories");
        }
    }
}
