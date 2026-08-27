namespace HebrewBooks.Diagnostics.Checks;

public sealed record EngineProbeResult(bool Loaded, string? Detail, string? Error);
