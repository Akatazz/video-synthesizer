using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Documents;
using Microsoft.Win32;

namespace VideoSynthesizer;

public partial class MainWindow : Window
{
    private SynthParameters parameters = new();
    private ISynthRenderer engine = new SynthEngine(480, 270);
    private WriteableBitmap bitmap;
    private enum RendererMode { Automatic, Cpu, Warp }
    private RendererMode rendererMode;
    private int renderWidth = 1280, renderHeight = 720;
    private (RendererMode Mode, int Width, int Height)? pendingRenderer;
    private string rendererNote = "";
    private static readonly (string Label, int Width, int Height)[] renderSizes = [
        ("Preview 480 × 270",480,270), ("Matrix 128 × 128",128,128),
        ("Matrix 256 × 256",256,256), ("Matrix 512 × 512",512,512),
        ("Linear 512 × 64",512,64), ("Wide 1024 × 128",1024,128),
        ("HD 1280 × 720",1280,720), ("Full HD 1920 × 1080",1920,1080) ];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1.0 / 30) };
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private bool paused, rendering, resetPending, closed;
    private double previousTime;
    private float synthTime;
    private readonly MicrophoneCapture microphone = new();
    private readonly NdiOutput ndi = new();
    private NdiSettings ndiSettings = new();
    private string ndiName = "LZX-V Synth Output (Windows)";
    private string? ndiRuntimePath;
    private Button? ndiButton;
    private readonly bool graphicsCheck = Environment.GetCommandLineArgs().Contains("--graphics-lifecycle-smoke-test");
    private readonly bool backgroundCheck = Environment.GetCommandLineArgs().Any(arg=>arg is "--background-smoke-test" or "--graphics-lifecycle-smoke-test");
    private byte[]? firstCheckFrame;
    private int checkFrames, graphicsStage, heldTicks;
    private byte[]? heldCheckFrame;

    public MainWindow()
    {
        InitializeComponent();
        bitmap = new WriteableBitmap(engine.Width, engine.Height, 96, 96, PixelFormats.Bgra32, null);
        Preview.Source = bitmap;
        rendererMode = Environment.GetEnvironmentVariable("VIDEO_SYNTH_RENDERER") switch {
            "cpu" => RendererMode.Cpu, "warp" => RendererMode.Warp, _ => RendererMode.Automatic
        };
        QueueRenderer();
        BuildControls();
        timer.Tick += RenderFrame;
        Loaded += (_, _) =>
        {
            if (backgroundCheck)
            {
                Hide();
                string[] args = Environment.GetCommandLineArgs();
                int flag = Array.IndexOf(args,"--background-smoke-test");
                if (flag >= 0 && flag + 1 < args.Length)
                {
                    try
                    {
                        ndiSettings = new NdiSettings { Width = 64, Height = 36 };
                        ndi.Start("LZX-V Background Check",args[flag + 1]);
                    }
                    catch (Exception error) { FailBackgroundCheck(error.Message); return; }
                }
            }
            timer.Start();
        };
        Closed += (_, _) => {
            closed = true; timer.Stop(); microphone.Dispose(); ndi.Dispose();
            // A queued compute/readback owns the device until its await completes.
            if (!rendering) engine.Dispose();
        };
    }

    private void BuildControls()
    {
        Controls.Children.Clear();
        BuildRendererControls();
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
        BuildNdiControls();
    }

    private void BuildRendererControls()
    {
        Controls.Children.Add(new TextBlock { Text="SYNTHESIS",FontSize=18,Margin=new Thickness(0,0,0,8) });
        AddChoice("Rendering",["Automatic GPU","CPU compatibility"],rendererMode==RendererMode.Cpu ? 1 : 0,index => {
            rendererMode = index==0 ? RendererMode.Automatic : RendererMode.Cpu;
            rendererNote = ""; QueueRenderer();
        });
        AddChoice("Synthesis resolution",renderSizes.Select(size=>size.Label).ToArray(),
            Array.FindIndex(renderSizes,size=>size.Width==renderWidth && size.Height==renderHeight),index=> {
                renderWidth=renderSizes[index].Width; renderHeight=renderSizes[index].Height;
                QueueRenderer();
            });
        Controls.Children.Add(new TextBlock { Text="Changing rendering or resolution resets feedback. NDI resizes this synthesis image to its output size.",
            TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12) });
    }

    private void QueueRenderer() => pendingRenderer=(rendererMode,renderWidth,renderHeight);

    private static (ISynthRenderer Renderer, string Note) CreateRenderer(RendererMode mode, int width, int height)
    {
        if (mode==RendererMode.Cpu) return (new SynthEngine(width,height),"");
        try { return (new Direct3DSynthRenderer(width,height,software:mode==RendererMode.Warp),""); }
        catch (Exception error) when (mode==RendererMode.Automatic) {
            return (new SynthEngine(width,height),$"GPU unavailable; using CPU. {error.Message}");
        }
    }

    private void BuildNdiControls()
    {
        Controls.Children.Add(new TextBlock { Text = "NDI OUTPUT", FontSize = 18, Margin = new Thickness(0, 20, 0, 8) });
        var linkText = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
        var link = new Hyperlink(new Run("NDI® technology · ndi.video")) { NavigateUri = new Uri("https://ndi.video") };
        link.RequestNavigate += (_, e) => { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); e.Handled = true; };
        linkText.Inlines.Add(link); Controls.Children.Add(linkText);
        var name = new TextBox { Text = ndiName, Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(name, "NDI source name");
        name.TextChanged += (_, _) => { StopNdi(); ndiName = name.Text; };
        Controls.Children.Add(name);
        var runtime = new Button { Content = "NDI runtime…", Margin = new Thickness(0, 0, 0, 8) };
        runtime.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Choose the installed NDI runtime DLL", Filter = "NDI runtime (*.dll)|*.dll" };
            if (dialog.ShowDialog(this) == true) { StopNdi(); ndiRuntimePath = dialog.FileName; NdiStatus.Text = "NDI runtime selected. Restart the app to change an already loaded runtime."; }
        };
        Controls.Children.Add(runtime);
        (string Label, int W, int H)[] resolutions = [
            ("Matrix 128 × 128",128,128), ("Matrix 256 × 256",256,256), ("Matrix 512 × 512",512,512),
            ("Linear 512 × 64",512,64), ("Wide 1024 × 128",1024,128), ("HD 1280 × 720",1280,720), ("Full HD 1920 × 1080",1920,1080) ];
        AddChoice("NDI resolution", resolutions.Select(p => p.Label).ToArray(), Array.FindIndex(resolutions, p => p.W == ndiSettings.Width && p.H == ndiSettings.Height), index =>
        { StopNdi(); ndiSettings = ndiSettings with { Width = resolutions[index].W, Height = resolutions[index].H }; });
        int[] rates = [15, 25, 30];
        AddChoice("NDI frame rate", rates.Select(rate => $"{rate} fps").ToArray(), Array.IndexOf(rates, ndiSettings.Fps), index =>
        { StopNdi(); ndiSettings = ndiSettings with { Fps = rates[index] }; });
        AddSlider("NDI black cutoff", 0, 0.25, ndiSettings.BlackCutoff, v => ndiSettings = ndiSettings with { BlackCutoff = v });
        AddSlider("NDI gamma", 0.5, 2.5, ndiSettings.Gamma, v => ndiSettings = ndiSettings with { Gamma = v });
        AddChoice("NDI channels", ["RGB", "Luma", "Red", "Green", "Blue"], ndiSettings.Channel, index => ndiSettings = ndiSettings with { Channel = index });
        AddChoice("NDI alpha", ["Opaque", "Luma alpha", "Inverted luma alpha"], ndiSettings.Alpha, index => ndiSettings = ndiSettings with { Alpha = index });
        ndiButton = new Button { Content = ndi.IsRunning ? "Stop NDI output" : "Start NDI output", Margin = new Thickness(0, 8, 0, 8) };
        ndiButton.Click += (_, _) =>
        {
            if (ndi.IsRunning) { StopNdi(); return; }
            try { ndiSettings.Validate(); ndi.Start(ndiName, ndiRuntimePath); ndiButton.Content = "Stop NDI output"; }
            catch (Exception error) { NdiStatus.Text = $"NDI unavailable: {error.Message}"; }
        };
        Controls.Children.Add(ndiButton);
    }

    private void AddChoice(string label, string[] items, int selected, Action<int> update)
    {
        Controls.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
        var choice = new ComboBox { ItemsSource = items, SelectedIndex = Math.Max(0, selected), Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(choice, label);
        choice.SelectionChanged += (_, _) => { if (choice.SelectedIndex >= 0) update(choice.SelectedIndex); };
        Controls.Children.Add(choice);
    }

    private void StopNdi() { ndi.Stop(); if (ndiButton != null) ndiButton.Content = "Start NDI output"; NdiStatus.Text = "NDI off"; }

    private void SubmitNdi()
    {
        ndi.Submit(engine.Pixels, engine.Width, engine.Height, ndiSettings);
        NdiStatus.Text = ndi.Status;
        if (!ndi.IsRunning && ndiButton != null) ndiButton.Content = "Start NDI output";
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
        rendering = true;
        try
        {
            if (pendingRenderer is { } request) {
                pendingRenderer = null;
                var created = await Task.Run(()=>CreateRenderer(request.Mode,request.Width,request.Height));
                if (closed) { created.Renderer.Dispose(); return; }
                engine.Dispose(); engine=created.Renderer;
                if (request.Mode!=RendererMode.Cpu || rendererMode==RendererMode.Cpu) rendererNote=created.Note;
                bitmap = new WriteableBitmap(engine.Width,engine.Height,96,96,PixelFormats.Bgra32,null);
                Preview.Source=bitmap;
                resetPending=true;
                firstCheckFrame=null; checkFrames=0;
            }
            double now = clock.Elapsed.TotalSeconds;
            double delta = Math.Min(now-previousTime,0.1);
            previousTime = now;
            if (paused && !resetPending) {
                SubmitNdi();
                if (graphicsCheck) VerifyGraphicsLifecycle(rendered:false);
                return;
            }
            if (resetPending) { engine.Reset(); synthTime=0; resetPending=false; }
            else synthTime += (float)delta;
            var snapshot=parameters;
            float time=synthTime;
            var frameClock=Stopwatch.StartNew();
            await Task.Run(() => engine.Render(time, snapshot, bands));
            if (closed) return;
            bitmap.WritePixels(new Int32Rect(0, 0, engine.Width, engine.Height), engine.Pixels, engine.Width * 4, 0);
            SubmitNdi();
            if (backgroundCheck) VerifyBackgroundFrame();
            Status.Text = $"{engine.Backend} · {engine.Width} × {engine.Height} · {frameClock.Elapsed.TotalMilliseconds:0.0} ms/frame · {(paused ? "Paused" : "Live")} {rendererNote}";
        }
        catch (Exception error)
        {
            if (backgroundCheck) FailBackgroundCheck(error.Message);
            else if (engine is Direct3DSynthRenderer && rendererMode==RendererMode.Automatic) {
                rendererNote=$"GPU stopped; using CPU. {error.Message}";
                pendingRenderer=(RendererMode.Cpu,renderWidth,renderHeight);
            } else { timer.Stop(); ShowError(error); }
        }
        finally { rendering=false; if (closed) engine.Dispose(); }
    }

    private void VerifyBackgroundFrame()
    {
        if (graphicsCheck) { VerifyGraphicsLifecycle(rendered:true); return; }
        firstCheckFrame ??= engine.Pixels.ToArray();
        checkFrames++;
        bool expectsNdi = Environment.GetCommandLineArgs().Length > Array.IndexOf(Environment.GetCommandLineArgs(),"--background-smoke-test") + 1;
        if (IsVisible) { FailBackgroundCheck("Test window became visible."); return; }
        if (checkFrames < 3) return;
        if (firstCheckFrame.SequenceEqual(engine.Pixels)) { FailBackgroundCheck("Hidden synthesis did not advance."); return; }
        if (expectsNdi && !ndi.Status.Contains("64 × 36"))
        {
            if (!ndi.IsRunning || clock.Elapsed.TotalSeconds > 8) FailBackgroundCheck(ndi.Status);
            return;
        }
        Console.WriteLine($"Hidden WPF synthesis advanced across {checkFrames} frames. {ndi.Status}");
        Application.Current.Shutdown(0);
    }

    private void VerifyGraphicsLifecycle(bool rendered)
    {
        if (IsVisible) throw new InvalidOperationException("Graphics test window became visible.");
        if (graphicsStage is 1 or 3) {
            if (rendered || heldCheckFrame == null || !heldCheckFrame.SequenceEqual(engine.Pixels))
                throw new InvalidOperationException("Paused synthesis did not hold its frame.");
            if (++heldTicks<3) return;
            if (graphicsStage==1) {
                pendingRenderer=(rendererMode,1920,1080);
                graphicsStage=2;
            } else {
                pendingRenderer=(RendererMode.Cpu,480,270);
                paused=false; graphicsStage=4;
            }
            return;
        }
        if (graphicsStage==2) {
            if (!paused || engine is not Direct3DSynthRenderer || engine.Width!=1920 || engine.Height!=1080 ||
                !engine.Pixels.Where((_,index)=>index%4==3).All(value=>value==255))
                throw new InvalidOperationException("Paused full-HD renderer replacement did not produce a complete frame.");
            heldCheckFrame=engine.Pixels.ToArray(); heldTicks=0; graphicsStage=3;
            return;
        }
        firstCheckFrame ??= engine.Pixels.ToArray();
        if (++checkFrames<3) return;
        if (firstCheckFrame.SequenceEqual(engine.Pixels)) throw new InvalidOperationException("Replacement renderer did not advance.");
        if (graphicsStage==0) {
            if (engine is not Direct3DSynthRenderer) throw new InvalidOperationException("The graphics test requires Direct3D; use VIDEO_SYNTH_RENDERER=warp on CI.");
            paused=true; heldTicks=0; heldCheckFrame=engine.Pixels.ToArray(); graphicsStage=1;
        } else {
            if (engine is not SynthEngine || engine.Width!=480 || engine.Height!=270)
                throw new InvalidOperationException("CPU compatibility renderer did not replace the GPU renderer.");
            Console.WriteLine("Hidden WPF GPU pause, paused full-HD resize, resume and CPU renderer replacement passed.");
            Application.Current.Shutdown(0);
        }
    }

    private static void FailBackgroundCheck(string error)
    {
        Console.Error.WriteLine("Background check failed: " + error);
        Application.Current.Shutdown(1);
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
