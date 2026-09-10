using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using HarmonicaScript.App.Views;
using HarmonicaScript.App.ViewModels;

namespace HarmonicaScript.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = _model;

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DragDrop.SetAllowDrop(this, true);

        if (Program.OpenPath is { } path && System.IO.File.Exists(path))
        {
            LoadFile(path);
        }
    }

    // Avalonia 11.3 replaced IDataObject with IDataTransfer; the old members are [Obsolete]
    // and this project treats warnings as errors, so the new API is the only one available.
    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.TryGetFile() is { } file && file.TryGetLocalPath() is { } path)
        {
            LoadFile(path);
        }
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open MIDI",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("MIDI") { Patterns = ["*.mid", "*.midi", "*.kar", "*.rmi"] },
                ],
            });

            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                LoadFile(path);
            }
        }
        catch (Exception ex)
        {
            _model.Status = ex.Message;
        }
    }

    private void OnReconvert(object? sender, RoutedEventArgs e)
    {
        _model.Reconvert();
        RefreshRehearsal();
    }

    private void LoadFile(string path)
    {
        _model.Load(path);
        RefreshRehearsal();
    }

    /// <summary>
    /// The rehearsal view is driven by the SAME InstrumentSimulator the round-trip test asserts
    /// against, so it is a rendering of a verified invariant rather than a second implementation
    /// that can silently drift from the engine.
    /// </summary>
    private void RefreshRehearsal()
    {
        if (_model.Outcome is null)
        {
            return;
        }

        var host = this.FindControl<ContentControl>("RehearsalHost");
        if (host is null)
        {
            return;
        }

        host.Content = new RehearsalView(
            _model.Outcome.Profiles.Instrument,
            Project.ConversionService.Simulate(_model.Outcome));
    }
}
