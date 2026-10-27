namespace Torray.Domain;

public sealed record AppSettings
{
    public bool HideFlyoutOnDeactivate { get; init; } = true;
}
