using OpenCvSharp;

const int maxCameraProbeCount = 10;

var availableCameras = DiscoverAvailableCameras(maxCameraProbeCount);

if (availableCameras.Count == 0)
{
	Console.WriteLine("Keine verfuegbaren Kameras gefunden.");
	return;
}

Console.WriteLine("Verfuegbare Kameras:");

foreach (var camera in availableCameras)
{
	Console.WriteLine($"[{camera.Index}] Kamera {camera.Index} ({camera.Width}x{camera.Height})");
}

var selectedCameraIndex = PromptForCameraSelection(availableCameras);

using var capture = new VideoCapture(selectedCameraIndex, VideoCaptureAPIs.DSHOW);

if (!capture.IsOpened())
{
	Console.WriteLine($"Kamera {selectedCameraIndex} konnte nicht geoeffnet werden.");
	return;
}

const string windowName = "NinjagoScanner Vorschau";
using var frame = new Mat();

Console.WriteLine("Vorschau gestartet. Druecke q oder ESC im Vorschaufenster zum Beenden.");

while (true)
{
	if (!capture.Read(frame) || frame.Empty())
	{
		Console.WriteLine("Konnte kein Bild von der Kamera lesen.");
		break;
	}

	Cv2.ImShow(windowName, frame);

	var key = Cv2.WaitKey(30);
	if (key == 27 || key == 'q' || key == 'Q')
	{
		break;
	}
}

Cv2.DestroyAllWindows();

return;

static List<CameraOption> DiscoverAvailableCameras(int maxProbeCount)
{
	var cameras = new List<CameraOption>();

	for (var index = 0; index < maxProbeCount; index++)
	{
		using var capture = new VideoCapture(index, VideoCaptureAPIs.DSHOW);
		if (!capture.IsOpened())
		{
			continue;
		}

		var width = Math.Max((int)capture.FrameWidth, 0);
		var height = Math.Max((int)capture.FrameHeight, 0);
		cameras.Add(new CameraOption(index, width, height));
	}

	return cameras;
}

static int PromptForCameraSelection(IReadOnlyList<CameraOption> cameras)
{
	while (true)
	{
		Console.Write("Bitte Kameranummer waehlen: ");
		var input = Console.ReadLine();

		if (int.TryParse(input, out var selectedIndex) && cameras.Any(camera => camera.Index == selectedIndex))
		{
			return selectedIndex;
		}

		Console.WriteLine("Ungueltige Auswahl. Bitte eine der angezeigten Kameranummern eingeben.");
	}
}

internal sealed record CameraOption(int Index, int Width, int Height);
