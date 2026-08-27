namespace HebrewBooks.Diagnostics.Checks;

public sealed record IndexContentProbe(string Corpus, string IdBase, string IndexPath, long IndexedDocs, int ProbeHits, string? Error = null);
