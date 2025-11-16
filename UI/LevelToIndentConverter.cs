// UI/LevelToIndentConverter.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace repotxt.UI
{
    public sealed class LevelToIndentConverter : IValueConverter
    {
        public double Indent { get; set; } = 22;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int level)
            {
                return new Thickness(level * Indent, 0, 0, 0);
            }
            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}