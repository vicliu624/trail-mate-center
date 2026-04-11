using TrailMateCenter.Localization;
using TrailMateCenter.Services;

namespace TrailMateCenter.ViewModels;

internal static class PropagationLandcoverPresentation
{
    public static string ResolveLabel(PropagationLandcoverClass landcoverClass)
    {
        return LocalizationService.Instance.GetString(landcoverClass switch
        {
            PropagationLandcoverClass.SparseForest => "Ui.Propagation.Landcover.SparseForest",
            PropagationLandcoverClass.DenseForest => "Ui.Propagation.Landcover.DenseForest",
            PropagationLandcoverClass.Water => "Ui.Propagation.Landcover.Water",
            _ => "Ui.Propagation.Landcover.BareGround",
        });
    }

    public static string ResolveAccentColorHex(PropagationLandcoverClass landcoverClass)
    {
        return landcoverClass switch
        {
            PropagationLandcoverClass.SparseForest => "#80D07A",
            PropagationLandcoverClass.DenseForest => "#2F9E44",
            PropagationLandcoverClass.Water => "#4AA3FF",
            _ => "#C3AE7A",
        };
    }

    public static int ResolveSortOrder(PropagationLandcoverClass landcoverClass)
    {
        return landcoverClass switch
        {
            PropagationLandcoverClass.DenseForest => 0,
            PropagationLandcoverClass.SparseForest => 1,
            PropagationLandcoverClass.Water => 2,
            _ => 3,
        };
    }
}
