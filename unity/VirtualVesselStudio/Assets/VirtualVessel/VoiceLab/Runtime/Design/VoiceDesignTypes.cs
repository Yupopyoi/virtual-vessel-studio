using System;
using System.Collections.Generic;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.VoiceLab.Training;

namespace VirtualVessel.VoiceLab.Design
{
    /// <summary>
    /// How the timbre moves (Voice Lab detailed design 8.3.2): steps of -6 to +6 (softness 0 to +6), 0 changing
    /// nothing. The scripts turn steps into edits, so previews and training speech use the same meaning.
    /// </summary>
    public readonly struct TimbreSettings : IEquatable<TimbreSettings>
    {
        public const float StepLimit = 6f;

        public TimbreSettings(float pitch, float age, float brightness, float breathiness, float softness = 0f)
        {
            Pitch = Clamp(pitch);
            Age = Clamp(age);
            Brightness = Clamp(brightness);
            Breathiness = Clamp(breathiness);
            Softness = Math.Max(0f, Clamp(softness));
        }

        /// <summary>Semitones.</summary>
        public float Pitch { get; }

        public float Age { get; }

        public float Brightness { get; }

        public float Breathiness { get; }

        /// <summary>0 to +6: only the soft side exists (a pressed side sounded harsh).</summary>
        public float Softness { get; }

        /// <summary>Writes the settings as the object at <paramref name="path"/> of a request.</summary>
        public void WriteTo(ConfigDocument document, string path)
        {
            document.Set(path + ".pitch", Pitch);
            document.Set(path + ".age", Age);
            document.Set(path + ".brightness", Brightness);
            document.Set(path + ".breathiness", Breathiness);
            document.Set(path + ".softness", Softness);
        }

        /// <summary>The settings as run.json keeps them. Pure.</summary>
        public TimbreValues ToValues()
        {
            return new TimbreValues { Pitch = Pitch, Age = Age, Brightness = Brightness, Breathiness = Breathiness, Softness = Softness };
        }

        public bool Equals(TimbreSettings other)
        {
            return Pitch == other.Pitch && Age == other.Age && Brightness == other.Brightness && Breathiness == other.Breathiness && Softness == other.Softness;
        }

        public override bool Equals(object obj)
        {
            return obj is TimbreSettings other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Pitch, Age, Brightness, Breathiness, Softness);
        }

        private static float Clamp(float step)
        {
            return Math.Max(-StepLimit, Math.Min(StepLimit, step));
        }
    }

    /// <summary>What candidates are made from: shelf word ids (voicelab/shelf.json) and the user's own words.</summary>
    public sealed class VoiceDescription
    {
        public VoiceDescription(IReadOnlyList<string> words, string freeWords)
        {
            Words = words ?? Array.Empty<string>();
            FreeWords = freeWords ?? string.Empty;
        }

        public IReadOnlyList<string> Words { get; }

        public string FreeWords { get; }

        /// <summary>Writes the request's "shelf" and "words"; the script builds and checks the instruction.</summary>
        public void WriteTo(ConfigDocument document)
        {
            document.Set("shelf", new List<string>(Words));
            document.Set("words", FreeWords);
        }
    }

    /// <summary>The pitch the screens show for a voice: the median and the 5 to 95% range, in Hz.</summary>
    public readonly struct PitchSummary
    {
        public PitchSummary(int medianHz, int lowHz, int highHz)
        {
            MedianHz = medianHz;
            LowHz = lowHz;
            HighHz = highHz;
        }

        public int MedianHz { get; }

        public int LowHz { get; }

        public int HighHz { get; }

        /// <summary>From a script result's fields; zeros when it found no voice. Pure.</summary>
        public static PitchSummary From(ConfigDocument fields)
        {
            return new PitchSummary(fields.Get("medianHz", 0), fields.Get("lowHz", 0), fields.Get("highHz", 0));
        }
    }

    /// <summary>One voice made from a description, to listen to and maybe build on.</summary>
    public sealed class VoiceCandidate
    {
        public VoiceCandidate(string id, int seed, string file, PitchSummary pitch, string instruction, string reference = null)
        {
            Id = id;
            Seed = seed;
            File = file;
            Pitch = pitch;
            Instruction = instruction;
            Reference = reference;
        }

        public string Id { get; }

        /// <summary>Reproduces the voice only under the same environment versions.</summary>
        public int Seed { get; }

        public string File { get; }

        public PitchSummary Pitch { get; }

        public string Instruction { get; }

        /// <summary>The audio it was made near, or null.</summary>
        public string Reference { get; }
    }

    /// <summary>A timbre preview: the edited sample of a candidate.</summary>
    public sealed class TimbrePreview
    {
        public TimbrePreview(string file, PitchSummary pitch)
        {
            File = file;
            Pitch = pitch;
        }

        public string File { get; }

        public PitchSummary Pitch { get; }
    }

    /// <summary>One candidate as generate.py reports it; read by the JSON parts' property matching.</summary>
    public sealed class CandidateFields
    {
        public int Seed { get; set; }

        public string File { get; set; }

        public int MedianHz { get; set; }

        public int LowHz { get; set; }

        public int HighHz { get; set; }
    }
}
