namespace Ophi.Infrastructure.Settings;

public class RegistrationSettings
{
    public const string SectionName = "Registration";

    /// <summary>
    /// Whether new accounts can sign up. When false, sign-up still works while the instance has
    /// no accounts at all, so a fresh deployment can create its owner; after that it is refused.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
