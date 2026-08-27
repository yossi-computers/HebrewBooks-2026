namespace HebrewBooks.Infrastructure.Settings;

public sealed class WindowPlacementOptions
{
	public bool Saved { get; set; }

	public bool Maximized { get; set; } = true;

	public double Left { get; set; }

	public double Top { get; set; }

	public double Width { get; set; }

	public double Height { get; set; }
}
