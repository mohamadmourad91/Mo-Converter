namespace Mo.App.Shell.Logic;

/// <summary>One position on the wheel: either an item or the "more" petal that turns the page.</summary>
public readonly record struct WheelSlot<T>(T? Item, bool IsMore);

/// <summary>
/// Splits a long list of choices into wheel pages. Up to <see cref="MaxPetals"/> items fit on one wheel;
/// beyond that each page shows <c>MaxPetals - 1</c> items plus a "more" petal.
/// </summary>
public static class WheelPager
{
    public const int MaxPetals = 12;

    public static int PageCount(int itemCount, int maxPetals = MaxPetals) =>
        itemCount <= maxPetals ? 1 : (itemCount + maxPetals - 2) / (maxPetals - 1);

    /// <summary>Slots for a page; the page index wraps around so "more" on the last page returns to the first.</summary>
    public static IReadOnlyList<WheelSlot<T>> Page<T>(IReadOnlyList<T> items, int page, int maxPetals = MaxPetals)
    {
        if (maxPetals < 2) throw new ArgumentOutOfRangeException(nameof(maxPetals));
        if (items.Count <= maxPetals) return items.Select(i => new WheelSlot<T>(i, false)).ToList();

        var pages = PageCount(items.Count, maxPetals);
        var index = ((page % pages) + pages) % pages;
        var perPage = maxPetals - 1;
        var slots = items.Skip(index * perPage).Take(perPage).Select(i => new WheelSlot<T>(i, false)).ToList();
        slots.Add(new WheelSlot<T>(default, true));
        return slots;
    }
}
