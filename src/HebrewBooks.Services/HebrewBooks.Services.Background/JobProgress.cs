namespace HebrewBooks.Services.Background;

public sealed record JobProgress(BackgroundProcessorService.Job Job, double Percent);
