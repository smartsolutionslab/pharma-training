namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

/// <summary>
/// What an <see cref="AnnouncementSweepPass"/> does with one stored event it found without a marker: announce it with
/// its marker, settle it with its marker alone (its announcement belongs to another stored event), or skip it because
/// it cannot be read or mapped. The pass handles each case with a switch without a default, so a new case fails the
/// build until it is handled.
/// </summary>
public union SweptEvent(AnnounceSweptEvent, MarkSweptEventOnly, UnreadableSweptEvent);
