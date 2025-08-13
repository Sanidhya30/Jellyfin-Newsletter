using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Newsletters.ItemEventNotifier;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Newsletters.ScheduledTasks
{
    /// <summary>
    /// Class ItemEventTask.
    /// </summary>
    public class ItemEventTask : IScheduledTask, IConfigurableScheduledTask
    {
        private const int RecheckIntervalSec = 30;
        private readonly ItemEventManager itemManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="ItemEventTask"/> class.
        /// </summary>
        /// <param name="itemAddedManager">The item event manager used to process added items.</param>
        public ItemEventTask(ItemEventManager itemAddedManager)
        {
            itemManager = itemAddedManager;
        }

        /// <inheritdoc />
        public string Name => "Newsletter Item Scraper";

        /// <inheritdoc />
        public string Description => "Gather info on recently added media and store it for Newsletters";

        /// <inheritdoc />
        public string Category => "Newsletters";

        /// <inheritdoc />
        public string Key => "ScanNewsletters";

        /// <inheritdoc />
        public bool IsHidden => true;

        /// <inheritdoc />
        public bool IsEnabled => true;

        /// <inheritdoc />
        public bool IsLogged => true;

        /// <summary>
        /// Creates the triggers that define when the task will run.
        /// </summary>
        /// <returns>IEnumerable{BaseTaskTrigger}.</returns>
        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromSeconds(RecheckIntervalSec).Ticks
                }
            };
        }

        /// <inheritdoc />
        public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            return itemManager.ProcessItemsAsync();
        }
    }
}