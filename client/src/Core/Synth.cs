using Godot;

namespace BoardEmpire.Core;

/// <summary>
/// Procedural sound synthesis. The game ships without audio files: every effect and music loop
/// is rendered to PCM at start-up, which keeps the install small and the sound set consistent.
/// </summary>
public static class Synth
{
    public const int Rate = 22050;

    private static readonly Random Noise = new(7);

    public static AudioStreamWav ToStream(float[] samples, bool loop = false)
    {
        var data = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short s = (short)(Math.Clamp(samples[i], -1f, 1f) * 30000);
            data[i * 2] = (byte)(s & 0xff);
            data[i * 2 + 1] = (byte)((s >> 8) & 0xff);
        }
        var stream = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data,
        };
        if (loop)
        {
            stream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            stream.LoopBegin = 0;
            stream.LoopEnd = samples.Length;
        }
        return stream;
    }

    private static float[] Buffer(double seconds) => new float[(int)(seconds * Rate)];

    private static float Env(int i, int n, double attack, double release)
    {
        double t = (double)i / Rate, total = (double)n / Rate;
        double a = attack <= 0 ? 1 : Math.Min(1, t / attack);
        double r = release <= 0 ? 1 : Math.Min(1, (total - t) / release);
        return (float)(a * r);
    }

    public static void AddTone(float[] buf, double start, double length, double freq, float gain, double decay = 6, bool soft = true)
    {
        int from = (int)(start * Rate), n = (int)(length * Rate);
        for (int i = 0; i < n && from + i < buf.Length; i++)
        {
            double t = (double)i / Rate;
            double wave = Math.Sin(2 * Math.PI * freq * t);
            if (!soft) wave = wave * 0.6 + 0.4 * Math.Sign(wave);
            wave += 0.25 * Math.Sin(4 * Math.PI * freq * t);
            buf[from + i] += (float)(wave * gain * Math.Exp(-decay * t)) * Env(i, n, 0.004, 0.02);
        }
    }

    public static void AddNoise(float[] buf, double start, double length, float gain, double decay, float smooth)
    {
        int from = (int)(start * Rate), n = (int)(length * Rate);
        float last = 0;
        for (int i = 0; i < n && from + i < buf.Length; i++)
        {
            float white = (float)(Noise.NextDouble() * 2 - 1);
            last += (white - last) * smooth;
            buf[from + i] += last * gain * (float)Math.Exp(-decay * i / Rate) * Env(i, n, 0.002, 0.01);
        }
    }

    public static AudioStreamWav Click() 
    {
        var b = Buffer(0.07);
        AddTone(b, 0, 0.07, 880, 0.35f, 50);
        AddNoise(b, 0, 0.02, 0.2f, 120, 0.6f);
        return ToStream(b);
    }

    public static AudioStreamWav DiceRoll()
    {
        var b = Buffer(0.75);
        var rng = new Random(3);
        double t = 0;
        while (t < 0.62)
        {
            AddNoise(b, t, 0.05, 0.55f, 70, 0.75f);
            AddTone(b, t, 0.04, 300 + rng.Next(500), 0.18f, 60, false);
            t += 0.035 + rng.NextDouble() * 0.07 + t * 0.06;
        }
        return ToStream(b);
    }

    public static AudioStreamWav DiceLand()
    {
        var b = Buffer(0.22);
        AddNoise(b, 0, 0.08, 0.7f, 45, 0.5f);
        AddTone(b, 0, 0.18, 140, 0.5f, 22);
        return ToStream(b);
    }

    public static AudioStreamWav Step()
    {
        var b = Buffer(0.09);
        AddTone(b, 0, 0.09, 520, 0.3f, 38);
        AddNoise(b, 0, 0.03, 0.2f, 90, 0.4f);
        return ToStream(b);
    }

    public static AudioStreamWav Coin()
    {
        var b = Buffer(0.4);
        AddTone(b, 0, 0.3, 1568, 0.3f, 12);
        AddTone(b, 0.07, 0.33, 2093, 0.32f, 10);
        return ToStream(b);
    }

    public static AudioStreamWav Purchase()
    {
        var b = Buffer(0.7);
        AddTone(b, 0, 0.5, 523.25, 0.25f, 5);
        AddTone(b, 0.09, 0.5, 659.25, 0.25f, 5);
        AddTone(b, 0.18, 0.5, 783.99, 0.28f, 5);
        return ToStream(b);
    }

    public static AudioStreamWav Build()
    {
        var b = Buffer(0.6);
        AddNoise(b, 0, 0.12, 0.6f, 30, 0.25f);
        AddTone(b, 0, 0.2, 110, 0.5f, 16);
        AddTone(b, 0.12, 0.4, 440, 0.2f, 7);
        AddTone(b, 0.2, 0.4, 660, 0.2f, 7);
        return ToStream(b);
    }

    public static AudioStreamWav Card()
    {
        var b = Buffer(0.3);
        AddNoise(b, 0, 0.25, 0.4f, 14, 0.9f);
        AddTone(b, 0.1, 0.2, 988, 0.15f, 14);
        return ToStream(b);
    }

    public static AudioStreamWav Jail()
    {
        var b = Buffer(0.7);
        AddTone(b, 0, 0.3, 196, 0.4f, 6, false);
        AddTone(b, 0.25, 0.4, 146.8, 0.45f, 5, false);
        AddNoise(b, 0.25, 0.2, 0.3f, 18, 0.3f);
        return ToStream(b);
    }

    public static AudioStreamWav Gavel()
    {
        var b = Buffer(0.5);
        AddNoise(b, 0, 0.06, 0.8f, 60, 0.35f);
        AddTone(b, 0, 0.2, 180, 0.5f, 20);
        AddNoise(b, 0.2, 0.06, 0.7f, 60, 0.35f);
        AddTone(b, 0.2, 0.25, 170, 0.5f, 18);
        return ToStream(b);
    }

    public static AudioStreamWav Notify()
    {
        var b = Buffer(0.45);
        AddTone(b, 0, 0.3, 880, 0.25f, 9);
        AddTone(b, 0.12, 0.33, 1318.5, 0.25f, 8);
        return ToStream(b);
    }

    public static AudioStreamWav Error()
    {
        var b = Buffer(0.3);
        AddTone(b, 0, 0.14, 220, 0.3f, 10, false);
        AddTone(b, 0.13, 0.17, 185, 0.3f, 9, false);
        return ToStream(b);
    }

    public static AudioStreamWav Fanfare()
    {
        var b = Buffer(1.9);
        double[] notes = { 523.25, 659.25, 783.99, 1046.5, 783.99, 1046.5 };
        double t = 0;
        for (int i = 0; i < notes.Length; i++)
        {
            double len = i >= 3 ? 0.5 : 0.22;
            AddTone(b, t, len + 0.3, notes[i], 0.26f, 3.5);
            AddTone(b, t, len + 0.3, notes[i] / 2, 0.14f, 3.5);
            t += i >= 3 ? 0.28 : 0.16;
        }
        return ToStream(b);
    }

    public static AudioStreamWav Bankrupt()
    {
        var b = Buffer(1.3);
        double[] notes = { 392, 349.2, 311.1, 261.6, 196 };
        for (int i = 0; i < notes.Length; i++) AddTone(b, i * 0.2, 0.5, notes[i], 0.28f, 5, false);
        return ToStream(b);
    }

    public static AudioStreamWav Whoosh()
    {
        var b = Buffer(0.35);
        int n = b.Length;
        float last = 0;
        for (int i = 0; i < n; i++)
        {
            float k = (float)i / n;
            float white = (float)(Noise.NextDouble() * 2 - 1);
            last += (white - last) * (0.05f + 0.5f * k);
            b[i] = last * 0.5f * (float)Math.Sin(Math.PI * k);
        }
        return ToStream(b);
    }

    /// <summary>Soft city hum for the board: filtered noise with a slow swell, loopable.</summary>
    public static AudioStreamWav Ambience()
    {
        var b = Buffer(6);
        float last = 0, slow = 0;
        for (int i = 0; i < b.Length; i++)
        {
            double t = (double)i / Rate;
            float white = (float)(Noise.NextDouble() * 2 - 1);
            last += (white - last) * 0.02f;
            slow += (white - slow) * 0.004f;
            float swell = 0.6f + 0.4f * (float)Math.Sin(2 * Math.PI * t / 6);
            b[i] = (last * 0.5f + slow * 1.6f) * swell * 0.5f;
        }
        // Blend the tail into the head so the loop point is inaudible.
        int fade = Rate / 2;
        for (int i = 0; i < fade; i++)
        {
            float k = (float)i / fade;
            b[i] = b[i] * k + b[b.Length - fade + i] * (1 - k);
        }
        Array.Resize(ref b, b.Length - fade);
        return ToStream(b, true);
    }

    /// <summary>
    /// One loopable music bed. Intensity 0..3 raises tempo, register and adds a pulse, which is
    /// how the score follows the match from relaxed opening to tense endgame.
    /// </summary>
    public static AudioStreamWav Music(int intensity)
    {
        double bpm = 78 + intensity * 14;
        double beat = 60.0 / bpm;
        int bars = 4;
        var b = Buffer(bars * 4 * beat);
        // i - vi - IV - V in C, voiced lower and darker as the tension rises.
        double[][] chords =
        {
            new[] { 261.63, 329.63, 392.0 }, new[] { 220.0, 261.63, 329.63 },
            new[] { 174.61, 220.0, 261.63 }, new[] { 196.0, 246.94, 293.66 },
        };
        if (intensity >= 2)
        {
            chords = new[]
            {
                new[] { 220.0, 261.63, 329.63 }, new[] { 174.61, 220.0, 261.63 },
                new[] { 146.83, 174.61, 220.0 }, new[] { 164.81, 207.65, 246.94 },
            };
        }
        for (int bar = 0; bar < bars; bar++)
        {
            double t0 = bar * 4 * beat;
            foreach (double f in chords[bar]) AddTone(b, t0, 4 * beat, f / 2, 0.07f, 0.5);
            AddTone(b, t0, 4 * beat, chords[bar][0] / 4, 0.12f, 0.7);
            int steps = intensity == 0 ? 4 : 8;
            for (int s = 0; s < steps; s++)
            {
                double t = t0 + s * (4 * beat / steps);
                double f = chords[bar][(s * (intensity + 1)) % 3] * (s % 4 == 3 ? 2 : 1);
                AddTone(b, t, beat * 0.9, f, 0.06f + 0.012f * intensity, 5);
            }
            if (intensity >= 1)
                for (int q = 0; q < 4; q++) AddNoise(b, t0 + q * beat + beat / 2, 0.05, 0.05f + 0.02f * intensity, 70, 0.8f);
            if (intensity >= 2)
                for (int q = 0; q < 4; q++) AddTone(b, t0 + q * beat, 0.16, 62, 0.28f, 26);
            if (intensity >= 3)
                for (int q = 0; q < 8; q++) AddTone(b, t0 + q * beat / 2, 0.09, chords[bar][0] * 2, 0.035f, 30, false);
        }
        return ToStream(b, true);
    }
}
