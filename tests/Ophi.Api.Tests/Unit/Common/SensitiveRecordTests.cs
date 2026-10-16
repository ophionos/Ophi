using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using Ophi.Domain.Logging;

namespace Ophi.Api.Tests.Unit.Common;

/// <summary>
/// Guards against credentials in logs. Wolverine logs a failing message with its <c>ToString()</c>,
/// so every record that carries a credential must mark it <see cref="SensitiveAttribute"/> and mask it.
/// </summary>
public partial class SensitiveRecordTests
{
    // A property name that looks like a credential. Generic names that are only sometimes a
    // credential (an outbound webhook's Url, a challenge's typed Text) are marked by hand instead.
    [GeneratedRegex("Password|Token|Secret|WebhookUrl|TopicUrl|Key$")]
    private static partial Regex CredentialName();

    private static readonly Assembly[] Scanned =
    [
        typeof(Program).Assembly,                                     // Ophi.Api
        typeof(SensitiveAttribute).Assembly,                          // Ophi.Domain
        typeof(Ophi.Infrastructure.Persistence.OphiDbContext).Assembly // Ophi.Infrastructure
    ];

    private static IEnumerable<Type> RecordTypes() =>
        Scanned.SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, ContainsGenericParameters: false } && t.GetMethod("<Clone>$") is not null);

    private static IEnumerable<PropertyInfo> PublicProperties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0);

    [Fact]
    public void Records_WithCredentialNamedProperties_MarkThemSensitive()
    {
        var unmarked = RecordTypes()
            .SelectMany(t => PublicProperties(t).Select(p => (Type: t, Property: p)))
            .Where(x => x.Property.PropertyType == typeof(string) && CredentialName().IsMatch(x.Property.Name))
            .Where(x => !x.Property.IsDefined(typeof(SensitiveAttribute)))
            .Select(x => $"{x.Type.FullName}.{x.Property.Name}");

        unmarked.Should().BeEmpty("a credential-named record property must carry [property: Sensitive]");
    }

    [Fact]
    public void Records_WithSensitiveProperties_MaskThemInToString()
    {
        var sensitiveTypes = RecordTypes()
            .Where(t => PublicProperties(t).Any(p => p.IsDefined(typeof(SensitiveAttribute))))
            .ToList();
        sensitiveTypes.Should().NotBeEmpty();

        var leaks = new List<string>();
        foreach (var type in sensitiveTypes)
        {
            var instance = RuntimeHelpers.GetUninitializedObject(type);
            var sentinels = new List<string>();
            foreach (var property in PublicProperties(type).Where(p => p.IsDefined(typeof(SensitiveAttribute))))
            {
                property.PropertyType.Should().Be<string>($"{type.FullName}.{property.Name} is masked as a string");
                var sentinel = $"sentinel-{Guid.NewGuid():N}";
                property.SetValue(instance, sentinel);
                sentinels.Add(sentinel);
            }

            var printed = instance.ToString()!;
            if (sentinels.Any(printed.Contains))
            {
                leaks.Add($"{type.FullName}: {printed}");
            }
        }

        leaks.Should().BeEmpty("the record must override PrintMembers with SensitivePrinter.PrintMembers");
    }

    [Fact]
    public void PrintMembers_MasksSensitiveValues_AndKeepsTheRest()
    {
        new Sample("a@example.com", "hunter2", null).ToString()
            .Should().Be("Sample { Email = a@example.com, Password = ***, Token =  }");
    }

    private record Sample(string Email, [property: Sensitive] string Password, [property: Sensitive] string? Token)
    {
        protected virtual bool PrintMembers(System.Text.StringBuilder builder) => SensitivePrinter.PrintMembers(this, builder);
    }
}
