using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace VideoSynthesizer;

public partial class MainWindow : Window
{
    private SynthParameters parameters = new();
    private readonly SynthEngine engine = new(480, 270);
    private readonly WriteableBitmap bitmap;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.0 / 30) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private bool paused, rendering, resetPending, closed;
    private double previousTime;
    private float synthTime;
    private readonly MicrophoneCapture microphone = new();

    public MainWindow()
    {
        InitializeComponent();
        bitmap = new WriteableBitmap(engine.Width, engine.Height, 96, 96, PixelFormats.Bgra32, null);
        Preview.Source = bitmap;
        BuildControls();
        timer.Tick += RenderFrame;
        Loaded += (_, _) => timer.Start();
        Closed += (_, _) => { closed = true; timer.Stop(); microphone.Dispose(); };
        Deactivated += (_, _) => StopMicrophone();
    }

    private void BuildControls()
    {
        Controls.Children.Clear();
        AddSlider("Oscillator frequency", 0, 30, parameters.OscFreq, v => parameters = parameters with { OscFreq = v });
        AddSlider("Oscillator speed", 0, 5, parameters.OscSpeed, v => parameters = parameters with { OscSpeed = v });
        AddSlider("Fractal zoom", 0.1, 4, parameters.FractalZoom, v => parameters = parameters with { FractalZoom = v });
        AddSlider("Fractal detail", 1, 30, parameters.FractalDetail, v => parameters = parameters with { FractalDetail = v });
        AddSlider("Fractal morph", -1, 1, parameters.FractalMorph, v => parameters = parameters with { FractalMorph = v });
        AddSlider("Cell feed", 0, 0.1, parameters.CaFeed, v => parameters = parameters with { CaFeed = v });
        AddSlider("Cell kill", 0, 0.1, parameters.CaKill, v => parameters = parameters with { CaKill = v });
        AddSlider("Feedback mix", 0, 1, parameters.FeedbackMix, v => parameters = parameters with { FeedbackMix = v });
        AddSlider("Color shift", 0, 1, parameters.ColorShift, v => parameters = parameters with { ColorShift = v });
        AddSlider("Mid → oscillator", 0, 1, parameters.AudioToOsc, v => parameters = parameters with { AudioToOsc = v });
        AddSlider("Bass → fractal", 0, 1, parameters.AudioToFractal, v => parameters = parameters with { AudioToFractal = v });
        AddSlider("High → cell feed", 0, 1, parameters.AudioToCA, v => parameters = parameters with { AudioToCA = v });
    }

    private void AddSlider(string name, double min, double max, float value, Action<float> update)
    {
        var label = new TextBlock { Text = $"{name}  {value:0.000}", Margin = new Thickness(0, 8, 0, 4) };
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Margin = new Thickness(0, 0, 0, 12) };
        System.Windows.Automation.AutomationProperties.SetName(slider, name);
        slider.ValueChanged += (_, e) => { update((float)e.NewValue); label.Text = $"{name}  {e.NewValue:0.000}"; };
        Controls.Children.Add(label);
        Controls.Children.Add(slider);
    }

    private async void RenderFrame(object? sender, EventArgs e)
    {
        if (rendering || closed) return;
        var bands = microphone.Bands;
        AudioStatus.Text = microphone.Error ?? (microphone.IsRunning
            ? $"Microphone live · Bass {bands.Bass:0.00} · Mid {bands.Mid:0.00} · High {bands.High:0.00}" : "Microphone off");
        if (!microphone.IsRunning) MicrophoneButton.Content = "Enable microphone";
        double now = clock.Elapsed.TotalSeconds;
        double delta = Math.Min(now - previousTime, 0.1);
        previousTime = now;
        if (paused && !resetPending) return;
        rendering = true;
        if (resetPending) { engine.Reset(); synthTime = 0; resetPending = false; }
        else synthTime += (float)delta;
        var snapshot = parameters;
        float time = synthTime;
        var frameClock = Stopwatch.StartNew();
        try
        {
            await Task.Run(() => engine.Render(time, snapshot, bands));
            if (closed) return;
            bitmap.WritePixels(new Int32Rect(0, 0, engine.Width, engine.Height), engine.Pixels, engine.Width * 4, 0);
            Status.Text = $"Windows preview · {engine.Width} × {engine.Height} · {frameClock.Elapsed.TotalMilliseconds:0} ms/frame · { (paused ? "Paused" : "Live") }";
        }
        catch (Exception error) { timer.Stop(); ShowError(error); }
        finally { rendering = false; }
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        paused = !paused;
        PauseButton.Content = paused ? "Resume" : "Pause";
        Status.Text = paused ? "Paused" : "Live";
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => resetPending = true;

    private void StopMicrophone()
    {
        microphone.Stop();
        MicrophoneButton.Content = "Enable microphone";
        AudioStatus.Text = "Microphone off";
    }

    private void Microphone_Click(object sender, RoutedEventArgs e)
    {
        if (microphone.IsRunning) { StopMicrophone(); return; }
        try { microphone.Start(); MicrophoneButton.Content = "Disable microphone"; }
        catch (Exception error) { AudioStatus.Text = $"Microphone unavailable: {error.Message}"; }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Synth preset (*.json)|*.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { parameters = SynthParameters.FromJson(File.ReadAllText(dialog.FileName)); BuildControls(); resetPending = true; }
        catch (Exception error) { ShowError(error); }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Synth preset (*.json)|*.json", FileName = "synth-preset.json" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, parameters.ToJson()); }
        catch (Exception error) { ShowError(error); }
    }

    private void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = "synth-output.png" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(dialog.FileName);
            encoder.Save(output);
        }
        catch (Exception error) { ShowError(error); }
    }

    private void ShowError(Exception error) => MessageBox.Show(this, error.Message, "Video Synthesizer", MessageBoxButton.OK, MessageBoxImage.Error);
}
