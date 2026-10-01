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
        // Taco: TODO: Extract into separate function?
        if (Directory.Exists(PuzzleSetsPath)) {
            var directory = new DirectoryInfo(PuzzleSetsPath);
            var fileInfos = directory.GetFiles("*.puzzles.json", SearchOption.AllDirectories);
            var puzzleSets = new List<(string directory, CustomPuzzleSetData puzzleSet)>(capacity: fileInfos.Length);

            Logger.LogInfo($"Found {fileInfos.Length} possible puzzle sets");

            foreach (var info in fileInfos) {
                Logger.LogInfo($"{info.FullName}");
                CustomPuzzleSetData data;
                try {
                    data = JsonUtility.FromJson<CustomPuzzleSetData>(File.ReadAllText(info.FullName));
                } catch (Exception ex) {
                    Logger.LogError(ex.Message);
                    Logger.LogError($"File {info.FullName} doesn't contain a valid puzzle set definition");
                    continue;
                }

                Logger.LogInfo($"Added puzzle set {info.Name} from {info.Directory.FullName}");
                puzzleSets.Add((directory: info.Directory.FullName, puzzleSet: data));
            }
            Puzzles = puzzleSets;
        }
        else {
            Logger.LogInfo("Creating the directory for custom puzzle sets");
            Directory.CreateDirectory(PuzzleSetsPath);
        }
    }
}

/// Taco: NOTE: BreakWatcher.puzzles[index] sets aftter which puzzle a break should start 

public static class CustomPuzzleRepository {
    public static Dictionary<string, PuzzleList[]> Puzzles = [];

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
            }
            group.puzzles = puzzles.ToArray();
        }

        Puzzles[data.meta.name] = groups.ToArray();
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
        List<Puzzle> testSet = new List<Puzzle>() {
            new Puzzle() {
                title = "Test01",
                rockOutput = new SignalMessage() {
                    signals = new int[] { 10, -2, 0 }
                },
                winningResponse = new SignalMessage() {
                    signals = new int[] { 0 }
                },
                uniqueID = 0,
                totalID = 0,
            },
            new Puzzle() {
                title = "Test02",
                rockOutput = new SignalMessage() {
                    signals = new int[] { 10, -2, 1 }
                },
                winningResponse = new SignalMessage() {
                    signals = new int[] { 0 }
                },
                uniqueID = 1,
                totalID = 1,
            },
            new Puzzle() {
                title = "Test03",
                rockOutput = new SignalMessage() {
                    signals = new int[] { 10, -2, 1 }
                },
                winningResponse = new SignalMessage() {
                    signals = new int[] { 0 }
                },
                uniqueID = 1,
                totalID = 1,
            },
        };

        __instance.puzzleLists = new PuzzleList[] {
            new PuzzleList() {
                puzzleGroupName = "Test Group 1",
                puzzles = [testSet[0], testSet[1]],
            },
            new PuzzleList() {
                puzzleGroupName = "Test Group 3",
                puzzles = [testSet[2]],
            },
        };

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
            listsCompleted = [ __instance.PuzzleLists[0] ],
            journalEntriesDialogue = meeting.dc,
        };

        var breakWatcher = GameObject.FindFirstObjectByType<BreakWatcher>();
        breakWatcher.puzzles[0] = testSet[2];
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

        meeting2.progressLogData = new ProgressLogData() {
            actSection = "ACT Test 2",
            nextActName = "ACT Test 3",
            actName = "Test",
        };

        // breakWatcher.puzzles[1] = testSet[];
        breakWatcher.leaveRoomEvents[1] = leaveEvent;
    }

    [HarmonyPatch(typeof(PuzzleManager), nameof(PuzzleManager.SetDataFromLoad))]
    [HarmonyPrefix]
    public static void FuckData(PuzzleManager __instance, ref ProgressData pd) {
        pd.currPuzz = 0;
    }
}
