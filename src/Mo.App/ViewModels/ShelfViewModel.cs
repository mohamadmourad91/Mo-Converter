using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Mo.Engine;

namespace Mo.App.ViewModels;

/// <summary>One file on the shelf: its name is shown, its full path is what gets dragged out.</summary>
public sealed record ShelfItem(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\'));
    public bool Exists => File.Exists(Path) || Directory.Exists(Path);
}

/// <summary>
/// The shelf: a holding area for results and files. Files are added by dropping them on the shelf or by
/// finished operations; they can be dragged out again. Missing files are dropped from the list.
/// </summary>
public sealed class ShelfViewModel : ObservableObject
{
    /// <summary>The shelf keeps at most this many entries; the oldest leave first.</summary>
    public const int Capacity = 40;

    public ShelfViewModel()
    {
        RemoveCommand = new ParameterCommand<ShelfItem>(item => Items.Remove(item));
        ClearCommand = new RelayCommand(() => Items.Clear(), () => Items.Count > 0);
        Items.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(CountText)); };
    }

    public ObservableCollection<ShelfItem> Items { get; } = [];

    public ICommand RemoveCommand { get; }
    public ICommand ClearCommand { get; }

    public bool IsEmpty => Items.Count == 0;

    public string CountText => Localizer.Current.N("shelf.count", Items.Count);

    /// <summary>Adds paths that exist; a file already on the shelf moves to the top instead of being listed twice.</summary>
    public int Add(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths.Where(p => File.Exists(p) || Directory.Exists(p)))
        {
            var existing = Items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) Items.Remove(existing);
            Items.Insert(0, new ShelfItem(path));
            added++;
        }
        while (Items.Count > Capacity) Items.RemoveAt(Items.Count - 1);
        return added;
    }

    /// <summary>Removes entries whose files were moved or deleted; returns how many were removed.</summary>
    public int Prune()
    {
        var gone = Items.Where(i => !i.Exists).ToList();
        foreach (var item in gone) Items.Remove(item);
        return gone.Count;
    }
}
