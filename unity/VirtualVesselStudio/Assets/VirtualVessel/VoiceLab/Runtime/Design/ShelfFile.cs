using System;
using System.Collections.Generic;
using System.Linq;
using VirtualVessel.ProjectData.Config;
using VirtualVessel.UI.VoiceLab;

namespace VirtualVessel.VoiceLab.Design
{
    /// <summary>
    /// Reads STEP 1's shelf from the scripts' voicelab/shelf.json (Voice Lab detailed design 8.3.1). The scripts
    /// build the instruction from it; Unity needs only the words, their slots and groups, and the examples.
    /// </summary>
    public static class ShelfFile
    {
        public const int SchemaVersion = 1;

        /// <summary>The shelf, or null with the reason when the file is not one this version reads. Pure.</summary>
        public static VoiceLabShelf Parse(string json, out string problem)
        {
            ConfigDocument document;
            try
            {
                document = ConfigDocument.Parse(json);
            }
            catch (FormatException exception)
            {
                problem = "The shelf is not JSON: " + exception.Message;
                return null;
            }

            if (document.Get("schemaVersion", 0) != SchemaVersion)
            {
                problem = $"The shelf's schema version is not {SchemaVersion}.";
                return null;
            }

            List<SlotFields> slots = document.Get("slots", new List<SlotFields>());
            List<WordFields> words = document.Get("words", new List<WordFields>());
            List<GroupFields> groups = document.Get("groups", new List<GroupFields>());
            var groupOfSlot = slots.Where(s => s.Group != null).ToDictionary(s => s.Id, s => s.Group, StringComparer.Ordinal);
            var defaults = new HashSet<string>(slots.Where(s => s.Default != null).Select(s => s.Default), StringComparer.Ordinal);
            if (words.Any(w => w.Id == null || w.Slot == null || !groupOfSlot.ContainsKey(w.Slot)))
            {
                problem = "A word of the shelf has no slot in a group.";
                return null;
            }

            var shelfGroups = groups.Select(g => new VoiceLabShelfGroup(
                g.Id,
                g.Main,
                words.Where(w => groupOfSlot[w.Slot] == g.Id).Select(w => new VoiceLabShelfWord(w.Id, w.Slot, defaults.Contains(w.Id))).ToArray()))
                .ToArray();
            var examples = document.Get("examples", new List<ExampleFields>())
                .Select(e => new VoiceLabExample(e.Id, e.Words ?? new List<string>()))
                .ToArray();
            problem = null;
            return new VoiceLabShelf(shelfGroups, examples);
        }

        private sealed class GroupFields
        {
            public string Id { get; set; }

            public bool Main { get; set; }
        }

        private sealed class SlotFields
        {
            public string Id { get; set; }

            public string Group { get; set; }

            public string Default { get; set; }
        }

        private sealed class WordFields
        {
            public string Id { get; set; }

            public string Slot { get; set; }
        }

        private sealed class ExampleFields
        {
            public string Id { get; set; }

            public List<string> Words { get; set; }
        }
    }
}
