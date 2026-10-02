using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using Newtonsoft.Json;

namespace TPFDS;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class TPFDSPlugin : BaseUnityPlugin
{
    public static string PuzzleSetsPath = Path.Combine(Paths.GameRootPath, "PuzzleSets");
    public static List<(string directory, CustomPuzzleSetData puzzleSet)> Puzzles = [];
    
    internal static new ManualLogSource Logger;

	private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        
    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        harmony.PatchAll();
        if (Directory.Exists(PuzzleSetsPath)) {
            Puzzles = LoadPuzzleSets();

            foreach (var entry in Puzzles) {
                // If dialogue fails to load, abort loading the puzzle set fully
                if (!CustomDialogueRepository.TryAdd(entry.puzzleSet))
                    continue;
                if (!CustomMeetingRepository.TryAdd(entry.puzzleSet))
                    continue;

                CustomStoryRepository.Add(entry.directory, entry.puzzleSet);
            }
        }
        else {
            Logger.LogInfo("Creating the directory for custom puzzle sets");
            Directory.CreateDirectory(PuzzleSetsPath);
        }
    }

    static List<(string, CustomPuzzleSetData)> LoadPuzzleSets() {
        var directory = new DirectoryInfo(PuzzleSetsPath);
        var fileInfos = directory.GetFiles("*.puzzles.json", SearchOption.AllDirectories);
        var puzzleSets = new List<(string directory, CustomPuzzleSetData puzzleSet)>(capacity: fileInfos.Length);

        Logger.LogInfo($"Found {fileInfos.Length} possible puzzle sets");

        foreach (var info in fileInfos) {
            CustomPuzzleSetData data;
            try {
                data = JsonConvert.DeserializeObject<CustomPuzzleSetData>(File.ReadAllText(info.FullName));
            } catch (Exception ex) {
                Logger.LogError(ex.Message);
                Logger.LogError($"File {info.FullName} doesn't contain a valid puzzle set definition");
                continue;
            }

            Logger.LogInfo($"Added puzzle set {data.meta.name} from {info.Directory.FullName}");
            puzzleSets.Add((directory: info.Directory.FullName, puzzleSet: data));
        }

        return puzzleSets;
    }
}

/// Taco: NOTE: BreakWatcher.puzzles[index] sets aftter which puzzle a break should start 

public static class CustomStoryRepository  {
    static Dictionary<string, PuzzleList[]> puzzles = [];
    static Dictionary<string, (string directory, MetaData meta)> info = [];

    static void LogEventLoadError(string reason, string puzzleName, CustomPuzzleSetData data) {
        TPFDSPlugin.Logger.LogError($"Event \"{puzzleName}\" {reason}. Skipping {data.meta.name}");
    }

    static SignalMessage MessageFrom(int[] signals) => new SignalMessage() { signals = signals };

    static bool IsPuzzle(string name) => name.StartsWith("puzzle:");
    static bool IsMeeting(string name) => name.StartsWith("meeting:");

    public static void Add(string directory, CustomPuzzleSetData data) {
        var groups = new List<PuzzleList>(capacity: data.puzzleGroups.Length);
        var meetingPuzzles = new List<Puzzle>(capacity: groups.Capacity);
        var meetings = new List<GameObject>(capacity: meetingPuzzles.Capacity);

        foreach (var entry in data.puzzleGroups) {
            var group = new PuzzleList() { puzzleGroupName = entry.title };
            var storyEvents = entry.storyline;
            var puzzles = new List<Puzzle>(capacity: storyEvents.Where(IsPuzzle).Count());
            var dialogues = CustomDialogueRepository.GetAllFromSet(data.meta.name);

            foreach (var storyEvent in storyEvents) {
                if (IsPuzzle(storyEvent)) {
                    var search = storyEvent["puzzle:".Length..];
                    if (!data.puzzles.TryGetValue(search, out var puzzle)) {
                        LogEventLoadError("doesn't exist", search, data);
                        return;
                    }

                    var acceptedCount = puzzle.validResponses.Length;
                    if (acceptedCount == 0) {
                        LogEventLoadError("has no valid responses", storyEvent, data);
                        return;
                    }

                    var vanillaPuzzle = new Puzzle() {
                        title = storyEvent,
                        rockOutput = MessageFrom(puzzle.message),
                        winningResponse = MessageFrom(puzzle.validResponses[0]),
                        allowAltResponses = acceptedCount > 1,
                        altResponses = acceptedCount > 1 ? puzzle.validResponses[1..].Select(MessageFrom).ToArray() : [],
                    };
                    puzzles.Add(vanillaPuzzle);
                }
                else if (IsMeeting(storyEvent)) {
                    meetingPuzzles.Add(puzzles[puzzles.Count - 1]);
                }
                else {
                    LogEventLoadError("unknown type of event. Should be one of \"puzzle:\" or \"meeting:\"", storyEvent, data);
                    return;
                }
            }
            group.puzzles = puzzles.ToArray();
            groups.Add(group);
        }

        puzzles[data.meta.name] = groups.ToArray();
        info[data.meta.name] = (directory, data.meta);

        TPFDSPlugin.Logger.LogInfo($"Loaded {data.meta.name}");
    }

    public static PuzzleList[] Get(string name) => puzzles[name];
}

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
            TPFDSPlugin.Logger.LogInfo($"Loaded {name}");
        }

        chunks[data.meta.name] = dialogues;
        return true;
    }
}

public static class CustomMeetingRepository {
    static Dictionary<string, GameObject[]> meetings = [];

    static bool MeetingLoadError(string reason, string name, CustomPuzzleSetData data) {
        TPFDSPlugin.Logger.LogError($"Loading meeting {name} failed: {reason}. Skipping {data.meta.name}");
        return false;
    }

    static bool TryAdd(CustomPuzzleSetData data) {
        var dialogues = CustomDialogueRepository.GetAllFromSet(data.meta.name);
        var meetingList = new List<GameObject>(capacity: data.meetings.Count);
        var week = 0;

        foreach (var entry in data.meetings) {
            var name = entry.Key;
            var meetingObj = entry.Value;

            var gObject = new GameObject();
            gObject.name = $"meeting:{name}";
            var leaveEvent = gObject.AddComponent<LeaveRoomEvent>();
            var meeting = gObject.AddComponent<Meeting>();
            leaveEvent.meeting = meeting;

            var chunk = new DialogueChunk() {
                logName = name,
                raw = "TODO: Build this from the dialogue frames",
                processedRaw = "TODO: Build this from the dialogue frames",
                frames = []
            };

            if (meetingObj.events.Length == 0)
                return MeetingLoadError("meeting contains no events", name, data);

            // Taco: NOTE: Parse only the first event for now, expand later
            foreach (var convo in meetingObj.events[0].conversations) {
                if (!dialogues.TryGetValue(convo, out var next))
                    return MeetingLoadError($"dialogue {convo} doesn't exist", name, data);
                chunk = CustomDialogueRepository.Merge(chunk, next);
            }
            chunk.dialogueType = DialogueType.dopplerBreak;

            meeting.journalEntryDC = new DialogueChunk() {
                logName = $"journal:{name}",
                dialogueType = DialogueType.journalEntries,
                raw = "TODO: Build this from the dialogue frames",
                processedRaw = "TODO: Build this from the dialogue frames",
                frames = new DialogueFrame[] {
                    new DialogueFrame() {
                        dialogueParts = new DialoguePart[] { new DialoguePart() { txt = meetingObj.journals["Alan"] } },
                        speaker = Speaker.Alan,
                    },
                    new DialogueFrame() {
                        dialogueParts = new DialoguePart[] { new DialoguePart() { txt = meetingObj.journals["Carrie"] } },
                        speaker = Speaker.Carrie,
                    },
                    new DialogueFrame() {
                        dialogueParts = new DialoguePart[] { new DialoguePart() { txt = meetingObj.journals["Doppler"] } },
                        speaker = Speaker.Doppler,
                    },
                    new DialogueFrame() {
                        dialogueParts = new DialoguePart[] { new DialoguePart() { txt = meetingObj.journals["Bautista"] } },
                        speaker = Speaker.BScientist,
                    },
                }
            };
            meeting.progressLogData = new ProgressLogData() {
                actSection = "ACT Test 1",
                nextActName = "ACT Test 2",
                actName = "This is a test",
                listsCompleted = [ ],
                journalEntriesDialogue = meeting.journalEntryDC,
                weekID = meetingObj.progressWeek ? week++ : -1,
            };
        }
        return true;
    }
}

[HarmonyPatch]
public static class CustomPuzzlesTest
{
    // TODO: Compiler stuff can probably be done better, removing puzzle events that change it let us set it wherever whenever
    // Or maybe puzzle event on first that sets it to what we want
    [HarmonyPatch(typeof(ConsoleDisplay), nameof(ConsoleDisplay.Awake))]
    [HarmonyPostfix]
    public static void OnLoad(ConsoleDisplay __instance) {
        __instance.UpdateToNewCompiler(new C_Reformatter());
    }

    [HarmonyPatch(typeof(BreakWatcher), nameof(BreakWatcher.ClearedID))]
    [HarmonyPostfix]
    public static void Testing(int id, int prevExceeded) {
        TPFDSPlugin.Logger.LogInfo("Found something: " + id);
    }

    [HarmonyPatch(typeof(ConsoleDisplay), nameof(ConsoleDisplay.UpdateToNewCompiler))]
    [HarmonyPrefix]
    public static bool PreventCompilerFromBeingDowngraded(SignalCompiler newCompiler) {
        if (newCompiler is not C_Reformatter) {
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(PuzzleManager), nameof(PuzzleManager.SetTotalPuzzleList))]
    [HarmonyPrefix]
    public static void FuckPuzzleList(PuzzleManager __instance) {
        // Taco: NOTE: Debug moment.
        // Please put the test.puzzles.json file in %GAMEROOT%/PuzzleSets for this to work 
        var set = CustomStoryRepository .Get("Puzzle Set One");
        __instance.puzzleLists = set;

        var testEvent = new GameObject();
        testEvent.name = "Test 0";
        var leaveEvent = testEvent.AddComponent<LeaveRoomEvent>();
        var meeting = testEvent.AddComponent<Meeting>();
        leaveEvent.meeting = meeting;
        leaveEvent.meetingManager = GameObject.FindFirstObjectByType<MeetingManager>();

        meeting.dc = CustomDialogueRepository.Get("Puzzle Set One", "dopp_confession");
        meeting.dc.dialogueType = DialogueType.dopplerBreak;
        meeting.journalEntryDC = meeting.dc;
        meeting.journalEntryDC.dialogueType = DialogueType.journalEntries;

        meeting.progressLogData = new ProgressLogData() {
            actSection = "ACT Test 1",
            nextActName = "ACT Test 2",
            actName = "This is a test",
            listsCompleted = [ ],
            journalEntriesDialogue = meeting.journalEntryDC,
            weekID = -1,
        };

        var breakWatcher = GameObject.FindFirstObjectByType<BreakWatcher>();
        var first_break = set[0];
        breakWatcher.puzzles[0] = first_break.Puzzles[1];
        breakWatcher.leaveRoomEvents[0] = leaveEvent;

        var testEvent2 = new GameObject();
        testEvent2.name = "Test 0";
        var leaveEvent2 = testEvent2.AddComponent<LeaveRoomEvent>();
        var meeting2 = testEvent2.AddComponent<Meeting>();
        leaveEvent2.meeting = meeting2;
        leaveEvent2.meetingManager = GameObject.FindFirstObjectByType<MeetingManager>();

        meeting2.dc = new DialogueChunk() {
            logName = "Test 1",
            dialogueType = DialogueType.dopplerBreak,
            raw = "TODO: Build this from the dialogue frames",
            processedRaw = "TODO: Build this from the dialogue frames",
            frames = new DialogueFrame[] {
                new DialogueFrame() { 
                    dialogueParts = new DialoguePart[] { new DialoguePart() {txt = "So... I like men." } },
                    speaker = Speaker.Doppler,
                },
                new DialogueFrame() { 
                    dialogueParts = new DialoguePart[] { new DialoguePart() {txt = "Oh~" } },
                    speaker = Speaker.Alan,
                },
                new DialogueFrame() { 
                    dialogueParts = new DialoguePart[] { new DialoguePart() {txt = "Umm. Okay Dr. Doppler." } },
                    speaker = Speaker.Carrie,
                },
                new DialogueFrame() { 
                    dialogueParts = new DialoguePart[] { new DialoguePart() {txt = "Clean." } },
                    speaker = Speaker.BScientist,
                },
            }
        };

        meeting2.journalEntryDC = meeting2.dc;
        meeting2.progressLogData = new ProgressLogData() {
            actSection = "ACT Test 2",
            nextActName = "ACT Test 3",
            actName = "Test",
            listsCompleted = [ set[0] ],
            journalEntriesDialogue = meeting2.dc,
            actBreak = true,
        };

        breakWatcher.puzzles[1] = set[0].Puzzles[2];
        breakWatcher.leaveRoomEvents[1] = leaveEvent2;
    }

    [HarmonyPatch(typeof(PuzzleManager), nameof(PuzzleManager.SetDataFromLoad))]
    [HarmonyPrefix]
    public static void FuckData(PuzzleManager __instance, ref ProgressData pd) {
        pd.currPuzz = 0;
    }
}
