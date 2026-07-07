var cardPhotosDirectory = Path.Combine(Environment.CurrentDirectory, "cardFotos");

if (!Directory.Exists(cardPhotosDirectory))
{
	Console.WriteLine($"Der Ordner '{cardPhotosDirectory}' wurde nicht gefunden.");
	return;
}

var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
	".jpg",
	".jpeg",
	".png",
	".bmp",
	".webp"
};

var cardImages = Directory
	.EnumerateFiles(cardPhotosDirectory)
	.Where(path => supportedExtensions.Contains(Path.GetExtension(path)))
	.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
	.ToList();

if (cardImages.Count == 0)
{
	Console.WriteLine($"Im Ordner '{cardPhotosDirectory}' wurden keine Kartenbilder gefunden.");
	return;
}

Console.WriteLine($"{cardImages.Count} Kartenbilder in '{cardPhotosDirectory}' gefunden:");

for (var index = 0; index < cardImages.Count; index++)
{
	Console.WriteLine($"[{index + 1}] {Path.GetFileName(cardImages[index])}");
}
