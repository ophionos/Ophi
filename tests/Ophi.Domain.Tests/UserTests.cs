using FluentAssertions;
using Ophi.Domain.Entities;

namespace Ophi.Domain.Tests;

public class UserTests
{
    [Fact]
    public void NewUser_HasNonEmptySecurityStamp()
    {
        var user = new User();

        user.SecurityStamp.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NewUser_SecurityStampIsUniquePerInstance()
    {
        var first = new User();
        var second = new User();

        first.SecurityStamp.Should().NotBe(second.SecurityStamp);
    }

    [Fact]
    public void ChangePassword_SetsNewHash()
    {
        var user = new User { PasswordHash = "old-hash" };

        user.ChangePassword("new-hash");

        user.PasswordHash.Should().Be("new-hash");
    }

    [Fact]
    public void ChangePassword_RotatesSecurityStamp()
    {
        var user = new User { PasswordHash = "old-hash" };
        var originalStamp = user.SecurityStamp;

        user.ChangePassword("new-hash");

        user.SecurityStamp.Should().NotBeNullOrWhiteSpace();
        user.SecurityStamp.Should().NotBe(originalStamp);
    }

    [Fact]
    public void ChangePassword_ClearsPasswordResetToken()
    {
        var user = new User
        {
            PasswordHash = "old-hash",
            PasswordResetTokenHash = "reset-token-hash",
            PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };

        user.ChangePassword("new-hash");

        user.PasswordResetTokenHash.Should().BeNull();
        user.PasswordResetTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public void UpdateProfile_SetsNameAndEmail()
    {
        var user = new User { Name = "Old Name", Email = "old@example.com" };

        user.UpdateProfile("New Name", "new@example.com");

        user.Name.Should().Be("New Name");
        user.Email.Should().Be("new@example.com");
    }

    [Fact]
    public void UpdateProfile_NormalizesEmailToLowercase()
    {
        var user = new User { Name = "Name", Email = "old@example.com" };

        user.UpdateProfile("Name", "MiXeD@Example.COM");

        user.Email.Should().Be("mixed@example.com");
    }

    [Fact]
    public void UpdateProfile_DoesNotRotateSecurityStamp()
    {
        // Only credential changes invalidate sessions; profile edits keep them alive.
        var user = new User { Name = "Name", Email = "old@example.com" };
        var originalStamp = user.SecurityStamp;

        user.UpdateProfile("New Name", "new@example.com");

        user.SecurityStamp.Should().Be(originalStamp);
    }
}
