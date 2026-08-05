using Grpc.Net.Client;
using NinjagoScanner.CatalogService.Protos;
using Google.Protobuf.WellKnownTypes;

var channel = GrpcChannel.ForAddress("http://localhost:5073");
var client = new CardCatalog.CardCatalogClient(channel);

var allCards = await client.ListAllCardsAsync(new Empty());
var cards = allCards.Cards.ToList();

var contains9 = cards
    .Where(c => c.SeriesName.Contains("Serie 9", StringComparison.OrdinalIgnoreCase))
    .OrderBy(c => c.SeriesName)
    .ThenBy(c => c.Category)
    .ThenBy(c => c.CardNumber)
    .ToList();

var exactSerie9 = cards
    .Where(c => string.Equals(c.SeriesName, "Serie 9", StringComparison.OrdinalIgnoreCase))
    .OrderBy(c => c.Category)
    .ThenBy(c => c.CardNumber)
    .ToList();

Console.WriteLine("COUNTS_BY_SERIES");
foreach (var grp in contains9.GroupBy(c => c.SeriesName).OrderBy(g => g.Key))
{
    Console.WriteLine($"{grp.Key}\t{grp.Count()}");
}

Console.WriteLine($"EXACT_SERIE_9_COUNT={exactSerie9.Count}");

var outPath = Path.Combine(Directory.GetCurrentDirectory(), "serie9_cards.tsv");
File.WriteAllLines(outPath, contains9.Select(c => $"{c.SeriesName}\t{c.Category}\t{c.CardNumber}\t{c.CardName}"));
Console.WriteLine($"OUT_FILE={outPath}");
