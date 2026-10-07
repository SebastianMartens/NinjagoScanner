using NinjagoScanner.Web.Data;

namespace NinjagoScanner.Web.Services;

/// <summary>German display texts for trade outcomes and balance.</summary>
public static class TradeLabels
{
    public static string Outcome(TradeStatus status) => status switch
    {
        TradeStatus.Pending => "Offen",
        TradeStatus.Executing => "Wird ausgeführt",
        TradeStatus.Completed => "Abgeschlossen",
        TradeStatus.Declined => "Abgelehnt",
        TradeStatus.Cancelled => "Zurückgezogen",
        TradeStatus.Failed => "Fehlgeschlagen",
        _ => status.ToString()
    };

    public static string BalanceText(TradeBalance balance)
    {
        if (balance.GiveCount == 0)
        {
            return "Noch keine Karten ausgewählt";
        }

        return balance.UnausgewogenCount == 0
            ? "Ausgewogen"
            : $"Unausgewogen ({balance.UnausgewogenCount} von {balance.GiveCount} Paaren)";
    }

    public static string WeightText(int weightDifference) => weightDifference switch
    {
        0 => "Gleicher Wert",
        > 0 => $"Du gibst {weightDifference} Wert mehr",
        _ => $"Du erhältst {-weightDifference} Wert mehr"
    };
}
