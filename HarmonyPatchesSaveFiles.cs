using System.IO;
using HarmonyLib;
using UnityEngine;

namespace TPFDS;

public static class HarmonyPatchesSaveFiles {
    // Hooks into the game's save routines.

    // DictionarySaver and ProgressSaver are the two classes that handle writing DICTIONARY-1.save and TMFDS.save respectively.
    // All meaningful file accesses by the game can be redirected by patching those two classes' Get*FilePath methods.

    // Autosaver.Start drives the initial save file load for the game.
    // It is one of the first MonoBehaviours invoked at game start and eventually calls a routine that loads puzzles,
    // so it can be used to perform save initialization.

    // Modifications:
    // 1. All Get*FilePath operations are redirected to the directory specified by GetCurrentSavefileRoot().
    // 2. Autosaver.Start creates a directory at GetCurrentSavefileRoot() if it does not already exist.
    //    It also copies the player's vanilla dictionary to that location if SeedNewDictionaryFromBaseGameDictionary() is set.
    // 3. RoomManager.TranslatorNamed is overridden to disable the long opening cutscene after setting the Translator's name.

    public static string GetCurrentSavefileRoot() {
        // Currently the key into CustomStoryRepository (puzzle title) is used to name the savefile folder.
        // Alternatively, this could be the JSON filename of the puzzle, or a composite key like puzzle title + puzzle author.
        return Application.persistentDataPath + Path.DirectorySeparatorChar + "TPFDS" + Path.DirectorySeparatorChar + TPFDSPlugin.SelectedPuzzle;
    }

    public static bool SeedNewDictionaryFromBaseGameDictionary() {
        // TODO this could be a puzzle pack parameter. Whether to start with the player's vanilla dictionary (true) or an empty dictionary (false).
        return true;
    }

    [HarmonyPatch(typeof(ProgressSaver), nameof(ProgressSaver.GetBackupFilePath))]
    public static class ProgressSaver__GetBackupFilePath {
        public static void Postfix(ProgressSaver __instance, ref string __result, int week) {
            // TPFDSPlugin.Logger.LogDebug($"ProgressSaver.GetBackupFilePath {week} -> {__result}");
            __result = GetCurrentSavefileRoot() + Path.DirectorySeparatorChar + __instance.fileName + __instance.backupPath + week + __instance.fileSuffix;
        }
    }

    [HarmonyPatch(typeof(ProgressSaver), nameof(ProgressSaver.GetFilePath))]
    public static class ProgressSaver__GetFilePath {
        public static void Postfix(ProgressSaver __instance, ref string __result) {
            // TPFDSPlugin.Logger.LogDebug($"ProgressSaver.GetFilePath void -> {__result}");
            __result = GetCurrentSavefileRoot() + Path.DirectorySeparatorChar + __instance.fileName + __instance.fileSuffix;
        }
    }

    [HarmonyPatch(typeof(ProgressSaver), nameof(ProgressSaver.GetOldFilePath))]
    public static class ProgressSaver__GetOldFilePath {
        public static void Postfix(ProgressSaver __instance, ref string __result) {
            // TPFDSPlugin.Logger.LogDebug($"ProgressSaver.GetOldFilePath void -> {__result}");
            string uniqueFileSafeTimeString = DataSaveLoad<int>.GetUniqueFileSafeTimeString();
            __result = GetCurrentSavefileRoot() + Path.DirectorySeparatorChar + __instance.fileName + uniqueFileSafeTimeString + __instance.fileSuffix;
        }
    }

    [HarmonyPatch(typeof(DictionarySaver), nameof(DictionarySaver.GetBackupDictFilePath))]
    public static class DictionarySaver__GetBackupDictFilePath {
        public static void Postfix(DictionarySaver __instance, ref string __result, int week) {
            // TPFDSPlugin.Logger.LogDebug($"DictionarySaver.GetBackupDictFilePath {week} -> {__result}");
            __result = GetCurrentSavefileRoot() + Path.DirectorySeparatorChar + __instance.fileName + "-" + __instance.dictCount + __instance.backupPath + week + __instance.fileSuffix;
        }
    }

    [HarmonyPatch(typeof(DictionarySaver), nameof(DictionarySaver.GetDictFilePath))]
    public static class DictionarySaver__GetDictFilePath {
        public static void Postfix(DictionarySaver __instance, ref string __result, int dictID) {
            // TPFDSPlugin.Logger.LogDebug($"DictionarySaver.GetDictFilePath {dictID} -> {__result}");
            __result = GetCurrentSavefileRoot() + Path.DirectorySeparatorChar + __instance.fileName + "-" + dictID + __instance.fileSuffix;
        }
    }

    [HarmonyPatch(typeof(DictionarySaver), nameof(DictionarySaver.GetOldDictFilePath))]
    public static class DictionarySaver__GetOldDictFilePath {
        public static void Postfix(DictionarySaver __instance, ref string __result, int dictID) {
            // TPFDSPlugin.Logger.LogDebug($"DictionarySaver.GetOldDictFilePath {dictID} -> {__result}");
            string uniqueFileSafeTimeString = DataSaveLoad<int>.GetUniqueFileSafeTimeString();
            __result = GetCurrentSavefileRoot() + Path.DirectorySeparatorChar + __instance.fileName + "-" + dictID + uniqueFileSafeTimeString + __instance.fileSuffix;
        }
    }

    [HarmonyPatch(typeof(Autosaver), nameof(Autosaver.Start))]
    public static class Autosaver__Start {
        public static void Prefix(Autosaver __instance) {
            // TPFDSPlugin.Logger.LogDebug($"Autosaver.Start void -> void");

            if (!Directory.Exists(GetCurrentSavefileRoot())) {
                TPFDSPlugin.Logger.LogInfo($"Will create a new save directory at {GetCurrentSavefileRoot()}");
            }

            // This operation will raise an exception (which Unity will silently catch) if some IO issue was encountered
            // No exception will be thrown if the directory was successfully created or already exists
            try {
                Directory.CreateDirectory(GetCurrentSavefileRoot());
            } catch {
                TPFDSPlugin.Logger.LogError($"Failed to find or create the save directory. Saving will likely silently fail.");
                throw;
            }

            // Seed the dictionary with the player's existing dictionary
            if (SeedNewDictionaryFromBaseGameDictionary()) {
                string vanillaDictionary1Path = Application.persistentDataPath + Path.DirectorySeparatorChar + "DICTIONARY-1.save";
                if (!File.Exists(__instance.dictionarySaver.GetDictFilePath(1))) {
                    if (File.Exists(vanillaDictionary1Path)) {
                        TPFDSPlugin.Logger.LogInfo($"Will initialize modded dictionary with base game dictionary.");
                        try {
                            File.Copy(vanillaDictionary1Path, __instance.dictionarySaver.GetDictFilePath(1), false);
                        } catch {
                            TPFDSPlugin.Logger.LogError($"Failed to copy the base game dictionary.");
                            throw;
                        }
                    } else {
                        TPFDSPlugin.Logger.LogError($"Wanted to initialize modded dictionary with the base game dictionary, but the base game dictionary does not exist.");
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(RoomManager), nameof(RoomManager.TranslatorNamed))]
    public static class RoomManager__TranslatorNamed {
        public static bool Prefix(RoomManager __instance) {
            // By overriding this function like so, we bypass the long opening cutscene on a fresh savefile
            // (with the asteroid travelling to Earth and Dr. Doppler's briefing) after setting the Translator's name.

            // TODO can the opening cutscene be tweaked for custom purposes?

            // TPFDSPlugin.Logger.LogDebug($"RoomManager.TranslatorNamed void -> void");
            __instance.hasPlayedOpeningCutscene = true;
            __instance.openingCutscene = false;
            __instance.StartRegularGameplay();
            return false;
        }
    }
}
