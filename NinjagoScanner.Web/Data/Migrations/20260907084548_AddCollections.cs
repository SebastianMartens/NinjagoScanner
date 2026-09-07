using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NinjagoScanner.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This is the first EF Core migration in a database that, until now, was created via
            // Database.EnsureCreated() (see Program.cs). EnsureCreated() already built every
            // Identity table (AspNetUsers, AspNetRoles, ...) on any pre-existing deployment, with
            // no __EFMigrationsHistory row to tell Migrate() that. The Identity portion below is
            // therefore written as idempotent raw SQL (IF NOT EXISTS) instead of the typed
            // CreateTable/CreateIndex EF scaffolded, so it's a no-op against an existing
            // EnsureCreated() database and a normal create against a brand new one. Collections
            // and CollectionMemberships are genuinely new either way, so they use the normal typed
            // API and always create for real.
            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AspNetRoles" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_AspNetRoles" PRIMARY KEY,
                    "Name" TEXT NULL,
                    "NormalizedName" TEXT NULL,
                    "ConcurrencyStamp" TEXT NULL
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AspNetUsers" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_AspNetUsers" PRIMARY KEY,
                    "UserName" TEXT NULL,
                    "NormalizedUserName" TEXT NULL,
                    "Email" TEXT NULL,
                    "NormalizedEmail" TEXT NULL,
                    "EmailConfirmed" INTEGER NOT NULL,
                    "PasswordHash" TEXT NULL,
                    "SecurityStamp" TEXT NULL,
                    "ConcurrencyStamp" TEXT NULL,
                    "PhoneNumber" TEXT NULL,
                    "PhoneNumberConfirmed" INTEGER NOT NULL,
                    "TwoFactorEnabled" INTEGER NOT NULL,
                    "LockoutEnd" TEXT NULL,
                    "LockoutEnabled" INTEGER NOT NULL,
                    "AccessFailedCount" INTEGER NOT NULL
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AspNetRoleClaims" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_AspNetRoleClaims" PRIMARY KEY AUTOINCREMENT,
                    "RoleId" TEXT NOT NULL,
                    "ClaimType" TEXT NULL,
                    "ClaimValue" TEXT NULL,
                    CONSTRAINT "FK_AspNetRoleClaims_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AspNetUserClaims" (
                    "Id" INTEGER NOT NULL CONSTRAINT "PK_AspNetUserClaims" PRIMARY KEY AUTOINCREMENT,
                    "UserId" TEXT NOT NULL,
                    "ClaimType" TEXT NULL,
                    "ClaimValue" TEXT NULL,
                    CONSTRAINT "FK_AspNetUserClaims_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AspNetUserLogins" (
                    "LoginProvider" TEXT NOT NULL,
                    "ProviderKey" TEXT NOT NULL,
                    "ProviderDisplayName" TEXT NULL,
                    "UserId" TEXT NOT NULL,
                    CONSTRAINT "PK_AspNetUserLogins" PRIMARY KEY ("LoginProvider", "ProviderKey"),
                    CONSTRAINT "FK_AspNetUserLogins_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AspNetUserRoles" (
                    "UserId" TEXT NOT NULL,
                    "RoleId" TEXT NOT NULL,
                    CONSTRAINT "PK_AspNetUserRoles" PRIMARY KEY ("UserId", "RoleId"),
                    CONSTRAINT "FK_AspNetUserRoles_AspNetRoles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "AspNetRoles" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_AspNetUserRoles_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "AspNetUserTokens" (
                    "UserId" TEXT NOT NULL,
                    "LoginProvider" TEXT NOT NULL,
                    "Name" TEXT NOT NULL,
                    "Value" TEXT NULL,
                    CONSTRAINT "PK_AspNetUserTokens" PRIMARY KEY ("UserId", "LoginProvider", "Name"),
                    CONSTRAINT "FK_AspNetUserTokens_AspNetUsers_UserId" FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
                );
                """);

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_AspNetRoleClaims_RoleId\" ON \"AspNetRoleClaims\" (\"RoleId\");");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"RoleNameIndex\" ON \"AspNetRoles\" (\"NormalizedName\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_AspNetUserClaims_UserId\" ON \"AspNetUserClaims\" (\"UserId\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_AspNetUserLogins_UserId\" ON \"AspNetUserLogins\" (\"UserId\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_AspNetUserRoles_RoleId\" ON \"AspNetUserRoles\" (\"RoleId\");");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"EmailIndex\" ON \"AspNetUsers\" (\"NormalizedEmail\");");
            migrationBuilder.Sql("CREATE UNIQUE INDEX IF NOT EXISTS \"UserNameIndex\" ON \"AspNetUsers\" (\"NormalizedUserName\");");

            migrationBuilder.CreateTable(
                name: "CollectionMemberships",
                columns: table => new
                {
                    CollectionId = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionMemberships", x => new { x.CollectionId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CollectionMemberships");

            migrationBuilder.DropTable(
                name: "Collections");

            // Deliberately does not drop the Identity tables - see the note in Up(): this
            // migration never asserted ownership of creating them (they may predate any migration
            // history), so it shouldn't assert ownership of dropping them either.
        }
    }
}
