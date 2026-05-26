using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transactatrack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomeSystemCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""Categories"" SET ""Kind"" = 'Income'
                  WHERE LOWER(""Name"") = 'income' AND ""Kind"" = 'User';

                INSERT INTO ""Categories"" (""Id"", ""FamilyId"", ""CreatedUtc"", ""Name"", ""Kind"")
                SELECT gen_random_uuid(), f.""Id"", (NOW() AT TIME ZONE 'UTC'), 'Income', 'Income'
                FROM ""Families"" f
                WHERE NOT EXISTS (
                    SELECT 1 FROM ""Categories"" c
                    WHERE c.""FamilyId"" = f.""Id"" AND c.""Kind"" = 'Income'
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE ""Categories"" SET ""Kind"" = 'User' WHERE ""Kind"" = 'Income';
            ");
        }
    }
}
