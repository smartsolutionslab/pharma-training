using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// PostgreSQL ended the save as a deadlock or a serialization failure with another save at the same moment, or the
/// request waited for a row another request held past the lock timeout (#375): an invitation recipient's row, which
/// another pharmacy's request may hold. Nothing was saved, and what the caller loaded may still be current, so the same
/// request may simply succeed when tried again; a save whose row version moved on is told that instead, to load again
/// first. The message names no pharmacy, since the other request need not be one of this pharmacy's.
/// </summary>
public sealed record OrganisationConcurrentSave()
    : ApiError("organisation.concurrent-save", "Another request was being saved at the same moment; nothing was saved. Try again.", HttpStatusCode.Conflict);
