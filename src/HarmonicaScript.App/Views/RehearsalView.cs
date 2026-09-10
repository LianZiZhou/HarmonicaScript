using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Timeline;

namespace HarmonicaScript.App.Views;

/// <summary>
/// The rehearsal view: eight jianpu buttons plus three mouse lamps, animated at a virtual playhead.
///
/// Two things make this worth building rather than a nice-to-have. It is driven by the SAME
/// <see cref="InstrumentSimulator"/> output that the round-trip test asserts against, so it is a
/// RENDERING OF A VERIFIED INVARIANT rather than a second implementation that can drift. And no
/// piano roll can show modifier state - the one thing a player of this instrument most needs to
/// see - so this is the only view that shows what your hands are actually doing.
///
/// If Branch A ever proves non-viable, this alone is a complete, zero-injection product.
/// </summary>
public sealed class RehearsalView : UserControl
{
    private readonly InstrumentProfile _profile;
    private readonly IReadOnlyList<SimulatedNote> _notes;
    private readonly List<Border> _degreeButtons = [];
    private readonly List<Border> _modifierLamps = [];
    private readonly TextBlock _clock = new() { FontFamily = FontFamily.Parse("monospace"), FontSize = 13 };
    private readonly Slider _playhead = new() { Minimum = 0, Width = 520 };
    private readonly DispatcherTimer _timer;

    private DateTime _startedAt = DateTime.MinValue;
    private bool _running;

    public RehearsalView(InstrumentProfile profile, SimulationResult simulation)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(simulation);

        _profile = profile;
        _notes = simulation.Notes;
        _playhead.Maximum = _notes.Count == 0 ? 1 : _notes[^1].UpMs;

        Content = BuildLayout();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        _playhead.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty && !_running)
            {
                Highlight((int)_playhead.Value);
            }
        };
    }

    private Control BuildLayout()
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var degree in _profile.Degrees)
        {
            var button = new Border
            {
                Width = 66,
                Height = 84,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.Parse("#20808080")),
                BorderBrush = new SolidColorBrush(Color.Parse("#40808080")),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = degree.Jianpu,
                            FontSize = 26,
                            FontWeight = FontWeight.SemiBold,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = KeyLabel(degree),
                            FontSize = 11,
                            Opacity = 0.55,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                    },
                },
            };

            _degreeButtons.Add(button);
            keys.Children.Add(button);
        }

        var lamps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var modifier in _profile.Modifiers)
        {
            var lamp = new Border
            {
                Width = 118,
                Height = 40,
                CornerRadius = new CornerRadius(20),
                Background = new SolidColorBrush(Color.Parse("#18808080")),
                Child = new TextBlock
                {
                    Text = $"{modifier.DisplayName.ZhHans}  {modifier.Binding.AsMouseButton}",
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            _modifierLamps.Add(lamp);
            lamps.Children.Add(lamp);
        }

        var play = new Button { Content = "▶", Width = 44 };
        play.Click += (_, _) =>
        {
            _running = !_running;
            _startedAt = DateTime.UtcNow - TimeSpan.FromMilliseconds(_playhead.Value);
            play.Content = _running ? "■" : "▶";
        };

        return new StackPanel
        {
            Spacing = 18,
            Margin = new Thickness(10),
            Children =
            {
                keys,
                lamps,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { play, _playhead, _clock },
                },
                new TextBlock
                {
                    Text = $"{_notes.Count} notes · simulated from the finished timeline, not from the source MIDI",
                    FontSize = 11,
                    Opacity = 0.55,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            },
        };
    }

    private string KeyLabel(DegreeSpec degree) => degree.Binding.Code switch
    {
        29 => "Z", 27 => "X", 6 => "C", 25 => "V",
        5 => "B", 17 => "N", 16 => "M", 54 => ",",
        _ => $"hid{degree.Binding.Code}",
    };

    private void Tick()
    {
        if (!_running)
        {
            return;
        }

        var atMs = (int)(DateTime.UtcNow - _startedAt).TotalMilliseconds;
        if (atMs > _playhead.Maximum)
        {
            _running = false;
            atMs = 0;
        }

        _playhead.Value = atMs;
        Highlight(atMs);
    }

    /// <summary>Lights whatever the simulator says is sounding at this instant.</summary>
    private void Highlight(int atMs)
    {
        _clock.Text = TimeSpan.FromMilliseconds(atMs).ToString(@"m\:ss\.fff");

        var sounding = _notes.FirstOrDefault(n => n.DownMs <= atMs && atMs < n.UpMs);
        var active = sounding.UpMs > 0 ? sounding.ActiveMask : (ushort)0;
        var degree = sounding.UpMs > 0 ? sounding.DegreeIndex : -1;

        for (var i = 0; i < _degreeButtons.Count; i++)
        {
            _degreeButtons[i].Background = new SolidColorBrush(
                Color.Parse(i == degree ? "#CC4A9EFF" : "#20808080"));
        }

        for (var i = 0; i < _modifierLamps.Count; i++)
        {
            var lit = (active & (1 << i)) != 0;

            // 降调 is left mouse, which is FIRE in this game. It gets a warning colour rather
            // than the same neutral blue as the others, so a player can see it at a glance.
            var colour = !lit ? "#18808080" : _profile.Modifiers[i].DutyWeightMilli > 80 ? "#CCC05050" : "#CC4A9EFF";
            _modifierLamps[i].Background = new SolidColorBrush(Color.Parse(colour));
        }
    }
}
