using System;

namespace HebrewBooks.Services.Background;

public sealed record JobCompletion(BackgroundProcessorService.Job Job, Exception? Error, bool Cancelled);
