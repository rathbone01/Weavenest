using MudBlazor;

namespace Weavenest.Configuration;

public static class WeavenestTheme
{
    public static MudTheme Theme => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#7B2FBE",
            PrimaryDarken = "#5C1F96",
            PrimaryLighten = "#9B59D0",
            Secondary = "#9C27B0",
            Tertiary = "#CE93D8",
            AppbarBackground = "#1A1A2E",
            DrawerBackground = "#1e1e2b",
            Surface = "#1E1E2E",
            Background = "#121212",
            TextPrimary = "#E0E0E0",
            TextSecondary = "#B0B0B0",
            ActionDefault = "#9B59D0",
        }
    };
}
