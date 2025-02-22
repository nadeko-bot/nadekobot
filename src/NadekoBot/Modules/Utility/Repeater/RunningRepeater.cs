#nullable disable
using NadekoBot.Db.Models;

namespace NadekoBot.Modules.Utility.Services;

public sealed class RunningRepeater
{
    public DateTime NextTime { get; private set; }

    public Repeater Repeater { get; }
    public int ErrorCount { get; set; }

    public RunningRepeater(Repeater repeater)
    {
        Repeater = repeater;
        NextTime = CalculateInitialExecution();
    }

    public void UpdateNextTime()
        => NextTime = DateTime.UtcNow + Repeater.Interval;

    private DateTime CalculateInitialExecution()
    {
        if (Repeater.StartTimeOfDay is not null)
        {
            // if there was a start time of day
            // calculate whats the next time of day repeat should trigger at
            // based on teh dateadded

            // i know this is not null because of the check in the query
            var added = Repeater.DateAdded;

            // initial trigger was the time of day specified by the command.
            var initialTriggerTimeOfDay = Repeater.StartTimeOfDay.Value;

            DateTime initialDateTime;

            // if added timeofday is less than specified timeofday for initial trigger
            // that means the repeater first ran that same day at that exact specified time
            if (added.TimeOfDay <= initialTriggerTimeOfDay)
                // in that case, just add the difference to make sure the timeofday is the same
                initialDateTime = added + (initialTriggerTimeOfDay - added.TimeOfDay);
            else
                // if not, then it ran at that time the following day
                // in other words; Add one day, and subtract how much time passed since that time of day
                initialDateTime = added + TimeSpan.FromDays(1) - (added.TimeOfDay - initialTriggerTimeOfDay);

            return CalculateInitialInterval(initialDateTime);
        }

        // if repeater is not running daily, its initial time is the time it was Added at, plus the interval
        return CalculateInitialInterval(Repeater.DateAdded + Repeater.Interval);
    }

    /// <summary>
    ///     Calculate when is the proper time to run the repeater again based on initial time repeater ran.
    /// </summary>
    /// <param name="initialDateTime">Initial time repeater ran at (or should run at).</param>
    private DateTime CalculateInitialInterval(DateTime initialDateTime)
    {
        // if the initial time is greater than now, that means the repeater didn't still execute a single time.
        // just schedule it
        if (initialDateTime > DateTime.UtcNow)
            return initialDateTime;

        // else calculate based on minutes difference

        // get the difference
        var diff = DateTime.UtcNow - initialDateTime;

        // see how many times the repeater theoretically ran already
        var triggerCount = diff / Repeater.Interval;

        // ok lets say repeater was scheduled to run 10h ago.
        // we have an interval of 2.4h
        // repeater should've ran 4 times- that's 9.6h
        // next time should be in 2h from now exactly
        // 10/2.4 is 4.166
        // 4.166 - Math.Truncate(4.166) is 0.166
        // initial interval multiplier is 1 - 0.166 = 0.834
        // interval (2.4h) * 0.834 is 2.0016 and that is the initial interval

        var initialIntervalMultiplier = 1 - (triggerCount - Math.Truncate(triggerCount));
        return DateTime.UtcNow + (Repeater.Interval * initialIntervalMultiplier);
    }

    public override bool Equals(object obj)
        => obj is RunningRepeater rr && rr.Repeater.Id == Repeater.Id;

    public override int GetHashCode()
        => Repeater.Id;
}