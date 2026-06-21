using NAudio.Dsp;
using NAudio.Wave;

namespace DancePilot.Services.LocalMusic;

public sealed class LocalAudioAnalysisService : IDisposable
{
    private const int FftSize = 2048;
    private const int FftExponent = 11;
    private const int AnalysisFrameCount = 8192;
    private static readonly SpectrumBandDefinition[] BandDefinitions =
    [
        new("SUB", "30-60", 30, 60),
        new("BASS", "60-120", 60, 120),
        new("LOW", "120-250", 120, 250),
        new("MID", "250-750", 250, 750),
        new("HIGH", "750-2K", 750, 2000),
        new("PRES", "2K-6K", 2000, 6000),
        new("AIR", "6K-12K", 6000, 12000)
    ];

    private readonly object _syncRoot = new();
    private AudioFileReader? _reader;
    private string? _filePath;
    private double[] _smoothedBands = [];
    private double[] _smoothedWaveform = [];

    public LocalAudioSpectrumSnapshot? Analyze(
        string filePath,
        TimeSpan position,
        int waveformBarCount = 60)
    {
        if (string.IsNullOrWhiteSpace(filePath)
            || waveformBarCount <= 0
            || !File.Exists(filePath))
        {
            return null;
        }

        lock (_syncRoot)
        {
            try
            {
                EnsureReader(filePath);
                if (_reader is null)
                {
                    return null;
                }

                var sampleRate = _reader.WaveFormat.SampleRate;
                var channels = Math.Max(1, _reader.WaveFormat.Channels);
                var monoSamples = ReadMonoWindow(_reader, sampleRate, channels, position);
                if (monoSamples.Length == 0)
                {
                    return null;
                }

                var bandLevels = Smooth(ref _smoothedBands, AnalyzeBands(monoSamples, sampleRate), attack: 0.58, release: 0.24);
                var waveformLevels = Smooth(ref _smoothedWaveform, AnalyzeWaveform(monoSamples, waveformBarCount), attack: 0.70, release: 0.30);
                var low = bandLevels.Take(3).DefaultIfEmpty(0).Average();
                var mid = bandLevels.Skip(3).Take(1).DefaultIfEmpty(0).Average();
                var high = bandLevels.Skip(4).DefaultIfEmpty(0).Average();

                return new LocalAudioSpectrumSnapshot(
                    bandLevels,
                    waveformLevels,
                    BandDefinitions.Select(band => band.Label).ToArray(),
                    BandDefinitions.Select(band => band.Range).ToArray(),
                    low,
                    mid,
                    high,
                    sampleRate);
            }
            catch
            {
                ResetReader();
                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            ResetReader();
        }
    }

    private void EnsureReader(string filePath)
    {
        if (string.Equals(_filePath, filePath, StringComparison.OrdinalIgnoreCase) && _reader is not null)
        {
            return;
        }

        ResetReader();
        _reader = new AudioFileReader(filePath);
        _filePath = filePath;
        _smoothedBands = [];
        _smoothedWaveform = [];
    }

    private void ResetReader()
    {
        _reader?.Dispose();
        _reader = null;
        _filePath = null;
    }

    private static double[] ReadMonoWindow(
        AudioFileReader reader,
        int sampleRate,
        int channels,
        TimeSpan position)
    {
        var halfWindow = TimeSpan.FromSeconds(AnalysisFrameCount / (sampleRate * 2d));
        var start = position > halfWindow
            ? position - halfWindow
            : TimeSpan.Zero;
        if (reader.TotalTime > TimeSpan.Zero && start > reader.TotalTime)
        {
            start = reader.TotalTime;
        }

        reader.CurrentTime = start;
        var interleaved = new float[AnalysisFrameCount * channels];
        var samplesRead = reader.Read(interleaved, 0, interleaved.Length);
        var framesRead = samplesRead / channels;
        if (framesRead <= 0)
        {
            return [];
        }

        var monoSamples = new double[framesRead];
        for (var frame = 0; frame < framesRead; frame++)
        {
            var sum = 0d;
            var sampleOffset = frame * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += interleaved[sampleOffset + channel];
            }

            monoSamples[frame] = Math.Clamp(sum / channels, -1d, 1d);
        }

        return monoSamples;
    }

    private static double[] AnalyzeBands(IReadOnlyList<double> monoSamples, int sampleRate)
    {
        var fftBuffer = new Complex[FftSize];
        for (var index = 0; index < FftSize; index++)
        {
            var sample = index < monoSamples.Count ? monoSamples[index] : 0d;
            fftBuffer[index].X = (float)(sample * FastFourierTransform.HammingWindow(index, FftSize));
            fftBuffer[index].Y = 0;
        }

        FastFourierTransform.FFT(true, FftExponent, fftBuffer);

        var bandTotals = new double[BandDefinitions.Length];
        var bandCounts = new int[BandDefinitions.Length];
        for (var bin = 1; bin < FftSize / 2; bin++)
        {
            var frequency = bin * sampleRate / (double)FftSize;
            var bandIndex = FindBandIndex(frequency);
            if (bandIndex < 0)
            {
                continue;
            }

            var magnitude = Math.Sqrt(
                fftBuffer[bin].X * fftBuffer[bin].X
                + fftBuffer[bin].Y * fftBuffer[bin].Y);
            bandTotals[bandIndex] += Math.Log10(1 + magnitude * 120);
            bandCounts[bandIndex]++;
        }

        var levels = new double[BandDefinitions.Length];
        for (var index = 0; index < levels.Length; index++)
        {
            levels[index] = bandCounts[index] == 0 ? 0 : bandTotals[index] / bandCounts[index];
        }

        var max = levels.DefaultIfEmpty(0).Max();
        if (max > 0.000001)
        {
            for (var index = 0; index < levels.Length; index++)
            {
                levels[index] = Math.Clamp(levels[index] / max, 0, 1);
            }
        }

        return levels;
    }

    private static int FindBandIndex(double frequency)
    {
        for (var index = 0; index < BandDefinitions.Length; index++)
        {
            var band = BandDefinitions[index];
            if (frequency >= band.MinimumFrequency && frequency < band.MaximumFrequency)
            {
                return index;
            }
        }

        return -1;
    }

    private static double[] AnalyzeWaveform(IReadOnlyList<double> monoSamples, int waveformBarCount)
    {
        var levels = new double[waveformBarCount];
        if (monoSamples.Count == 0)
        {
            return levels;
        }

        for (var bar = 0; bar < waveformBarCount; bar++)
        {
            var start = bar * monoSamples.Count / waveformBarCount;
            var end = Math.Max(start + 1, (bar + 1) * monoSamples.Count / waveformBarCount);
            var sumSquares = 0d;
            for (var sample = start; sample < end && sample < monoSamples.Count; sample++)
            {
                sumSquares += monoSamples[sample] * monoSamples[sample];
            }

            levels[bar] = Math.Sqrt(sumSquares / Math.Max(1, end - start));
        }

        var max = levels.DefaultIfEmpty(0).Max();
        if (max > 0.000001)
        {
            for (var index = 0; index < levels.Length; index++)
            {
                levels[index] = Math.Clamp(levels[index] / max, 0, 1);
            }
        }

        return levels;
    }

    private static double[] Smooth(
        ref double[] previous,
        double[] current,
        double attack,
        double release)
    {
        if (previous.Length != current.Length)
        {
            previous = current.ToArray();
            return current.ToArray();
        }

        var smoothed = new double[current.Length];
        for (var index = 0; index < current.Length; index++)
        {
            var blend = current[index] >= previous[index] ? attack : release;
            smoothed[index] = previous[index] + ((current[index] - previous[index]) * blend);
        }

        previous = smoothed;
        return smoothed.ToArray();
    }

    private sealed record SpectrumBandDefinition(
        string Label,
        string Range,
        double MinimumFrequency,
        double MaximumFrequency);
}

public sealed record LocalAudioSpectrumSnapshot(
    IReadOnlyList<double> Bands,
    IReadOnlyList<double> Waveform,
    IReadOnlyList<string> Labels,
    IReadOnlyList<string> Ranges,
    double Low,
    double Mid,
    double High,
    int SampleRate,
    double BeatPulse = 0);
