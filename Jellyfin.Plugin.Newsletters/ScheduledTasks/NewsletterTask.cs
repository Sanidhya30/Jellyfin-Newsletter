using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.Clients;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Newsletters.ScheduledTasks
{
    /// <summary>
    /// Class RefreshMediaLibraryTask.
    /// </summary>
    public class NewsletterTask(IEnumerable<IClient> clientsInstance, Logger loggerInstance) : IScheduledTask
    {
        private readonly IEnumerable<IClient> clients = clientsInstance;
        private readonly Logger logger = loggerInstance;

        /// <inheritdoc />
        public string Name => "Newsletter";

        /// <inheritdoc />
        public string Description => "Send Newsletters to all the specified hooks in plugin";

        /// <inheritdoc />
        public string Category => "Newsletters";

        /// <inheritdoc />
        public string Key => "Newsletters";

        /// <summary>
        /// Creates the triggers that define when the task will run.
        /// </summary>
        /// <returns>IEnumerable{BaseTaskTrigger}.</returns>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            yield return new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(168).Ticks
            };
        }

        /// <inheritdoc />
        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(0);
            
            // Call the Notify/Send for each client
            NotifyAll();

            progress.Report(100);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Sends newsletters using all configured clients and archives the data if at least one send is successful.
        /// </summary>
        public void NotifyAll()
        {
            bool result = false;
            foreach (var client in clients)
            {
                logger.Debug($"Send triggered for the {client}");
                result |= client.Send();
            }

            // If we the result is True i.e. even if any one client was successful
            // to send the newsletter we'll move the current database
            if (result)
            {
                logger.Debug("Atleast one of the client sent the newsletter. Proceeding forward...");
                clients.First().CopyNewsletterDataToArchive();
            }
            else
            {
                // There could be a case when there is no newsletter to be send. So marking this as Info rather an Error
                // for now.
                logger.Info("None of the client were able to send the newsletter. Please check the plugin configuration.");
            }
        }
    }
}