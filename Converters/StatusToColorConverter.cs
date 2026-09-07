using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AutoUpgrade.Models;
using AutoUpgrade.Services;

namespace AutoUpgrade.Converters;

/// <summary>
/// Wise Design Language badge background palette with dark mode support.
/// </summary>
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TaskStatusType status)
        {
            if (ThemeManager.IsDarkMode)
            {
                return status switch
                {
                    TaskStatusType.Approved => new SolidColorBrush(Color.FromRgb(0x17, 0x3A, 0x21)), // dark-emerald
                    TaskStatusType.Declined => new SolidColorBrush(Color.FromRgb(0x3D, 0x2D, 0x12)), // dark-amber
                    TaskStatusType.Error => new SolidColorBrush(Color.FromRgb(0x3E, 0x18, 0x1B)),    // dark-rose
                    TaskStatusType.Running => new SolidColorBrush(Color.FromRgb(0x12, 0x32, 0x47)),  // dark-sky
                    _ => new SolidColorBrush(Color.FromRgb(0x25, 0x2B, 0x21))                       // dark-canvas
                };
            }

            return status switch
            {
                TaskStatusType.Approved => new SolidColorBrush(Color.FromRgb(0xE2, 0xF6, 0xD5)), // primary-pale #e2f6d5
                TaskStatusType.Declined => new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)), // warning-pale #fef3c7
                TaskStatusType.Error => new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2)),    // negative-pale #fee2e2
                TaskStatusType.Running => new SolidColorBrush(Color.FromRgb(0xE0, 0xF2, 0xFE)),  // cyan-pale #e0f2fe
                _ => new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xE6))                       // canvas-soft #e8ebe6
            };
        }

        return ThemeManager.IsDarkMode
            ? new SolidColorBrush(Color.FromRgb(0x25, 0x2B, 0x21))
            : new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xE6));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Wise Design Language badge text foreground palette with dark mode support.
/// </summary>
public class StatusToForegroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TaskStatusType status)
        {
            if (ThemeManager.IsDarkMode)
            {
                return status switch
                {
                    TaskStatusType.Approved => new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)), // bright emerald #4ade80
                    TaskStatusType.Declined => new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)), // bright amber #fbbf24
                    TaskStatusType.Error => new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),    // bright rose #f87171
                    TaskStatusType.Running => new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)),  // bright sky #38bdf8
                    _ => new SolidColorBrush(Color.FromRgb(0xB2, 0xB8, 0xAC))                       // muted sage #b2b8ac
                };
            }

            return status switch
            {
                TaskStatusType.Approved => new SolidColorBrush(Color.FromRgb(0x05, 0x4D, 0x28)), // positive-deep #054d28
                TaskStatusType.Declined => new SolidColorBrush(Color.FromRgb(0xB8, 0x67, 0x00)), // warning-deep #b86700
                TaskStatusType.Error => new SolidColorBrush(Color.FromRgb(0xA7, 0x00, 0x0D)),    // negative-darkest #a7000d
                TaskStatusType.Running => new SolidColorBrush(Color.FromRgb(0x03, 0x69, 0xA1)),  // cyan-deep #0369a1
                _ => new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x45))                       // body #454745
            };
        }

        return ThemeManager.IsDarkMode
            ? new SolidColorBrush(Color.FromRgb(0xB2, 0xB8, 0xAC))
            : new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x45));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
