using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Newtonsoft.Json;

namespace TPFDS;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class TPFDSPlugin : BaseUnityPlugin
{
    public static string PuzzleSetsPath = Path.Combine(Paths.GameRootPath, "PuzzleSets");
    public static List<(string directory, CustomPuzzleSetData puzzleSet)> Puzzles = [];
    public static string SelectedPuzzle = "";

    internal static new ManualLogSource Logger;

	private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        
    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        CustomPuzzlesTest.IdeaClusterAggregator_ILHook.Apply();

        harmony.PatchAll();
        if (Directory.Exists(PuzzleSetsPath)) {
            Puzzles = LoadPuzzleSets();

            foreach (var entry in Puzzles) {
                if (!CustomDialogueRepository.TryAdd(entry.puzzleSet))
                    continue;
                if (!CustomMeetingRepository.TryAdd(entry.puzzleSet))
                    continue;
                if (!CustomHypothesisRepository.TryAdd(entry.puzzleSet))
                    continue;

                CustomStoryRepository.Add(entry.directory, entry.puzzleSet);
            }
        }
        else {
            Logger.LogInfo("Creating the directory for custom puzzle sets");
            Directory.CreateDirectory(PuzzleSetsPath);
        }

        // polymeric: I placed this field in TPFDSPlugin to store the ID of the loaded puzzle.
        //            From what I see the puzzle should be selected before Unity calls Autosaver.Start()
        //            so we can use the game's original savefile loading routines.
        //            Autosaver.Start() also eventually calls PuzzleManager.SetTotalPuzzleList().
        // HarmonyPatchesSaveFiles.GetCurrentSavefileRoot
        // CustomPuzzlesTest.FuckPuzzleList
        // Taco: NOTE: Debug moment.
        // Please put the test.puzzles.json file in %GAMEROOT%/PuzzleSets for this to work
        TPFDSPlugin.SelectedPuzzle = "Puzzle Set One";
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
        var data = CustomStoryRepository.Get(TPFDSPlugin.SelectedPuzzle);
        // Setup puzzles
        __instance.puzzleLists = data.puzzles;

        // Setup meetings
        var breakWatcher = GameObject.FindFirstObjectByType<BreakWatcher>();
        var manager = GameObject.FindFirstObjectByType<MeetingManager>();
        foreach (var obj in data.meetings) {
            obj.meetingManager = manager;
        }
        breakWatcher.puzzles = data.meetingPuzzles;
        breakWatcher.leaveRoomEvents = data.meetings;

        // Setup enabling tabs
        var adder = GameObject.FindFirstObjectByType<TabAdder>();
        adder.puzzles = data.tabEnablers;
        SortTabs(adder);

        // Setup banter
        var aggregator = GameObject.FindFirstObjectByType<IdeaAggregatorMono>().ideaAggregator;
        aggregator.clusters = data.banter;
        aggregator.clusterMap = [];
        modded = true;
        aggregator.InitClusterMap();

        // Setup hypotheses
        var hypoDict = GameObject.FindFirstObjectByType<DictionaryHypotheses>();
        hypoDict.puzzles = data.hypotheses.triggers;
        hypoDict.hypos = data.hypotheses.hypos;
        hypoDict.termClusters = data.hypotheses.clusters;
        hypoDict.ConfigDict();
    }

    /// <summary>
    ///   Sorts <see cref="TabAdder.orderedTabs"/> and its inherited <see cref="UpdateWithPuzzleCount.puzzles"/>
    ///   in place for use by <see cref="TabAdder.ClearedID(int, int)" /> <br />
    ///   Uses selection sort
    /// </summary>
    static void SortTabs(TabAdder adder) {
        int length = adder.puzzles.Length;
        for (int i = 0; i < length; i++) {
            for (int j = i; j < length; j++) {
                if (adder.puzzles[j].TotalID < adder.puzzles[i].TotalID) {
                    var temp = (adder.puzzles[i], adder.orderedTabs[i]);
                    (adder.puzzles[i], adder.orderedTabs[i]) = (adder.puzzles[j], adder.orderedTabs[j]);
                    (adder.puzzles[j], adder.orderedTabs[j]) = (temp.Item1, temp.Item2);
                }
            }
        }
    }

    // Reset puzzle count to 0 for development
    #if DEBUG
    [HarmonyPatch(typeof(PuzzleManager), nameof(PuzzleManager.SetDataFromLoad))]
    [HarmonyPrefix]
    public static void FuckData(PuzzleManager __instance, ref ProgressData pd) {
        pd.currPuzz = 0;
    }
    #endif

    [HarmonyPatch(typeof(DictionaryWindow), nameof(DictionaryWindow.Open))]
    [HarmonyPostfix]
    public static void AllowReformat(DictionaryWindow __instance) {
        // Allow reformatting
        __instance.ActivateGlobalOptions();
        __instance.InitializeGlobalOptions();
        __instance.globalOptionsButton.SetActive(true);
    }

    [HarmonyPatch(typeof(DictionaryNotes), nameof(DictionaryNotes.Open))]
    [HarmonyPostfix]
    public static void AllowReformatInNotes(DictionaryNotes __instance, DictionaryEntry de) {
        __instance.TurnOnCompileSettings();
    }

    [HarmonyPatch(typeof(ListenerCondition), nameof(ListenerCondition.MetCon))]
    [HarmonyPostfix]
    public static void MetCon(ListenerCondition __instance, ref bool __result) {
        if (__result == false && __instance.puzzleIs && __instance.puzzValue == null) {
            __result = true;
        }
    }

    public static ILHook IdeaClusterAggregator_ILHook = new(
        typeof(IdeaAggregator).GetMethod(nameof(IdeaAggregator.InitClusterMap), BindingFlags.NonPublic | BindingFlags.Instance),
        InitIdeaClusterMapPatch,
        new ILHookConfig() {
            ManualApply = true,
        }
    );

    static bool modded = false;
    static void InitIdeaClusterMapPatch(ILContext il) {
        TPFDSPlugin.Logger.LogInfo($"Patching {nameof(InitIdeaClusterMapPatch)}");
        var cursor = new ILCursor(il);

        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate((IdeaAggregator aggregator) => {
            if (!modded) return;

            aggregator.clusters[0].Activate(on: true);
        });

        if (!cursor.TryGotoNext(MoveType.Before, i => i.MatchStloc(0), i => i.MatchBr(out _))) {
            TPFDSPlugin.Logger.LogError($"Failed to inject {nameof(InitIdeaClusterMapPatch)}");
            return;
        }

        cursor.EmitDelegate((int startIndex) => {
            var index = modded ? 1 : startIndex;
            modded = false;

            return index;
        });
    }
}
