using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Oa.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiFactorAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastTotpTimeStep",
                table: "user_account",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "user_account",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MfaEnabledAt",
                table: "user_account",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaSecretCiphertext",
                table: "user_account",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MfaUpdatedAt",
                table: "user_account",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryCodeHashesJson",
                table: "user_account",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateTable(
                name: "mfa_challenge",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PendingSecretCiphertext = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mfa_challenge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mfa_challenge_oa_user_UserId",
                        column: x => x.UserId,
                        principalTable: "oa_user",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mfa_challenge_TokenHash",
                table: "mfa_challenge",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mfa_challenge_UserId_ConsumedAt_ExpiresAt",
                table: "mfa_challenge",
                columns: new[] { "UserId", "ConsumedAt", "ExpiresAt" });

            migrationBuilder.Sql("""
                UPDATE auth_session
                SET "RevokedAt" = NOW(), "RevokedReason" = 'MFA_ROLLOUT'
                WHERE "RevokedAt" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mfa_challenge");

            migrationBuilder.DropColumn(
                name: "LastTotpTimeStep",
                table: "user_account");

            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "user_account");

            migrationBuilder.DropColumn(
                name: "MfaEnabledAt",
                table: "user_account");

            migrationBuilder.DropColumn(
                name: "MfaSecretCiphertext",
                table: "user_account");

            migrationBuilder.DropColumn(
                name: "MfaUpdatedAt",
                table: "user_account");

            migrationBuilder.DropColumn(
                name: "RecoveryCodeHashesJson",
                table: "user_account");
        }
    }
}
