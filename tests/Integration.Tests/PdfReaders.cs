namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The tests that take every PDF reader of the editorial service (#316). They run alone, after every parallel test, so
/// no other test records a PDF while they hold the readers; each of them frees the readers again before it ends.
/// They run after the parallel editorial classes too (<see cref="EditorialChapterFilesTests"/>,
/// <see cref="EditorialTestDraftTests"/>), which record PDFs of their own.
/// </summary>
[CollectionDefinition(nameof(PdfReaders), DisableParallelization = true)]
public sealed class PdfReaders;
