using System.Collections.Generic;
using UnityEngine;

namespace TPFDS;

public static class CustomMeetingRepository {
    static Dictionary<string, Dictionary<string, GameObject>> meetings = [];

    public static Dictionary<string, GameObject> Get(string setName) => meetings[setName];

    static bool MeetingLoadError(string reason, string name, CustomPuzzleSetData data) {
        TPFDSPlugin.Logger.LogError($"Loading meeting {name} failed: {reason}. Skipping {data.meta.name}");
        return false;
    }

    public static bool TryAdd(CustomPuzzleSetData data) {
        var dialogues = CustomDialogueRepository.GetAllFromSet(data.meta.name);
        var setMeetings = new Dictionary<string, GameObject>(capacity: data.meetings.Count);
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

            foreach (var convo in meetingObj.conversations) {
                if (!dialogues.TryGetValue(convo, out var next))
                    return MeetingLoadError($"dialogue {convo} doesn't exist", name, data);
                chunk = CustomDialogueRepository.Merge(chunk, next);
            }

            // Taco: NOTE: Set to dopplerBreak, change based on the config later
            chunk.dialogueType = DialogueType.dopplerBreak;

            meeting.dc = chunk;

            if (meetingObj.progressWeek || meetingObj.progressAct) {
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
                            dialogueParts = new DialoguePart[] { new DialoguePart() { txt = meetingObj.journals["Bautista"] } },
                            speaker = Speaker.BScientist,
                        },
                        new DialogueFrame() {
                            dialogueParts = new DialoguePart[] { new DialoguePart() { txt = meetingObj.journals["Carrie"] } },
                            speaker = Speaker.Carrie,
                        },
                        new DialogueFrame() {
                            dialogueParts = new DialoguePart[] { new DialoguePart() { txt = meetingObj.journals["Doppler"] } },
                            speaker = Speaker.Doppler,
                        },
                    }
                };
            }
            if (meetingObj.progressAct || meetingObj.progressWeek) {
                meeting.progressLogData = new ProgressLogData() {
                    actSection = "ACT Test 1",
                    nextActName = "ACT Test 2",
                    actName = "This is a test",
                    listsCompleted = [ ],
                    journalEntriesDialogue = meeting.journalEntryDC,
                    weekID = meetingObj.progressWeek ? week++ : -1,
                };
            }
            setMeetings[name] = gObject;
        }

        meetings[data.meta.name] = setMeetings;
        return true;
    }
}
