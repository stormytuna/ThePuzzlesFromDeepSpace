using System.Collections.Generic;

namespace TPFDS;

public static class CustomDialogueRepository {
    static Dictionary<string, Dictionary<string, DialogueChunk>> chunks = [];

    static Dictionary<string, Speaker> speakerMap = new() {
        { "Alan",     Speaker.Alan },
        { "Akers",    Speaker.Alan },
        { "A",        Speaker.Alan },
        { "Bautista", Speaker.BScientist },
        { "Bryan",    Speaker.BScientist },
        { "B",        Speaker.BScientist },
        { "Carrie",   Speaker.Carrie },
        { "Collins",  Speaker.Carrie },
        { "C",        Speaker.Carrie },
        { "Doppler",  Speaker.Doppler },
        { "Dopp",     Speaker.Doppler },
        { "Douglas",  Speaker.Doppler },
        { "D",        Speaker.Doppler },
        { "Autolog",  Speaker.AutoLog },
        { "Log",      Speaker.AutoLog },
    };

    static bool DialogueLoadError(string reason, string name, CustomPuzzleSetData data) {
        TPFDSPlugin.Logger.LogError($"Loading dialogue {name} failed: {reason}. Skipping {data.meta.name}");
        return false;
    }

    /// <summary> When using the result, set .dialogueType </summary>
    public static DialogueChunk Get(string set, string name) {
        return chunks[set][name];
    }

    public static DialogueChunk Merge(DialogueChunk one, DialogueChunk two) {
        return new DialogueChunk() {
            logName = one.name,
            raw = "TODO: Build this from the dialogue frames",
            processedRaw = "TODO: Build this from the dialogue frames",
            frames = [ ..one.Frames, ..two.Frames ]
        };
    }

    public static Dictionary<string, DialogueChunk> GetAllFromSet(string set) => chunks[set];

    public static bool TryAdd(CustomPuzzleSetData data) {
        var dialogues = new Dictionary<string, DialogueChunk>(capacity: data.conversations.Count);
        foreach (var entry in data.conversations) {
            var name = entry.Key;
            var bubbles = entry.Value;
            var frames = new DialogueFrame[bubbles.Length];

            for (int i = 0; i < bubbles.Length; i++) {
                var bubble = bubbles[i];
                if (!speakerMap.TryGetValue(bubble.speaker, out var speaker))
                    return DialogueLoadError($"unknown speaker {bubble.speaker}", name, data);

                frames[i] = new DialogueFrame() {
                    dialogueParts = [ new DialoguePart() { txt = bubble.text } ],
                    speaker = speaker
                };
            }

           dialogues[name] = new DialogueChunk() {
                logName = name,
                raw = "TODO: Build this from the dialogue frames",
                processedRaw = "TODO: Build this from the dialogue frames",
                frames = frames
            };
            TPFDSPlugin.Logger.LogInfo($"Loaded {name}: {dialogues[name]}");
        }

        chunks[data.meta.name] = dialogues;
        return true;
    }
}
