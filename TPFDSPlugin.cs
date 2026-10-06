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
        var data = CustomStoryRepository.Get("Puzzle Set One");
        __instance.puzzleLists = data.puzzles;

        var breakWatcher = GameObject.FindFirstObjectByType<BreakWatcher>();
        var meetings = data.meetings;
        foreach (var obj in meetings) {
            obj.meetingManager = GameObject.FindFirstObjectByType<MeetingManager>();
        }
        breakWatcher.puzzles = data.meetingPuzzles;
        breakWatcher.leaveRoomEvents = data.meetings;
    }

    [HarmonyPatch(typeof(PuzzleManager), nameof(PuzzleManager.SetDataFromLoad))]
    [HarmonyPrefix]
    public static void FuckData(PuzzleManager __instance, ref ProgressData pd) {
        pd.currPuzz = 0;
    }
}
