using System.Globalization;
using Avalonia.Data.Converters;
using GitCiHud.Models;
namespace GitCiHud.ViewModels;
public sealed class JobStatusConverter : IValueConverter
{
    public static JobStatusConverter Instance { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is CiStatus status ? MainHudViewModel.JobSymbol(status) : "?";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
