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

            Logger.LogInfo(Puzzles.Count);
            foreach (var entry in Puzzles) CustomPuzzleRepository.Add(entry.directory, entry.puzzleSet);
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

public static class CustomPuzzleRepository {
    static Dictionary<string, PuzzleList[]> puzzles = [];
    static Dictionary<string, (string directory, MetaData meta)> info = [];

    static void LogPuzzleLoadError(string reason, string puzzleName, CustomPuzzleSetData data) {
        TPFDSPlugin.Logger.LogError($"Puzzle \"{puzzleName}\" ${reason}. Skipping {data.meta.name}");
    }

    static SignalMessage MessageFrom(int[] signals) => new SignalMessage() { signals = signals };

    public static void Add(string directory, CustomPuzzleSetData data) {
        var groups = new List<PuzzleList>(capacity: data.puzzleGroups.Length);
        foreach (var entry in data.puzzleGroups) {
            var group = new PuzzleList() { puzzleGroupName = entry.title };
            var puzzles = new List<Puzzle>(capacity: entry.puzzles.Length);

            foreach (var puzzleName in entry.puzzles) {
                if (!data.puzzles.TryGetValue(puzzleName, out var puzzle)) {
                    LogPuzzleLoadError("doesn't exist", puzzleName, data);
                    return;
                }

                var acceptedCount = puzzle.validResponses.Length;
                if (acceptedCount == 0) {
                    LogPuzzleLoadError("has no valid responses", puzzleName, data);
                    return;
                }

                var vanillaPuzzle = new Puzzle() {
                    title = puzzleName,
                    rockOutput = MessageFrom(puzzle.message),
                    winningResponse = MessageFrom(puzzle.validResponses[0]),
                    allowAltResponses = acceptedCount > 1,
                    altResponses = acceptedCount > 1 ? puzzle.validResponses[1..].Select(MessageFrom).ToArray() : [],
                };
                puzzles.Add(vanillaPuzzle);
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
        // Taco: NOTE: Debug moment. Please out the test.puzzles.json file in %GAMEROOT%/PuzzleSets for this to work 
        var set = CustomPuzzleRepository.Get("Puzzle Set One");
        TPFDSPlugin.Logger.LogInfo(set.Count());
        TPFDSPlugin.Logger.LogInfo(set[0]);
        __instance.puzzleLists = set;

        var testEvent = new GameObject();
        testEvent.name = "Test 0";
        var leaveEvent = testEvent.AddComponent<LeaveRoomEvent>();
        var meeting = testEvent.AddComponent<Meeting>();
        leaveEvent.meeting = meeting;
        leaveEvent.meetingManager = GameObject.FindFirstObjectByType<MeetingManager>();

        meeting.dc = new DialogueChunk() {
            logName = "Test 0",
            dialogueType = DialogueType.dopplerBreak,
            raw = "TODO: Build this from the dialogue frames",
            processedRaw = "TODO: Build this from the dialogue frames",
            frames = new DialogueFrame[] {
                new DialogueFrame() { 
                    dialogueParts = new DialoguePart[] { new DialoguePart() {txt = "Hi guys!" } },
                    speaker = Speaker.Doppler,
                },
                new DialogueFrame() { 
                    dialogueParts = new DialoguePart[] { new DialoguePart() {txt = "Hiya!" } },
                    speaker = Speaker.Alan,
                },
                new DialogueFrame() { 
                    dialogueParts = new DialoguePart[] { new DialoguePart() {txt = "Hello everyone." } },
                    speaker = Speaker.Carrie,
                },
                new DialogueFrame() { 
                    dialogueParts = new DialoguePart[] { new DialoguePart() {txt = "Hmm." } },
                    speaker = Speaker.BScientist,
                },
            }
        };

        meeting.journalEntryDC = meeting.dc;

        meeting.progressLogData = new ProgressLogData() {
            actSection = "ACT Test 1",
            nextActName = "ACT Test 2",
            actName = "This is a test",
            listsCompleted = [ ],
            journalEntriesDialogue = meeting.dc,
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
