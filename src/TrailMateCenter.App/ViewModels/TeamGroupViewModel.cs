using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace TrailMateCenter.ViewModels;

public sealed partial class TeamGroupViewModel : ObservableObject
{
    public TeamGroupViewModel(string name, IEnumerable<SubjectViewModel> members, SubjectViewModel? selected)
    {
        Name = name;
        Members = new ObservableCollection<SubjectViewModel>(members);
        SelectedSubject = selected;
        Count = Members.Count;
        Members.CollectionChanged += OnMembersChanged;
    }

    public string Name { get; }
    public ObservableCollection<SubjectViewModel> Members { get; }

    [ObservableProperty]
    private SubjectViewModel? _selectedSubject;

    [ObservableProperty]
    private int _count;

    public bool HasMembers => Count > 0;

    private void OnMembersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Count = Members.Count;
        OnPropertyChanged(nameof(HasMembers));
    }
}
