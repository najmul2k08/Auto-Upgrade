using System;
using System.Windows;
using System.Windows.Media;

namespace AutoUpgrade.Services;

public static class ThemeManager
{
    public static bool IsDarkMode { get; private set; } = false;
    public static event Action<bool>? ThemeChanged;

    public static void SetTheme(ResourceDictionary resources, bool isDark)
    {
        IsDarkMode = isDark;
        ApplyPalette(resources, isDark);
        ThemeChanged?.Invoke(isDark);
    }

    private static SolidColorBrush Hex(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    public static Brush GetStatusBrush(Models.TaskStatusType status)
    {
        if (IsDarkMode)
        {
            return status switch
            {
                Models.TaskStatusType.Approved => Hex("#173A21"),
                Models.TaskStatusType.Declined => Hex("#3D2D12"),
                Models.TaskStatusType.Error => Hex("#3E181B"),
                Models.TaskStatusType.Running => Hex("#123247"),
                _ => Hex("#252B21")
            };
        }

        return status switch
        {
            Models.TaskStatusType.Approved => Hex("#E2F6D5"),
            Models.TaskStatusType.Declined => Hex("#FEF3C7"),
            Models.TaskStatusType.Error => Hex("#FEE2E2"),
            Models.TaskStatusType.Running => Hex("#E0F2FE"),
            _ => Hex("#E8EBE6")
        };
    }

    public static Brush GetStatusForegroundBrush(Models.TaskStatusType status)
    {
        if (IsDarkMode)
        {
            return status switch
            {
                Models.TaskStatusType.Approved => Hex("#4ADE80"),
                Models.TaskStatusType.Declined => Hex("#FBBF24"),
                Models.TaskStatusType.Error => Hex("#F87171"),
                Models.TaskStatusType.Running => Hex("#38BDF8"),
                _ => Hex("#B2B8AC")
            };
        }

        return status switch
        {
            Models.TaskStatusType.Approved => Hex("#054D28"),
            Models.TaskStatusType.Declined => Hex("#B86700"),
            Models.TaskStatusType.Error => Hex("#A7000D"),
            Models.TaskStatusType.Running => Hex("#0369A1"),
            _ => Hex("#454745")
        };
    }

    public static void ApplyPalette(ResourceDictionary resources, bool isDark)
    {
        if (isDark)
        {
            // Wise Midnight / Dark Slate Theme Palette
            resources["ThemeWindowBg"] = Hex("#12130F");
            resources["ThemeCardBg"] = Hex("#1C1F19");
            resources["ThemeSurfaceSubtle"] = Hex("#242821");
            resources["ThemeBorder"] = Hex("#30362B");
            resources["ThemeBorderSubtle"] = Hex("#272C22");

            resources["ThemeTextPrimary"] = Hex("#F4F6F2");
            resources["ThemeTextSecondary"] = Hex("#B2B8AC");
            resources["ThemeTextMuted"] = Hex("#7E8578");

            resources["ThemeAccent"] = Hex("#9FE870");
            resources["ThemeAccentHover"] = Hex("#B3F08C");
            resources["ThemeAccentPress"] = Hex("#A0DF75");

            resources["ThemeSecButtonBg"] = Hex("#242821");
            resources["ThemeSecButtonHover"] = Hex("#2E332A");
            resources["ThemeSecButtonBorder"] = Hex("#3A4134");

            resources["ThemeDangerBg"] = Hex("#351B1D");
            resources["ThemeDangerFg"] = Hex("#F87171");
            resources["ThemeDangerBorder"] = Hex("#5C282C");
            resources["ThemeDangerHoverBg"] = Hex("#D03238");

            resources["ThemeInputBg"] = Hex("#161814");
            resources["ThemeInputBorder"] = Hex("#30362B");
            resources["ThemeInputText"] = Hex("#F4F6F2");

            resources["ThemeDataGridRowAlt"] = Hex("#191C16");
            resources["ThemeDataGridHeaderBg"] = Hex("#242821");
            resources["ThemeDataGridSelected"] = Hex("#223D1E");
            resources["ThemeDataGridSelectedFg"] = Hex("#9FE870");

            resources["ThemeStatApprovedBg"] = Hex("#172A1A");
            resources["ThemeStatApprovedBorder"] = Hex("#244A29");
            resources["ThemeStatApprovedFg"] = Hex("#4ADE80");

            resources["ThemeStatDeclinedBg"] = Hex("#322610");
            resources["ThemeStatDeclinedBorder"] = Hex("#543D15");
            resources["ThemeStatDeclinedFg"] = Hex("#FBBF24");

            resources["ThemeStatErrorsBg"] = Hex("#351A1D");
            resources["ThemeStatErrorsBorder"] = Hex("#5B2428");
            resources["ThemeStatErrorsFg"] = Hex("#F87171");

            resources["ThemeStatProcessingFg"] = Hex("#38BDF8");
        }
        else
        {
            // Wise Signature Light Theme Palette
            resources["ThemeWindowBg"] = Hex("#E8EBE6");
            resources["ThemeCardBg"] = Hex("#FFFFFF");
            resources["ThemeSurfaceSubtle"] = Hex("#F4F6F2");
            resources["ThemeBorder"] = Hex("#DCE1D8");
            resources["ThemeBorderSubtle"] = Hex("#E8EBE6");

            resources["ThemeTextPrimary"] = Hex("#0E0F0C");
            resources["ThemeTextSecondary"] = Hex("#454745");
            resources["ThemeTextMuted"] = Hex("#868685");

            resources["ThemeAccent"] = Hex("#9FE870");
            resources["ThemeAccentHover"] = Hex("#CDFFAD");
            resources["ThemeAccentPress"] = Hex("#C5EDAB");

            resources["ThemeSecButtonBg"] = Hex("#FFFFFF");
            resources["ThemeSecButtonHover"] = Hex("#F4F6F2");
            resources["ThemeSecButtonBorder"] = Hex("#D2D8CE");

            resources["ThemeDangerBg"] = Hex("#FEE2E2");
            resources["ThemeDangerFg"] = Hex("#A7000D");
            resources["ThemeDangerBorder"] = Hex("#FCA5A5");
            resources["ThemeDangerHoverBg"] = Hex("#D03238");

            resources["ThemeInputBg"] = Hex("#F4F6F2");
            resources["ThemeInputBorder"] = Hex("#D2D8CE");
            resources["ThemeInputText"] = Hex("#0E0F0C");

            resources["ThemeDataGridRowAlt"] = Hex("#FAFBF9");
            resources["ThemeDataGridHeaderBg"] = Hex("#F4F6F2");
            resources["ThemeDataGridSelected"] = Hex("#E2F6D5");
            resources["ThemeDataGridSelectedFg"] = Hex("#0E0F0C");

            resources["ThemeStatApprovedBg"] = Hex("#F2FBEF");
            resources["ThemeStatApprovedBorder"] = Hex("#C5EDAB");
            resources["ThemeStatApprovedFg"] = Hex("#054D28");

            resources["ThemeStatDeclinedBg"] = Hex("#FEF3C7");
            resources["ThemeStatDeclinedBorder"] = Hex("#FDE68A");
            resources["ThemeStatDeclinedFg"] = Hex("#B86700");

            resources["ThemeStatErrorsBg"] = Hex("#FEE2E2");
            resources["ThemeStatErrorsBorder"] = Hex("#FCA5A5");
            resources["ThemeStatErrorsFg"] = Hex("#A7000D");

            resources["ThemeStatProcessingFg"] = Hex("#0369A1");
        }
    }
}

