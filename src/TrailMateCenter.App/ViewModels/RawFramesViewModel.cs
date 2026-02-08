using System.Collections.ObjectModel;
using Avalonia.Threading;
using TrailMateCenter.Models;
using TrailMateCenter.Storage;

namespace TrailMateCenter.ViewModels;

public sealed class RawFramesViewModel
{
    private readonly SessionStore _sessionStore;

    public RawFramesViewModel(SessionStore sessionStore)
    {
        _sessionStore = sessionStore;
        foreach (var frame in _sessionStore.SnapshotRawFrames().OrderByDescending(f => f.Timestamp))
            Frames.Add(new RawFrameViewModel(frame));
        _sessionStore.RawFrameAdded += OnRawFrameAdded;
    }

    public ObservableCollection<RawFrameViewModel> Frames { get; } = new();

    private void OnRawFrameAdded(object? sender, RawFrameRecord frame)
    {
        Dispatcher.UIThread.Post(() => Frames.Insert(0, new RawFrameViewModel(frame)));
    }
}
