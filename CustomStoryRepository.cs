using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace TPFDS;

public static class CustomStoryRepository  {
    static Dictionary<string, PuzzleList[]> puzzles = [];
    static Dictionary<string, GameObject[]> meetings = [];
    static Dictionary<string, LeaveRoomEvent[]> leaveRoomEvents = [];
    static Dictionary<string, Puzzle[]> meetingPuzzles = [];
    static Dictionary<string, (string directory, MetaData meta)> info = [];

    static void LogEventLoadError(string reason, string puzzleName, CustomPuzzleSetData data) {
        TPFDSPlugin.Logger.LogError($"Event \"{puzzleName}\" {reason}. Skipping {data.meta.name}");
    }

    static SignalMessage MessageFrom(int[] signals) => new SignalMessage() { signals = signals };

    static bool IsPuzzle(string name) => name.StartsWith("puzzle:");
    static bool IsMeeting(string name) => name.StartsWith("meeting:");

    public static void Add(string directory, CustomPuzzleSetData data) {
        var groups = new List<PuzzleList>(capacity: data.puzzleGroups.Length);
        var local_meetingPuzzles = new List<Puzzle>(capacity: groups.Capacity);
        var local_meetings = new List<GameObject>(capacity: local_meetingPuzzles.Capacity);
        var local_leaveRoomEvents = new List<LeaveRoomEvent>(capacity: local_meetingPuzzles.Capacity);

        var finishedGroups = new Queue<PuzzleList>(capacity: data.puzzleGroups.Length);

        var setMeetings = CustomMeetingRepository.Get(data.meta.name);
        var dialogues = CustomDialogueRepository.GetAllFromSet(data.meta.name);

        var addMeeting = false;
        var currAct = 0;

        foreach (var entry in data.puzzleGroups) {
            var group = new PuzzleList() { puzzleGroupName = entry.title };
            var storyEvents = entry.storyline;
            var puzzles = new List<Puzzle>(capacity: storyEvents.Where(IsPuzzle).Count());

            for (int i = 0; i < storyEvents.Length; i++) {
                var storyEvent = storyEvents[i];

                if (IsPuzzle(storyEvent)) {
                    var search = storyEvent["puzzle:".Length..];
                    if (!data.puzzles.TryGetValue(search, out var puzzle)) {
                        LogEventLoadError("doesn't exist", storyEvent, data);
                        return;
                    }

                    var acceptedCount = puzzle.validResponses.Length;

                    var vanillaPuzzle = new Puzzle() {
                        title = storyEvent,
                        rockOutput = MessageFrom(puzzle.message),
                        allowAltResponses = acceptedCount > 1,
                        altResponses = acceptedCount > 1 ? puzzle.validResponses[1..].Select(MessageFrom).ToArray() : [],
                    };
                    if (acceptedCount > 0) {
                        vanillaPuzzle.winningResponse = MessageFrom(puzzle.validResponses[0]);
                    }
                    puzzles.Add(vanillaPuzzle);
                    if (addMeeting) {
                        local_meetingPuzzles.Add(vanillaPuzzle);
                        addMeeting = false;
                    }
                }
                else if (IsMeeting(storyEvent)) {
                    var search = storyEvent["meeting:".Length..];
                    if (!setMeetings.TryGetValue(search, out var meeting)) {
                        LogEventLoadError("doesn't exist", storyEvent, data);
                    }
                    var meetingObj = data.meetings[search];

                    addMeeting = true;
                    var thisMeeting  = GameObject.Instantiate(meeting);
                    GameObject.DontDestroyOnLoad(thisMeeting);
                    local_meetings.Add(thisMeeting);
                    local_leaveRoomEvents.Add(thisMeeting.GetComponent<LeaveRoomEvent>());

                    var progressData = thisMeeting.GetComponent<Meeting>().progressLogData;
                    progressData.actBreak = meetingObj.progressAct;
                    if (meetingObj.progressWeek || meetingObj.progressAct) {
                        progressData.listsCompleted = finishedGroups.ToArray();
                        TPFDSPlugin.Logger.LogInfo(finishedGroups.Count);
                        finishedGroups.Clear();
                    }
                    if (meetingObj.progressAct) {
                        progressData.actBreak = true;
                        progressData.actName = data.acts[currAct].name;
                        progressData.actSection = $"ACT {data.acts[currAct].act}";
                        currAct++;
                        var actData = data.acts[currAct];
                        progressData.nextActName = $"ACT {actData.act}";
                    }
                    thisMeeting.GetComponent<Meeting>().progressLogData = progressData;

                    // If this was the first meeting, then it couldn't have been chained
                    if (local_meetings.Count <= 1)
                        continue;

                    // If the previous event wasn't a meeting, there is no chain
                    if (!IsMeeting(storyEvents[i - 1]))
                        continue;

                    var previousMeeting = local_meetings[local_meetings.Count - 2];

                    search = previousMeeting.name["meeting:".Length..];
                    var previousMeetingObj = data.meetings[search];

                    // If the previous meeting progressed the game, there is no chain
                    if (previousMeetingObj.progressAct || previousMeetingObj.progressWeek)
                        continue;

                    previousMeeting.GetComponent<Meeting>().chainedMeeting = thisMeeting.GetComponent<Meeting>();
                    previousMeeting.GetComponent<Meeting>().chainedLRE = thisMeeting.GetComponent<LeaveRoomEvent>();
                    addMeeting = false;
                }
                else {
                    LogEventLoadError("unknown type of event. Should be one of \"puzzle:\" or \"meeting:\"", storyEvent, data);
                    return;
                }
            }
            group.puzzles = puzzles.ToArray();
            groups.Add(group);
            finishedGroups.Enqueue(group);
        }

        puzzles[data.meta.name] = groups.ToArray();
        meetings[data.meta.name] = local_meetings.ToArray();
        meetingPuzzles[data.meta.name] = local_meetingPuzzles.ToArray();
        leaveRoomEvents[data.meta.name] = local_leaveRoomEvents.ToArray();
        info[data.meta.name] = (directory, data.meta);

        TPFDSPlugin.Logger.LogInfo($"Loaded {data.meta.name}");
    }

    public class SetData {
        public PuzzleList[] puzzles;
        public LeaveRoomEvent[] meetings;
        public Puzzle[] meetingPuzzles;
    }

    public static SetData Get(string name) => new SetData() {
        puzzles = puzzles[name],
        meetings = leaveRoomEvents[name],
        meetingPuzzles = meetingPuzzles[name],
    };
}
