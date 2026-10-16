using System.Reflection;
using System.Text;

namespace Ophi.Domain.Logging;

/// <summary>
/// Marks a record property whose value is a credential (password, token, secret URL, key).
/// The record must also route its <c>PrintMembers</c> through <see cref="SensitivePrinter"/>,
/// so that <c>ToString()</c> masks the value. Wolverine logs a failing message with its
/// <c>ToString()</c>, and FluentValidation puts it in the exception message.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveAttribute : Attribute;

public static class SensitivePrinter
{
    public const string Mask = "***";

    /// <summary>
    /// Replacement for a record's synthesized <c>PrintMembers</c>:
    /// <c>protected virtual bool PrintMembers(StringBuilder builder) => SensitivePrinter.PrintMembers(this, builder);</c>
    /// Same <c>Name = value</c> output, but a non-null <see cref="SensitiveAttribute"/> value prints as <see cref="Mask"/>.
    /// </summary>
    public static bool PrintMembers(object record, StringBuilder builder)
    {
        var first = true;
        foreach (var property in record.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(", ");
            }

            first = false;
            var value = property.GetValue(record);
            builder.Append(property.Name).Append(" = ");
            builder.Append(value is not null && property.IsDefined(typeof(SensitiveAttribute)) ? Mask : value);
        }

        return !first;
    }
}
