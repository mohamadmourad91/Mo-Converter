// Several tests switch Localizer.Current (a process-wide setting); running them one after another keeps them independent.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
