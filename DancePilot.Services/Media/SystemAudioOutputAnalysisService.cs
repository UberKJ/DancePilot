using DancePilot.Services.LocalMusic;
using NAudio.Dsp;
using NAudio.Wave;

namespace DancePilot.Services.Media;

public sealed class SystemAudioOutputAnalysisService : IDisposable
{
    private const int FftSize = 2048;
    private const int FftExponent = 11;
    private const int AnalysisFrameCount = 8192;
    private const double MinimumSignalRms = 0.0012;
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
    private WasapiLoopbackCapture? _capture;
    private WaveFormat? _waveFormat;
    private double[] _ringBuffer = [];
    private int _ringWriteIndex;
    private int _ringSampleCount;
    private bool _captureRunning;
    private DateTimeOffset _lastStartAttempt = DateTimeOffset.MinValue;
    private double[] _smoothedBands = [];
    private double[] _smoothedWaveform = [];
    private double[] _previousRawBands = [];
    private double _smoothedBeatPulse;

    public LocalAudioSpectrumSnapshot? Analyze(int waveformBarCount = 60)
    {
        EnsureCaptureStarted();

        double[] samples;
        int sampleRate;
        lock (_syncRoot)
        {
            if (!_captureRunning || _waveFormat is null || _ringSampleCount < FftSize)
            {
                return null;
            }

            sampleRate = _waveFormat.SampleRate;
            samples = CopyLatestSamples(Math.Min(AnalysisFrameCount, _ringSampleCount));
        }

        var rms = RootMeanSquare(samples);
        if (rms < MinimumSignalRms)
        {
            return null;
        }

        var rawBands = AnalyzeBands(samples, sampleRate);
        var rawWaveform = AnalyzeWaveform(samples, waveformBarCount);
        double[] bandLevels;
        double[] waveformLevels;
        double beatPulse;
        lock (_syncRoot)
        {
            beatPulse = CalculateBeatPulse(rawBands, rms);
            bandLevels = Smooth(ref _smoothedBands, rawBands, attack: 0.66, release: 0.28);
            waveformLevels = Smooth(ref _smoothedWaveform, rawWaveform, attack: 0.78, release: 0.34);
        }

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
            sampleRate,
            beatPulse);
    }

    public void Dispose()
    {
        lock (_syncRoot)
        {
            StopCapture();
        }
    }

    private void EnsureCaptureStarted()
    {
        lock (_syncRoot)
        {
            if (_captureRunning)
            {
                return;
            }

            if (DateTimeOffset.UtcNow - _lastStartAttempt < TimeSpan.FromSeconds(3))
            {
                return;
            }

            _lastStartAttempt = DateTimeOffset.UtcNow;
            try
            {
                StopCapture();
                _capture = new WasapiLoopbackCapture();
                _waveFormat = _capture.WaveFormat;
                ResetRingBuffer(_waveFormat.SampleRate);
                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += OnRecordingStopped;
                _capture.StartRecording();
                _captureRunning = true;
            }
            catch
            {
                StopCapture();
            }
        }
    }

    private void StopCapture()
    {
        if (_capture is not null)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
        }

        _capture = null;
        _waveFormat = null;
        _captureRunning = false;
        _ringBuffer = [];
        _ringWriteIndex = 0;
        _ringSampleCount = 0;
        _smoothedBeatPulse = 0;
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        lock (_syncRoot)
        {
            StopCapture();
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_syncRoot)
        {
            if (_waveFormat is null || _ringBuffer.Length == 0)
            {
                return;
            }

            var bytesPerSample = Math.Max(1, _waveFormat.BitsPerSample / 8);
            var channels = Math.Max(1, _waveFormat.Channels);
            var blockAlign = Math.Max(bytesPerSample * channels, _waveFormat.BlockAlign);
            var frameCount = e.BytesRecorded / blockAlign;
            for (var frame = 0; frame < frameCount; frame++)
            {
                var offset = frame * blockAlign;
                var sum = 0d;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += ReadSample(e.Buffer, offset + channel * bytesPerSample, _waveFormat);
                }

                WriteSample(sum / channels);
            }
        }
    }

    private void ResetRingBuffer(int sampleRate)
    {
        _ringBuffer = new double[Math.Max(sampleRate * 2, AnalysisFrameCount * 2)];
        _ringWriteIndex = 0;
        _ringSampleCount = 0;
        _smoothedBands = [];
        _smoothedWaveform = [];
        _previousRawBands = [];
    }

    private void WriteSample(double sample)
    {
        _ringBuffer[_ringWriteIndex] = Math.Clamp(sample, -1d, 1d);
        _ringWriteIndex = (_ringWriteIndex + 1) % _ringBuffer.Length;
        _ringSampleCount = Math.Min(_ringSampleCount + 1, _ringBuffer.Length);
    }

    private double[] CopyLatestSamples(int count)
    {
        var samples = new double[count];
        var start = (_ringWriteIndex - count + _ringBuffer.Length) % _ringBuffer.Length;
        for (var index = 0; index < count; index++)
        {
            samples[index] = _ringBuffer[(start + index) % _ringBuffer.Length];
        }

        return samples;
    }

    private double CalculateBeatPulse(double[] rawBands, double rms)
    {
        var bassEnergy = rawBands.Take(2).DefaultIfEmpty(0).Average();
        var spectralFlux = 0d;
        if (_previousRawBands.Length == rawBands.Length)
        {
            for (var index = 0; index < rawBands.Length; index++)
            {
                spectralFlux += Math.Max(0, rawBands[index] - _previousRawBands[index]);
            }

            spectralFlux /= rawBands.Length;
        }

        _previousRawBands = rawBands.ToArray();
        var pulse = Math.Clamp((bassEnergy * 0.72) + (spectralFlux * 1.15) + (rms * 3.2), 0, 1);
        var blend = pulse >= _smoothedBeatPulse ? 0.82 : 0.18;
        _smoothedBeatPulse += (pulse - _smoothedBeatPulse) * blend;
        return Math.Clamp(_smoothedBeatPulse, 0, 1);
    }

    private static double ReadSample(byte[] buffer, int offset, WaveFormat waveFormat)
    {
        if (offset < 0 || offset >= buffer.Length)
        {
            return 0;
        }

        var encoding = waveFormat.Encoding;
        if (encoding is WaveFormatEncoding.IeeeFloat)
        {
            return offset + 4 <= buffer.Length ? Math.Clamp(BitConverter.ToSingle(buffer, offset), -1d, 1d) : 0;
        }

        if (encoding is WaveFormatEncoding.Extensible && waveFormat.BitsPerSample == 32)
        {
            var floatValue = offset + 4 <= buffer.Length ? BitConverter.ToSingle(buffer, offset) : 0;
            if (!double.IsNaN(floatValue) && Math.Abs(floatValue) <= 4)
            {
                return Math.Clamp(floatValue, -1d, 1d);
            }
        }

        if (encoding is not (WaveFormatEncoding.Pcm or WaveFormatEncoding.Extensible))
        {
            return 0;
        }

        return waveFormat.BitsPerSample switch
        {
            16 when offset + 2 <= buffer.Length => BitConverter.ToInt16(buffer, offset) / 32768d,
            24 when offset + 3 <= buffer.Length => ReadInt24(buffer, offset) / 8388608d,
            32 when offset + 4 <= buffer.Length => BitConverter.ToInt32(buffer, offset) / 2147483648d,
            _ => 0
        };
    }

    private static int ReadInt24(byte[] buffer, int offset)
    {
        var sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
        if ((sample & 0x800000) != 0)
        {
            sample |= unchecked((int)0xFF000000);
        }

        return sample;
    }

    private static double[] AnalyzeBands(IReadOnlyList<double> monoSamples, int sampleRate)
    {
        var fftBuffer = new Complex[FftSize];
        var sourceOffset = Math.Max(0, monoSamples.Count - FftSize);
        for (var index = 0; index < FftSize; index++)
        {
            var sourceIndex = sourceOffset + index;
            var sample = sourceIndex < monoSamples.Count ? monoSamples[sourceIndex] : 0d;
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
            bandTotals[bandIndex] += Math.Log10(1 + magnitude * 150);
            bandCounts[bandIndex]++;
        }

        var levels = new double[BandDefinitions.Length];
        for (var index = 0; index < levels.Length; index++)
        {
            levels[index] = bandCounts[index] == 0 ? 0 : bandTotals[index] / bandCounts[index];
        }

        var max = levels.DefaultIfEmpty(0).Max();
        if (max <= 0.000001)
        {
            return levels;
        }

        for (var index = 0; index < levels.Length; index++)
        {
            levels[index] = Math.Clamp(levels[index] / max, 0, 1);
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
        if (max <= 0.000001)
        {
            return levels;
        }

        for (var index = 0; index < levels.Length; index++)
        {
            levels[index] = Math.Clamp(levels[index] / max, 0, 1);
        }

        return levels;
    }

    private static double RootMeanSquare(IReadOnlyList<double> samples)
    {
        if (samples.Count == 0)
        {
            return 0;
        }

        var sumSquares = 0d;
        for (var index = 0; index < samples.Count; index++)
        {
            sumSquares += samples[index] * samples[index];
        }

        return Math.Sqrt(sumSquares / samples.Count);
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
