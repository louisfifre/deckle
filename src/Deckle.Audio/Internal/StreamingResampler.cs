namespace Deckle.Audio.Internal;

// Sample-rate conversion of a mono float stream, in Deckle's own code: the
// second step (after MonoConverter) from the device's mix format to the
// 16 kHz mono float every consumer expects.
//
// Why it is written here. The point of the WASAPI capture is a signal Deckle
// processes itself, the same on every machine, so the one stage that shapes the
// spectrum between the microphone and the consumers cannot be left to a
// driver or to the audio engine. Everything below is plain double arithmetic
// on a table computed from the two rates alone: the same input gives the same
// output, whatever the chunking, the machine or the Windows build.
//
// What it is. A polyphase windowed-sinc low-pass interpolator. The output rate
// and the input rate are reduced to a ratio L/M; output sample j sits at input
// time j*M/L, so it lies between two input samples at one of L fixed
// fractional positions (its phase). For each phase the table holds the kernel
// that places the output exactly there:
//
//   - a sinc low-pass cut at 0.45 times the lower of the two rates (7.2 kHz
//     for a 16 kHz output, 90 % of its Nyquist), so nothing above the output's
//     band folds back into it when the rate goes down;
//   - a Kaiser window (beta 8, about 80 dB of attenuation), 16 zero crossings
//     on each side of the centre;
//   - each phase's kernel normalised to a sum of one, so a constant input stays
//     constant whatever the phase (no gain ripple between output samples).
//
// It is zero-phase: the output is not delayed against the input, it only needs
// HalfWidth input samples of look-ahead, which Flush supplies with silence.
// Equal rates bypass the filter entirely, so a 16 kHz microphone is not
// low-passed for nothing.
//
// One instance per take, one thread. Process returns a span over an internal
// array, valid until the next call.
internal sealed class StreamingResampler
{
    // Zero crossings of the sinc kept on each side of the kernel's centre. 16
    // keeps the transition band narrow (cutoff 7.2 kHz, stopband from 8 kHz)
    // at a cost of 2 * HalfWidth multiply-adds per output sample.
    private const int ZeroCrossings = 16;

    // Kaiser beta: about 80 dB of stopband attenuation.
    private const double KaiserBeta = 8.0;

    // Share of the lower Nyquist frequency that the low-pass keeps.
    private const double CutoffShare = 0.9;

    private readonly bool _passthrough;
    private readonly int _up;            // L
    private readonly int _down;          // M
    private readonly int _halfWidth;     // kernel reach on each side, in input samples
    private readonly double[][] _phases; // _up kernels of 2 * _halfWidth taps
    private readonly long _flushPadding;

    // The input kept between calls: _history[0] is global input sample
    // _historyStart. Samples before the first one of the stream read as zero.
    private float[] _history = new float[1024];
    private int _historyCount;
    private long _historyStart;
    private long _inputTotal;
    private long _nextOutput;

    private float[] _output = new float[512];

    public StreamingResampler(int inputRate, int outputRate)
    {
        if (inputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
        if (outputRate <= 0) throw new ArgumentOutOfRangeException(nameof(outputRate));

        InputRate = inputRate;
        OutputRate = outputRate;

        if (inputRate == outputRate)
        {
            _passthrough = true;
            _phases = [];
            return;
        }

        int gcd = Gcd(inputRate, outputRate);
        _up = outputRate / gcd;
        _down = inputRate / gcd;

        // Cutoff as a share of the input rate, in cycles per input sample.
        double cutoff = CutoffShare * 0.5 * Math.Min(inputRate, outputRate) / inputRate;
        _halfWidth = (int)Math.Ceiling(ZeroCrossings / (2.0 * cutoff));
        _flushPadding = _halfWidth;

        _phases = new double[_up][];
        for (int phase = 0; phase < _up; phase++)
            _phases[phase] = BuildKernel(phase, cutoff);
    }

    public int InputRate { get; }
    public int OutputRate { get; }

    // Input samples of look-ahead the filter needs before it can emit; the
    // latency this stage adds, in input samples.
    public int LookAheadSamples => _passthrough ? 0 : _halfWidth;

    // Feeds input samples, returns the output samples now computable.
    public ReadOnlySpan<float> Process(ReadOnlySpan<float> input)
    {
        if (_passthrough)
        {
            _inputTotal += input.Length;
            return input;
        }

        Append(input);
        _inputTotal += input.Length;
        return Produce(finalising: false);
    }

    // Ends the stream: feeds the silence the last samples need as look-ahead
    // and returns the remaining output, so the output length is exactly
    // ceil(input length * output rate / input rate).
    public ReadOnlySpan<float> Flush()
    {
        if (_passthrough) return ReadOnlySpan<float>.Empty;

        Append(new float[_flushPadding]);
        return Produce(finalising: true);
    }

    private ReadOnlySpan<float> Produce(bool finalising)
    {
        long inputEnd = _historyStart + _historyCount;
        int written = 0;

        while (true)
        {
            long position = _nextOutput * _down;
            long centre = position / _up;
            int phase = (int)(position % _up);

            if (finalising)
            {
                // Every output whose instant lies inside the real input.
                if (position >= _inputTotal * _up) break;
            }
            else if (centre + _halfWidth >= inputEnd)
            {
                break;
            }

            if (written == _output.Length)
                Array.Resize(ref _output, _output.Length * 2);
            _output[written++] = (float)Convolve(_phases[phase], centre);
            _nextOutput++;
        }

        Trim();
        return _output.AsSpan(0, written);
    }

    // sum over k in [-halfWidth + 1, halfWidth] of kernel * x[centre + k].
    private double Convolve(double[] kernel, long centre)
    {
        long first = centre - _halfWidth + 1;
        double sum = 0;
        for (int i = 0; i < kernel.Length; i++)
        {
            long index = first + i - _historyStart;
            if (index < 0 || index >= _historyCount) continue;
            sum += kernel[i] * _history[index];
        }
        return sum;
    }

    private void Append(ReadOnlySpan<float> input)
    {
        int required = _historyCount + input.Length;
        if (required > _history.Length)
            Array.Resize(ref _history, Math.Max(required, _history.Length * 2));
        input.CopyTo(_history.AsSpan(_historyCount));
        _historyCount = required;
    }

    // Drops what no later output can reach: the next one needs input from
    // (its centre - halfWidth + 1) on.
    private void Trim()
    {
        long keepFrom = _nextOutput * _down / _up - _halfWidth + 1;
        int drop = (int)Math.Clamp(keepFrom - _historyStart, 0, _historyCount);
        if (drop == 0) return;

        Array.Copy(_history, drop, _history, 0, _historyCount - drop);
        _historyCount -= drop;
        _historyStart += drop;
    }

    // The kernel for one phase: taps at input offsets k - fraction, k running
    // from -halfWidth + 1 to halfWidth, where fraction = phase / L in [0, 1).
    private double[] BuildKernel(int phase, double cutoff)
    {
        double fraction = (double)phase / _up;
        var taps = new double[2 * _halfWidth];
        double sum = 0;
        for (int i = 0; i < taps.Length; i++)
        {
            double offset = (i - _halfWidth + 1) - fraction;
            double window = Kaiser(offset / _halfWidth);
            double tap = 2.0 * cutoff * Sinc(2.0 * cutoff * offset) * window;
            taps[i] = tap;
            sum += tap;
        }
        for (int i = 0; i < taps.Length; i++)
            taps[i] /= sum;
        return taps;
    }

    private static double Sinc(double x)
        => Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);

    // Kaiser window over x in [-1, 1].
    private static double Kaiser(double x)
    {
        if (Math.Abs(x) >= 1.0) return 0.0;
        return BesselI0(KaiserBeta * Math.Sqrt(1.0 - x * x)) / BesselI0(KaiserBeta);
    }

    // Modified Bessel function of the first kind, order zero, by its power
    // series; converges fast for the argument range used here (0 to beta).
    private static double BesselI0(double x)
    {
        double sum = 1.0;
        double term = 1.0;
        double quarterSquare = x * x / 4.0;
        for (int k = 1; k < 60; k++)
        {
            term *= quarterSquare / ((double)k * k);
            sum += term;
            if (term < sum * 1e-17) break;
        }
        return sum;
    }

    private static int Gcd(int a, int b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}
