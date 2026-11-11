using Rebus.Bus;

namespace Acheve.Application.ProcessManager.Handlers
{
    // Deletes data bus attachments the case no longer needs.
    public interface ICaseAttachments
    {
        Task Delete(string ticket);
    }

    public sealed class CaseAttachments(IBus bus, ILogger<CaseAttachments> logger) : ICaseAttachments
    {
        public async Task Delete(string ticket)
        {
            try
            {
                await bus.Advanced.DataBus.Delete(ticket);
            }
            catch (Exception e)
            {
                // Not worth failing the message: the sweeper deletes what is left behind.
                logger.LogWarning(e, "Could not delete the attachment {ticket}.", ticket);
            }
        }
    }
}
