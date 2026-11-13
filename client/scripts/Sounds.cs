using Client.Core;
using Godot;

// QoL I3: short sounds made in code (no sound files): a few sine notes with a soft start and end. Off unless the
// player turns them on in the Menu. Nothing here is random; the cues come from the server's events (SoundCues).
public partial class Sounds : Node
{
    private const int Rate = 22050;
    private readonly Dictionary<SoundCue, AudioStreamWav> _streams = [];
    private readonly AudioStreamPlayer _player = new() { MaxPolyphony = 4, VolumeDb = -8 };
    private readonly List<SoundCue> _played = [];

    public bool Enabled { get; set; }

    // What was played, in order (for the UI check).
    public IReadOnlyList<SoundCue> Played => _played;

    public override void _Ready()
    {
        AddChild(_player);
        _streams[SoundCue.YourMove] = Tones((523, 90), (784, 140));
        _streams[SoundCue.Activation] = Tones((660, 60));
        _streams[SoundCue.Pass] = Tones((880, 90));
        _streams[SoundCue.Fail] = Tones((220, 140));
        _streams[SoundCue.Scoring] = Tones((523, 80), (659, 80), (784, 160));
    }

    public void Play(SoundCue cue)
    {
        if (!Enabled || !_streams.TryGetValue(cue, out var stream))
        {
            return;
        }
        _played.Add(cue);
        _player.Stream = stream;
        _player.Play();
    }

    private static AudioStreamWav Tones(params (double Hz, int Ms)[] notes)
    {
        var samples = new List<short>();
        foreach (var (hz, ms) in notes)
        {
            var count = Rate * ms / 1000;
            for (var i = 0; i < count; i++)
            {
                var edge = Math.Min(1.0, Math.Min(i, count - i) / (Rate * 0.01));
                samples.Add((short)(Math.Sin(2 * Math.PI * hz * i / Rate) * edge * 0.4 * short.MaxValue));
            }
        }
        var data = new byte[samples.Count * 2];
        for (var i = 0; i < samples.Count; i++)
        {
            data[2 * i] = (byte)(samples[i] & 0xff);
            data[2 * i + 1] = (byte)((samples[i] >> 8) & 0xff);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Rate, Stereo = false, Data = data };
    }
}
