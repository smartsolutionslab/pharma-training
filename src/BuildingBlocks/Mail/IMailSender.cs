namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>Sends a plain-text mail to one address.</summary>
public interface IMailSender
{
    Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken);
}
