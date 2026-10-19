using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace TPFDS;

public static class CustomStoryRepository  {
    static Dictionary<string, Dictionary<string, Puzzle>> allPuzzles = [];
    static Dictionary<string, Puzzle[]> allPuzzlesArray = [];
    static Dictionary<string, PuzzleList[]> puzzles = [];
    static Dictionary<string, GameObject[]> meetings = [];
    static Dictionary<string, LeaveRoomEvent[]> leaveRoomEvents = [];
    static Dictionary<string, Puzzle[]> meetingPuzzles = [];
    static Dictionary<string, (string directory, MetaData meta)> info = [];
    static Dictionary<string, IdeaCluster[]> banter = [];
    static Dictionary<string, Puzzle[]> tabEnablers = [];
    static Dictionary<string, Puzzle[]> hypothesesTriggers = [];
    static Dictionary<string, DictionaryHypotheses.TermCluster[]> termClusters = [];
    static Dictionary<string, TermHypotheses[]> hypotheses = [];

    static void LogEventLoadError(string reason, string puzzleName, CustomPuzzleSetData data) {
        TPFDSPlugin.Logger.LogError($"Event \"{puzzleName}\" {reason}. Skipping {data.meta.name}");
    }

    static SignalMessage MessageFrom(int[] signals) => new SignalMessage() { signals = signals };

    static Dictionary<string, int> Tabs = new() {
        { "puzzle_log", 0 },
        { "log", 1 },
        { "ideas", 2 },
        { "dictionary", 3 },
        { "achievements", 4 },
        { "calculator", 5 },
        { "whiteboard", 6 },
        { "reference", 7 },
        { "visual", 8 },
    };

    static bool IsPuzzle(string name) => name.StartsWith("puzzle:");
    static bool IsMeeting(string name) => name.StartsWith("meeting:");
    static bool UnlocksTab(string name) => name.StartsWith("tab:");
    static bool UnlocksHypotheses(string name) => name.StartsWith("hypotheses:");

    public static void Add(string directory, CustomPuzzleSetData data) {
        int total = 0;
        int local = 0;
        allPuzzles[data.meta.name] = [];
        var groups = new List<PuzzleList>(capacity: data.puzzleGroups.Length);
        var local_meetingPuzzles = new List<Puzzle>(capacity: groups.Capacity);
        var local_meetings = new List<GameObject>(capacity: local_meetingPuzzles.Capacity);
        var local_leaveRoomEvents = new List<LeaveRoomEvent>(capacity: local_meetingPuzzles.Capacity);
        var puzzlesList = new List<Puzzle>(capacity: data.puzzles.Count);

        var finishedGroups = new Queue<PuzzleList>(capacity: data.puzzleGroups.Length);

        var setMeetings = CustomMeetingRepository.GetAllFromSet(data.meta.name);
        var dialogues = CustomDialogueRepository.GetAllFromSet(data.meta.name);
        var setHypotheses = CustomHypothesisRepository.GetAllFromSet(data.meta.name);

        var currAct = 0;
        var addMeeting = false;
        var addTab = new Queue<int>(capacity: Tabs.Count());
        var tabPuzzles = new Puzzle[Tabs.Count];
        System.Array.Fill(tabPuzzles, new Puzzle() { totalID = int.MaxValue });

        var addHypotheses = new Queue<(DictionaryHypotheses.TermCluster cluster, TermHypotheses[] hypo)>(capacity: data.hypotheses.Count);
        var hypothesesPuzzles = new List<Puzzle>(capacity: data.hypotheses.Count);
        var local_hypotheses = new List<TermHypotheses>(capacity: data.hypotheses.Count);
        var local_termClusters = new List<DictionaryHypotheses.TermCluster>(capacity: data.hypotheses.Count);

        foreach (var entry in data.puzzleGroups) {
            local = 0;
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
                        totalID = total++,
                        uniqueID = local++,
                    };
                    if (acceptedCount > 0) {
                        vanillaPuzzle.winningResponse = MessageFrom(puzzle.validResponses[0]);
                    }
                    puzzles.Add(vanillaPuzzle);
                    if (addMeeting) {
                        local_meetingPuzzles.Add(vanillaPuzzle);
                        addMeeting = false;
                    }
                    while (addTab.TryDequeue(out var index)) {
                        tabPuzzles[index] = vanillaPuzzle;
                    }
                    while (addHypotheses.TryDequeue(out var hypothesis)) {
                        hypothesesPuzzles.Add(vanillaPuzzle);
                        local_hypotheses.AddRange(hypothesis.hypo);
                        local_termClusters.Add(hypothesis.cluster);
                    }
                    allPuzzles[data.meta.name][search] = vanillaPuzzle;
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
                else if (UnlocksTab(storyEvent)) {
                    var search = storyEvent["tab:".Length..];
                    if (!Tabs.TryGetValue(search, out var index)) {
                        LogEventLoadError("unknown type tab. Should be one of ", string.Join(", ", Tabs.Keys), data);
                        return;
                    }
                    addTab.Enqueue(index);
                }
                else if (UnlocksHypotheses(storyEvent)) {
                    var search = storyEvent["hypotheses:".Length..];
                    if (!setHypotheses.TryGetValue(search, out var hypothesis)) {
                        LogEventLoadError("doesn't exist", search, data);
                        return;
                    }

                    addHypotheses.Enqueue(hypothesis);
                }
                else {
                    LogEventLoadError("unknown type of event. Should be one of \"puzzle:\", \"meeting:\", \"tab\", \"hypotheses\"", storyEvent, data);
                    return;
                }
            }
            group.puzzles = puzzles.ToArray();
            groups.Add(group);
            finishedGroups.Enqueue(group);
            puzzlesList.AddRange(puzzles);
        }

        var puzzlesArray = puzzlesList.ToArray();
        if (!LoadBanter(data, puzzlesArray)) {
            return;
        }

        var setName = data.meta.name;
        puzzles[setName] = groups.ToArray();
        allPuzzlesArray[setName] = puzzlesArray;
        meetings[setName] = local_meetings.ToArray();
        meetingPuzzles[setName] = local_meetingPuzzles.ToArray();
        leaveRoomEvents[setName] = local_leaveRoomEvents.ToArray();
        info[setName] = (directory, data.meta);
        tabEnablers[setName] = tabPuzzles;
        termClusters[setName] = local_termClusters.ToArray();
        hypothesesTriggers[setName] = hypothesesPuzzles.ToArray();
        hypotheses[setName] = local_hypotheses.ToArray();

        TPFDSPlugin.Logger.LogInfo($"Loaded {data.meta.name}");
    }

    static bool BanterLoadError(string reason, string name) {
        TPFDSPlugin.Logger.LogError($"Failed to load banter {name}: {reason}");
        return false;
    }

    public static bool LoadBanter(CustomPuzzleSetData data, Puzzle[] allPuzzlesArray) {
        var maxConditions = typeof(DialogueEvent.Condition).GetFields().Length;
        var dialogues = CustomDialogueRepository.GetAllFromSet(data.meta.name);
        var clusters = new Dictionary<Puzzle, IdeaCluster>(capacity: allPuzzlesArray.Length);
        var nullCluster = new IdeaCluster() { puzzle = null, listeners = [] };
        var setPuzzles = allPuzzles[data.meta.name];

        foreach (var kvp in data.banter) {
            var entry = kvp.Value;
            var i = kvp.Key;

            if (entry.disabled)
                continue;

            if (entry.triggers.Length == 0) {
                return BanterLoadError("no trigger conditions present", i);
            }

            var chunk = new DialogueChunk() {
                logName = entry.name,
                raw = "TODO: Build this from the dialogue frames",
                processedRaw = "TODO: Build this from the dialogue frames",
                frames = []
            };

            foreach (var convo in entry.conversation) {
                if (!dialogues.TryGetValue(convo, out var next))
                    return BanterLoadError($"dialogue {convo} doesn't exist", i);

                chunk = CustomDialogueRepository.Merge(chunk, next);
            }

            foreach (var trigger in entry.triggers) {
                var conditions = new List<ListenerCondition>(capacity: maxConditions);
                var listenerAsset = new AdvancedListenerAsset() {
                    puzzleNudge = entry.isHint,
                    name = entry.name,
                    dc = chunk,
                };

                if (trigger.puzzleIs is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.PuzzleIs,
                        puzzValue = setPuzzles[trigger.puzzleIs],
                    });
                }
                else {
                    _ = BanterLoadError("Null puzzle", i);
                    conditions.Add(new ListenerCondition() {
                        puzzleIs = true,
                        puzzValue = null,
                    });
                }
                if (trigger.afterPuzzle is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.PastPuzzle,
                        value = setPuzzles[trigger.afterPuzzle].totalID,
                    });
                }
                if (trigger.beforePuzzle is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.BeforePuzzle,
                        value = setPuzzles[trigger.beforePuzzle].totalID,
                    });
                }
                if (trigger.timeOnPuzzle is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.TimeOnPuzzle,
                        value = (float)trigger.timeOnPuzzle,
                    });
                }
                if (trigger.sessionTime is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.TimeInSession,
                        value = (float)trigger.sessionTime,
                    });
                }
                if (trigger.fails is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.FailsOnPuzzle,
                        value = (float)trigger.fails,
                    });
                }
                if (trigger.hintsReceived is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.NudgesReceived,
                        value = (float)trigger.hintsReceived,
                    });
                }
                if (trigger.answerHas is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.SubmittedStringContains,
                        strValue = trigger.answerHas,
                    });
                }
                if (trigger.submittedAndWaiting is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.SubmittedStringWait,
                        strValue = trigger.submittedAndWaiting,
                    });
                }
                if (trigger.submittedCorrect is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.SubmittedStringWin,
                        strValue = trigger.submittedCorrect,
                    });
                }
                if (trigger.submittedWrong is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.SubmittedStringFail,
                        strValue = trigger.submittedWrong,
                    });
                }
                if (trigger.submittedAndWaitingSigs is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.SubmittedSigMessWait,
                        sigValue = MessageFrom(trigger.submittedAndWaitingSigs),
                    });
                }
                if (trigger.submittedCorrectSigs is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.SubmittedSigMessWin,
                        sigValue = MessageFrom(trigger.submittedCorrectSigs),
                    });
                }
                if (trigger.submittedWrongSigs is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.SubmittedSigMessFail,
                        sigValue = MessageFrom(trigger.submittedWrongSigs),
                    });
                }
                if (trigger.changedWordTo is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.EditEntryToName,
                        strValue = trigger.changedWordTo,
                    });
                }
                if (trigger.changedWordFrom is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.EditEntryFromName,
                        strValue = trigger.changedWordFrom,
                    });
                }
                if (trigger.changedSignalTo is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.EditEntryIDFromName,
                        value = trigger.changedSignalTo.signal,
                        strValue = trigger.changedSignalTo.word,
                    });
                }
                if (trigger.changedSignalFrom is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.EditEntryIDFromName,
                        value = trigger.changedSignalTo.signal,
                        strValue = trigger.changedSignalTo.word,
                    });
                }
                if (trigger.signalIs is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.DictEntryIs,
                        value = trigger.changedSignalTo.signal,
                        strValue = trigger.changedSignalTo.word,
                    });
                }
                if (trigger.signalContains is not null) {
                    conditions.Add(new ListenerCondition() {
                        listenChannel = ListenChannel.EditEntryIDContains,
                        value = trigger.changedSignalTo.signal,
                        strValue = trigger.changedSignalTo.word,
                    });
                }

                listenerAsset.conditions = conditions.ToArray();
                var puzzle = conditions[0].puzzValue;

                if (puzzle == null) {
                    nullCluster.AddListener(listenerAsset);
                    continue;
                }

                if (!clusters.TryGetValue(puzzle, out var cluster)) {
                    cluster = new IdeaCluster() {
                        puzzle = conditions[0].puzzValue,
                        listeners = [ listenerAsset ]
                    };

                    clusters[puzzle] = cluster;
                } else {
                    cluster.AddListener(listenerAsset);
                }
            }
        }

        banter[data.meta.name] = [ nullCluster, ..clusters.Values ];
        return true;
    }

    public class SetData {
        public PuzzleList[] puzzles;
        public LeaveRoomEvent[] meetings;
        public Puzzle[] meetingPuzzles;
        public IdeaCluster[] banter;
        public Puzzle[] tabEnablers;
        public (DictionaryHypotheses.TermCluster[] clusters, Puzzle[] triggers, TermHypotheses[] hypos) hypotheses;
    }

    public static SetData Get(string name) => new SetData() {
        puzzles = puzzles[name],
        meetings = leaveRoomEvents[name],
        meetingPuzzles = meetingPuzzles[name],
        banter = banter[name],
        tabEnablers = tabEnablers[name],
        hypotheses = (termClusters[name], hypothesesTriggers[name], hypotheses[name])
    };
}
