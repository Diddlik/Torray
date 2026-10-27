using System.Windows;

namespace Torray.App;

internal static class Strings
{
    public static string Get(string key) => (string)Application.Current.FindResource(key);

    public static string Format(string key, params object?[] args) => string.Format(Get(key), args);
}
