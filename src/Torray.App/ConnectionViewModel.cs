using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Torray.Domain;

namespace Torray.App;

public sealed partial class ConnectionViewModel : ObservableObject
{
    private readonly ITransmissionRpcClientFactory _clientFactory;
    private readonly Guid _id;

    public ConnectionViewModel(ConnectionSettings? existing, string? password, ITransmissionRpcClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
        var connection = existing ?? new ConnectionSettings();
        _id = connection.Id;
        CanRemove = existing is not null;
        Name = connection.Name ?? string.Empty;
        Scheme = connection.Scheme;
        Host = connection.Host;
        Port = connection.Port.ToString(CultureInfo.InvariantCulture);
        RpcPath = connection.RpcPath;
        Username = connection.Username ?? string.Empty;
        Password = password ?? string.Empty;
    }

    public event Action<bool>? CloseRequested;

    public IReadOnlyList<string> Schemes { get; } = ["http", "https"];

    public bool CanRemove { get; }

    public bool RemoveRequested { get; private set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Scheme { get; set; }

    [ObservableProperty]
    public partial string Host { get; set; }

    [ObservableProperty]
    public partial string Port { get; set; }

    [ObservableProperty]
    public partial string RpcPath { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    // Not observable: PasswordBox cannot bind, the view pushes changes here.
    public string Password { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    public (ConnectionSettings Connection, string? Password)? Result { get; private set; }

    [RelayCommand]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        if (TryBuild() is not { } connection)
        {
            return;
        }

        SetStatus(Strings.Get("ConnectionTesting"), isError: false);
        try
        {
            var info = await _clientFactory.Create(connection, NullIfEmpty(Password)).GetSessionAsync(cancellationToken);
            SetStatus(Strings.Format("ConnectionTestSucceeded", info.Version), isError: false);
        }
        catch (TransmissionRpcException ex)
        {
            SetStatus(Strings.Format("RpcError" + ex.Kind, ex.Message), isError: true);
        }
        catch (OperationCanceledException)
        {
            SetStatus(string.Empty, isError: false);
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (TryBuild() is { } connection)
        {
            Result = (connection, NullIfEmpty(Password));
            CloseRequested?.Invoke(true);
        }
    }

    [RelayCommand]
    private void Remove()
    {
        RemoveRequested = true;
        CloseRequested?.Invoke(true);
    }

    private ConnectionSettings? TryBuild()
    {
        var host = Host.Trim();
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            SetStatus(Strings.Get("ConnectionInvalidHost"), isError: true);
            return null;
        }

        if (!int.TryParse(Port, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            SetStatus(Strings.Get("ConnectionInvalidPort"), isError: true);
            return null;
        }

        var path = RpcPath.Trim();
        if (!path.StartsWith('/'))
        {
            SetStatus(Strings.Get("ConnectionInvalidPath"), isError: true);
            return null;
        }

        return new ConnectionSettings
        {
            Id = _id,
            Name = NullIfEmpty(Name.Trim()),
            Scheme = Scheme,
            Host = host,
            Port = port,
            RpcPath = path,
            Username = NullIfEmpty(Username.Trim()),
        };
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;
}
