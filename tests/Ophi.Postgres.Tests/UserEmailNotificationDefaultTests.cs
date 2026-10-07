using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Ophi.TestHelpers;

namespace Ophi.Postgres.Tests;

/// <summary>
/// Guards the SQL column default behind <c>User.EmailNotificationsEnabled</c>.
///
/// The C# property initializer (<c>= true</c>) only applies to entities constructed in .NET, and
/// every other test seeds users that way — so a migration that shipped <c>defaultValue: false</c>
/// would pass the whole suite while silently muting alert email for every account that existed
/// before the deploy. Only a row written without the column can tell the two defaults apart, and
/// only real Postgres applies the DDL default the migration actually ships.
/// </summary>
[Collection("Postgres")]
public class UserEmailNotificationDefaultTests(PostgresFixture fixture)
{
    [Fact]
    public async Task ExistingUserRow_WrittenWithoutTheColumn_DefaultsToEmailEnabled()
    {
        await using var ctx = fixture.CreateContext();

        var id = Guid.NewGuid();

        // Deliberately raw: EF always sends the column, which would test the CLR initializer
        // rather than the DDL default. This is the shape of a row that predates the migration.
        await ctx.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Users" ("Id", "Email", "Name", "PasswordHash", "EmailVerified",
                                 "AffiliatesEnabled", "DiscordNotificationsEnabled",
                                 "CreatedAt", "UpdatedAt")
            VALUES ({0}, 'legacy@example.com', 'Legacy User', 'hash', false,
                    true, false, now(), now())
            """,
            [id],
            TestContext.Current.CancellationToken);

        var user = await ctx.Users.AsNoTracking()
            .FirstAsync(u => u.Id == id, TestContext.Current.CancellationToken);

        user.EmailNotificationsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task OptingOut_PersistsAsFalse()
    {
        // The column default must not be a database-level override that re-enables on write.
        await using var ctx = fixture.CreateContext();

        var user = TestEntityFactory.User().Build();
        user.EmailNotificationsEnabled = false;
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var readCtx = fixture.CreateContext();
        var reloaded = await readCtx.Users.AsNoTracking()
            .FirstAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken);

        reloaded.EmailNotificationsEnabled.Should().BeFalse();
    }
}
