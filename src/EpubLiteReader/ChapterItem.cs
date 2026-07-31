using System.ComponentModel;

namespace EpubLiteReader;

/// <summary>One node in the chapter tree, mapped from an EPUB navigation item.</summary>
public sealed class ChapterItem : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isSelected;

    public string Title { get; init; } = "";

    /// <summary>0-based spine index, or null when this node has no navigable destination.</summary>
    public int? SpineIndex { get; init; }

    /// <summary>Fragment/anchor within the spine HTML, if any.</summary>
    public string? Anchor { get; init; }

    public List<ChapterItem> Children { get; } = new();

    public ChapterItem? Parent { get; set; }

    public int Depth { get; init; }

    public int SourceOrder { get; init; }

    public bool IsNavigable => SpineIndex.HasValue;

    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnChanged(nameof(IsExpanded)); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnChanged(nameof(IsSelected)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Title;

    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
